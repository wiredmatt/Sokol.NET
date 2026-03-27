// ChessGame.cs — Wrapper around the Lynx chess engine for Chess game state.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lynx;
using Lynx.Model;
using Lynx.UCI.Commands.Engine;
using Lynx.UCI.Commands.GUI;

/// <summary>
/// Controls which search back-end ChessGame uses for AI moves.
/// </summary>
public enum AISearchMode
{
    /// <summary>Single-threaded: calls Engine.BestMove() directly (original behavior).</summary>
    SingleEngine,
    /// <summary>Multi-threaded: drives Lynx's Searcher via its channel-based UCI interface.
    /// Thread count is set by <see cref="ChessGame.SearcherThreadCount"/> before the first AI move.</summary>
    MultithreadedSearcher
}

public enum GamePhase
{
    PlayerTurn,
    AIThinking,
    GameOver
}

public enum GameOverReason
{
    None,
    Checkmate,
    Stalemate,
    FiftyMoveRule,
    InsufficientMaterial
}

public sealed class ChessGame
{
    private readonly Engine _engine;

    // -----------------------------------------------------------------------
    // Search-mode configuration
    // -----------------------------------------------------------------------

    private AISearchMode _searchMode = AISearchMode.MultithreadedSearcher;

    /// <summary>
    /// Switch between single-threaded <see cref="AISearchMode.SingleEngine"/> and
    /// multi-threaded <see cref="AISearchMode.MultithreadedSearcher"/> AI back-end.
    /// Safe to change between moves; takes effect on the next AI turn.
    /// </summary>
    public AISearchMode SearchMode
    {
        get => _searchMode;
        set => _searchMode = value;
    }

    /// <summary>
    /// Number of search threads used when <see cref="SearchMode"/> is
    /// <see cref="AISearchMode.MultithreadedSearcher"/>.
    /// Defaults to <c>max(1, Environment.ProcessorCount - 1)</c>.
    /// Must be set before the first AI move in Searcher mode (or before calling
    /// <see cref="Reset"/> which re-initialises the Searcher).
    /// </summary>
    public int SearcherThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

    // Searcher infrastructure (lazy — created on first use in MultithreadedSearcher mode)
    private Searcher? _searcher;
    private Channel<string>? _searcherInputChannel;
    private Channel<object>? _searcherOutputChannel;
    private CancellationTokenSource? _searcherCts;
    private Task? _searcherRunTask;

    public Side HumanSide { get; private set; } = Side.White;
    public GamePhase Phase { get; private set; } = GamePhase.PlayerTurn;
    public GameOverReason OverReason { get; private set; } = GameOverReason.None;
    public int AiDepth { get; set; } = 5;
    public string? LastMoveUCI { get; private set; }
    public string? LastMoveAlgebraic { get; private set; }

    // The square the human has selected (0-63, or -1 if none)
    public int SelectedSquare { get; private set; } = -1;

    // Valid destination squares for the selected piece
    private readonly List<Move> _legalMovesFromSelected = new();
    public IReadOnlyList<Move> LegalMovesFromSelected => _legalMovesFromSelected;

    // All legal moves in the current position (cached after each move)
    private Move[] _allLegalMoves = Array.Empty<Move>();
    public IReadOnlyList<Move> AllLegalMoves => _allLegalMoves;

    // Snapshot of the board used by the render thread — updated only on the main thread
    // so the background AI search thread cannot corrupt it.
    private readonly int[] _boardSnapshot = new int[64];
    private readonly object _snapshotLock = new();
    public ReadOnlySpan<int> BoardSnapshot => _boardSnapshot;

    public ChessGame()
    {
        _engine = new Engine(SilentChannelWriter<object>.Instance);
        RefreshLegalMoves();
        TakeSnapshot();
        Console.WriteLine("[ChessGame] Initialized. Side to move: " + _engine.Game.CurrentPosition.Side);
    }

    public void Reset(Side humanSide = Side.White)
    {
        Console.WriteLine($"[ChessGame] Reset — human plays {humanSide}");
        HumanSide = humanSide;
        OverReason = GameOverReason.None;
        SelectedSquare = -1;
        LastMoveUCI = null;
        LastMoveAlgebraic = null;
        _legalMovesFromSelected.Clear();
        _engine.NewGame();
        _searcher?.NewGame();
        RefreshLegalMoves();
        TakeSnapshot();

        // At game start White is to move. If the human picked Black,
        // AI (White) should move immediately.
        Phase = _engine.Game.CurrentPosition.Side == HumanSide
            ? GamePhase.PlayerTurn
            : GamePhase.AIThinking;

        Console.WriteLine($"[ChessGame] Reset phase → {Phase}");
    }

    // -----------------------------------------------------------------------
    // Board access
    // -----------------------------------------------------------------------

    /// <summary>Safe for the render thread — reads from the snapshot, not the live engine position.</summary>
    public Piece GetPieceAt(int square)
    {
        lock (_snapshotLock)
        {
            return (Piece)_boardSnapshot[square];
        }
    }

    public Side CurrentSideToMove => _engine.Game.CurrentPosition.Side;

    public bool IsHumanTurn => Phase == GamePhase.PlayerTurn && CurrentSideToMove == HumanSide;

    // -----------------------------------------------------------------------
    // Square selection & move execution
    // -----------------------------------------------------------------------

    /// <summary>Returns true if the selection changed (triggers highlight refresh).</summary>
    public bool SelectSquare(int square)
    {
        if (!IsHumanTurn) return false;

        var piece = GetPieceAt(square);

        // Clicking on an owned piece selects it
        bool ownPiece = piece != Piece.None && IsOwnPiece(piece);

        if (SelectedSquare == -1)
        {
            if (!ownPiece) return false;
            SelectedSquare = square;
            _legalMovesFromSelected.Clear();
            foreach (var m in _allLegalMoves)
                if (m.SourceSquare() == square)
                    _legalMovesFromSelected.Add(m);
            return true;
        }

        // Second click: try to move
        Move? foundMove = FindMove(SelectedSquare, square);

        if (foundMove.HasValue)
        {
            ApplyHumanMove(foundMove.Value);
            SelectedSquare = -1;
            _legalMovesFromSelected.Clear();
            return true;
        }

        // Re-select a different own piece or deselect
        if (ownPiece)
        {
            SelectedSquare = square;
            _legalMovesFromSelected.Clear();
            foreach (var m in _allLegalMoves)
                if (m.SourceSquare() == square)
                    _legalMovesFromSelected.Add(m);
        }
        else
        {
            SelectedSquare = -1;
            _legalMovesFromSelected.Clear();
        }
        return true;
    }

    private Move? FindMove(int from, int to)
    {
        Move? nonPromotion = null;
        Move? queenPromotion = null;
        foreach (var m in _allLegalMoves)
        {
            if (m.SourceSquare() == from && m.TargetSquare() == to)
            {
                if (!m.IsPromotion()) return m;
                // Prefer queen promotion for auto-promotion
                var pp = (Piece)m.PromotedPiece();
                if (pp == Piece.Q || pp == Piece.q)
                    queenPromotion = m;
                else
                    nonPromotion ??= m;
            }
        }
        return queenPromotion ?? nonPromotion;
    }

    private void ApplyHumanMove(Move move)
    {
        string algebraicBase = move.ToEPDString(_engine.Game.CurrentPosition);
        LastMoveUCI = move.UCIString();
        Console.WriteLine($"[ChessGame] Human move: {LastMoveUCI}");
        _engine.Game.MakeMove(move);
        _engine.Game.UpdateInitialPosition();
        RefreshLegalMoves();
        TakeSnapshot();   // snapshot BEFORE handing off to AI thread
        CheckGameOver();
        LastMoveAlgebraic = AppendCheckSuffix(algebraicBase);
        if (Phase == GamePhase.PlayerTurn)
        {
            Phase = GamePhase.AIThinking;
            Console.WriteLine("[ChessGame] Phase → AIThinking");
        }
    }

    // -----------------------------------------------------------------------
    // AI
    // -----------------------------------------------------------------------

    /// <summary>Called after Phase is set to AIThinking. Blocking call — run on a worker thread.
    /// NOTE: BestMove() internally makes/unmakes many moves during search. The render thread
    /// must NOT read CurrentPosition during this time — it reads _boardSnapshot instead.
    /// </summary>
    /// <summary>
    /// Lazily initialises the <see cref="Searcher"/> and its background Run loop.
    /// Called automatically before the first AI move in MultithreadedSearcher mode.
    /// </summary>
    private void EnsureSearcherInitialized()
    {
        if (_searcher != null) return;

        Configuration.EngineSettings.Threads = SearcherThreadCount;
        _searcherInputChannel = Channel.CreateUnbounded<string>();
        _searcherOutputChannel = Channel.CreateUnbounded<object>();
        _searcher = new Searcher(_searcherInputChannel.Reader, _searcherOutputChannel.Writer);
        _searcherCts = new CancellationTokenSource();
        // Fire-and-forget: Searcher.Run catches/logs its own exceptions.
        _searcherRunTask = _searcher.Run(_searcherCts.Token);
        Console.WriteLine($"[ChessGame] Searcher initialised with {SearcherThreadCount} thread(s)");
    }

    public void ExecuteAIMove()
    {
        if (Phase != GamePhase.AIThinking)
        {
            Console.WriteLine($"[ChessGame] ExecuteAIMove called but Phase={Phase}, skipping");
            return;
        }

        Console.WriteLine($"[ChessGame] AI thinking (depth={AiDepth}, mode={SearchMode})...");
        using var positionBeforeAIMove = new Position(_engine.Game.CurrentPosition);

        Move bestMove;
        string algebraicBase;

        if (SearchMode == AISearchMode.SingleEngine)
        {
            // Original single-threaded path.
            // BestMove internally calls Game.MakeMove + Game.UpdateInitialPosition.
            // During search, CurrentPosition.Board[] is volatile — use _boardSnapshot for rendering.
            var result = _engine.BestMove(new GoCommand($"go depth {AiDepth}"));
            bestMove = result.BestMove;
            algebraicBase = bestMove.ToEPDString(positionBeforeAIMove);
        }
        else
        {
            // Multi-threaded path via Lynx's Searcher.
            // The Searcher runs on its own internal Engine instances; _engine.Game is only read
            // here to get the FEN, then the AI move is applied to it manually below.
            EnsureSearcherInitialized();

            // Sync the Searcher's internal game state to the current board position.
            _searcher!.AdjustPosition($"position fen {_engine.Game.FEN}");

            // Send the go command through the channel that Searcher.Run() listens on.
            _searcherInputChannel!.Writer.TryWrite($"go depth {AiDepth}");

            // Drain the output channel: skip intermediate SearchResult (info) lines,
            // stop when the Searcher writes BestMoveCommand.
            SearchResult? latestResult = null;
            while (true)
            {
                var item = _searcherOutputChannel!.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
                if (item is SearchResult sr)
                    latestResult = sr;
                else if (item is BestMoveCommand)
                    break;
            }

            bestMove = latestResult!.BestMove;
            algebraicBase = bestMove.ToEPDString(positionBeforeAIMove);

            // Apply the move to _engine.Game (mirrors what Engine.BestMove does internally).
            _engine.Game.MakeMove(bestMove);
            _engine.Game.UpdateInitialPosition();
        }

        LastMoveUCI = bestMove.UCIString();
        Console.WriteLine($"[ChessGame] AI played: {LastMoveUCI}");

        RefreshLegalMoves();
        TakeSnapshot();   // commit AI move to snapshot

        LogBoardSnapshot();

        CheckGameOver();
        LastMoveAlgebraic = AppendCheckSuffix(algebraicBase);

        if (Phase != GamePhase.GameOver)
        {
            Phase = GamePhase.PlayerTurn;
            Console.WriteLine("[ChessGame] Phase → PlayerTurn");
        }
        else
        {
            Console.WriteLine($"[ChessGame] Phase → GameOver ({OverReason})");
        }
    }

    /// <summary>
    /// Debug helper: disables AI turn handoff by returning control to the player.
    /// Useful for reproducing rendering bugs without background search/move execution.
    /// </summary>
    public void DebugForcePlayerTurn()
    {
        if (Phase == GamePhase.AIThinking)
        {
            Phase = GamePhase.PlayerTurn;
            Console.WriteLine("[ChessGame] Debug: forced Phase → PlayerTurn (AI disabled)");
        }
    }

    // -----------------------------------------------------------------------
    // Game-over detection
    // -----------------------------------------------------------------------

    private void CheckGameOver()
    {
        Console.WriteLine($"[ChessGame] CheckGameOver: legalMoves={_allLegalMoves.Length}, halfMoveClock={_engine.Game.HalfMovesWithoutCaptureOrPawnMove}");
        if (_allLegalMoves.Length == 0)
        {
            Phase = GamePhase.GameOver;
            bool inCheck = _engine.Game.CurrentPosition.IsInCheck();
            OverReason = inCheck ? GameOverReason.Checkmate : GameOverReason.Stalemate;
            Console.WriteLine($"[ChessGame] GameOver → {OverReason}");
            return;
        }

        if (_engine.Game.HalfMovesWithoutCaptureOrPawnMove >= 100)
        {
            Phase = GamePhase.GameOver;
            OverReason = GameOverReason.FiftyMoveRule;
            Console.WriteLine("[ChessGame] GameOver → FiftyMoveRule");
        }
    }

    private string AppendCheckSuffix(string algebraic)
    {
        if (string.IsNullOrEmpty(algebraic))
        {
            return algebraic;
        }

        // Avoid duplicating suffixes if already present.
        if (algebraic.EndsWith("+") || algebraic.EndsWith("#"))
        {
            return algebraic;
        }

        bool inCheck = _engine.Game.CurrentPosition.IsInCheck();
        if (!inCheck)
        {
            return algebraic;
        }

        return _allLegalMoves.Length == 0 ? algebraic + "#" : algebraic + "+";
    }

    public bool IsInCheck() => _engine.Game.CurrentPosition.IsInCheck();

    // -----------------------------------------------------------------------
    // Legal move generation
    // -----------------------------------------------------------------------

    private void RefreshLegalMoves()
    {
        var pos = _engine.Game.CurrentPosition;

        Span<Move> pool = stackalloc Move[256];
        Span<ulong> evalBuf = stackalloc ulong[EvaluationContext.RequiredBufferSize];
        var evalCtx = new EvaluationContext(evalBuf);

        var pseudoLegal = MoveGenerator.GenerateAllMoves(pos, ref evalCtx, pool);

        var legal = new List<Move>(pseudoLegal.Length);
        foreach (var move in pseudoLegal)
        {
            var gs = pos.MakeMove(move);
            if (pos.WasProduceByAValidMove())
                legal.Add(move);
            pos.UnmakeMove(move, gs);
        }

        _allLegalMoves = legal.ToArray();
        Console.WriteLine($"[ChessGame] RefreshLegalMoves: {_allLegalMoves.Length} legal moves for {pos.Side}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private bool IsOwnPiece(Piece piece)
    {
        bool isWhite = piece >= Piece.P && piece <= Piece.K;
        return HumanSide == Side.White ? isWhite : !isWhite;
    }

    // -----------------------------------------------------------------------
    // Snapshot helpers
    // -----------------------------------------------------------------------

    /// <summary>Copies the current live board into _boardSnapshot. Call only from the main thread
    /// (after a move is fully committed) to avoid the AI search corrupting the render view.</summary>
    private void TakeSnapshot()
    {
        var board = _engine.Game.CurrentPosition.Board;
        lock (_snapshotLock)
        {
            for (int i = 0; i < 64; i++)
                _boardSnapshot[i] = board[i];
        }
        Console.WriteLine("[ChessGame] Snapshot taken");
    }

    private void LogBoardSnapshot()
    {
        int pieceCount = 0;
        lock (_snapshotLock)
        {
            for (int i = 0; i < 64; i++)
                if (_boardSnapshot[i] != (int)Piece.None) pieceCount++;
        }
        Console.WriteLine($"[ChessGame] Snapshot piece count: {pieceCount}/32");
    }
}

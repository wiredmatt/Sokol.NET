// CheckersGame.cs — Full Checkers / Draughts game logic.
// Supports 8×8 and 10×10 boards, configurable rules (flying kings,
// mandatory capture, backward capture, max-capture selection).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Checkers
{
    // -----------------------------------------------------------------------
    // Enums & config types
    // -----------------------------------------------------------------------

    public enum PieceColor : byte { None = 0, Light = 1, Dark = 2 }

    public enum PieceType : byte { None = 0, Man = 1, King = 2 }

    public enum GamePhase
    {
        NotStarted,     // Before first game
        Configure,      // UI config screen
        PlayerTurn,
        AIThinking,
        GameOver,
    }

    public enum KingBehavior { FlyingKings, NoFlyingKings }
    public enum CaptureBehavior { MandatoryMaxMen, MandatoryAny, Optional }
    public enum BackwardCapture { Allowed, Forbidden }

    public struct GameRules
    {
        public int BoardSize;               // 8 or 10
        public KingBehavior Kings;
        public CaptureBehavior Capture;
        public BackwardCapture Backward;

        public static GameRules International => new GameRules
        {
            BoardSize = 8,
            Kings     = KingBehavior.FlyingKings,
            Capture   = CaptureBehavior.MandatoryMaxMen,
            Backward  = BackwardCapture.Allowed,
        };
    }

    // -----------------------------------------------------------------------
    // Cell / Piece
    // -----------------------------------------------------------------------

    public struct Piece
    {
        public PieceColor Color;
        public PieceType  Type;
        public bool IsEmpty => Color == PieceColor.None;
    }

    // -----------------------------------------------------------------------
    // A move: from-square to-square, with list of captured squares
    // -----------------------------------------------------------------------

    public struct CheckersMove
    {
        public int From;                    // board index
        public int To;                      // board index
        public List<int> Captures;          // indices of captured pieces in order
        public List<int> Path;              // all squares visited during multi-hop

        public CheckersMove(int from, int to)
        {
            From     = from;
            To       = to;
            Captures = new List<int>();
            Path     = new List<int> { from, to };
        }
    }

    // -----------------------------------------------------------------------
    // Board
    // -----------------------------------------------------------------------

    public class CheckersBoard
    {
        public int       Size;      // 8 or 10
        public Piece[]   Cells;

        public CheckersBoard(int size)
        {
            Size  = size;
            Cells = new Piece[size * size];
        }

        public CheckersBoard Clone()
        {
            var b     = new CheckersBoard(Size);
            Array.Copy(Cells, b.Cells, Cells.Length);
            return b;
        }

        public int Idx(int row, int col) => row * Size + col;

        public Piece Get(int row, int col) => Cells[Idx(row, col)];

        public void Set(int row, int col, Piece p) => Cells[Idx(row, col)] = p;

        public bool InBounds(int row, int col) => row >= 0 && row < Size && col >= 0 && col < Size;

        // Dark squares only (checkers uses dark squares)
        public bool IsDarkSquare(int row, int col) => (row + col) % 2 != 0;

        /// <summary>Human-friendly coordinate label: col letter + row number.
        /// Col A–H (or A–J for 10×10) left-to-right; row 1 = bottom for Light side.</summary>
        public string CellLabel(int idx)
        {
            int row = idx / Size;
            int col = idx % Size;
            char colLetter = (char)('A' + col);
            int  rowNum    = row + 1;   // row 0 = top displayed as 1 for now
            return $"{colLetter}{rowNum}";
        }

        // -----------------------------------------------------------------------
        // Setup
        // -----------------------------------------------------------------------

        public void SetupStandard()
        {
            Array.Clear(Cells, 0, Cells.Length);
            int rows = (Size == 8) ? 3 : 4;    // rows of pieces per side
            for (int row = 0; row < Size; row++)
            for (int col = 0; col < Size; col++)
            {
                if (!IsDarkSquare(row, col)) continue;
                if (row < rows)
                    Set(row, col, new Piece { Color = PieceColor.Dark, Type = PieceType.Man });
                else if (row >= Size - rows)
                    Set(row, col, new Piece { Color = PieceColor.Light, Type = PieceType.Man });
            }
        }

        // -----------------------------------------------------------------------
        // Count
        // -----------------------------------------------------------------------

        public int CountPieces(PieceColor color)
        {
            int n = 0;
            foreach (var p in Cells) if (p.Color == color) n++;
            return n;
        }
    }

    // -----------------------------------------------------------------------
    // Move Generator
    // -----------------------------------------------------------------------

    public static class MoveGenerator
    {
        // Returns all legal moves for the given color, respecting rules.
        public static List<CheckersMove> GetAllMoves(CheckersBoard board, PieceColor color, GameRules rules)
        {
            // Collect all captures first
            var captures = GetAllCaptureMoves(board, color, rules);
            if (captures.Count > 0)
            {
                if (rules.Capture == CaptureBehavior.Optional)
                    goto skipMandatory;
                // MandatoryAny: any capture is fine
                if (rules.Capture == CaptureBehavior.MandatoryAny)
                    return captures;
                // MandatoryMaxMen: must take the sequence that captures the most pieces
                int maxCap = 0;
                foreach (var m in captures)
                    if (m.Captures.Count > maxCap) maxCap = m.Captures.Count;
                var best = new List<CheckersMove>();
                foreach (var m in captures)
                    if (m.Captures.Count == maxCap) best.Add(m);
                return best;
            }

            skipMandatory:
            return GetAllSimpleMoves(board, color, rules);
        }

        // -----------------------------------------------------------------------
        // Simple (non-capture) moves
        // -----------------------------------------------------------------------
        static List<CheckersMove> GetAllSimpleMoves(CheckersBoard board, PieceColor color, GameRules rules)
        {
            var moves = new List<CheckersMove>();
            int sz = board.Size;
            for (int idx = 0; idx < sz * sz; idx++)
            {
                var piece = board.Cells[idx];
                if (piece.Color != color) continue;
                int row = idx / sz, col = idx % sz;
                AddSimpleMoves(board, row, col, piece, rules, moves);
            }
            return moves;
        }

        static void AddSimpleMoves(CheckersBoard board, int row, int col, Piece piece, GameRules rules, List<CheckersMove> moves)
        {
            int sz = board.Size;
            int[] dRows, dCols;
            GetDirections(piece, rules, out dRows, out dCols);

            if (piece.Type == PieceType.King && rules.Kings == KingBehavior.FlyingKings)
            {
                // Flying king: slide any distance along diagonals
                foreach (int dr in new[] { -1, 1 })
                foreach (int dc in new[] { -1, 1 })
                {
                    int r = row + dr, c = col + dc;
                    while (board.InBounds(r, c) && board.Get(r, c).IsEmpty)
                    {
                        moves.Add(new CheckersMove(board.Idx(row, col), board.Idx(r, c)));
                        r += dr; c += dc;
                    }
                }
            }
            else
            {
                foreach (int dr in dRows)
                {
                    int r = row + dr;
                    foreach (int dc in dCols)
                    {
                        int c = col + dc;
                        if (board.InBounds(r, c) && board.Get(r, c).IsEmpty)
                            moves.Add(new CheckersMove(board.Idx(row, col), board.Idx(r, c)));
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // Capture moves (recursive multi-hop)
        // -----------------------------------------------------------------------
        static List<CheckersMove> GetAllCaptureMoves(CheckersBoard board, PieceColor color, GameRules rules)
        {
            var moves = new List<CheckersMove>();
            int sz = board.Size;
            for (int idx = 0; idx < sz * sz; idx++)
            {
                var piece = board.Cells[idx];
                if (piece.Color != color) continue;
                int row = idx / sz, col = idx % sz;
                var seq = new CheckersMove(idx, idx) { Path = new List<int> { idx } };
                // Start with a temporary board clone for recursion
                ExpandCaptures(board.Clone(), row, col, piece, seq, rules, moves, new HashSet<int>());
            }
            return moves;
        }

        static void ExpandCaptures(CheckersBoard board, int row, int col, Piece piece, CheckersMove seq,
                                   GameRules rules, List<CheckersMove> results, HashSet<int> capturedInSeq)
        {
            bool foundCapture = false;
            int  sz           = board.Size;

            if (piece.Type == PieceType.King && rules.Kings == KingBehavior.FlyingKings)
            {
                // Flying king capture
                foreach (int dr in new[] { -1, 1 })
                foreach (int dc in new[] { -1, 1 })
                {
                    // Slide until we find enemy, then can land on any empty beyond
                    int r = row + dr, c = col + dc;
                    int capturedIdx = -1;
                    while (board.InBounds(r, c))
                    {
                        var cell = board.Get(r, c);
                        if (!cell.IsEmpty)
                        {
                            if (cell.Color != piece.Color && !capturedInSeq.Contains(board.Idx(r, c)))
                                capturedIdx = board.Idx(r, c);
                            break;
                        }
                        r += dr; c += dc;
                    }
                    if (capturedIdx < 0) continue;
                    // Can land on any empty square beyond the captured piece
                    int lr = r + dr, lc = c + dc;
                    while (board.InBounds(lr, lc) && board.Get(lr, lc).IsEmpty)
                    {
                        foundCapture = true;
                        var newSeq = CloneSeq(seq, board.Idx(lr, lc), capturedIdx);
                        // Apply on cloned board
                        var nb = board.Clone();
                        nb.Cells[board.Idx(row, col)] = default;
                        var capturedPiece = nb.Cells[capturedIdx];
                        nb.Cells[capturedIdx] = default;
                        nb.Cells[board.Idx(lr, lc)] = piece;
                        var pieceAtDest = TryPromote(nb, piece, lr, nb.Size);
                        nb.Cells[board.Idx(lr, lc)] = pieceAtDest;

                        var newCaptured = new HashSet<int>(capturedInSeq) { capturedIdx };
                        // Restore captured piece so neighbor captures can still use it
                        nb.Cells[capturedIdx] = capturedPiece;
                        ExpandCaptures(nb, lr, lc, pieceAtDest, newSeq, rules, results, newCaptured);
                        lr += dr; lc += dc;
                    }
                }
            }
            else
            {
                // Normal piece / no-flying-king king
                int[] dRows, dCols;
                GetCaptureDirections(piece, rules, out dRows, out dCols);
                foreach (int dr in dRows)
                foreach (int dc in dCols)
                {
                    int mr = row + dr, mc = col + dc;       // middle (enemy)
                    int lr = row + 2*dr, lc = col + 2*dc;  // landing
                    if (!board.InBounds(mr, mc) || !board.InBounds(lr, lc)) continue;
                    var mid = board.Get(mr, mc);
                    if (mid.IsEmpty || mid.Color == piece.Color) continue;
                    int midIdx = board.Idx(mr, mc);
                    if (capturedInSeq.Contains(midIdx)) continue;
                    if (!board.Get(lr, lc).IsEmpty) continue;

                    foundCapture = true;
                    var newSeq = CloneSeq(seq, board.Idx(lr, lc), midIdx);
                    var nb = board.Clone();
                    nb.Cells[board.Idx(row, col)] = default;
                    var capturedPiece = nb.Cells[midIdx];
                    nb.Cells[midIdx] = default;
                    nb.Cells[board.Idx(lr, lc)] = piece;
                    var pieceAtDest = TryPromote(nb, piece, lr, nb.Size);
                    nb.Cells[board.Idx(lr, lc)] = pieceAtDest;
                    // Restore captured piece position for cross-capture detection
                    nb.Cells[midIdx] = capturedPiece;
                    var newCaptured = new HashSet<int>(capturedInSeq) { midIdx };
                    ExpandCaptures(nb, lr, lc, pieceAtDest, newSeq, rules, results, newCaptured);
                }
            }

            if (!foundCapture && seq.Captures.Count > 0)
            {
                // Leaf: record this completed capture sequence
                results.Add(seq);
            }
        }

        static CheckersMove CloneSeq(CheckersMove seq, int newTo, int capturedIdx)
        {
            var m = new CheckersMove(seq.From, newTo)
            {
                Captures = new List<int>(seq.Captures) { capturedIdx },
                Path     = new List<int>(seq.Path) { newTo },
            };
            return m;
        }

        static Piece TryPromote(CheckersBoard board, Piece piece, int row, int size)
        {
            if (piece.Type == PieceType.King) return piece;
            int promRow = (piece.Color == PieceColor.Light) ? 0 : size - 1;
            if (row == promRow)
                return new Piece { Color = piece.Color, Type = PieceType.King };
            return piece;
        }

        static void GetDirections(Piece piece, GameRules rules, out int[] dRows, out int[] dCols)
        {
            dCols = new[] { -1, 1 };
            if (piece.Type == PieceType.King)
                dRows = new[] { -1, 1 };
            else if (piece.Color == PieceColor.Light)
                dRows = new[] { -1 };   // Light moves up (decreasing row)
            else
                dRows = new[] { 1 };    // Dark moves down (increasing row)
        }

        static void GetCaptureDirections(Piece piece, GameRules rules, out int[] dRows, out int[] dCols)
        {
            dCols = new[] { -1, 1 };
            if (piece.Type == PieceType.King)
                dRows = new[] { -1, 1 };
            else if (rules.Backward == BackwardCapture.Allowed)
                dRows = new[] { -1, 1 };
            else if (piece.Color == PieceColor.Light)
                dRows = new[] { -1 };
            else
                dRows = new[] { 1 };
        }
    }

    // -----------------------------------------------------------------------
    // Main game class
    // -----------------------------------------------------------------------

    public class CheckersGame
    {
        public GameRules     Rules;
        public CheckersBoard Board;
        public GamePhase     Phase       = GamePhase.Configure;
        public PieceColor    Turn        = PieceColor.Light; // Light = human player 1
        public bool          HumanIsLight = true;
        public int           AiDepth     = 6;
        public PieceColor    Winner      = PieceColor.None;
        public bool          IsDraw      = false;

        public int  LightWins = 0;
        public int  DarkWins  = 0;

        // Currently selected piece index (-1 = none)
        public int SelectedPiece = -1;

        // Available moves from selected piece
        public List<CheckersMove> SelectedMoves = new();

        // All legal moves for the current player
        public List<CheckersMove> AllLegalMoves = new();

        // Set to true after a human move; the app starts AI after a short delay
        public bool AITurnPending { get; private set; }
        // Incremented on every applied move (lets the app detect new moves)
        public int  MoveCount     { get; private set; }

        // Pending AI result (volatile not allowed on nullable struct; use lock via _aiMoveReady flag)
        private CheckersMove? _pendingAIMove = null;
        private volatile bool _aiMoveReady   = false;

        // Move history for undo
        private readonly Stack<(CheckersBoard board, PieceColor turn, int selPiece)> _history = new();

        // Last move (for highlighting animation)
        public CheckersMove? LastMove = null;

        /// <summary>Fired just BEFORE a move is applied, while Board.Cells still contains captured pieces.</summary>
        public System.Action<CheckersMove>? BeforeApplyMove;

        public CheckersGame()
        {
            Rules = GameRules.International;
            Board = new CheckersBoard(Rules.BoardSize);
        }

        // -----------------------------------------------------------------------
        // Start / reset
        // -----------------------------------------------------------------------

        public void StartNewGame()
        {
            Board = new CheckersBoard(Rules.BoardSize);
            Board.SetupStandard();
            Turn         = PieceColor.Light;
            Phase        = GamePhase.PlayerTurn;
            Winner       = PieceColor.None;
            IsDraw       = false;
            SelectedPiece = -1;
            SelectedMoves.Clear();
            LastMove      = null;
            AITurnPending = false;
            MoveCount     = 0;
            _aiMoveReady  = false;
            _pendingAIMove= null;
            _history.Clear();
            RefreshLegalMoves();

            // If AI plays first (human is Dark), trigger AI
            if (!IsHumanTurn())
                RequestAIMove();
        }

        // -----------------------------------------------------------------------
        // Human input: select piece / make move
        // -----------------------------------------------------------------------

        public bool SelectCell(int cellIdx)
        {
            if (Phase != GamePhase.PlayerTurn) return false;
            if (!IsHumanTurn()) return false;

            var piece = Board.Cells[cellIdx];

            // If a piece is already selected, try to apply the move
            if (SelectedPiece >= 0 && SelectedPiece != cellIdx)
            {
                // Find if there is a move from selected piece to the tapped destination
                CheckersMove? move = FindMove(SelectedPiece, cellIdx);
                if (move.HasValue)
                {
                    Console.WriteLine($"[HUMAN] SelectCell: applying move {Board.CellLabel(SelectedPiece)}->{Board.CellLabel(cellIdx)}");
                    ApplyMove(move.Value);
                    return true;  // real move made
                }
                // Clicked on our own piece – reselect
                if (piece.Color == HumanColor())
                {
                    SelectedPiece = cellIdx;
                    RefreshSelectedMoves();
                    return false;  // just a reselection
                }
                // Deselect
                SelectedPiece = -1;
                SelectedMoves.Clear();
                return false;
            }

            // Select a piece
            if (piece.Color == HumanColor())
            {
                // Only allow selecting pieces that have legal moves
                var movesFromHere = AllLegalMoves.FindAll(m => m.From == cellIdx);
                if (movesFromHere.Count > 0)
                {
                    SelectedPiece = cellIdx;
                    SelectedMoves = movesFromHere;
                    return false;  // just a selection, not a move
                }
            }
            return false;
        }

        CheckersMove? FindMove(int from, int to)
        {
            foreach (var m in SelectedMoves)
                if (m.From == from && m.To == to) return m;
            return null;
        }

        // -----------------------------------------------------------------------
        // Apply a move
        // -----------------------------------------------------------------------

        public void ApplyMove(CheckersMove move)
        {
            BeforeApplyMove?.Invoke(move);

            // Save undo
            _history.Push((Board.Clone(), Turn, SelectedPiece));

            var piece = Board.Cells[move.From];

            // DEBUG LOG: trace every move
            {
                string fromLabel = Board.CellLabel(move.From);
                string toLabel   = Board.CellLabel(move.To);
                string caps = move.Captures.Count > 0
                    ? " captures=[" + string.Join(",", move.Captures.ConvertAll(c => Board.CellLabel(c))) + "]"
                    : "";
                string path = move.Path.Count > 2
                    ? " path=[" + string.Join("->", move.Path.ConvertAll(p => Board.CellLabel(p))) + "]"
                    : "";
                Console.WriteLine($"[MOVE] {Turn} {piece.Type} {fromLabel}->{toLabel}{caps}{path}  (MoveCount will be {MoveCount+1})");
            }

            // Mid-sequence promotion: a Man that lands on its promotion row during a
            // multi-hop capture is immediately crowned (matches ExpandCaptures behaviour).
            if (piece.Type == PieceType.Man && move.Path.Count > 2)
            {
                int midPromRow = (piece.Color == PieceColor.Light) ? 0 : Board.Size - 1;
                for (int i = 1; i < move.Path.Count - 1; i++)
                {
                    if (move.Path[i] / Board.Size == midPromRow)
                    {
                        piece = new Piece { Color = piece.Color, Type = PieceType.King };
                        Console.WriteLine($"[PROMOTE] {piece.Color} promoted mid-sequence at {Board.CellLabel(move.Path[i])}");
                        break;
                    }
                }
            }

            // Move piece
            Board.Cells[move.From] = default;
            Board.Cells[move.To]   = piece;

            // Remove captured pieces
            foreach (int cap in move.Captures)
                Board.Cells[cap] = default;

            // Promotion at final destination
            int row    = move.To / Board.Size;
            int promRow = (piece.Color == PieceColor.Light) ? 0 : Board.Size - 1;
            bool promoted = piece.Type == PieceType.Man && row == promRow;
            if (promoted)
                Board.Cells[move.To] = new Piece { Color = piece.Color, Type = PieceType.King };

            if (promoted)
                Console.WriteLine($"[PROMOTE] {piece.Color} promoted at {Board.CellLabel(move.To)} (row={row}, promRow={promRow})");

            MoveCount++;
            LastMove      = move;
            SelectedPiece = -1;
            SelectedMoves.Clear();

            // Check game over
            if (CheckGameOver()) return;

            // Switch turn
            Turn = Opponent(Turn);
            RefreshLegalMoves();

            // If no moves available for next player -> GameOver
            if (AllLegalMoves.Count == 0)
            {
                Winner = Turn == PieceColor.Light ? PieceColor.Dark : PieceColor.Light;
                RecordWin(Winner);
                Phase = GamePhase.GameOver;
                return;
            }

            // Signal app to start AI after a short delay
            if (!IsHumanTurn())
                AITurnPending = true;
        }

        bool CheckGameOver()
        {
            // If a side has no pieces left
            int lightCount = Board.CountPieces(PieceColor.Light);
            int darkCount  = Board.CountPieces(PieceColor.Dark);
            if (lightCount == 0)
            {
                Winner = PieceColor.Dark;
                RecordWin(Winner);
                Phase  = GamePhase.GameOver;
                return true;
            }
            if (darkCount == 0)
            {
                Winner = PieceColor.Light;
                RecordWin(Winner);
                Phase  = GamePhase.GameOver;
                return true;
            }
            return false;
        }

        void RecordWin(PieceColor color)
        {
            if (color == PieceColor.Light) LightWins++;
            else DarkWins++;
        }

        // -----------------------------------------------------------------------
        // AI
        // -----------------------------------------------------------------------

        public void RequestAIMove()
        {
            AITurnPending = false;
            Phase        = GamePhase.AIThinking;
            _aiMoveReady = false;
            _pendingAIMove = null;

            Console.WriteLine($"[AI] RequestAIMove: color={Turn}, depth={AiDepth}");
            // DEBUG: log all legal moves available for AI
            var legalNow = MoveGenerator.GetAllMoves(Board, Turn, Rules);
            Console.WriteLine($"[AI] Legal moves count={legalNow.Count}");
            foreach (var lm in legalNow)
                Console.WriteLine($"[AI]   candidate: {Board.CellLabel(lm.From)}->{Board.CellLabel(lm.To)} caps={lm.Captures.Count}");

            var boardSnap = Board.Clone();
            var aiColor   = Turn;
            var rules     = Rules;
            int depth     = AiDepth;

#if WEB
            var result = CheckersAI.GetBestMove(boardSnap, aiColor, rules, depth);
            _pendingAIMove = result;
            _aiMoveReady   = true;
#else
            Task.Run(() =>
            {
                var result = CheckersAI.GetBestMove(boardSnap, aiColor, rules, depth);
                _pendingAIMove = result;
                _aiMoveReady   = true;
            });
#endif
        }

        public void PollAIResult()
        {
            if (Phase != GamePhase.AIThinking) return;
            if (!_aiMoveReady) return;

            var move = _pendingAIMove;
            _aiMoveReady   = false;
            _pendingAIMove = null;
            Phase          = GamePhase.PlayerTurn;   // restore before ApplyMove changes it

            if (move.HasValue)
            {
                Console.WriteLine($"[AI] PollAIResult: applying move {Board.CellLabel(move.Value.From)}->{Board.CellLabel(move.Value.To)} caps={move.Value.Captures.Count}");
                ApplyMove(move.Value);
            }
            else
            {
                Console.WriteLine($"[AI] PollAIResult: AI has no moves, human wins");
                // AI has no moves: human wins
                Winner = HumanColor();
                RecordWin(Winner);
                Phase = GamePhase.GameOver;
            }
        }

        // -----------------------------------------------------------------------
        // Undo
        // -----------------------------------------------------------------------

        public void Undo()
        {
            if (_history.Count == 0) return;
            if (Phase == GamePhase.AIThinking) return;

            // Pop twice if AI moved (undo AI move + human move)
            if (_history.Count >= 2)
            {
                _history.Pop();
                var (b, t, sp) = _history.Pop();
                Board         = b;
                Turn          = t;
                SelectedPiece = sp;
            }
            else
            {
                var (b, t, sp) = _history.Pop();
                Board         = b;
                Turn          = t;
                SelectedPiece = sp;
            }

            Phase    = GamePhase.PlayerTurn;
            Winner   = PieceColor.None;
            IsDraw   = false;
            LastMove = null;
            RefreshLegalMoves();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        public PieceColor HumanColor() => HumanIsLight ? PieceColor.Light : PieceColor.Dark;

        bool IsHumanTurn() => Turn == HumanColor();

        static PieceColor Opponent(PieceColor c) =>
            c == PieceColor.Light ? PieceColor.Dark : PieceColor.Light;

        void RefreshLegalMoves()
        {
            AllLegalMoves = MoveGenerator.GetAllMoves(Board, Turn, Rules);
        }

        void RefreshSelectedMoves()
        {
            SelectedMoves = AllLegalMoves.FindAll(m => m.From == SelectedPiece);
        }

        // All squares this piece can legally move TO (from any selected piece legal move)
        public HashSet<int> GetValidDestinations()
        {
            var set = new HashSet<int>();
            foreach (var m in SelectedMoves) set.Add(m.To);
            return set;
        }

        // All pieces that have at least one legal move
        public HashSet<int> GetMovablePieces()
        {
            var set = new HashSet<int>();
            foreach (var m in AllLegalMoves) set.Add(m.From);
            return set;
        }
    }
}

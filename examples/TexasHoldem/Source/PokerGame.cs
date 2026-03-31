// PokerGame.cs — Wraps TexasHoldemGame engine; runs game on background thread;
// exposes a thread-safe snapshot for the render thread.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TexasHoldem.Logic;
using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.GameMechanics;
using TexasHoldem.Logic.Players;
using TexasHoldem.AI.SmartPlayer;
using TexasHoldem.Logic.Helpers;

// ──────────────────────────────────────────────────────────────
// Game phase visible to the render thread
// ──────────────────────────────────────────────────────────────
public enum PokerPhase
{
    Idle,               // No game running
    WaitingForHuman,    // Human must act (game thread is blocked)
    AITurn,             // AI is executing turns
    HandOver,           // Between hands — short pause
    GameOver,           // Tournament finished
}

// ──────────────────────────────────────────────────────────────
// Per-player info in the render snapshot
// ──────────────────────────────────────────────────────────────
public class PlayerRenderInfo
{
    public string Name = "";
    public int Money;
    public bool IsHuman;
    public bool IsInHand;
    public bool IsCurrentTurn;
    public int CurrentRoundBet;
    public Card? Card1;   // Only set for human or at showdown
    public Card? Card2;
    public string LastAction = "";  // e.g. FOLD, CHECK, CALL $X, RAISE $X
    public string HandDescription = "";  // set during HandOver
    public bool IsHandWinner;            // set during HandOver
    public bool IsAllIn;                 // player has committed all chips but is still in hand
}

// ──────────────────────────────────────────────────────────────
// Showdown card info (what each player had at showdown)
// ──────────────────────────────────────────────────────────────
public class ShowdownInfo
{
    public string PlayerName = "";
    public Card? Card1;
    public Card? Card2;
    public string HandDescription = "";  // e.g. "Pair", "Full House"
    public bool IsWinner;
}

// ──────────────────────────────────────────────────────────────
// Immutable snapshot of game state for the render thread.
// Replaced atomically each update.
// ──────────────────────────────────────────────────────────────
public class RenderSnapshot
{
    public PokerPhase Phase = PokerPhase.Idle;
    public List<Card> CommunityCards = new();
    public PlayerRenderInfo[] Players = Array.Empty<PlayerRenderInfo>();
    public int HumanSeatIndex = 0;
    public int DealerSeatIndex = 0;
    public int Pot;
    public int HandNumber;
    public GameRoundType RoundType = GameRoundType.PreFlop;
    public string StatusMessage = "Press New Game to start";

    // Human available actions (only relevant when Phase == WaitingForHuman)
    public bool CanCheck;
    public bool CanFold = true;
    public int MoneyToCall;
    public bool CanRaise;
    public int MinRaise;
    public int MaxRaise;
    public int HumanMoneyLeft;

    // Showdown results
    public List<ShowdownInfo> Showdown = new();
    public string WinnerName = "";
    public int WinAmount;
    // Per-hand winner (shown during HandOver between hands)
    public string HandWinnerName = "";
    public int HandWinAmount;

    // The 5 actual cards that form the winning hand (community + hole cards), set during HandOver
    public List<Card> WinningCards = new();

    // For "last action" display
    public string LastActionDescription = "";

    // Which seat index performed the most recent action (-1 = none)
    public int LastActionSeatIdx = -1;

    // Active blind seats (-1 = unknown)
    public int SmallBlindSeatIdx = -1;
    public int BigBlindSeatIdx   = -1;
    public int SmallBlind        = 1;
}

// ──────────────────────────────────────────────────────────────
// HumanPlayer — IPlayer implementation that blocks on GetTurn()
// until the UI provides an action via PokerGame.Submit*()
// ──────────────────────────────────────────────────────────────
internal class HumanPlayer : BasePlayer
{
    public override string Name => "You";
    public override int BuyIn => -1; // use game default

    private readonly PokerGame _game;

    public HumanPlayer(PokerGame game)
    {
        _game = game;
    }

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        // Auto-post blinds for human (standard behavior)
        _game.OnBlindPosted(_game.HumanSeatIndex, context);
        return context.BlindAction;
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        return _game.BlockUntilHumanActs(context);
    }

#if WEB
    public override async Task<TexasHoldem.Logic.Players.PlayerAction> GetTurnAsync(IGetTurnContext context)
    {
        return await _game.BlockUntilHumanActsAsync(context);
    }
#endif

    public override void StartHand(IStartHandContext context)
    {
        base.StartHand(context);
        _game.OnHumanHandStart(context, FirstCard, SecondCard);
    }

    public override void StartRound(IStartRoundContext context)
    {
        base.StartRound(context);
        _game.OnRoundStart(context, CommunityCards);
    }

    public override void EndRound(IEndRoundContext context)
    {
        _game.OnRoundEnd(context);
    }

    public override void EndHand(IEndHandContext context)
    {
        _game.OnHandEnd(context);
    }

    public override void EndGame(IEndGameContext context)
    {
        _game.OnGameEnd(context);
    }
}

// ──────────────────────────────────────────────────────────────
// ObservingAIPlayer — wraps SmartPlayer; forwards state events
// to PokerGame so we can update the render snapshot
// ──────────────────────────────────────────────────────────────
internal class ObservingAIPlayer : BasePlayer
{
    private readonly SmartPlayer _ai;
    private readonly PokerGame _game;
    private readonly int _seatIndex;

    public readonly PlayerStyle Style;

    public ObservingAIPlayer(PokerGame game, int seatIndex, string name, PlayerStyle style = PlayerStyle.Balanced)
    {
        _game = game;
        _seatIndex = seatIndex;
        Style = style;
        // SmartPlayer uses a random GUID name; we override it via the wrapping approach
        _ai = new SmartPlayer(style);
        _forcedName = name;
    }

    private readonly string _forcedName;
    public override string Name => _forcedName;
    public override int BuyIn => -1;

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        _game.OnBlindPosted(_seatIndex, context);
        return context.BlindAction;
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        // Notify game we're in an AI turn
        _game.OnAITurnStart(_seatIndex, context);
        var action = _ai.GetTurn(context);
        _game.OnAITurnEnd(_seatIndex, action);
        return action;
    }

#if WEB
    public override async Task<TexasHoldem.Logic.Players.PlayerAction> GetTurnAsync(IGetTurnContext context)
    {
        _game.OnAITurnStart(_seatIndex, context);
        var action = _ai.GetTurn(context);
        await _game.OnAITurnEndAsync(_seatIndex, action);
        return action;
    }
#endif

    public override void StartHand(IStartHandContext context)
    {
        base.StartHand(context);
        _ai.StartHand(context);
        _game.OnAIHandStart(_seatIndex, context);
    }

    public override void StartRound(IStartRoundContext context)
    {
        base.StartRound(context);
        _ai.StartRound(context);
        // In simulation mode there is no HumanPlayer to drive OnRoundStart; seatIdx 0 does it once per round.
        if (_game.SimulationMode && _seatIndex == 0)
            _game.OnRoundStart(context, CommunityCards);
    }

    public override void EndRound(IEndRoundContext context)
    {
        _ai.EndRound(context);
        if (_game.SimulationMode && _seatIndex == 0)
            _game.OnRoundEnd(context);
    }

    public override void EndHand(IEndHandContext context)
    {
        _ai.EndHand(context);
        if (_game.SimulationMode && _seatIndex == 0)
            _game.OnHandEnd(context);
    }

    public override void EndGame(IEndGameContext context)
    {
        _ai.EndGame(context);
        if (_game.SimulationMode && _seatIndex == 0)
            _game.OnGameEnd(context);
    }
}

// ──────────────────────────────────────────────────────────────
// Main PokerGame class
// ──────────────────────────────────────────────────────────────
public class PokerGame
{
    // ── Public state ──────────────────────────────────────────
    // Atomically replaced; render thread reads this without locking.
    public volatile RenderSnapshot Snapshot = new();

    // Fires on the game thread when a new snapshot is ready.
    // The render thread doesn't use this (it just reads Snapshot each frame).
    public Action? OnSnapshotUpdated;

    // ── Configuration ─────────────────────────────────────────
    public int NumAIPlayers { get; private set; }
    public int InitialBuyIn { get; private set; }
    public int HumanSeatIndex => _humanSeatIdx;
    // Blind configuration — read by GameThreadEntry when starting a new game
    public int  InitialSmallBlindIndex { get; set; } = 0;
    public bool EscalateBlinds         { get; set; } = false;
    public int  BlindsHandPeriod       { get; set; } = 10;
    // When true: no human player — all seats are AI, hands advance automatically
    public bool SimulationMode         { get; set; } = false;
    // Max hands to play in simulation mode (0 = unlimited / run to tournament end)
    public int  SimulationHands        { get; set; } = 100;

    // ── Threading helpers ─────────────────────────────────────
#if !WEB
    private readonly SemaphoreSlim        _humanActionSignal = new(0, 1);
    private readonly ManualResetEventSlim _aiSleepInterrupt  = new(false); // set by StopGame to wake AI delay
    private readonly ManualResetEventSlim _continueSignal    = new(false); // set when human presses Continue
#else
    // WEB (single-threaded WASM): async/await based — no blocking spin loops.
    private TaskCompletionSource<PlayerAction>? _webHumanActionTcs;
    private TaskCompletionSource<bool>?         _webContinueTcs;
    private volatile bool                       _webAIInterruptReady;
    // Frame-pumped delay: resolved by TickWebDelay() called from Frame() each RAF tick.
    private TaskCompletionSource<bool>?         _webDelayTcs;
    private long                                _webDelayUntilTicks;
#endif
    private IGetTurnContext? _lastAITurnCtx; // saved in OnAITurnStart, used in OnAITurnEnd
    private PlayerAction? _pendingHumanAction;
    private readonly object _actionLock = new();

    // ── Internal game state (game thread) ─────────────────────
    private HumanPlayer? _humanPlayer;
    private ObservingAIPlayer[]? _aiPlayers;
    private string[] _playerNames = Array.Empty<string>();
    private int _humanSeatIdx;

    // State for building the snapshot (game thread writes, we publish atomically)
    private readonly object _snapshotLock = new();
    private Card? _humanCard1, _humanCard2;
    private List<Card> _communityCards = new();
    private int[] _playerMoney = Array.Empty<int>();
    private bool[] _playerInHand = Array.Empty<bool>();
    private int[] _playerRoundBets = Array.Empty<int>();
    private int _pot;
    private int _handNumber;
    private int _lastInitializedHand = -1;  // tracks which hand has already been reset in sim mode
    private GameRoundType _roundType;
    private int _dealerIdx;
    private int _smallBlindSeatIdx = -1;
    private int _bigBlindSeatIdx   = -1;
    private int _smallBlind        = 1;
    private List<ShowdownInfo> _showdown = new();
    private string _winnerName = "";
    private int _winAmount;
    private string _lastAction = "";
    private string[] _playerLastActions = Array.Empty<string>();
    private string _handWinnerName = "";
    private int _handWinAmount;
    private List<Card> _winningCards = new();  // 5 cards forming the winning hand
    private int _lastActionSeatIdx = -1;
    private int[] _moneyAtHandStart = Array.Empty<int>();    private readonly List<string> _pendingHandLogs = new();  // buffered during StartHand calls
    // Cancellation for stopping the game thread
    private volatile bool _stopRequested;
#if !WEB
    private Thread? _gameThread;
#else
    private Task? _gameTask;
#endif
    // Per-game generation counter: incremented each StartGame() to invalidate stale tasks/threads.
    private volatile int _gameId = 0;
#if !WEB
    // Desktop: [ThreadStatic] so each thread captures its own generation at start.
    [ThreadStatic] private static int _threadGameId;
#else
    // WEB: single-threaded; generation captured as an instance field at task start.
    private int _webCurrentGameId;
#endif

    // Configurable delay between AI actions (written from UI thread, read from game thread)
    public volatile int ActionDelayMs = 1000;

    // ── Public API ────────────────────────────────────────────

    public void StartGame(int numAI, int buyIn)
    {
        StopGame();

        NumAIPlayers = Math.Clamp(numAI, 1, 9);
        InitialBuyIn = Math.Max(buyIn, 100);

        _gameId++;   // invalidate any stale threads still running
        _stopRequested = false;
        // Drain any leftover human action from the previous game so the new game
        // doesn't immediately fold the human player on their first turn.
        lock (_actionLock)
        {
            _pendingHumanAction = null;
            _pending_action_set = false;
        }
#if !WEB
        while (_humanActionSignal.Wait(0)) { }  // drain leftover semaphore count
        _aiSleepInterrupt.Reset();               // re-arm for new game
        _continueSignal.Reset();                 // re-arm continue signal

        _gameThread = new Thread(GameThreadEntry)
        {
            Name = "PokerGameThread",
            IsBackground = true,
        };
        _gameThread.Start();
#else
        // WEB: async/await approach — game runs as a true async Task (no spin loops).
        _webHumanActionTcs   = null;
        _webContinueTcs      = null;
        _webAIInterruptReady = false;
        _gameTask = GameTaskEntryAsync();
#endif
    }

    public void StopGame()
    {
        _stopRequested = true;
#if !WEB
        _aiSleepInterrupt.Set();   // wake any in-progress AI action delay
        _continueSignal.Set();     // unblock any HandOver wait
#else
        _webAIInterruptReady = true;
        _webContinueTcs?.TrySetResult(false);
        _webHumanActionTcs?.TrySetResult(PlayerAction.Fold());
#endif

        // Unblock the human player if it's waiting
        SubmitFold();

#if !WEB
        _gameThread?.Join(500);
        _gameThread = null;
#else
        _gameTask = null;  // task self-terminates via _stopRequested / gameId checks
#endif

        // Reset snapshot
        Snapshot = new RenderSnapshot
        {
            Phase = PokerPhase.Idle,
            StatusMessage = "Press New Game to start",
        };
    }

    // Signal-only stop for use during app shutdown — does NOT join the thread
    // (the background thread will be killed when the process exits).
    public void StopGameFast()
    {
        _stopRequested = true;
#if !WEB
        _aiSleepInterrupt.Set();
        _continueSignal.Set();  // unblock HandOver wait
#else
        _webAIInterruptReady = true;
        _webContinueTcs?.TrySetResult(false);
        _webHumanActionTcs?.TrySetResult(PlayerAction.Fold());
#endif
        SubmitFold();           // unblock human player if waiting
    }

    public void SubmitContinue()
    {
#if !WEB
        _continueSignal.Set();
#else
        _webContinueTcs?.TrySetResult(true);
#endif
    }

    public void SubmitFold()
    {
        SetHumanAction(PlayerAction.Fold());
    }

    public void SubmitCall()
    {
        SetHumanAction(PlayerAction.CheckOrCall());
    }

    public void SubmitRaise(int amount)
    {
        SetHumanAction(PlayerAction.Raise(amount));
    }

    // ── Called by HumanPlayer on the game thread ──────────────

    internal PlayerAction BlockUntilHumanActs(IGetTurnContext context)
    {
#if !WEB
        if (_stopRequested || _threadGameId != _gameId)
#else
        if (_stopRequested || _webCurrentGameId != _gameId)
#endif
            return PlayerAction.Fold();

        // Update snapshot with human action context
        lock (_snapshotLock)
        {
            _roundType = context.RoundType;
            _pot = context.CurrentPot;
        }
        PublishSnapshot(PokerPhase.WaitingForHuman, context);

        // Block until UI provides action
#if !WEB
        while (!_stopRequested && _threadGameId == _gameId)
        {
            bool signaled = _humanActionSignal.Wait(100); // 100ms timeout for stop-check
            if (signaled || _pending_action_set)
                break;
        }
#else
        // WEB: this code path is only used from the synchronous GetTurn() — not called on WEB.
        // (WEB uses GetTurnAsync / BlockUntilHumanActsAsync instead.)
        // Fall through immediately; the action should already be set via the async path.
#endif

#if !WEB
        if (_stopRequested || _threadGameId != _gameId)
#else
        if (_stopRequested || _webCurrentGameId != _gameId)
#endif
            return PlayerAction.Fold();

        PlayerAction? action;
        lock (_actionLock)
        {
            action = _pendingHumanAction;
            _pendingHumanAction = null;
            _pending_action_set = false;
        }
        var result = action ?? PlayerAction.Fold();
        lock (_snapshotLock)
        {
            if (_humanSeatIdx < _playerLastActions.Length)
            {
                _playerLastActions[_humanSeatIdx] = FormatActionLabel(result);
                if (result.Type == PlayerActionType.Fold)
                    _playerInHand[_humanSeatIdx] = false;
            }
            _lastActionSeatIdx = _humanSeatIdx;
        }
        Console.WriteLine($"  [You] {FormatActionLabel(result)}");
        return result;
    }

    internal void OnHumanHandStart(IStartHandContext context, Card c1, Card c2)
    {
        ThrowIfStopped();
        lock (_snapshotLock)
        {
            _humanCard1 = c1;
            _humanCard2 = c2;
            _showdown.Clear();
            _winnerName = "";
            _winAmount = 0;
            _lastAction = "";
            _handNumber = context.HandNumber;
            _dealerIdx  = 0;  // will be overridden below after lock block
            _handWinnerName = "";
            _handWinAmount = 0;
            _winningCards = new List<Card>();
            // Update human's money from context (reflects previous hand result)
            _playerMoney[_humanSeatIdx] = context.MoneyLeft;
            _moneyAtHandStart[_humanSeatIdx] = context.MoneyLeft;
            // Reset hand state for all players
            for (int i = 0; i < _playerInHand.Length; i++) _playerInHand[i] = true;
            for (int i = 0; i < _playerLastActions.Length; i++) _playerLastActions[i] = "";
            // Determine dealer/SB/BB seat indices from engine context
            int dealerSeat = Array.IndexOf(_playerNames, context.FirstPlayerName);
            _dealerIdx = dealerSeat >= 0 ? dealerSeat : 0;
            int n = _playerNames.Length;
            _smallBlindSeatIdx = n == 2 ? _dealerIdx : (_dealerIdx + 1) % n;
            _bigBlindSeatIdx   = n == 2 ? (_dealerIdx + 1) % n : (_dealerIdx + 2) % n;
            _smallBlind        = context.SmallBlind;
        }
        lock (_snapshotLock)
        {
            _pendingHandLogs.Clear();  // clear any leftovers from a broken hand
            _pendingHandLogs.Add($"--- HAND #{context.HandNumber} --- (BB: ${context.SmallBlind * 2})");
            // _pendingHandLogs.Add($"  [You] {c1} {c2} | stack: ${context.MoneyLeft}");
        }
        PublishSnapshot(PokerPhase.AITurn, null);
    }

    internal void OnRoundStart(IStartRoundContext context, IReadOnlyCollection<Card> community)
    {
        ThrowIfStopped();
        lock (_snapshotLock)
        {
            _communityCards = community.ToList();
            _roundType = context.RoundType;
            _pot = context.CurrentPot;
        }
        // Flush deal logs once per hand at PreFlop start (after all StartHand callbacks complete)
        if (context.RoundType == GameRoundType.PreFlop)
        {
            List<string> lines;
            lock (_snapshotLock) { lines = new List<string>(_pendingHandLogs); _pendingHandLogs.Clear(); }
            foreach (var line in lines) Console.WriteLine(line);
        }
        var communityStr = string.Join(" ", community.Select(c => $"{c}"));
        Console.WriteLine($"  [ROUND: {context.RoundType}] Community: [{communityStr}] | Pot: ${context.CurrentPot}");
        PublishSnapshot(PokerPhase.AITurn, null);
    }

    internal void OnRoundEnd(IEndRoundContext context)
    {
        // Summarize actions for display
        var actions = context.RoundActions;
        if (actions.Count > 0)
        {
            var last = actions.Last();
            lock (_snapshotLock)
            {
                _lastAction = $"{last.PlayerName}: {last.Action}";
            }
        }
    }

    internal void OnHandEnd(IEndHandContext context)
    {
        var sdList = new List<ShowdownInfo>();
        string handWinner = "";
        int handWinAmt = 0;
        var evaluator = new HandEvaluator();

        if (context.ShowdownCards.Count > 0)
        {
            // Evaluate each player's hand and find the best
            BestHand? bestHand = null;
            string bestPlayerName = "";

            foreach (var kv in context.ShowdownCards)
            {
                var holeCards = kv.Value.ToArray();
                IEnumerable<Card> allCards = holeCards.Concat(_communityCards);
                BestHand best;
                try { best = evaluator.GetBestHand(allCards); }
                catch { best = new BestHand(HandRankType.HighCard, holeCards.Take(5).Select(c => c.Type).ToList()); }

                sdList.Add(new ShowdownInfo
                {
                    PlayerName = kv.Key,
                    Card1 = holeCards.Length > 0 ? holeCards[0] : null,
                    Card2 = holeCards.Length > 1 ? holeCards[1] : null,
                    HandDescription = FormatHandRank(best.RankType),
                    IsWinner = false,
                });

                if (bestHand == null || best.CompareTo(bestHand) > 0)
                {
                    bestHand = best;
                    bestPlayerName = kv.Key;
                }
            }

            // Mark winner
            var winnerSd = sdList.FirstOrDefault(s => s.PlayerName == bestPlayerName);
            if (winnerSd != null) winnerSd.IsWinner = true;
            handWinner = bestPlayerName;

            // Reconstruct the 5 actual winning cards (with suit) matching the BestHand card types
            if (bestHand != null && context.ShowdownCards.ContainsKey(bestPlayerName))
            {
                var pool = context.ShowdownCards[bestPlayerName].Concat(_communityCards).ToList();
                var winList = new List<Card>();
                foreach (var ct in bestHand.Cards)
                {
                    var match = pool.FirstOrDefault(c => c.Type == ct);
                    if (match != null) { winList.Add(match); pool.Remove(match); }
                }
                lock (_snapshotLock) { _winningCards = winList; }
            }
        }
        else
        {
            // No showdown — all others folded; find the last player in hand
            for (int i = 0; i < _playerInHand.Length; i++)
            {
                if (_playerInHand[i]) { handWinner = _playerNames[i]; break; }
            }
        }

        // Determine pot won by comparing money vs hand start
        // (money is updated before EndHand is called by the engine)
        handWinAmt = _pot; // approximate; actual distribution is already done

        // Log results
        Console.WriteLine($"=== HAND #{_handNumber} ENDED ===");
        if (sdList.Count > 0)
        {
            foreach (var sd in sdList)
                Console.WriteLine($"  SHOWDOWN: {sd.PlayerName} [{sd.Card1} {sd.Card2}] → {sd.HandDescription}{(sd.IsWinner ? " *** WINS ***" : "")}");
        }
        else
        {
            Console.WriteLine($"  No showdown — everyone folded except {handWinner}");
        }
        Console.WriteLine($"  Winner: {handWinner} | Pot: ~${handWinAmt}");
        Console.WriteLine();

        lock (_snapshotLock)
        {
            _showdown = sdList;
            _handWinnerName = handWinner;
            _handWinAmount = handWinAmt;
        }
#if !WEB
        _continueSignal.Reset();
#endif
        PublishSnapshot(PokerPhase.HandOver, null);

        // In simulation mode auto-advance; stop early if hand limit reached.
        if (SimulationMode)
        {
            if (SimulationHands > 0 && _handNumber >= SimulationHands)
            {
                Console.WriteLine($"=== SIMULATION COMPLETE: {_handNumber} hands played ===");
                _stopRequested = true;
                throw new GameStoppedException();
            }
            return;
        }

        // Wait until player presses Continue (or game is stopped)
#if !WEB
        while (!_stopRequested && _threadGameId == _gameId)
            if (_continueSignal.Wait(100)) break;
#endif
        // WEB path: waiting is handled by OnHandEndAsync — called from the async game path.
    }

    internal void OnGameEnd(IEndGameContext context)
    {
        lock (_snapshotLock)
        {
            _winnerName = context.WinnerName;
        }
        Console.WriteLine($"=== TOURNAMENT OVER: {context.WinnerName} WINS! ===");
        PublishSnapshot(PokerPhase.GameOver, null);
    }

    internal void OnBlindPosted(int seatIdx, IPostingBlindContext context)
    {
        ThrowIfStopped();
        lock (_snapshotLock)
        {
            _pot = context.CurrentPot + context.BlindAction.Money;
            if (seatIdx < _playerMoney.Length)
            {
                _playerMoney[seatIdx] = context.CurrentStackSize;
                _playerRoundBets[seatIdx] = context.BlindAction.Money;
            }
        }
        PublishSnapshot(PokerPhase.AITurn, null);
    }

    internal void OnAITurnStart(int seatIdx, IGetTurnContext context)
    {
        ThrowIfStopped();
        _lastAITurnCtx = context;
        lock (_snapshotLock)
        {
            _roundType = context.RoundType;
            _pot = context.CurrentPot;
            // Update AI player's displayed stack from engine context
            if (seatIdx < _playerMoney.Length)
                _playerMoney[seatIdx] = context.MoneyLeft;
        }
        PublishSnapshot(PokerPhase.AITurn, null);
    }

    internal void OnAITurnEnd(int seatIdx, PlayerAction action)
    {
        var ctx = _lastAITurnCtx;
        // The raw action from the AI has Money=0 for CheckOrCall; use MoneyToCall from context.
        string label;
        if (action.Type == PlayerActionType.CheckCall && action.Money == 0 && ctx != null && ctx.MoneyToCall > 0)
            label = $"CALL ${ctx.MoneyToCall}";
        else
            label = FormatActionLabel(action);

        lock (_snapshotLock)
        {
            _lastAction = $"{_playerNames[seatIdx]}: {label}";
            _lastActionSeatIdx = seatIdx;
            if (seatIdx < _playerLastActions.Length)
            {
                _playerLastActions[seatIdx] = label;
                if (action.Type == PlayerActionType.Fold)
                    _playerInHand[seatIdx] = false;
            }
            // Update AI player's stack after their action
            if (seatIdx < _playerMoney.Length && ctx != null)
            {
                int spent = action.Type == PlayerActionType.CheckCall ? ctx.MoneyToCall : action.Money;
                _playerMoney[seatIdx] = Math.Max(0, ctx.MoneyLeft - spent);
            }
        }
        Console.WriteLine($"  [{_playerNames[seatIdx]}] {label}");
        // Publish action immediately so render thread can show it during the delay
        PublishSnapshot(PokerPhase.AITurn, null);
        // Only delay if this is still the current game (stale tasks/threads skip the wait)
#if !WEB
        if (!_stopRequested && _threadGameId == _gameId)
            _aiSleepInterrupt.Wait(Math.Max(50, ActionDelayMs));
#endif
        // WEB path: delay is handled by OnAITurnEndAsync — called from the async game path.
    }

    internal void OnAIHandStart(int seatIdx, IStartHandContext context)
    {
        ThrowIfStopped();
        lock (_snapshotLock)
        {
            if (seatIdx < _playerMoney.Length)
            {
                _playerMoney[seatIdx] = context.MoneyLeft;
                _moneyAtHandStart[seatIdx] = context.MoneyLeft;
            }
        }
        // In simulation mode the FIRST player to fire StartHand for a new hand number does the full
        // hand reset.  We detect this via HandNumber, not seatIdx, because the engine rotates its
        // player list each hand so seatIdx==0 is NOT necessarily called first.
        if (SimulationMode && context.HandNumber != _lastInitializedHand)
        {
            lock (_snapshotLock)
            {
                _lastInitializedHand = context.HandNumber;
                _showdown.Clear();
                _lastAction = "";
                _handNumber = context.HandNumber;
                _handWinnerName = "";
                _handWinAmount = 0;
                _winningCards = new List<Card>();
                for (int i = 0; i < _playerInHand.Length;     i++) _playerInHand[i]     = true;
                for (int i = 0; i < _playerLastActions.Length; i++) _playerLastActions[i] = "";

                int dealerSeat = Array.IndexOf(_playerNames, context.FirstPlayerName);
                _dealerIdx = dealerSeat >= 0 ? dealerSeat : 0;
                int n = _playerNames.Length;
                _smallBlindSeatIdx = n == 2 ? _dealerIdx : (_dealerIdx + 1) % n;
                _bigBlindSeatIdx   = n == 2 ? (_dealerIdx + 1) % n : (_dealerIdx + 2) % n;
                _smallBlind        = context.SmallBlind;
                _pendingHandLogs.Clear();
                _pendingHandLogs.Add($"--- HAND #{context.HandNumber} --- (BB: ${context.SmallBlind * 2})");
            }
        }
        // Log this player's hole cards (always after the header)
        lock (_snapshotLock) { _pendingHandLogs.Add($"  [{_playerNames[seatIdx]}] {context.FirstCard} {context.SecondCard} | stack: ${context.MoneyLeft}"); }
    }

    // ── Private helpers ───────────────────────────────────────

    private volatile bool _pending_action_set;

    private void SetHumanAction(PlayerAction action)
    {
        lock (_actionLock)
        {
            _pendingHumanAction = action;
            _pending_action_set = true;
        }
#if !WEB
        // Release the semaphore (allow one signal; don't double-release)
        if (_humanActionSignal.CurrentCount == 0)
            _humanActionSignal.Release();
#else
        _webHumanActionTcs?.TrySetResult(action);
#endif
    }

    private static string FormatHandRank(HandRankType rank) => rank switch
    {
        HandRankType.HighCard      => "High Card",
        HandRankType.Pair          => "Pair",
        HandRankType.TwoPairs      => "Two Pairs",
        HandRankType.ThreeOfAKind  => "Three of a Kind",
        HandRankType.Straight      => "Straight",
        HandRankType.Flush         => "Flush",
        HandRankType.FullHouse     => "Full House",
        HandRankType.FourOfAKind   => "Four of a Kind",
        HandRankType.StraightFlush => "Straight Flush",
        _                          => rank.ToString(),
    };

    private static string FormatActionLabel(PlayerAction action) => action.Type switch
    {
        PlayerActionType.Fold      => "FOLD",
        PlayerActionType.CheckCall => action.Money > 0 ? $"CALL ${action.Money}" : "CHECK",
        PlayerActionType.Raise     => $"RAISE ${action.Money}",
        _                          => action.ToString() ?? "",
    };

    // Thrown from game-thread callbacks when stop is requested, causing game.Start() to unwind immediately.
    private sealed class GameStoppedException : Exception { }

    private void ThrowIfStopped()
    {
#if !WEB
        if (_stopRequested || _threadGameId != _gameId)
#else
        if (_stopRequested || _webCurrentGameId != _gameId)
#endif
            throw new GameStoppedException();
    }

#if WEB
    // ── WEB async equivalents for blocking operations ────────────────────────

    /// <summary>Async version of BlockUntilHumanActs — awaits TCS instead of spinning.</summary>
    internal async Task<PlayerAction> BlockUntilHumanActsAsync(IGetTurnContext context)
    {
        if (_stopRequested || _webCurrentGameId != _gameId)
            return PlayerAction.Fold();

        lock (_snapshotLock)
        {
            _roundType = context.RoundType;
            _pot = context.CurrentPot;
        }
        PublishSnapshot(PokerPhase.WaitingForHuman, context);

        // Reset any leftover pending action
        lock (_actionLock)
        {
            if (_pending_action_set)
            {
                var stored = _pendingHumanAction ?? PlayerAction.Fold();
                _pendingHumanAction = null;
                _pending_action_set = false;
                return stored;
            }
        }

        // Create a new TCS and await it
        _webHumanActionTcs = new TaskCompletionSource<PlayerAction>(TaskCreationOptions.None);
        var action = await _webHumanActionTcs.Task;
        _webHumanActionTcs = null;

        if (_stopRequested || _webCurrentGameId != _gameId)
            return PlayerAction.Fold();

        // Clear any synchronous pending action (shouldn't be set, but defensive)
        lock (_actionLock)
        {
            _pendingHumanAction = null;
            _pending_action_set = false;
        }

        lock (_snapshotLock)
        {
            if (_humanSeatIdx < _playerLastActions.Length)
            {
                _playerLastActions[_humanSeatIdx] = FormatActionLabel(action);
                if (action.Type == PlayerActionType.Fold)
                    _playerInHand[_humanSeatIdx] = false;
            }
            _lastActionSeatIdx = _humanSeatIdx;
        }
        Console.WriteLine($"  [You] {FormatActionLabel(action)}");
        return action;
    }

    /// <summary>Async version of OnAITurnEnd — awaits Task.Delay for the action display period.</summary>
    internal async Task OnAITurnEndAsync(int seatIdx, PlayerAction action)
    {
        var ctx = _lastAITurnCtx;
        string label;
        if (action.Type == PlayerActionType.CheckCall && action.Money == 0 && ctx != null && ctx.MoneyToCall > 0)
            label = $"CALL ${ctx.MoneyToCall}";
        else
            label = FormatActionLabel(action);

        lock (_snapshotLock)
        {
            _lastAction = $"{_playerNames[seatIdx]}: {label}";
            _lastActionSeatIdx = seatIdx;
            if (seatIdx < _playerLastActions.Length)
            {
                _playerLastActions[seatIdx] = label;
                if (action.Type == PlayerActionType.Fold)
                    _playerInHand[seatIdx] = false;
            }
            if (seatIdx < _playerMoney.Length && ctx != null)
            {
                int spent = action.Type == PlayerActionType.CheckCall ? ctx.MoneyToCall : action.Money;
                _playerMoney[seatIdx] = Math.Max(0, ctx.MoneyLeft - spent);
            }
        }
        Console.WriteLine($"  [{_playerNames[seatIdx]}] {label}");
        PublishSnapshot(PokerPhase.AITurn, null);

        if (!_stopRequested && _webCurrentGameId == _gameId)
        {
            int delay = Math.Max(50, ActionDelayMs);
            _webDelayUntilTicks = DateTime.UtcNow.AddMilliseconds(delay).Ticks;
            _webDelayTcs = new TaskCompletionSource<bool>(TaskCreationOptions.None);
            await _webDelayTcs.Task;
            _webDelayTcs = null;
        }
    }

    /// <summary>Called every frame from the render loop to pump the AI action delay TCS.</summary>
    public void TickWebDelay()
    {
        if (_webDelayTcs != null && DateTime.UtcNow.Ticks >= _webDelayUntilTicks)
        {
            var tcs = _webDelayTcs;
            _webDelayTcs = null;
            tcs.TrySetResult(true);
        }
    }

    /// <summary>Async HandEnd wait — awaits TCS for Continue press.</summary>
    internal async Task OnHandEndAsync()
    {
        _webContinueTcs = new TaskCompletionSource<bool>(TaskCreationOptions.None);
        await _webContinueTcs.Task;
        _webContinueTcs = null;
    }

    /// <summary>Async entry point for the game — replaces GameThreadEntry on WEB.</summary>
    private async Task GameTaskEntryAsync()
    {
        _webCurrentGameId = _gameId;

        try
        {
            if (SimulationMode)
            {
                int totalPlayers = Math.Max(NumAIPlayers, 2);
                _humanSeatIdx = -1;

                _aiPlayers = new ObservingAIPlayer[totalPlayers];
                _playerNames = new string[totalPlayers];
                _playerMoney = new int[totalPlayers];
                _playerInHand = new bool[totalPlayers];
                _playerRoundBets = new int[totalPlayers];
                _playerLastActions = new string[totalPlayers];
                _moneyAtHandStart = new int[totalPlayers];

                PlayerStyle[] simStyles = { PlayerStyle.Tight, PlayerStyle.Balanced, PlayerStyle.Aggressive, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Tight, PlayerStyle.Aggressive, PlayerStyle.Balanced, PlayerStyle.LAG, PlayerStyle.Tight };
                for (int i = 0; i < totalPlayers; i++)
                {
                    string name = $"AI-{i + 1}";
                    _aiPlayers[i] = new ObservingAIPlayer(this, i, name, simStyles[i % simStyles.Length]);
                    _playerNames[i] = name;
                    _playerMoney[i] = InitialBuyIn;
                    _playerLastActions[i] = "";
                }

                Console.WriteLine("=== SIMULATION START ===");
                for (int i = 0; i < totalPlayers; i++)
                    Console.WriteLine($"  {_playerNames[i]}: {_aiPlayers[i].Style}");
                Console.WriteLine();

                var simPlayers = new System.Collections.Generic.List<TexasHoldem.Logic.Players.IPlayer>(_aiPlayers);
                var simGame = new TexasHoldem.Logic.GameMechanics.TexasHoldemGame(simPlayers, InitialBuyIn)
                {
                    InitialSmallBlindIndex = InitialSmallBlindIndex,
                    EscalateBlinds         = EscalateBlinds,
                    BlindsHandPeriod       = BlindsHandPeriod,
                };
                await simGame.StartAsync(null);
            }
            else
            {
                int totalPlayers = NumAIPlayers + 1;
                _humanSeatIdx = 0;

                _humanPlayer = new HumanPlayer(this);
                _aiPlayers = new ObservingAIPlayer[NumAIPlayers];
                _playerNames = new string[totalPlayers];
                _playerMoney = new int[totalPlayers];
                _playerInHand = new bool[totalPlayers];
                _playerRoundBets = new int[totalPlayers];
                _playerLastActions = new string[totalPlayers];
                _moneyAtHandStart = new int[totalPlayers];

                _playerNames[0] = "You";
                PlayerStyle[] aiStyles = { PlayerStyle.Aggressive, PlayerStyle.Tight, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Aggressive, PlayerStyle.Tight, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Balanced };
                for (int i = 0; i < NumAIPlayers; i++)
                {
                    string name = $"AI-{i + 1}";
                    _aiPlayers[i] = new ObservingAIPlayer(this, i + 1, name, aiStyles[i % aiStyles.Length]);
                    _playerNames[i + 1] = name;
                }

                for (int i = 0; i < totalPlayers; i++)
                    _playerMoney[i] = InitialBuyIn;

                var players = new System.Collections.Generic.List<TexasHoldem.Logic.Players.IPlayer>(totalPlayers) { _humanPlayer };
                players.AddRange(_aiPlayers);

                var game = new TexasHoldem.Logic.GameMechanics.TexasHoldemGame(players, InitialBuyIn)
                {
                    InitialSmallBlindIndex = InitialSmallBlindIndex,
                    EscalateBlinds         = EscalateBlinds,
                    BlindsHandPeriod       = BlindsHandPeriod,
                };
                await game.StartAsync(this.OnHandEndAsync);
            }

            if (!_stopRequested)
                PublishSnapshot(PokerPhase.GameOver, null);
        }
        catch (GameStoppedException)
        {
            // Expected when StopGame() is called mid-game.
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PokerGame async error: {ex}");
        }
    }
#endif

    private void GameThreadEntry()
    {
        // Capture this task/thread's game generation at start.
        // If StartGame() is called again, _gameId is incremented so stale tasks/threads bail out fast.
#if !WEB
        _threadGameId = _gameId;
#else
        _webCurrentGameId = _gameId;
#endif
        try
        {
            if (SimulationMode)
            {
                // ── Simulation: every seat is an AI player ──────────────
                int totalPlayers = Math.Max(NumAIPlayers, 2);
                _humanSeatIdx = -1;   // no human seat

                _aiPlayers = new ObservingAIPlayer[totalPlayers];
                _playerNames = new string[totalPlayers];
                _playerMoney = new int[totalPlayers];
                _playerInHand = new bool[totalPlayers];
                _playerRoundBets = new int[totalPlayers];
                _playerLastActions = new string[totalPlayers];
                _moneyAtHandStart = new int[totalPlayers];

                PlayerStyle[] simStyles = { PlayerStyle.Tight, PlayerStyle.Balanced, PlayerStyle.Aggressive, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Tight, PlayerStyle.Aggressive, PlayerStyle.Balanced, PlayerStyle.LAG, PlayerStyle.Tight };
                for (int i = 0; i < totalPlayers; i++)
                {
                    string name = $"AI-{i + 1}";
                    _aiPlayers[i] = new ObservingAIPlayer(this, i, name, simStyles[i % simStyles.Length]);
                    _playerNames[i] = name;
                    _playerMoney[i] = InitialBuyIn;
                    _playerLastActions[i] = "";
                }

                Console.WriteLine("=== SIMULATION START ===");
                for (int i = 0; i < totalPlayers; i++)
                    Console.WriteLine($"  {_playerNames[i]}: {_aiPlayers[i].Style}");
                Console.WriteLine();

                var simPlayers = new List<IPlayer>(_aiPlayers);
                var simGame = new TexasHoldemGame(simPlayers, InitialBuyIn)
                {
                    InitialSmallBlindIndex = InitialSmallBlindIndex,
                    EscalateBlinds         = EscalateBlinds,
                    BlindsHandPeriod       = BlindsHandPeriod,
                };
                simGame.Start();
            }
            else
            {
                // ── Normal: human + AI players ────────────────────────────
                int totalPlayers = NumAIPlayers + 1;
                _humanSeatIdx = 0;   // Human is always seat 0 for rendering

                // Build player list: human first, then AI
                _humanPlayer = new HumanPlayer(this);
            _aiPlayers = new ObservingAIPlayer[NumAIPlayers];
            _playerNames = new string[totalPlayers];
            _playerMoney = new int[totalPlayers];
            _playerInHand = new bool[totalPlayers];
            _playerRoundBets = new int[totalPlayers];
            _playerLastActions = new string[totalPlayers];
            _moneyAtHandStart = new int[totalPlayers];

            _playerNames[0] = "You";
            PlayerStyle[] aiStyles = { PlayerStyle.Aggressive, PlayerStyle.Tight, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Aggressive, PlayerStyle.Tight, PlayerStyle.LAG, PlayerStyle.Balanced, PlayerStyle.Balanced };
            for (int i = 0; i < NumAIPlayers; i++)
            {
                string name = $"AI-{i + 1}";
                _aiPlayers[i] = new ObservingAIPlayer(this, i + 1, name, aiStyles[i % aiStyles.Length]);
                _playerNames[i + 1] = name;
            }

            for (int i = 0; i < totalPlayers; i++)
                _playerMoney[i] = InitialBuyIn;

                // Build player list for engine (engine wants list<IPlayer>)
                var players = new List<IPlayer>(totalPlayers) { _humanPlayer };
                players.AddRange(_aiPlayers);

                // Run game — this blocks the game thread for the entire tournament
                var game = new TexasHoldemGame(players, InitialBuyIn)
                {
                    InitialSmallBlindIndex = InitialSmallBlindIndex,
                    EscalateBlinds         = EscalateBlinds,
                    BlindsHandPeriod       = BlindsHandPeriod,
                };
                game.Start();
            } // end normal mode

            if (!_stopRequested)
            {
                PublishSnapshot(PokerPhase.GameOver, null);
            }
        }
        catch (GameStoppedException)
        {
            // Expected when StopGame() is called mid-game; exit cleanly.
        }
        catch (Exception ex)
        {
            // Log to console; don't crash
            Console.Error.WriteLine($"PokerGame thread error: {ex}");
        }
    }

    private void PublishSnapshot(PokerPhase phase, IGetTurnContext? turnCtx)
    {
        if (_stopRequested && phase != PokerPhase.Idle) return;
#if !WEB
        if (_threadGameId != _gameId) return;  // stale thread — don't overwrite new game's snapshot
#else
        if (_webCurrentGameId != _gameId) return;  // stale task — don't overwrite new game's snapshot
#endif

        var snap = new RenderSnapshot();
        snap.Phase = phase;
        snap.HandNumber = _handNumber;

        lock (_snapshotLock)
        {
            snap.CommunityCards = new List<Card>(_communityCards);
            snap.Pot = _pot;
            snap.RoundType = _roundType;
            snap.HumanSeatIndex = _humanSeatIdx;
            snap.DealerSeatIndex = _dealerIdx;
            snap.LastActionDescription = _lastAction;
            snap.Showdown = new List<ShowdownInfo>(_showdown);
            snap.WinnerName = _winnerName;
            snap.WinAmount = _winAmount;
            snap.HandWinnerName = _handWinnerName;
            snap.HandWinAmount = _handWinAmount;
            snap.WinningCards = new List<Card>(_winningCards);
            snap.LastActionSeatIdx = _lastActionSeatIdx;
            snap.SmallBlindSeatIdx = _smallBlindSeatIdx;
            snap.BigBlindSeatIdx   = _bigBlindSeatIdx;
            snap.SmallBlind        = _smallBlind;
        }

        // Build players array
        int n = _playerNames.Length;
        snap.Players = new PlayerRenderInfo[n];
        for (int i = 0; i < n; i++)
        {
            var p = new PlayerRenderInfo
            {
                Name = _playerNames[i],
                Money = _playerMoney[i],
                IsHuman = (i == _humanSeatIdx),
                IsInHand = _playerInHand[i],
                CurrentRoundBet = _playerRoundBets[i],
                IsCurrentTurn = false,
                LastAction = i < _playerLastActions.Length ? (_playerLastActions[i] ?? "") : "",
                IsAllIn = _playerMoney[i] == 0 && _playerInHand[i],
            };

            if (i == _humanSeatIdx)
            {
                lock (_snapshotLock)
                {
                    p.Card1 = _humanCard1;
                    p.Card2 = _humanCard2;
                }
                // During showdown, check if human is listed
                var humanSd = snap.Showdown.FirstOrDefault(x => x.PlayerName == _playerNames[i]);
                if (humanSd != null)
                {
                    p.HandDescription = humanSd.HandDescription;
                    p.IsHandWinner = humanSd.IsWinner;
                }
                else if (snap.Showdown.Count == 0 && snap.HandWinnerName == _playerNames[i])
                {
                    p.IsHandWinner = true; // won by everyone else folding
                }
            }
            else
            {
                // Show AI cards only at showdown
                if (snap.Showdown.Count > 0)
                {
                    var sd = snap.Showdown.FirstOrDefault(x => x.PlayerName == _playerNames[i]);
                    if (sd != null)
                    {
                        p.Card1 = sd.Card1;
                        p.Card2 = sd.Card2;
                        p.HandDescription = sd.HandDescription;
                        p.IsHandWinner = sd.IsWinner;
                    }
                }
                else if (snap.HandWinnerName == _playerNames[i])
                {
                    p.IsHandWinner = true; // won by everyone else folding
                }
            }

            snap.Players[i] = p;
        }

        // Human turn context
        if (phase == PokerPhase.WaitingForHuman && turnCtx != null)
        {
            snap.CanCheck = turnCtx.CanCheck;
            snap.CanFold = true;
            snap.MoneyToCall = turnCtx.MoneyToCall;
            snap.CanRaise = turnCtx.CanRaise;
            snap.MinRaise = turnCtx.MinRaise;
            snap.MaxRaise = turnCtx.MoneyLeft - turnCtx.MoneyToCall;
            snap.HumanMoneyLeft = turnCtx.MoneyLeft;

            var roundName = snap.RoundType switch
            {
                GameRoundType.PreFlop => "Pre-Flop",
                GameRoundType.Flop => "Flop",
                GameRoundType.Turn => "Turn",
                GameRoundType.River => "River",
                _ => snap.RoundType.ToString()
            };
            snap.StatusMessage = $"Your turn — {roundName} | Pot: ${snap.Pot}";
            snap.Players[_humanSeatIdx].IsCurrentTurn = true;
        }
        else if (phase == PokerPhase.GameOver)
        {
            snap.StatusMessage = snap.WinnerName == "You"
                ? "🏆 You win the tournament!"
                : $"Game over — {snap.WinnerName} wins!";
        }
        else if (phase == PokerPhase.HandOver)
        {
            string winMsg = snap.HandWinnerName.Length > 0
                ? $"{snap.HandWinnerName} wins the pot (${snap.HandWinAmount})!"
                : $"Hand {snap.HandNumber} complete";
            snap.StatusMessage = winMsg;
        }
        else
        {
            snap.StatusMessage = $"Hand #{snap.HandNumber} — {(snap.RoundType)} | Pot: ${snap.Pot}";
        }

        Snapshot = snap;
        OnSnapshotUpdated?.Invoke();
    }
}

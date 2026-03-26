// ChessAI.cs — Async wrapper for AI move computation.
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs the Lynx engine AI move on a background thread (desktop) or
/// synchronously on the same thread (WebAssembly / single-threaded WASM).
/// </summary>
public static class ChessAI
{
    // Normal mode: execute AI on background thread.
    private const bool USE_BACKGROUND_THREAD = true;

    private static volatile bool _pending = false;
    private static volatile bool _completionQueued = false;
    private static readonly object _completionLock = new();
    private static Action? _completionAction;

    public static bool IsPending => _pending;

    /// <summary>
    /// Request an AI move. The <paramref name="game"/> Phase must be AIThinking when called.
    /// On completion, Phase is set by <see cref="ChessGame.ExecuteAIMove"/>.
    /// </summary>
    public static void RequestMove(ChessGame game, Action? onComplete = null)
    {
        if (_pending)
        {
            Console.WriteLine("[ChessAI] RequestMove called but already pending — skipped");
            return;
        }
        _pending = true;
        Console.WriteLine("[ChessAI] RequestMove started");

#if WEB
        // WebAssembly: single-threaded — execute synchronously
        try
        {
            game.ExecuteAIMove();
        }
        finally
        {
            _pending = false;
            Console.WriteLine("[ChessAI] Move complete (WEB)");
            lock (_completionLock)
            {
                _completionAction = onComplete;
                _completionQueued = true;
            }
        }
#else
        if (USE_BACKGROUND_THREAD)
        {
            // Desktop/mobile: run on a thread pool thread
            Task.Run(() =>
            {
                try
                {
                    game.ExecuteAIMove();
                }
                finally
                {
                    _pending = false;
                    Console.WriteLine("[ChessAI] Move complete (background thread)");
                    lock (_completionLock)
                    {
                        _completionAction = onComplete;
                        _completionQueued = true;
                    }
                }
            });
        }
        else
        {
            // Desktop debug: execute synchronously on the main thread.
            try
            {
                game.ExecuteAIMove();
            }
            finally
            {
                _pending = false;
                Console.WriteLine("[ChessAI] Move complete (main thread)");
                lock (_completionLock)
                {
                    _completionAction = onComplete;
                    _completionQueued = true;
                }
            }
        }
#endif
    }

    /// <summary>
    /// Must be called from the main thread each frame.
    /// Runs any queued completion callback there to avoid cross-thread UI/game-state updates.
    /// </summary>
    public static void PumpCompleted()
    {
        if (!_completionQueued)
        {
            return;
        }

        Action? callback = null;
        lock (_completionLock)
        {
            if (_completionQueued)
            {
                callback = _completionAction;
                _completionAction = null;
                _completionQueued = false;
            }
        }

        callback?.Invoke();
    }
}

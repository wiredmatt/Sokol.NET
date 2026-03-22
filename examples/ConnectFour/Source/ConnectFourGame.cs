// ConnectFourGame.cs — Game state management for Connect 4.
// Tracks board state, phases, drop animation, win detection, and AI moves.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConnectFour
{
    public enum CellState : byte
    {
        Empty   = 0,
        Player1 = 1,   // Human (Red)
        Player2 = 2,   // AI (Yellow)
    }

    public enum GamePhase
    {
        PlayerTurn,
        AIThinking,
        Dropping,    // disc-fall animation in progress
        GameOver,
    }

    public class ConnectFourGame
    {
        public const int COLS = 7;
        public const int ROWS = 6;

        // Cells[row * COLS + col], row 0 = bottom, row 5 = top
        public CellState[] Cells = new CellState[ROWS * COLS];

        public GamePhase Phase          = GamePhase.PlayerTurn;
        public CellState Winner         = CellState.Empty;
        public bool      IsDraw         = false;
        public bool      PlayerIsPlayer1 = true;  // human plays Player1 (Red)
        public int       AiDepth        = 6;
        public int       Player1Wins    = 0;
        public int       Player2Wins    = 0;

        public int LastPlacedCol = -1;
        public int LastPlacedRow = -1;

        // Win cells: 4 linear indices (row * COLS + col) for the winning run
        public int[] WinCells = Array.Empty<int>();

        // Drop animation
        public int       DropAnimCol       = -1;
        public float     DropAnimY         = -1f;   // current Y of falling disc; -1 = not active
        public CellState DropAnimColor     = CellState.Empty;
        private bool     _lastDropWasAI;

        // Pending AI result: int.MinValue = still computing
        private volatile int _pendingAICol = int.MinValue;

        // Undo history
        private readonly Stack<CellState[]> _history = new();

        // -------------------------------------------------------------------
        // Construction / reset
        // -------------------------------------------------------------------
        public ConnectFourGame() => Reset();

        public void Reset()
        {
            Array.Clear(Cells, 0, ROWS * COLS);
            Phase         = GamePhase.PlayerTurn;
            Winner        = CellState.Empty;
            IsDraw        = false;
            LastPlacedCol = -1;
            LastPlacedRow = -1;
            WinCells      = Array.Empty<int>();
            DropAnimCol   = -1;
            DropAnimY     = -1f;
            DropAnimColor = CellState.Empty;
            _pendingAICol = int.MinValue;
            _history.Clear();
        }

        // -------------------------------------------------------------------
        // Cell accessors
        // -------------------------------------------------------------------
        public CellState Get(int row, int col) => Cells[row * COLS + col];
        public void      Set(int row, int col, CellState v) => Cells[row * COLS + col] = v;

        public bool IsColumnFull(int col)
        {
            if (col < 0 || col >= COLS) return true;
            return Get(ROWS - 1, col) != CellState.Empty;
        }

        public bool IsBoardFull()
        {
            for (int col = 0; col < COLS; col++)
                if (!IsColumnFull(col)) return false;
            return true;
        }

        /// <summary>Returns the row the next disc would land in for the given column, or -1 if full.</summary>
        public int NextRow(int col)
        {
            for (int row = 0; row < ROWS; row++)
                if (Get(row, col) == CellState.Empty) return row;
            return -1;
        }

        // -------------------------------------------------------------------
        // Win detection
        // -------------------------------------------------------------------
        public (CellState winner, int[] winCells) CheckWin()
        {
            // Horizontal
            for (int row = 0; row < ROWS; row++)
            for (int col = 0; col <= COLS - 4; col++)
            {
                var c = Get(row, col);
                if (c != CellState.Empty &&
                    c == Get(row, col+1) && c == Get(row, col+2) && c == Get(row, col+3))
                    return (c, new[]{ row*COLS+col, row*COLS+col+1, row*COLS+col+2, row*COLS+col+3 });
            }
            // Vertical
            for (int col = 0; col < COLS; col++)
            for (int row = 0; row <= ROWS - 4; row++)
            {
                var c = Get(row, col);
                if (c != CellState.Empty &&
                    c == Get(row+1, col) && c == Get(row+2, col) && c == Get(row+3, col))
                    return (c, new[]{ row*COLS+col, (row+1)*COLS+col, (row+2)*COLS+col, (row+3)*COLS+col });
            }
            // Diagonal ↗
            for (int row = 0; row <= ROWS - 4; row++)
            for (int col = 0; col <= COLS - 4; col++)
            {
                var c = Get(row, col);
                if (c != CellState.Empty &&
                    c == Get(row+1, col+1) && c == Get(row+2, col+2) && c == Get(row+3, col+3))
                    return (c, new[]{ row*COLS+col, (row+1)*COLS+col+1, (row+2)*COLS+col+2, (row+3)*COLS+col+3 });
            }
            // Diagonal ↖
            for (int row = 0; row <= ROWS - 4; row++)
            for (int col = 3; col < COLS; col++)
            {
                var c = Get(row, col);
                if (c != CellState.Empty &&
                    c == Get(row+1, col-1) && c == Get(row+2, col-2) && c == Get(row+3, col-3))
                    return (c, new[]{ row*COLS+col, (row+1)*COLS+col-1, (row+2)*COLS+col-2, (row+3)*COLS+col-3 });
            }
            return (CellState.Empty, Array.Empty<int>());
        }

        // -------------------------------------------------------------------
        // Place a piece
        // -------------------------------------------------------------------
        public bool TryDropPiece(int col, CellState piece)
        {
            if (col < 0 || col >= COLS) return false;
            if (IsColumnFull(col)) return false;

            int targetRow = NextRow(col);
            if (targetRow < 0) return false;

            _history.Push((CellState[])Cells.Clone());
            Set(targetRow, col, piece);
            LastPlacedCol    = col;
            LastPlacedRow    = targetRow;
            DropAnimCol      = col;
            DropAnimY        = global::ConnectfourApp.DROP_START_Y;
            DropAnimColor    = piece;
            _lastDropWasAI   = piece != (PlayerIsPlayer1 ? CellState.Player1 : CellState.Player2);
            Phase            = GamePhase.Dropping;
            return true;
        }

        // -------------------------------------------------------------------
        // Animate disc fall each frame
        // -------------------------------------------------------------------
        public void UpdateDropAnimation(float dt)
        {
            if (Phase != GamePhase.Dropping || DropAnimCol < 0) return;

            DropAnimY -= global::ConnectfourApp.DROP_SPEED * dt;

            float targetY = global::ConnectfourApp.DISC_REST_Y;
            if (DropAnimY <= targetY)
            {
                DropAnimY     = -1f;
                DropAnimCol   = -1;
                DropAnimColor = CellState.Empty;

                var (winner, wc) = CheckWin();
                if (winner != CellState.Empty)
                {
                    Winner   = winner;
                    WinCells = wc;
                    if (winner == CellState.Player1) Player1Wins++;
                    else                             Player2Wins++;
                    Phase = GamePhase.GameOver;
                }
                else if (IsBoardFull())
                {
                    IsDraw = true;
                    Phase  = GamePhase.GameOver;
                }
                else if (_lastDropWasAI)
                {
                    Phase = GamePhase.PlayerTurn;
                }
                else
                {
                    RequestAIMove();
                }
            }
        }

        // -------------------------------------------------------------------
        // AI move (runs on background thread except on Web)
        // -------------------------------------------------------------------
        public void RequestAIMove()
        {
            Phase         = GamePhase.AIThinking;
            _pendingAICol = int.MinValue;

            CellState ai    = PlayerIsPlayer1 ? CellState.Player2 : CellState.Player1;
            CellState human = PlayerIsPlayer1 ? CellState.Player1 : CellState.Player2;
            var aiBoard     = C4Board.FromCells(Cells, ai, human);
            int depth       = AiDepth;

#if WEB
            _pendingAICol = ConnectFourAI.GetMove(aiBoard, depth);
#else
            Task.Run(() => { _pendingAICol = ConnectFourAI.GetMove(aiBoard, depth); });
#endif
        }

        /// <summary>Call each frame to pick up completed async AI result.</summary>
        public void PollAIResult()
        {
            if (Phase != GamePhase.AIThinking) return;
            if (_pendingAICol == int.MinValue) return;

            int col       = _pendingAICol;
            _pendingAICol = int.MinValue;

            CellState ai = PlayerIsPlayer1 ? CellState.Player2 : CellState.Player1;
            if (col < 0 || !TryDropPiece(col, ai))
            {
                IsDraw = true;
                Phase  = GamePhase.GameOver;
            }
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------
        public void Undo()
        {
            if (_history.Count == 0) return;
            if (Phase == GamePhase.AIThinking || Phase == GamePhase.Dropping) return;

            // Pop twice (undo AI response + human move)
            if (_history.Count >= 2)
            {
                _history.Pop();
                Cells = _history.Pop();
            }
            else
            {
                Cells = _history.Pop();
            }

            Phase         = GamePhase.PlayerTurn;
            Winner        = CellState.Empty;
            IsDraw        = false;
            WinCells      = Array.Empty<int>();
            LastPlacedCol = -1;
            LastPlacedRow = -1;
        }
    }
}

// ReversiGame.cs — High-level game state management for Reversi.
// Decorates the Board bitfield AI engine with game-state bookkeeping,
// valid-move queries, flip-animation tracking, and pass/skip logic.

using System;
using System.Collections.Generic;

namespace Reversi
{
    public enum CellState : byte
    {
        Empty    = 0,
        Black    = 1,   // current player (you, the human)
        White    = 2,   // AI opponent
    }

    public enum GamePhase
    {
        PlayerTurn,
        AIThinking,
        AnimatingFlip,
        GameOver,
    }

    /// <summary>Tracks a single disc that is currently flipping (180° rotation).</summary>
    public struct FlipAnimation
    {
        public int   CellIndex;         // 0-63
        public float Progress;          // 0 → 1
        public float Duration;          // seconds for full flip
        public CellState TargetColor;
    }

    public class ReversiGame
    {
        // -------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------
        public CellState[] Cells = new CellState[64];  // public for renderer
        public GamePhase       Phase        = GamePhase.PlayerTurn;
        public int             BlackScore   = 2;
        public int             WhiteScore   = 2;
        public bool            PlayerIsBlack = true;   // human plays as Black by default
        public int             AiDepth      = 5;

        public List<FlipAnimation> FlipAnimations = new List<FlipAnimation>();

        // Last flipped cells (for creating animations)
        private List<int> _pendingFlips = new();
        private int       _pendingPlaceCell = -1;

        // HistoryStack for Undo
        private Stack<CellState[]> _history = new();

        // -------------------------------------------------------------------
        // Construction / reset
        // -------------------------------------------------------------------
        public ReversiGame() => Reset();

        public void Reset()
        {
            Array.Clear(Cells, 0, 64);
            // Standard start: d4=White, e4=Black, d5=Black, e5=White
            // Row-major: index = row*8+col.  d=col3, e=col4, row3=row3, row4=row4
            Cells[27] = CellState.White;
            Cells[28] = CellState.Black;
            Cells[35] = CellState.Black;
            Cells[36] = CellState.White;
            Phase = GamePhase.PlayerTurn;
            FlipAnimations.Clear();
            _history.Clear();
            UpdateScore();
        }

        // -------------------------------------------------------------------
        // Valid moves
        // -------------------------------------------------------------------
        /// <summary>Returns list of cell indices where the given side can legally play.</summary>
        public List<int> GetValidMoves(CellState side)
        {
            var board  = GetBoard(side);
            var result = new List<int>(20);
            for (int i = 0; i < 64; i++)
            {
                if (board.PlacePiece(i, out _)) result.Add(i);
            }
            return result;
        }

        // -------------------------------------------------------------------
        // Apply a player move
        // -------------------------------------------------------------------
        /// <summary>
        /// Attempt to place a piece for <paramref name="side"/> at <paramref name="cellIndex"/>.
        /// Returns true and starts flip animations if the move was legal.
        /// </summary>
        public bool TryApplyMove(int cellIndex, CellState side)
        {
            if (cellIndex < 0 || cellIndex >= 64) return false;
            if (Cells[cellIndex] != CellState.Empty) return false;

            var board = GetBoard(side);
            if (!board.PlacePiece(cellIndex, out Board newBoard)) return false;

            // Push undo snapshot
            _history.Push((CellState[])Cells.Clone());

            // Find which cells flipped
            _pendingFlips.Clear();
            CellState opponent = (side == CellState.Black) ? CellState.White : CellState.Black;
            for (int i = 0; i < 64; i++)
            {
                ulong bit = 1UL << i;
                bool wasOpponent = (Cells[i] == opponent);
                bool nowPlayer   = ((newBoard.PlayerPieces & bit) != 0);
                if (wasOpponent && nowPlayer) _pendingFlips.Add(i);
            }

            // Apply new board state
            ApplyBoardState(newBoard, side);
            Cells[cellIndex] = side;

            // Start flip animations
            foreach (int fi in _pendingFlips)
                StartFlipAnimation(fi, side);

            UpdateScore();
            Phase = GamePhase.AnimatingFlip;
            return true;
        }

        // -------------------------------------------------------------------
        // AI move (async)
        // -------------------------------------------------------------------
        public void RequestAIMove()
        {
            Phase = GamePhase.AIThinking;
            CellState ai   = PlayerIsBlack ? CellState.White : CellState.Black;
            var board = GetBoard(ai);
            ReversiAI.GetMoveAsync(board, AiDepth, (bestIndex) =>
            {
                _pendingPlaceCell = bestIndex;
            });
        }

        /// <summary>Call each frame to poll for completed async AI result.</summary>
        public void PollAIResult()
        {
            if (Phase != GamePhase.AIThinking) return;
            if (_pendingPlaceCell < 0) return;

            int idx = _pendingPlaceCell;
            _pendingPlaceCell = -1;

            CellState ai   = PlayerIsBlack ? CellState.White : CellState.Black;
            if (!TryApplyMove(idx, ai))
            {
                // AI had no valid move, skip turn
                AfterMoveTransition(ai);
            }
        }

        // -------------------------------------------------------------------
        // Flip animation tick
        // -------------------------------------------------------------------
        public void UpdateAnimations(float dt)
        {
            bool anyRunning = false;
            for (int i = FlipAnimations.Count - 1; i >= 0; i--)
            {
                var anim = FlipAnimations[i];
                anim.Progress += dt / anim.Duration;
                if (anim.Progress >= 1.0f)
                {
                    anim.Progress = 1.0f;
                    FlipAnimations.RemoveAt(i);
                }
                else
                {
                    FlipAnimations[i] = anim;
                    anyRunning = true;
                }
            }

            if (!anyRunning && Phase == GamePhase.AnimatingFlip)
            {
                // Determine whose turn was just completed
                // We figure this out from which side just placed
                CellState justPlayed = PlayerIsBlack ? CellState.Black : CellState.White;
                AfterMoveTransition(justPlayed);
            }
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------
        public void Undo()
        {
            if (_history.Count == 0) return;
            if (Phase == GamePhase.AIThinking) return;

            // Pop twice (undo player + AI move) if AI already responded
            if (_history.Count >= 2)
            {
                _history.Pop();
                Cells = _history.Pop();
            }
            else if (_history.Count == 1)
            {
                Cells = _history.Pop();
            }

            FlipAnimations.Clear();
            Phase = GamePhase.PlayerTurn;
            UpdateScore();
        }

        // -------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------
        private void AfterMoveTransition(CellState justPlayed)
        {
            CellState human = PlayerIsBlack ? CellState.Black : CellState.White;
            CellState ai    = PlayerIsBlack ? CellState.White : CellState.Black;

            CellState nextSide = (justPlayed == human) ? ai : human;

            bool nextHasMoves = GetValidMoves(nextSide).Count > 0;
            bool currHasMoves = GetValidMoves(justPlayed).Count > 0;

            if (!nextHasMoves && !currHasMoves)
            {
                Phase = GamePhase.GameOver;
            }
            else if (!nextHasMoves)
            {
                // Skip: nextSide has no moves, same side plays again
                if (justPlayed == human)
                    Phase = GamePhase.PlayerTurn;   // human plays again (AI skipped)
                else
                    RequestAIMove();                // AI plays again (human skipped)
            }
            else
            {
                // Normal transition
                if (nextSide == human)
                    Phase = GamePhase.PlayerTurn;
                else
                    RequestAIMove();
            }
        }

        private void StartFlipAnimation(int cellIndex, CellState targetColor)
        {
            FlipAnimations.Add(new FlipAnimation
            {
                CellIndex   = cellIndex,
                Progress    = 0f,
                Duration    = 0.35f,
                TargetColor = targetColor,
            });
        }

        private void ApplyBoardState(Board newBoard, CellState playerSide)
        {
            CellState opponentSide = (playerSide == CellState.Black) ? CellState.White : CellState.Black;
            for (int i = 0; i < 64; i++)
            {
                ulong bit = 1UL << i;
                if ((newBoard.PlayerPieces & bit) != 0)
                    Cells[i] = playerSide;
                else if ((newBoard.OpponentPieces & bit) != 0)
                    Cells[i] = opponentSide;
                // Empty cells remain Empty
            }
        }

        private Board GetBoard(CellState playerSide)
        {
            CellState opponentSide = (playerSide == CellState.Black) ? CellState.White : CellState.Black;
            ulong player = 0, opponent = 0;
            for (int i = 0; i < 64; i++)
            {
                ulong bit = 1UL << i;
                if (Cells[i] == playerSide)          player   |= bit;
                else if (Cells[i] == opponentSide)   opponent |= bit;
            }
            return new Board(player, opponent);
        }

        private void UpdateScore()
        {
            int b = 0, w = 0;
            foreach (var c in Cells)
            {
                if (c == CellState.Black) b++;
                else if (c == CellState.White) w++;
            }
            BlackScore = b;
            WhiteScore = w;
        }
    }
}

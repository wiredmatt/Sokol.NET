// ReversiAI.cs — Improved alpha-beta AI with move ordering, better heuristics,
//               and parallel root search on non-web platforms.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Reversi
{
    public static class ReversiAI
    {
        // Static position weight table — indexed by board position 0..63.
        // Corners are most valuable; X-squares (diagonally adjacent to corners) are dangerous.
        private static readonly int[] POSITION_WEIGHTS =
        {
            100, -20,  10,   5,   5,  10, -20, 100,
            -20, -50,  -2,  -2,  -2,  -2, -50, -20,
             10,  -2,   5,   2,   2,   5,  -2,  10,
              5,  -2,   2,   1,   1,   2,  -2,   5,
              5,  -2,   2,   1,   1,   2,  -2,   5,
             10,  -2,   5,   2,   2,   5,  -2,  10,
            -20, -50,  -2,  -2,  -2,  -2, -50, -20,
            100, -20,  10,   5,   5,  10, -20, 100,
        };

        // Pre-sorted move order by POSITION_WEIGHTS descending (cached at startup).
        private static readonly int[] MOVE_ORDER = BuildMoveOrder();

        private static int[] BuildMoveOrder()
        {
            var indices = new int[64];
            for (int i = 0; i < 64; i++) indices[i] = i;
            Array.Sort(indices, (a, b) => POSITION_WEIGHTS[b].CompareTo(POSITION_WEIGHTS[a]));
            return indices;
        }

        // Corner masks for stability / heuristic calculations
        private const ulong UL_SIDES_MASK      = 0x1010101010101ff;
        private const ulong UR_SIDES_MASK      = 0x80808080808080ff;
        private const ulong LL_SIDES_MASK      = 0xff01010101010101;
        private const ulong LR_SIDES_MASK      = 0xff80808080808080;
        private const ulong AROUND_CORNER_MASK = 0x42c300000000c342;

        // Collect root moves into a buffer and sort by positional weight.
        private static List<(int index, Board newBoard)> GetRootMoves(Board board)
        {
            var moves = new List<(int, Board)>(32);
            foreach (int index in MOVE_ORDER)
            {
                if (board.PlacePiece(index, out Board newBoard))
                    moves.Add((index, newBoard));
            }
            return moves;
        }

        /// <summary>
        /// Find the best move for the current player using alpha-beta search.
        /// Returns the board index (0-63) or -1 if no move is available.
        /// </summary>
        public static int GetMove(Board board, int depth)
        {
            if (depth == 0) return -1;

            var moves = GetRootMoves(board);
            if (moves.Count == 0) return -1;
            if (moves.Count == 1) return moves[0].index;

#if !WEB
            // Parallel root search — each root move is evaluated on its own thread.
            int bestPos   = moves[0].index;
            int bestScore = int.MinValue;
            object sync   = new object();

            Parallel.ForEach(moves, move =>
            {
                int s = -AlphaBeta(move.newBoard.Swapped(), depth - 1, int.MinValue / 2, int.MaxValue / 2);
                lock (sync)
                {
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestPos   = move.index;
                    }
                }
            });

            return bestPos;
#else
            // Single-threaded search for WebAssembly.
            int bestPosST   = -1;
            int alpha       = int.MinValue / 2;
            int bestScoreST = alpha;

            foreach (var (index, newBoard) in moves)
            {
                int s = -AlphaBeta(newBoard.Swapped(), depth - 1, int.MinValue / 2, -alpha);
                if (s > bestScoreST)
                {
                    bestScoreST = s;
                    bestPosST   = index;
                }
                if (s > alpha) alpha = s;
            }

            return bestPosST;
#endif
        }

        /// <summary>
        /// Run the AI search on a background thread and invoke the callback with the result.
        /// Avoids blocking the render thread.
        /// </summary>
        public static void GetMoveAsync(Board board, int depth, System.Action<int> callback)
        {
#if !WEB
            Task.Run(() =>
            {
                int move = GetMove(board, depth);
                callback(move);
            });
#else
            // On Web/WASM threading is unavailable — run synchronously.
            int move = GetMove(board, depth);
            callback(move);
#endif
        }

        private static int AlphaBeta(Board board, int depth, int alpha, int beta)
        {
            if (depth == 0) return Heuristics(board);

            bool madeAMove = false;

            foreach (int index in MOVE_ORDER)
            {
                if (board.PlacePiece(index, out Board newBoard))
                {
                    int val = -AlphaBeta(newBoard.Swapped(), depth - 1, -beta, -alpha);
                    if (val > alpha) alpha = val;
                    madeAMove = true;
                    if (alpha >= beta) break;  // beta cut-off
                }
            }

            if (madeAMove) return alpha;

            // No moves for current player — check if opponent can move (pass).
            Board passed = board.Swapped();
            bool opponentHasMoves = false;
            foreach (int index in MOVE_ORDER)
            {
                if (passed.PlacePiece(index, out _)) { opponentHasMoves = true; break; }
            }

            if (opponentHasMoves)
            {
                // Pass — opponent moves, depth is not decremented (no disc was placed).
                return -AlphaBeta(passed, depth, -beta, -alpha);
            }

            // Both players have no moves — game over.
            return WinHeuristics(board);
        }

        private static int WinHeuristics(Board board)
        {
            int player   = board.PlayerScore();
            int opponent = board.OpponentScore();
            if (player > opponent)
                return 200000 + 1000 * (player - opponent);
            if (player < opponent)
                return -200000 - 1000 * (opponent - player);
            return 0; // draw
        }

        private static int Heuristics(Board board)
        {
            ulong player   = board.PlayerPieces;
            ulong opponent = board.OpponentPieces;

            // Positional score — sum of weight table values
            int score = 0;
            ulong tmp = player;
            while (tmp != 0)
            {
                int idx = BitOperations.TrailingZeroCount(tmp);
                score += POSITION_WEIGHTS[idx];
                tmp &= tmp - 1;
            }
            tmp = opponent;
            while (tmp != 0)
            {
                int idx = BitOperations.TrailingZeroCount(tmp);
                score -= POSITION_WEIGHTS[idx];
                tmp &= tmp - 1;
            }

            // Corner and stable-edge bonus
            score += CornerAndSidesHeuristics(player) * 4;
            score -= CornerAndSidesHeuristics(opponent);

            // X-square penalty (adjacent to corners, dangerous unless corner is taken)
            score -= (int)BitOperations.PopCount(player   & AROUND_CORNER_MASK) * 3;
            score += (int)BitOperations.PopCount(opponent & AROUND_CORNER_MASK) * 3;

            return score;
        }

        private static int CornerAndSidesHeuristics(ulong pieces)
        {
            int score = 0;

            if ((pieces & 1UL) != 0)
            {
                score += 50;
                score += (int)BitOperations.PopCount(pieces & UL_SIDES_MASK) << 2;
            }
            if ((pieces & 128UL) != 0)
            {
                score += 50;
                score += (int)BitOperations.PopCount(pieces & UR_SIDES_MASK) << 2;
            }
            if ((pieces & 0x100000000000000UL) != 0)
            {
                score += 50;
                score += (int)BitOperations.PopCount(pieces & LL_SIDES_MASK) << 2;
            }
            if ((pieces & 0x8000000000000000UL) != 0)
            {
                score += 50;
                score += (int)BitOperations.PopCount(pieces & LR_SIDES_MASK) << 2;
            }

            return score;
        }
    }
}

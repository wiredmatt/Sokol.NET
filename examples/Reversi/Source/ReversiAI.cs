// ReversiAI.cs — C# port of the Rust alphabeta AI from bothello1

using System.Threading.Tasks;

namespace Reversi
{
    public static class ReversiAI
    {
        // Masks — same constants as the Rust version
        private const ulong UL_SIDES_MASK      = 0x1010101010101ff;   // upper and left edges
        private const ulong UR_SIDES_MASK      = 0x80808080808080ff;  // upper and right edges
        private const ulong LL_SIDES_MASK      = 0xff01010101010101;  // lower and left edges
        private const ulong LR_SIDES_MASK      = 0xff80808080808080;  // lower and right edges
        private const ulong AROUND_CORNER_MASK = 0x42c300000000c342;  // adjacent to corners

        /// <summary>
        /// Find the best move for the current player using alpha-beta search.
        /// Returns the board index (0-63) or -1 if no move is available.
        /// </summary>
        public static int GetMove(Board board, int depth)
        {
            if (depth == 0) return -1;

            int bestPos = -1;
            int score = int.MinValue / 2;
            int bestScore = score;

            for (int index = 0; index < 64; index++)
            {
                if (board.PlacePiece(index, out Board newBoard))
                {
                    int s = -AlphaBeta(newBoard.Swapped(), depth - 1, int.MinValue / 2, -score);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestPos = index;
                    }
                    score = System.Math.Max(score, s);
                }
            }

            return bestPos;
        }

        /// <summary>
        /// Run the AI search on a background thread and invoke the callback with the result.
        /// Avoids blocking the render thread on mobile.
        /// </summary>
        public static void GetMoveAsync(Board board, int depth, System.Action<int> callback)
        {
            Task.Run(() =>
            {
                int move = GetMove(board, depth);
                callback(move);
            });
        }

        private static int AlphaBeta(Board board, int depth, int alpha, int beta)
        {
            if (depth == 0) return Heuristics(board);

            bool madeAMove = false;

            for (int index = 0; index < 64; index++)
            {
                if (board.PlacePiece(index, out Board newBoard))
                {
                    alpha = System.Math.Max(alpha, -AlphaBeta(newBoard.Swapped(), depth - 1, -beta, -alpha));
                    madeAMove = true;
                    if (beta <= alpha) break;
                }
            }

            return madeAMove ? alpha : WinHeuristics(board);
        }

        private static int WinHeuristics(Board board)
        {
            int player   = board.PlayerScore();
            int opponent = board.OpponentScore();
            if (player > opponent)
                return 1000 * (player - opponent);
            return -200000;
        }

        private static int Heuristics(Board board)
        {
            int score = board.OpponentScore() - board.PlayerScore();

            score += CornerAndSidesHeuristics(board.PlayerPieces) << 2;
            score -= CornerAndSidesHeuristics(board.OpponentPieces);

            score += (int)(System.Numerics.BitOperations.PopCount(board.OpponentPieces & AROUND_CORNER_MASK)) << 2;
            return score;
        }

        private static int CornerAndSidesHeuristics(ulong pieces)
        {
            int score = 0;

            // Upper-left corner
            if ((pieces & 1UL) != 0)
            {
                score += 50;
                score += (int)System.Numerics.BitOperations.PopCount(pieces & UL_SIDES_MASK) << 2;
            }
            // Upper-right corner
            if ((pieces & 128UL) != 0)
            {
                score += 50;
                score += (int)System.Numerics.BitOperations.PopCount(pieces & UR_SIDES_MASK) << 2;
            }
            // Lower-left corner
            if ((pieces & 0x100000000000000UL) != 0)
            {
                score += 50;
                score += (int)System.Numerics.BitOperations.PopCount(pieces & LL_SIDES_MASK) << 2;
            }
            // Lower-right corner
            if ((pieces & 0x8000000000000000UL) != 0)
            {
                score += 50;
                score += (int)System.Numerics.BitOperations.PopCount(pieces & LR_SIDES_MASK) << 2;
            }

            return score;
        }
    }
}

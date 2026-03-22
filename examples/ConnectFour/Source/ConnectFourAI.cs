// ConnectFourAI.cs — Alpha-beta negamax AI for Connect 4.
// Uses a compact column-major bitboard for fast win detection.

using System;
using System.Threading.Tasks;

namespace ConnectFour
{
    // -----------------------------------------------------------------------
    // Compact bitboard: column-major, bit = col * (HEIGHT+1) + row, row 0 = bottom.
    // Relative to the CURRENT player to move.
    // -----------------------------------------------------------------------
    public struct C4Board
    {
        public const int WIDTH  = 7;
        public const int HEIGHT = 6;

        public ulong PlayerBits;    // bits belonging to current player
        public ulong OpponentBits;  // bits belonging to opponent
        public int   MoveCount;

        // Bit index for cell (col, row)
        public static int BitIdx(int col, int row) => col * (HEIGHT + 1) + row;

        // Full-column bitmask (HEIGHT bits, starting at col * (HEIGHT+1))
        static ulong ColMask(int col) => ((1UL << HEIGHT) - 1) << (col * (HEIGHT + 1));

        // Count pieces in a column (= height of the stack)
        public int ColHeight(int col)
        {
            ulong colBits = (PlayerBits | OpponentBits) & ColMask(col);
            int h = 0;
            for (int row = 0; row < HEIGHT; row++)
                if ((colBits & (1UL << BitIdx(col, row))) != 0) h = row + 1;
            return h;
        }

        public bool CanPlay(int col)
        {
            if (col < 0 || col >= WIDTH) return false;
            return ColHeight(col) < HEIGHT;
        }

        // Bitmask of the next playable slot in a column
        public ulong PlayBit(int col) => 1UL << BitIdx(col, ColHeight(col));

        // Returns new board after current player plays in col (roles are swapped).
        // The current player adds the piece to their own bits, then sides swap so
        // that the *next* player to move is always PlayerBits.
        public C4Board Play(int col) => new C4Board
        {
            PlayerBits   = OpponentBits,                // next mover = old opponent
            OpponentBits = PlayerBits | PlayBit(col),   // old current player + new piece
            MoveCount    = MoveCount + 1,
        };

        // True if playing col gives current player a win
        public bool IsWinningMove(int col) => HasAlignment(PlayerBits | PlayBit(col));

        // True if the given bitboard contains 4-in-a-row
        public static bool HasAlignment(ulong pos)
        {
            // Horizontal: shift by HEIGHT+1
            ulong m = pos & (pos >> (HEIGHT + 1));
            if ((m & (m >> (2 * (HEIGHT + 1)))) != 0) return true;
            // Diagonal /: shift by HEIGHT
            m = pos & (pos >> HEIGHT);
            if ((m & (m >> (2 * HEIGHT))) != 0) return true;
            // Diagonal \: shift by HEIGHT+2
            m = pos & (pos >> (HEIGHT + 2));
            if ((m & (m >> (2 * (HEIGHT + 2)))) != 0) return true;
            // Vertical: shift by 1
            m = pos & (pos >> 1);
            if ((m & (m >> 2)) != 0) return true;
            return false;
        }

        // Build from the game's flat cells array; ai = perspective player, human = opponent
        public static C4Board FromCells(CellState[] cells, CellState ai, CellState human)
        {
            ulong pb = 0, ob = 0;
            int   mc = 0;
            for (int col = 0; col < WIDTH;  col++)
            for (int row = 0; row < HEIGHT; row++)
            {
                var c = cells[row * WIDTH + col];
                if      (c == ai)    { pb |= 1UL << BitIdx(col, row); mc++; }
                else if (c == human) { ob |= 1UL << BitIdx(col, row); mc++; }
            }
            return new C4Board { PlayerBits = pb, OpponentBits = ob, MoveCount = mc };
        }
    }

    // -----------------------------------------------------------------------
    // AI: alpha-beta negamax with win-first and block-first ordering
    // -----------------------------------------------------------------------
    public static class ConnectFourAI
    {
        // Explore center columns first — improves pruning significantly
        private static readonly int[] ColumnOrder = { 3, 2, 4, 1, 5, 0, 6 };

        // Max score: win in one move after placing = (42 - moves) / 2
        private static int MaxScore(C4Board b) => (C4Board.WIDTH * C4Board.HEIGHT + 1 - b.MoveCount) / 2;
        private static int MinScore(C4Board b) => -(C4Board.WIDTH * C4Board.HEIGHT     - b.MoveCount) / 2;

        public static int GetMove(C4Board board, int depth)
        {
            // 1. Immediate win
            foreach (int col in ColumnOrder)
                if (board.CanPlay(col) && board.IsWinningMove(col))
                    return col;

            // 2. Block opponent immediate win
            var opp = new C4Board
            {
                PlayerBits   = board.OpponentBits,
                OpponentBits = board.PlayerBits,
                MoveCount    = board.MoveCount,
            };
            foreach (int col in ColumnOrder)
                if (opp.CanPlay(col) && opp.IsWinningMove(col))
                    return col;

            // 3. Alpha-beta search
            int bestCol   = 3;
            int bestScore = int.MinValue;

#if !WEB
            object sync = new object();
            Parallel.ForEach(ColumnOrder, col =>
            {
                if (!board.CanPlay(col)) return;
                int s = -AlphaBeta(board.Play(col), depth - 1, int.MinValue / 2, int.MaxValue / 2);
                lock (sync) { if (s > bestScore) { bestScore = s; bestCol = col; } }
            });
#else
            foreach (int col in ColumnOrder)
            {
                if (!board.CanPlay(col)) continue;
                int s = -AlphaBeta(board.Play(col), depth - 1, int.MinValue / 2, int.MaxValue / 2);
                if (s > bestScore) { bestScore = s; bestCol = col; }
            }
#endif
            return bestCol;
        }

        private static int AlphaBeta(C4Board board, int depth, int alpha, int beta)
        {
            // Win for current player on next move — return immediately
            foreach (int col in ColumnOrder)
                if (board.CanPlay(col) && board.IsWinningMove(col))
                    return MaxScore(board);

            // Draw: no moves available
            bool anyMove = false;
            foreach (int col in ColumnOrder)
                if (board.CanPlay(col)) { anyMove = true; break; }
            if (!anyMove) return 0;

            // Tighten the upper bound: since no immediate win exists, the best
            // achievable score is one step back from the theoretical maximum.
            // This is the key pruning technique from the reference blog.
            int max = MaxScore(board) - 1;
            if (beta > max) { beta = max; if (alpha >= beta) return beta; }

            if (depth == 0) return HeuristicEval(board);

            foreach (int col in ColumnOrder)
            {
                if (!board.CanPlay(col)) continue;
                int val = -AlphaBeta(board.Play(col), depth - 1, -beta, -alpha);
                if (val >= beta) return val;  // beta cut-off
                if (val >  alpha) alpha = val;
            }
            return alpha;
        }

        // Simple positional heuristic: center-column and height bonuses
        private static int HeuristicEval(C4Board board)
        {
            int score = 0;
            for (int col = 0; col < C4Board.WIDTH; col++)
            {
                int colWeight = 3 - Math.Abs(col - 3); // 0,1,2,3,2,1,0
                for (int row = 0; row < C4Board.HEIGHT; row++)
                {
                    ulong mask = 1UL << C4Board.BitIdx(col, row);
                    int   w    = colWeight + row;        // higher rows also better
                    if      ((board.PlayerBits   & mask) != 0) score += w;
                    else if ((board.OpponentBits & mask) != 0) score -= w;
                }
            }
            return score;
        }
    }
}

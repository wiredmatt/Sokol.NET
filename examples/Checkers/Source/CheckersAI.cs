// CheckersAI.cs — Alpha-beta minimax AI for Checkers.
// Uses piece count + king advantage + positional heuristic + mobility + back-rank bonus.

using System;
using System.Collections.Generic;

namespace Checkers
{
    public static class CheckersAI
    {
        // Position value tables (8×8 and 10×10)
        // Higher value = more desirable square
        private static readonly int[] POS_WEIGHTS_8 =
        {
             0,  4,  0,  4,  0,  4,  0,  4,
             4,  0,  3,  0,  3,  0,  3,  0,
             0,  3,  0,  2,  0,  2,  0,  2,
             2,  0,  2,  0,  1,  0,  1,  0,
             0,  1,  0,  1,  0,  2,  0,  2,
             2,  0,  2,  0,  2,  0,  3,  0,
             0,  3,  0,  3,  0,  3,  0,  4,
             4,  0,  4,  0,  4,  0,  4,  0,
        };

        private static readonly int[] POS_WEIGHTS_10 =
        {
             0,  4,  0,  4,  0,  4,  0,  4,  0,  4,
             4,  0,  3,  0,  3,  0,  3,  0,  3,  0,
             0,  3,  0,  2,  0,  2,  0,  2,  0,  2,
             2,  0,  2,  0,  1,  0,  1,  0,  1,  0,
             0,  1,  0,  1,  0,  1,  0,  1,  0,  1,
             1,  0,  1,  0,  1,  0,  1,  0,  1,  0,
             0,  1,  0,  1,  0,  1,  0,  2,  0,  2,
             2,  0,  2,  0,  2,  0,  2,  0,  3,  0,
             0,  3,  0,  3,  0,  3,  0,  3,  0,  4,
             4,  0,  4,  0,  4,  0,  4,  0,  4,  0,
        };

        private const int PIECE_VALUE    = 100;
        private const int KING_VALUE     = 280;
        private const int WIN_SCORE      = 10000;
        private const int MOBILITY_BONUS = 3;    // per legal move available
        private const int BACK_RANK_BONUS = 15;  // per piece still on own back rank (guard king-row)

        /// <summary>
        /// Return the best move for the given color on the given board.
        /// Returns null if no moves are available.
        /// </summary>
        public static CheckersMove? GetBestMove(CheckersBoard board, PieceColor color, GameRules rules, int depth)
        {
            var moves = MoveGenerator.GetAllMoves(board, color, rules);
            if (moves.Count == 0) return null;
            if (moves.Count == 1) return moves[0];

            // Order moves: captures first (most captures first), then by eval
            SortMoves(moves);

            int bestScore = int.MinValue;
            CheckersMove? bestMove = null;

#if !WEB
            // Evaluate root moves in parallel on desktop/mobile for faster response
            object sync = new object();
            Parallel.ForEach(moves, move =>
            {
                var nb = ApplyMoveToBoard(board, move);
                int score = AlphaBeta(nb, Opponent(color), color, rules, depth - 1,
                                      int.MinValue / 2, int.MaxValue / 2);
                lock (sync)
                {
                    if (score > bestScore) { bestScore = score; bestMove = move; }
                }
            });
#else
            foreach (var move in moves)
            {
                var nb = ApplyMoveToBoard(board, move);
                // AlphaBeta returns score from aiColor's perspective — no negation needed.
                int score = AlphaBeta(nb, Opponent(color), color, rules, depth - 1,
                                      int.MinValue / 2, int.MaxValue / 2);
                if (score > bestScore) { bestScore = score; bestMove = move; }
            }
#endif
            return bestMove;
        }

        static int AlphaBeta(CheckersBoard board, PieceColor currentColor, PieceColor aiColor,
                             GameRules rules, int depth, int alpha, int beta)
        {
            var moves = MoveGenerator.GetAllMoves(board, currentColor, rules);

            if (moves.Count == 0)
            {
                // Current player has no moves — they lose
                return currentColor == aiColor ? -WIN_SCORE : WIN_SCORE;
            }

            if (depth == 0)
                return Evaluate(board, aiColor, rules);

            // Order moves for better alpha-beta cutoffs
            SortMoves(moves);

            bool maximizing = currentColor == aiColor;

            if (maximizing)
            {
                int value = int.MinValue / 2;
                foreach (var move in moves)
                {
                    var nb = ApplyMoveToBoard(board, move);
                    int s = AlphaBeta(nb, Opponent(currentColor), aiColor, rules, depth - 1, alpha, beta);
                    if (s > value) value = s;
                    if (value > alpha) alpha = value;
                    if (alpha >= beta) break;
                }
                return value;
            }
            else
            {
                int value = int.MaxValue / 2;
                foreach (var move in moves)
                {
                    var nb = ApplyMoveToBoard(board, move);
                    int s = AlphaBeta(nb, Opponent(currentColor), aiColor, rules, depth - 1, alpha, beta);
                    if (s < value) value = s;
                    if (value < beta) beta = value;
                    if (alpha >= beta) break;
                }
                return value;
            }
        }

        /// <summary>Sort moves in-place: most captures first, then by capture count descending.</summary>
        static void SortMoves(List<CheckersMove> moves)
        {
            // Stable sort: captures before non-captures, then by capture count descending
            moves.Sort((a, b) => b.Captures.Count.CompareTo(a.Captures.Count));
        }

        static int Evaluate(CheckersBoard board, PieceColor aiColor, GameRules rules)
        {
            var weights    = board.Size == 8 ? POS_WEIGHTS_8 : POS_WEIGHTS_10;
            var oppColor   = Opponent(aiColor);
            int score      = 0;
            int sz         = board.Size;

            // Back rank rows for each color
            int aiBackRow  = (aiColor  == PieceColor.Light) ? 0        : sz - 1;
            int oppBackRow = (oppColor == PieceColor.Light) ? 0        : sz - 1;

            for (int i = 0; i < sz * sz; i++)
            {
                var p = board.Cells[i];
                if (p.IsEmpty) continue;
                int pval = (p.Type == PieceType.King ? KING_VALUE : PIECE_VALUE) + weights[i];
                if (p.Color == aiColor)
                {
                    score += pval;
                    // Reward keeping men on own back rank (they guard promotion)
                    if (p.Type == PieceType.Man && (i / sz) == aiBackRow)
                        score += BACK_RANK_BONUS;
                }
                else
                {
                    score -= pval;
                    if (p.Type == PieceType.Man && (i / sz) == oppBackRow)
                        score -= BACK_RANK_BONUS;
                }
            }

            // Mobility bonus: more legal moves = better
            int aiMobility  = MoveGenerator.GetAllMoves(board, aiColor,  rules).Count;
            int oppMobility = MoveGenerator.GetAllMoves(board, oppColor, rules).Count;
            score += (aiMobility - oppMobility) * MOBILITY_BONUS;

            return score;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static PieceColor Opponent(PieceColor c) =>
            c == PieceColor.Light ? PieceColor.Dark : PieceColor.Light;

        static CheckersBoard ApplyMoveToBoard(CheckersBoard board, CheckersMove move)
        {
            var nb    = board.Clone();
            var piece = nb.Cells[move.From];
            nb.Cells[move.From] = default;
            foreach (int cap in move.Captures)
                nb.Cells[cap] = default;
            nb.Cells[move.To] = piece;

            // Promote
            int row     = move.To / nb.Size;
            int promRow = (piece.Color == PieceColor.Light) ? 0 : nb.Size - 1;
            if (piece.Type == PieceType.Man && row == promRow)
                nb.Cells[move.To] = new Piece { Color = piece.Color, Type = PieceType.King };

            return nb;
        }
    }
}

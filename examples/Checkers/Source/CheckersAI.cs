// CheckersAI.cs — Alpha-beta minimax AI for Checkers.
// Uses piece count + king advantage + positional heuristic + mobility + back-rank bonus.

using System;
using System.Collections.Generic;

namespace Checkers
{
    public static class CheckersAI
    {
        // Positional tables for KINGS only (symmetric — kings value center control).
        // Center squares are highest so kings gravitate toward active positions.
        private static readonly int[] KING_POS_8 =
        {
             0,  2,  0,  2,  0,  2,  0,  2,
             2,  0,  3,  0,  3,  0,  3,  0,
             0,  3,  0,  4,  0,  4,  0,  3,
             3,  0,  4,  0,  5,  0,  4,  0,
             0,  4,  0,  5,  0,  4,  0,  3,
             3,  0,  4,  0,  4,  0,  3,  0,
             0,  3,  0,  3,  0,  3,  0,  2,
             2,  0,  2,  0,  2,  0,  2,  0,
        };

        private static readonly int[] KING_POS_10 =
        {
             0,  2,  0,  2,  0,  2,  0,  2,  0,  2,
             2,  0,  3,  0,  3,  0,  3,  0,  3,  0,
             0,  3,  0,  4,  0,  4,  0,  4,  0,  3,
             3,  0,  4,  0,  5,  0,  5,  0,  4,  0,
             0,  4,  0,  5,  0,  5,  0,  5,  0,  4,
             4,  0,  5,  0,  5,  0,  5,  0,  4,  0,
             0,  4,  0,  5,  0,  5,  0,  4,  0,  3,
             3,  0,  4,  0,  4,  0,  4,  0,  3,  0,
             0,  3,  0,  3,  0,  3,  0,  3,  0,  2,
             2,  0,  2,  0,  2,  0,  2,  0,  2,  0,
        };

        private const int PIECE_VALUE      = 100;
        private const int KING_VALUE       = 280;
        private const int WIN_SCORE        = 10000;
        private const int MOBILITY_BONUS   = 3;   // per extra legal move vs opponent
        private const int BACK_RANK_BONUS  = 10;  // per guard on own home row (capped at 2)
        // Half-court bonus: once a piece crosses the midline it is in enemy territory
        // and worth significantly more — mirrors Draughts-AI _piece_and_board2val (7 vs 5, +40%).
        private const int ENEMY_HALF_BONUS = 40;
        private const int MAN_ADV_SCALE    = 4;   // small ramp within each half (tiebreaker)

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
            var kingWeights = board.Size == 8 ? KING_POS_8 : KING_POS_10;
            var oppColor    = Opponent(aiColor);
            int score       = 0;
            int sz          = board.Size;
            int mid         = sz / 2;  // row index of midline (4 for 8x8)

            // Home row: the very last row for each color (where pieces haven't yet moved).
            // Light promotes at row 0, starts + home = row sz-1.
            // Dark  promotes at row sz-1, starts + home = row 0.
            int aiHomeRow  = (aiColor  == PieceColor.Light) ? sz - 1 : 0;
            int oppHomeRow = (oppColor == PieceColor.Light) ? sz - 1 : 0;

            int aiBackGuards = 0, oppBackGuards = 0;

            for (int i = 0; i < sz * sz; i++)
            {
                var p = board.Cells[i];
                if (p.IsEmpty) continue;
                int row = i / sz;
                int col = i % sz;

                int pval;
                if (p.Type == PieceType.King)
                {
                    // Kings: high fixed value + center-biased positional bonus.
                    pval = KING_VALUE + kingWeights[i];
                }
                else
                {
                    // Men: directional advancement + big half-court bonus.
                    //
                    // Advancement: 0 at home row, (sz-1) at promotion row.
                    int adv = (p.Color == PieceColor.Light) ? (sz - 1 - row) : row;

                    // Half-court bonus (Draughts-AI _piece_and_board2val): once a piece
                    // crosses the midline it is in enemy territory and is worth ~40% more.
                    // This also means opponent pieces deep in OUR half draw a bigger penalty,
                    // since their pval is higher and we subtract it.
                    bool inEnemyHalf = (p.Color == PieceColor.Light) ? (row < mid) : (row >= mid);
                    int halfBonus    = inEnemyHalf ? ENEMY_HALF_BONUS : 0;

                    // Small center-column tiebreaker (inner columns have more move options).
                    int centerBonus  = Math.Min(col, sz - 1 - col);

                    pval = PIECE_VALUE + adv * MAN_ADV_SCALE + halfBonus + centerBonus;
                }

                if (p.Color == aiColor)
                {
                    score += pval;
                    if (p.Type == PieceType.Man && row == aiHomeRow)
                        aiBackGuards++;
                }
                else
                {
                    score -= pval;
                    if (p.Type == PieceType.Man && row == oppHomeRow)
                        oppBackGuards++;
                }
            }

            // Back-rank guard bonus: reward having 1-2 men on the very home row so the
            // opponent can't easily run pieces behind our formation and promote freely.
            score += Math.Min(aiBackGuards,  2) * BACK_RANK_BONUS;
            score -= Math.Min(oppBackGuards, 2) * BACK_RANK_BONUS;

            // Mobility: more legal moves = better board control and fewer forced captures.
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

            // Mid-sequence promotion: a Man that lands on its promotion row during a
            // multi-hop capture is immediately crowned — matches CheckersGame.ApplyMove.
            if (piece.Type == PieceType.Man && move.Path.Count > 2)
            {
                int midPromRow = (piece.Color == PieceColor.Light) ? 0 : nb.Size - 1;
                for (int pi = 1; pi < move.Path.Count - 1; pi++)
                {
                    if (move.Path[pi] / nb.Size == midPromRow)
                    {
                        piece = new Piece { Color = piece.Color, Type = PieceType.King };
                        break;
                    }
                }
            }

            nb.Cells[move.To] = piece;

            // Final destination promotion.
            int row     = move.To / nb.Size;
            int promRow = (piece.Color == PieceColor.Light) ? 0 : nb.Size - 1;
            if (piece.Type == PieceType.Man && row == promRow)
                nb.Cells[move.To] = new Piece { Color = piece.Color, Type = PieceType.King };

            return nb;
        }
    }
}

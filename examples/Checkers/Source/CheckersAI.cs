// CheckersAI.cs — Alpha-beta minimax AI for Checkers.
// Uses piece count + king advantage + positional heuristic + mobility + back-rank bonus,
// transposition table, killer moves, quiescence search, late-move reduction.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Checkers
{
    // ---------------------------------------------------------------------------
    // Transposition table
    // ---------------------------------------------------------------------------
    enum TTFlag : byte { Exact, LowerBound, UpperBound }

    struct TTEntry
    {
        public ulong Hash;
        public int   Eval;    // pure positional value — repPenalty is NOT baked in
        public int   Depth;
        public TTFlag Flag;
        public int   BestMoveFrom; // actual board cell of the best move (-1 = none)
        public int   BestMoveTo;
    }

    // ---------------------------------------------------------------------------
    // Killer move table  (2 killers per depth level)
    // ---------------------------------------------------------------------------
    struct KillerSlot
    {
        public int Move0From, Move0To;
        public int Move1From, Move1To;

        public void Store(int from, int to)
        {
            if (from == Move0From && to == Move0To) return; // already first slot
            Move1From = Move0From; Move1To = Move0To;
            Move0From = from;      Move0To = to;
        }

        public bool IsKiller0(int from, int to) => from == Move0From && to == Move0To;
        public bool IsKiller1(int from, int to) => from == Move1From && to == Move1To;
    }

    // Zero-allocation repetition tracker: a push/pop stack of position hashes on the current
    // search path. Replaces the per-node Dictionary<ulong,int> copy that caused O(n) allocation
    // at every interior node.
    sealed class RepStack
    {
        private readonly ulong[] _h;
        private int _top;

        public RepStack(int capacity, Dictionary<ulong, int>? seed = null)
        {
            _h = new ulong[capacity];
            if (seed != null)
                foreach (var kvp in seed)
                    for (int i = 0; i < kvp.Value && _top < capacity; i++)
                        _h[_top++] = kvp.Key;
        }

        private RepStack(ulong[] src, int top)
        {
            _h = (ulong[])src.Clone();
            _top = top;
        }

        public RepStack Clone() => new RepStack(_h, _top);

        public int CountOf(ulong hash)
        {
            int c = 0;
            for (int i = 0; i < _top; i++) if (_h[i] == hash) c++;
            return c;
        }

        public void Push(ulong hash) => _h[_top++] = hash;
        public void Pop() => _top--;
    }

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

        // Man positional tables (player-specific so they are directional).
        // Values from the reference engine concept: back-rank pieces and small center columns rewarded.
        // Light moves toward row 0 (lower index), so higher rows are "behind" light.
        private static readonly int[] MAN_POS_LIGHT_8 =
        {
            // row 0 (light promotion row — almost there)
             0,  0,  0,  0,  0,  0,  0,  0,
            // row 1
             0,  3,  0,  3,  0,  3,  0,  0,
            // row 2
             0,  2,  0,  3,  0,  2,  0,  0,
            // row 3
             0,  0,  1,  0,  1,  0,  0,  0,
            // row 4
             0,  0,  0,  1,  0,  1,  0,  0,
            // row 5
             0,  0,  1,  0,  1,  0,  0,  0,
            // row 6
             0,  0,  0,  0,  0,  0,  0,  1,
            // row 7 (light home row — back rank guard credit handled separately)
             0,  0,  3,  0,  3,  0,  3,  0,
        };

        private const int PIECE_VALUE      = 100;
        private const int KING_VALUE       = 280;
        private const int WIN_SCORE        = 10000;
        private const int BACK_RANK_BONUS  = 10;  // per guard on own home row (capped at 2)
        // Half-court bonus: once a piece crosses the midline it is in enemy territory
        // and worth significantly more.
        private const int ENEMY_HALF_BONUS = 40;
        private const int MAN_ADV_SCALE    = 4;   // small ramp within each half (tiebreaker)
        private const int REPETITION_PENALTY = 30; // per occurrence of a position in the search path
        // Edge-column penalty: men on col=0 or col=sz-1 have only one forward diagonal and
        // tend to become permanently stranded when the opponent guards their only advance square.
        private const int MAN_EDGE_PENALTY   = 15;
        // Piece-count advantage bonus (tiered, same concept as reference engine).
        // When ahead by 1+ pieces, earn a positional bonus that scales with how many remain.
        private const int KING_CORNER_PENALTY = 20; // penalty for own king stuck in a corner

        // Transposition table: power-of-2 size for cheap masking.
        private const int TT_SIZE = 1 << 20; // ~1 M entries
        private static readonly TTEntry[] _tt = new TTEntry[TT_SIZE];

        /// <summary>
        /// Return the best move for the given color on the given board.
        /// Returns null if no moves are available.
        /// positionCounts: game-level position hash counts so the AI avoids moves that
        /// approach the 3-fold repetition limit.
        /// </summary>
        public static CheckersMove? GetBestMove(CheckersBoard board, PieceColor color, GameRules rules, int depth,
                                                Dictionary<ulong, int>? positionCounts = null)
        {
            var moves = MoveGenerator.GetAllMoves(board, color, rules);
            if (moves.Count == 0) return null;
            if (moves.Count == 1) return moves[0];

            // Order moves: captures first (most captures first), then by eval
            SortMoves(moves);

            int bestScore = int.MinValue;
            CheckersMove? bestMove = null;

            // Seed repetition stack from game-level history (zero allocation during search).
            var rootRep = new RepStack(512, positionCounts);

            // Allocate killer table for this search (one slot per depth level + quiescence)
            int maxDepth = depth + 4; // +4 for quiescence headroom
            var killers  = new KillerSlot[maxDepth + 1];

#if !WEB
            // Evaluate root moves in parallel on desktop/mobile for faster response
            object sync = new object();
            var scoreMap = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            Parallel.ForEach(moves, move =>
            {
                var nb = ApplyMoveToBoard(board, move);
                // Each parallel branch clones the rep stack (one allocation per root move)
                var localRep = rootRep.Clone();
                var localKillers = (KillerSlot[])killers.Clone();
                int score = AlphaBeta(nb, Opponent(color), color, rules, depth - 1,
                                      int.MinValue / 2, int.MaxValue / 2, localRep,
                                      localKillers, 1);
                string key = $"{board.CellLabel(move.From)}->{board.CellLabel(move.To)}";
                scoreMap[key] = score;
                lock (sync)
                {
                    if (score > bestScore) { bestScore = score; bestMove = move; }
                }
            });
            foreach (var kv in scoreMap.OrderByDescending(x => x.Value))
                Console.WriteLine($"[AI] score {kv.Key} = {kv.Value}");
#else
            foreach (var move in moves)
            {
                var nb = ApplyMoveToBoard(board, move);
                int score = AlphaBeta(nb, Opponent(color), color, rules, depth - 1,
                                      int.MinValue / 2, int.MaxValue / 2, rootRep,
                                      killers, 1);
                Console.WriteLine($"[AI] score {board.CellLabel(move.From)}->{board.CellLabel(move.To)} = {score}");
                if (score > bestScore) { bestScore = score; bestMove = move; }
            }
#endif
            return bestMove;
        }

        static int AlphaBeta(CheckersBoard board, PieceColor currentColor, PieceColor aiColor,
                             GameRules rules, int depth, int alpha, int beta,
                             RepStack rep,
                             KillerSlot[] killers, int ply)
        {
            var moves = MoveGenerator.GetAllMoves(board, currentColor, rules);

            if (moves.Count == 0)
            {
                // Current player has no moves — they lose
                return currentColor == aiColor ? -WIN_SCORE : WIN_SCORE;
            }

            // Repetition penalty: discourages revisiting positions.
            // Applied to the returned score but NOT stored in TT (it is path-dependent).
            ulong posHash  = BoardHash(board, currentColor);
            int repCount   = rep.CountOf(posHash);
            // From AI's (fixed) perspective: penalise when AI is the one repeating;
            // grant a bonus when the opponent is stuck repeating.
            int repAdj = (currentColor == aiColor)
                ? -(repCount * REPETITION_PENALTY)
                :  (repCount * REPETITION_PENALTY);
            rep.Push(posHash); // record this visit; popped before every return below

            // --- Transposition table lookup ---
            int ttIndex = (int)(posHash & (TT_SIZE - 1));
            ref TTEntry tte = ref _tt[ttIndex];
            if (tte.Hash == posHash && tte.Depth >= depth)
            {
                int raw = tte.Eval; // stored as pure positional value
                switch (tte.Flag)
                {
                    case TTFlag.Exact: rep.Pop(); return raw + repAdj;
                    case TTFlag.LowerBound: if (raw > alpha) alpha = raw; break;
                    case TTFlag.UpperBound: if (raw < beta)  beta  = raw; break;
                }
                if (alpha >= beta) { rep.Pop(); return raw + repAdj; }
            }

            // --- Quiescence search at depth 0: keep searching captures ---
            if (depth <= 0)
            {
                bool hasCapture = moves.Exists(m => m.Captures.Count > 0);
                if (!hasCapture)
                { rep.Pop(); return Evaluate(board, aiColor) + repAdj; }
                // Fall through with depth 0 — loop below handles capture-only moves
            }

            // Order moves: TT best move first, then killers, then captures, then rest.
            // BUG FIX #4: pass actual From/To coords (not a list index) for TT move ordering.
            int ttBestFrom = (tte.Hash == posHash) ? tte.BestMoveFrom : -1;
            int ttBestTo   = (tte.Hash == posHash) ? tte.BestMoveTo   : -1;
            SortMovesWithHints(moves, ttBestFrom, ttBestTo,
                               ply < killers.Length ? killers[ply] : default);

            // If quiescence mode, strip non-captures
            if (depth <= 0)
            {
                moves.RemoveAll(m => m.Captures.Count == 0);
                if (moves.Count == 0)
                { rep.Pop(); return Evaluate(board, aiColor) + repAdj; }
            }

            bool maximizing = currentColor == aiColor;
            int  alphaOrig  = alpha;
            int  bestVal    = maximizing ? int.MinValue / 2 : int.MaxValue / 2;
            int  bestFrom   = -1, bestTo = -1;

            for (int mi = 0; mi < moves.Count; mi++)
            {
                var move = moves[mi];
                var nb   = ApplyMoveToBoard(board, move);

                // Late Move Reduction: reduce depth for late non-capture moves at sufficient depth
                int childDepth = depth - 1;
                if (depth >= 3 && mi >= 5 && move.Captures.Count == 0)
                    childDepth--;

                int s = AlphaBeta(nb, Opponent(currentColor), aiColor, rules, childDepth,
                                  alpha, beta, rep, killers, ply + 1);

                // Re-search at full depth if LMR returned a surprisingly good score
                if (childDepth < depth - 1 && (maximizing ? s > alpha : s < beta))
                    s = AlphaBeta(nb, Opponent(currentColor), aiColor, rules, depth - 1,
                                  alpha, beta, rep, killers, ply + 1);

                if (maximizing)
                {
                    if (s > bestVal) { bestVal = s; bestFrom = move.From; bestTo = move.To; }
                    if (bestVal > alpha) alpha = bestVal;
                    if (alpha >= beta)
                    {
                        if (move.Captures.Count == 0 && ply < killers.Length)
                            killers[ply].Store(move.From, move.To);
                        break;
                    }
                }
                else
                {
                    if (s < bestVal) { bestVal = s; bestFrom = move.From; bestTo = move.To; }
                    if (bestVal < beta) beta = bestVal;
                    if (alpha >= beta)
                    {
                        if (move.Captures.Count == 0 && ply < killers.Length)
                            killers[ply].Store(move.From, move.To);
                        break;
                    }
                }
            }

            // BUG FIX #3: store pure positional bestVal in TT (without repAdj).
            // repAdj is path-dependent; baking it in caused future TT lookups to use
            // the wrong value when the path history differed.
            TTFlag flag;
            if (bestVal <= alphaOrig)  flag = TTFlag.UpperBound;
            else if (bestVal >= beta)  flag = TTFlag.LowerBound;
            else                       flag = TTFlag.Exact;
            _tt[ttIndex] = new TTEntry { Hash = posHash, Eval = bestVal, Depth = depth,
                                         Flag = flag, BestMoveFrom = bestFrom, BestMoveTo = bestTo };

            rep.Pop();
            return bestVal + repAdj;
        }

        /// <summary>Sort moves: TT best first, killers next, captures (most first), then rest.</summary>
        static void SortMovesWithHints(List<CheckersMove> moves, int ttFrom, int ttTo, KillerSlot killers)
        {
            moves.Sort((a, b) =>
            {
                int sa = ScoreMove(a, ttFrom, ttTo, killers);
                int sb = ScoreMove(b, ttFrom, ttTo, killers);
                return sb.CompareTo(sa);
            });
        }

        static int ScoreMove(CheckersMove m, int ttFrom, int ttTo, KillerSlot killers)
        {
            if (ttFrom >= 0 && m.From == ttFrom && m.To == ttTo) return 3000;
            if (killers.IsKiller0(m.From, m.To)) return 2000;
            if (killers.IsKiller1(m.From, m.To)) return 1900;
            if (m.Captures.Count > 0) return 100 + m.Captures.Count * 10;
            return 0;
        }

        /// <summary>Sort moves in-place: most captures first, then by capture count descending.</summary>
        static void SortMoves(List<CheckersMove> moves)
        {
            moves.Sort((a, b) => b.Captures.Count.CompareTo(a.Captures.Count));
        }

        static int Evaluate(CheckersBoard board, PieceColor aiColor)
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
            int aiCount = 0, oppCount = 0;
            var aiKingPos = new System.Collections.Generic.List<(int r, int c)>();
            var oppPos    = new System.Collections.Generic.List<(int r, int c)>();

            // Corner squares for king-corner penalty
            int cornerRow0 = 0, cornerRow1 = sz - 1;

            for (int i = 0; i < sz * sz; i++)
            {
                var p = board.Cells[i];
                if (p.IsEmpty) continue;
                int row = i / sz;
                int col = i % sz;

                int pval;
                if (p.Type == PieceType.King)
                {
                    pval = KING_VALUE + kingWeights[i];

                    // King-in-corner penalty: corners are low-mobility traps.
                    if ((row == cornerRow0 || row == cornerRow1) && (col == 0 || col == sz - 1))
                    {
                        if (p.Color == aiColor)  score -= KING_CORNER_PENALTY;
                        else                      score += KING_CORNER_PENALTY;
                        // Don't add pval for corner king — it's penalised enough
                        if (p.Color == aiColor) { aiCount++; aiKingPos.Add((row, col)); }
                        else                    { oppCount++; oppPos.Add((row, col)); }
                        continue;
                    }
                }
                else
                {
                    // Men: directional advancement + big half-court bonus.
                    int adv = (p.Color == PieceColor.Light) ? (sz - 1 - row) : row;
                    bool inEnemyHalf = (p.Color == PieceColor.Light) ? (row < mid) : (row >= mid);
                    int halfBonus    = inEnemyHalf ? ENEMY_HALF_BONUS : 0;
                    int centerBonus  = Math.Min(col, sz - 1 - col);

                    // Directional man positional table (8x8 only; fall back to generic for 10x10).
                    int manPosBonus = 0;
                    if (sz == 8)
                    {
                        // Light's table is indexed as-is; Dark's is mirrored vertically.
                        int tblIdx = (p.Color == PieceColor.Light)
                            ? i
                            : (sz * sz - 1 - i);
                        if (tblIdx < MAN_POS_LIGHT_8.Length)
                            manPosBonus = MAN_POS_LIGHT_8[tblIdx];
                    }

                    // Edge-column penalty: a man on col=0 or col=sz-1 has only one forward
                    // diagonal, making it prone to getting permanently stuck on the A/H file.
                    // Penalise regardless of row so the search actively avoids these squares.
                    int edgePenalty = (col == 0 || col == sz - 1) ? MAN_EDGE_PENALTY : 0;

                    pval = PIECE_VALUE + adv * MAN_ADV_SCALE + halfBonus + centerBonus + manPosBonus - edgePenalty;
                }

                if (p.Color == aiColor)
                {
                    score += pval;
                    aiCount++;
                    if (p.Type == PieceType.Man && row == aiHomeRow)
                        aiBackGuards++;
                    if (p.Type == PieceType.King)
                        aiKingPos.Add((row, col));
                }
                else
                {
                    score -= pval;
                    oppCount++;
                    if (p.Type == PieceType.Man && row == oppHomeRow)
                        oppBackGuards++;
                    oppPos.Add((row, col));
                }
            }

            // Back-rank guard bonus.
            score += Math.Min(aiBackGuards,  2) * BACK_RANK_BONUS;
            score -= Math.Min(oppBackGuards, 2) * BACK_RANK_BONUS;

            // Piece-count advantage bonus (tiered):
            // When ahead by 1+ pieces we get a bonus that scales with how thinned-out the
            // board is — in an endgame a single extra piece is worth much more than in opening.
            int diff = aiCount - oppCount;
            if (diff != 0)
            {
                int total = aiCount + oppCount;
                int bonus = PieceCountBonus(Math.Max(aiCount, oppCount), total);
                score += diff > 0 ? bonus : -bonus;
            }

            // Tail-pin bonus: an AI king that sits directly behind two enemy men in a
            // diagonal line "pins" both. Concept from reference engine.
            score += TailPinBonus(board, aiColor, sz);

            // Endgame king-chase: drive each AI king toward its nearest opponent.
            int totalPieces = aiCount + oppCount;
            if (totalPieces <= 8 && aiKingPos.Count > 0 && oppPos.Count > 0)
            {
                const int ENDGAME_CHASE = 6;
                foreach (var (kr, kc) in aiKingPos)
                {
                    int minDist = sz * 2;
                    foreach (var (or, oc) in oppPos)
                    {
                        int d = Math.Max(Math.Abs(kr - or), Math.Abs(kc - oc));
                        if (d < minDist) minDist = d;
                    }
                    score += (sz - minDist) * ENDGAME_CHASE;
                }
            }

            return score;
        }

        /// <summary>
        /// Tiered piece-count advantage bonus. The leading side earns more when the board
        /// is thin (fewer total pieces = more decisive endgame).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int PieceCountBonus(int leadingCount, int totalPieces)
        {
            // Bonus for being ahead diminishes as more pieces remain on the board.
            if (totalPieces <= 4)  return 200;
            if (totalPieces <= 6)  return 150;
            if (totalPieces <= 8)  return 100;
            if (totalPieces <= 10) return 60;
            if (totalPieces <= 12) return 30;
            return 10;
        }

        /// <summary>
        /// Tail-pin bonus: count diagonal "pins" where an AI king sits two steps behind two
        /// enemy men in the same diagonal direction. Each pin scores +5.
        /// </summary>
        static int TailPinBonus(CheckersBoard board, PieceColor aiColor, int sz)
        {
            PieceColor opp = Opponent(aiColor);
            int aiPins = 0, oppPins = 0;

            for (int i = 0; i < sz * sz; i++)
            {
                var p = board.Cells[i];
                if (p.Type != PieceType.King) continue;

                int row = i / sz;
                int col = i % sz;

                // Check four diagonal directions for two-step inline enemy men.
                int[] drs = { -1, -1, 1,  1 };
                int[] dcs = { -1,  1, -1, 1 };

                for (int d = 0; d < 4; d++)
                {
                    int r1 = row + drs[d], c1 = col + dcs[d];
                    int r2 = row + drs[d]*2, c2 = col + dcs[d]*2;
                    if (r1 < 0 || r1 >= sz || c1 < 0 || c1 >= sz) continue;
                    if (r2 < 0 || r2 >= sz || c2 < 0 || c2 >= sz) continue;

                    var p1 = board.Cells[r1 * sz + c1];
                    var p2 = board.Cells[r2 * sz + c2];

                    if (p1.IsEmpty || p2.IsEmpty) continue;
                    if (p1.Type != PieceType.Man || p2.Type != PieceType.Man) continue;

                    PieceColor pinnedColor = (p.Color == aiColor) ? opp : aiColor;
                    if (p1.Color == pinnedColor && p2.Color == pinnedColor)
                    {
                        if (p.Color == aiColor) aiPins++;
                        else                    oppPins++;
                    }
                }
            }

            return (aiPins - oppPins) * 5;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static PieceColor Opponent(PieceColor c) =>
            c == PieceColor.Light ? PieceColor.Dark : PieceColor.Light;

        /// <summary>Fast board hash for repetition detection.</summary>
        static ulong BoardHash(CheckersBoard board, PieceColor turn)
        {
            ulong hash = turn == PieceColor.Light ? 0xA5A5A5A5A5A5A5A5UL : 0x5A5A5A5A5A5A5A5AUL;
            for (int i = 0; i < board.Cells.Length; i++)
            {
                var p = board.Cells[i];
                if (p.IsEmpty) continue;
                ulong pcode = (ulong)i * 1000003UL
                            + (p.Color == PieceColor.Light ? 1UL : 2UL)
                            + (p.Type  == PieceType.King   ? 4UL : 0UL);
                hash ^= pcode * 0x9E3779B97F4A7C15UL;
            }
            return hash;
        }

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

namespace Rummy.Logic.Melds
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Cards;

    /// <summary>
    /// Pure validation and scoring utilities for Rummy melds.
    /// All methods are stateless. Handles Jester wildcards for Tabletop Meld.
    /// </summary>
    public static class MeldValidator
    {
        // ─────────────────────────────────────────────────────────
        // Point values
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Point value of a card for Gin Rummy deadwood scoring.
        /// Ace = 1, number cards = face value, J/Q/K = 10.
        /// </summary>
        public static int GinPointValue(Card card)
        {
            if (card.IsJester) return 0; // Wildcards not used in Gin Rummy
            return card.Type switch
            {
                CardType.Ace   => 1,
                CardType.Two   => 2,
                CardType.Three => 3,
                CardType.Four  => 4,
                CardType.Five  => 5,
                CardType.Six   => 6,
                CardType.Seven => 7,
                CardType.Eight => 8,
                CardType.Nine  => 9,
                CardType.Ten   => 10,
                CardType.Jack  => 10,
                CardType.Queen => 10,
                CardType.King  => 10,
                _              => 0,
            };
        }

        /// <summary>
        /// Point value of a card for Tabletop Meld scoring (entry threshold + end-round scoring).
        /// Ace = 1, number cards = face value, J/Q/K = 10, Jester held = 30 penalty points.
        /// </summary>
        public static int TabletopPointValue(Card card)
        {
            if (card.IsJester) return 30;
            return GinPointValue(card);
        }

        /// <summary>
        /// Rummy rank of a card (Ace = 1 low, King = 13).
        /// Used for run validation where Ace is always low.
        /// </summary>
        public static int RummyRank(Card card)
        {
            return card.Type switch
            {
                CardType.Ace   => 1,
                CardType.Two   => 2,
                CardType.Three => 3,
                CardType.Four  => 4,
                CardType.Five  => 5,
                CardType.Six   => 6,
                CardType.Seven => 7,
                CardType.Eight => 8,
                CardType.Nine  => 9,
                CardType.Ten   => 10,
                CardType.Jack  => 11,
                CardType.Queen => 12,
                CardType.King  => 13,
                _              => 0,
            };
        }

        // ─────────────────────────────────────────────────────────
        // Meld validation
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="cards"/> form a valid set:
        /// 3–4 cards, same rank, no two with the same suit (Jesters fill wildcard slots).
        /// </summary>
        public static bool IsValidSet(IList<Card> cards)
        {
            if (cards == null || cards.Count < 3 || cards.Count > 4)
                return false;

            var nonWild = cards.Where(c => !c.IsJester).ToList();
            // All non-wild cards must share the same rank
            if (nonWild.Count == 0) return false; // all jesters — don't count as a set
            var rank = nonWild[0].Type;
            if (nonWild.Any(c => c.Type != rank)) return false;
            // No duplicate suits among non-wild
            var suits = nonWild.Select(c => c.Suit).ToList();
            return suits.Count == suits.Distinct().Count();
        }

        /// <summary>
        /// Returns true if <paramref name="cards"/> form a valid run:
        /// 3+ cards, same suit, consecutive ranks (Ace = 1, low only; Jesters fill wildcard gaps).
        /// </summary>
        public static bool IsValidRun(IList<Card> cards)
        {
            if (cards == null || cards.Count < 3)
                return false;

            var nonWild = cards.Where(c => !c.IsJester).ToList();
            if (nonWild.Count == 0) return false;

            // All non-wild cards must share the same suit
            var suit = nonWild[0].Suit;
            if (nonWild.Any(c => c.Suit != suit)) return false;

            // Extract ranks and determine run extents
            var ranks = nonWild.Select(c => RummyRank(c)).OrderBy(r => r).ToList();
            int wildcards = cards.Count - nonWild.Count;

            // The full span of the run = [min..max] — verify that the number of
            // missing slots (gaps) can be filled by available wildcards.
            int min  = ranks[0];
            int max  = ranks[^1];
            int span = max - min + 1; // total slots needed for contiguous run

            if (span > cards.Count) return false; // too many gaps even with wildcards
            if (span < nonWild.Count) return false; // duplicate ranks in run

            // Count how many rank-slots within [min..max] are missing from nonWild
            int gaps = span - nonWild.Count;
            return gaps <= wildcards;
        }

        /// <summary>
        /// Returns true if <paramref name="cards"/> form any valid Rummy meld (set or run).
        /// </summary>
        public static bool IsValid(IList<Card> cards)
            => IsValidSet(cards) || IsValidRun(cards);

        /// <summary>
        /// Determines the meld type of a valid group, or null if invalid.
        /// Prefers Set when both interpretations are technically possible (edge case).
        /// </summary>
        public static MeldType? GetMeldType(IList<Card> cards)
        {
            if (IsValidSet(cards)) return MeldType.Set;
            if (IsValidRun(cards)) return MeldType.Run;
            return null;
        }

        // ─────────────────────────────────────────────────────────
        // Deadwood minimisation (Gin Rummy)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the combination of melds from <paramref name="hand"/> that minimises deadwood.
        /// Returns the minimum deadwood total; outputs the best melds and leftover cards.
        /// </summary>
        public static int MinDeadwood(
            IList<Card> hand,
            out List<Meld>  bestMelds,
            out List<Card>  deadwood)
        {
            var best = new State { Deadwood = int.MaxValue };
            var current = new State { Cards = hand.ToList() };
            Backtrack(current, 0, ref best);

            bestMelds = best.Melds ?? new List<Meld>();
            deadwood  = best.Leftover ?? hand.ToList();
            return best.Deadwood;
        }

        // ── Internal backtracker ──────────────────────────────────

        struct State
        {
            public List<Card>  Cards;
            public List<Meld>  Melds;
            public List<Card>  Leftover;
            public int         Deadwood;
        }

        static void Backtrack(State s, int startIdx, ref State best)
        {
            // Lower-bound prune: even if remaining cards are all zero-value, can we beat best?
            int floorDw = s.Cards.Skip(startIdx).Sum(c => 0); // 0 lower bound

            bool foundMeld = false;
            var cards = s.Cards;
            int n = cards.Count;

            for (int i = startIdx; i < n; i++)
            {
                // Try sets starting at cards[i]
                for (int j = i + 1; j < n; j++)
                {
                    for (int k = j + 1; k < n; k++)
                    {
                        var trio = new List<Card> { cards[i], cards[j], cards[k] };
                        if (IsValidSet(trio) || IsValidRun(trio))
                        {
                            foundMeld = true;
                            var meldType = IsValidSet(trio) ? MeldType.Set : MeldType.Run;

                            // Try extending to 4-card meld
                            List<int> indices4 = TryExtend4(cards, i, j, k, meldType);
                            foreach (var quad in AllCombos4(cards, i, j, k))
                            {
                                CheckMeld(s, quad.indices, quad.cards, startIdx, ref best);
                            }
                            // Also try the 3-card version
                            CheckMeld(s, new[] { i, j, k }, trio, startIdx, ref best);
                        }
                    }
                }
            }

            // No meld found from startIdx onwards — score this leaf
            if (!foundMeld)
            {
                int dw = cards.Skip(startIdx).Sum(c => GinPointValue(c));
                int totalDw = (s.Cards.Count > 0 ? s.Cards.Take(startIdx).Sum(c => 0) : 0) + dw;
                // Deadwood is just the sum of unmatched cards (those not in any meld)
                // We need to calculate properly:
                var leftover = new List<Card>();
                for (int i = startIdx; i < cards.Count; i++)
                    leftover.Add(cards[i]);
                int actualDw = leftover.Sum(c => GinPointValue(c));

                if (actualDw < best.Deadwood)
                {
                    best.Deadwood = actualDw;
                    best.Melds    = new List<Meld>(s.Melds ?? new List<Meld>());
                    best.Leftover = leftover;
                }
            }
        }

        static void CheckMeld(State s, int[] idxs, List<Card> meldCards, int startIdx, ref State best)
        {
            var meldType = IsValidSet(meldCards) ? MeldType.Set : MeldType.Run;
            var meld = new Meld(meldType, meldCards);

            // Remove meld cards from working list (in reverse index order to preserve indices)
            var remaining = new List<Card>(s.Cards);
            foreach (var idx in idxs.OrderByDescending(x => x))
                remaining.RemoveAt(idx);

            var newState = new State
            {
                Cards  = remaining,
                Melds  = new List<Meld>(s.Melds ?? new List<Meld>()) { meld },
            };

            // Continue from the new list start (recompute from 0 since indices shifted)
            Backtrack(newState, 0, ref best);
        }

        static List<int> TryExtend4(IList<Card> cards, int i, int j, int k, MeldType type)
        {
            var result = new List<int>();
            for (int x = 0; x < cards.Count; x++)
            {
                if (x == i || x == j || x == k) continue;
                var quad = new List<Card> { cards[i], cards[j], cards[k], cards[x] };
                bool valid = type == MeldType.Set ? IsValidSet(quad) : IsValidRun(quad);
                if (valid) result.Add(x);
            }
            return result;
        }

        struct Quad { public int[] indices; public List<Card> cards; }

        static IEnumerable<Quad> AllCombos4(IList<Card> cards, int i, int j, int k)
        {
            for (int x = 0; x < cards.Count; x++)
            {
                if (x == i || x == j || x == k) continue;
                var quad = new List<Card> { cards[i], cards[j], cards[k], cards[x] };
                if (IsValidSet(quad) || IsValidRun(quad))
                    yield return new Quad { indices = new[] { i, j, k, x }, cards = quad };
            }
        }
    }
}

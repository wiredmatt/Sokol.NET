namespace Rummy.Logic.GinRummy
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Cards;
    using Rummy.Logic.Melds;

    // ────────────────────────────────────────────────────────────────────────────
    // The Strategist — optimal AI using minimum-deadwood heuristic
    // ────────────────────────────────────────────────────────────────────────────

    public class StrategistAI : IGinAI
    {
        public bool TakeDiscard(IList<Card> hand, Card topDiscard)
        {
            // Take the discard if it reduces deadwood more than not taking it
            int dwWithout = MeldValidator.MinDeadwood(hand, out _, out _);

            var withDiscard = new List<Card>(hand) { topDiscard };
            // Simulate discarding the worst card from the 11-card hand
            int bestDw = int.MaxValue;
            for (int i = 0; i < withDiscard.Count; i++)
            {
                var candidate = new List<Card>(withDiscard);
                candidate.RemoveAt(i);
                int dw = MeldValidator.MinDeadwood(candidate, out _, out _);
                if (dw < bestDw) bestDw = dw;
            }
            return bestDw < dwWithout;
        }

        public int ChooseDiscard(IList<Card> hand)
        {
            // Discard the card whose removal minimises deadwood; tie-break by highest point value
            int bestDw    = int.MaxValue;
            int bestPts   = -1;
            int bestIdx   = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var candidate = new List<Card>(hand);
                candidate.RemoveAt(i);
                int dw  = MeldValidator.MinDeadwood(candidate, out _, out _);
                int pts = MeldValidator.GinPointValue(hand[i]);
                if (dw < bestDw || (dw == bestDw && pts > bestPts))
                {
                    bestDw  = dw;
                    bestPts = pts;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public bool ShouldKnock(IList<Card> hand)
        {
            int dw = MeldValidator.MinDeadwood(hand, out _, out _);
            // Knock if deadwood ≤ 5 (conservative — waits for a good knock)
            return dw <= 5;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // The Conservator — conservative AI; prefers low deadwood, rare early knocks
    // ────────────────────────────────────────────────────────────────────────────

    public class ConservatorAI : IGinAI
    {
        public bool TakeDiscard(IList<Card> hand, Card topDiscard)
        {
            // Only take discard if it directly completes a meld
            var withDiscard = new List<Card>(hand) { topDiscard };
            for (int i = 0; i < hand.Count; i++)
            {
                var candidate = new List<Card>(withDiscard);
                candidate.RemoveAt(i);
                int dw = MeldValidator.MinDeadwood(candidate, out _, out _);
                if (dw == 0) return true; // completes Gin
            }
            return false;
        }

        public int ChooseDiscard(IList<Card> hand)
        {
            // Discard highest-value isolated card
            int maxPts = -1;
            int maxIdx = 0;
            MeldValidator.MinDeadwood(hand, out _, out var deadwood);
            // Prefer to discard from deadwood
            for (int i = 0; i < hand.Count; i++)
            {
                int pts = MeldValidator.GinPointValue(hand[i]);
                bool isDeadwood = deadwood.Contains(hand[i]);
                if (isDeadwood && pts > maxPts) { maxPts = pts; maxIdx = i; }
            }
            if (maxPts >= 0) return maxIdx;
            // No deadwood (hand fully melded) — find a safe discard that preserves GIN
            for (int i = 0; i < hand.Count; i++)
            {
                var candidate = new List<Card>(hand);
                candidate.RemoveAt(i);
                if (MeldValidator.MinDeadwood(candidate, out _, out _) == 0)
                    return i;
            }
            return 0;
        }

        public bool ShouldKnock(IList<Card> hand)
        {
            int dw = MeldValidator.MinDeadwood(hand, out _, out _);
            return dw == 0; // only knocks on Gin
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // The Casual — semi-random, sometimes suboptimal
    // ────────────────────────────────────────────────────────────────────────────

    public class CasualAI : IGinAI
    {
        private static readonly Random _rng = new Random();

        public bool TakeDiscard(IList<Card> hand, Card topDiscard)
        {
            // 40% random take, otherwise use simple heuristic
            if (_rng.NextDouble() < 0.40) return _rng.Next(2) == 0;
            return MeldValidator.GinPointValue(topDiscard) <= 5;
        }

        public int ChooseDiscard(IList<Card> hand)
        {
            // If all cards are melded, find a safe discard that preserves GIN
            MeldValidator.MinDeadwood(hand, out _, out var deadwood);
            if (deadwood.Count == 0)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    var candidate = new List<Card>(hand);
                    candidate.RemoveAt(i);
                    if (MeldValidator.MinDeadwood(candidate, out _, out _) == 0)
                        return i;
                }
                return 0;
            }

            // 30% random discard, otherwise discard highest-point deadwood card
            if (_rng.NextDouble() < 0.30)
                return _rng.Next(hand.Count);

            int maxPts = -1;
            int maxIdx = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                bool dw  = deadwood.Any(c => ReferenceEquals(c, hand[i]));
                int  pts = MeldValidator.GinPointValue(hand[i]);
                if (dw && pts > maxPts) { maxPts = pts; maxIdx = i; }
            }
            return maxPts >= 0 ? maxIdx : _rng.Next(hand.Count);
        }

        public bool ShouldKnock(IList<Card> hand)
        {
            int dw = MeldValidator.MinDeadwood(hand, out _, out _);
            // Knock randomly when eligible
            return dw <= 10 && _rng.Next(3) == 0;
        }
    }
}

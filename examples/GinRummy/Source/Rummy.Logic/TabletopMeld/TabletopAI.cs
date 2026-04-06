namespace Rummy.Logic.TabletopMeld
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Cards;
    using Rummy.Logic.Melds;

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers used by all Tabletop AI archetypes
    // ─────────────────────────────────────────────────────────────────────────

    internal static class TabletopAIHelper
    {
        /// <summary>
        /// Find all valid groups of 3+ cards within a hand.
        /// Returns groups sorted by descending total point value.
        /// </summary>
        public static List<List<Card>> FindPlayableGroups(IReadOnlyList<Card> hand)
        {
            var results = new List<List<Card>>();
            var handList = hand.ToList();

            // Try all combinations of 3, 4, ... cards
            for (int size = handList.Count; size >= 3; size--)
                FindCombinations(handList, size, 0, new List<Card>(), results);

            // Deduplicate and sort by point value descending
            return results
                .Where(g => MeldValidator.IsValid(g))
                .OrderByDescending(g => g.Sum(MeldValidator.TabletopPointValue))
                .ToList();
        }

        private static void FindCombinations(
            List<Card> hand, int size, int start,
            List<Card> current, List<List<Card>> results)
        {
            if (current.Count == size)
            {
                results.Add(new List<Card>(current));
                return;
            }
            for (int i = start; i < hand.Count; i++)
            {
                current.Add(hand[i]);
                FindCombinations(hand, size, i + 1, current, results);
                current.RemoveAt(current.Count - 1);
            }
        }

        /// <summary>Total point value of a hand.</summary>
        public static int HandValue(IReadOnlyList<Card> hand) =>
            hand.Sum(MeldValidator.TabletopPointValue);

        /// <summary>
        /// Given a hand, find a set of non-overlapping groups that satisfy the
        /// first-entry ≥30-point requirement, preferring minimum cards played.
        /// Returns null if impossible.
        /// </summary>
        public static List<List<Card>>? FindEntryMelds(IReadOnlyList<Card> hand)
        {
            var all = FindPlayableGroups(hand);
            // Greedy: pick the highest-value groups without reusing cards
            var used    = new HashSet<int>(); // indices into hand
            var chosen  = new List<List<Card>>();
            int total   = 0;

            // Map back to hand indices for overlap detection
            var handList = hand.ToList();
            foreach (var group in all)
            {
                // Find indices of these cards in the hand (handle duplicates)
                var indices = new List<int>();
                var tempHand = handList.ToList();
                bool ok = true;
                foreach (var c in group)
                {
                    int idx = tempHand.FindIndex(h => h.Equals(c));
                    if (idx < 0 || used.Contains(OriginalIndex(handList, c, used)))
                    {
                        ok = false;
                        break;
                    }
                    int origIdx = OriginalIndex(handList, c, used);
                    used.Add(origIdx);
                    indices.Add(origIdx);
                    tempHand.RemoveAt(idx);
                }
                if (!ok) { foreach (var i in indices) used.Remove(i); continue; }

                chosen.Add(group);
                total += group.Sum(MeldValidator.TabletopPointValue);
                if (total >= TabletopMeldGame.EntryMinPoints) return chosen;
            }
            return null;
        }

        private static int OriginalIndex(List<Card> hand, Card card, HashSet<int> usedIndices)
        {
            for (int i = 0; i < hand.Count; i++)
                if (!usedIndices.Contains(i) && hand[i].Equals(card))
                    return i;
            return -1;
        }

        /// <summary>
        /// Try to extend any existing table group with a card from the working hand.
        /// Returns (handIdx, groupIdx, position) or null if nothing fits.
        /// </summary>
        public static (int handIdx, int groupIdx, int position)?
            FindExtension(IReadOnlyList<Card> hand, IReadOnlyList<IReadOnlyList<Card>> table)
        {
            for (int h = 0; h < hand.Count; h++)
            {
                var card = hand[h];
                for (int g = 0; g < table.Count; g++)
                {
                    var group = table[g].ToList();
                    // Try appending at start
                    var testStart = new List<Card> { card };
                    testStart.AddRange(group);
                    if (MeldValidator.IsValid(testStart)) return (h, g, 0);

                    // Try appending at end
                    var testEnd = new List<Card>(group) { card };
                    if (MeldValidator.IsValid(testEnd)) return (h, g, group.Count);
                }
            }
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The Strategist — plays optimally; forms highest-value melds first,
    // extends table groups when possible, and manages hand efficiently.
    // ─────────────────────────────────────────────────────────────────────────

    public class TabletopStrategistAI : ITabletopAI
    {
        public bool TakeTurn(TabletopMeldGame game, TabletopSnapshot snap)
        {
            bool madeAPlay = false;
            var hand  = game.AIWorkingHand;
            var table = game.AIWorkingTable;

            // --- Entry attempt ---
            if (!snap.Players.First(p => !p.IsHuman && p.Index == snap.CurrentPlayerIdx).HasEntered)
                return TryEntry(game, snap);

            // --- Entered: extend table groups first, then form new groups ---
            bool progress = true;
            while (progress)
            {
                progress = false;
                hand  = game.AIWorkingHand;
                table = game.AIWorkingTable;

                // Try extending existing groups
                var ext = TabletopAIHelper.FindExtension(hand, table);
                if (ext.HasValue)
                {
                    game.AIAddToGroup(ext.Value.handIdx, ext.Value.groupIdx, ext.Value.position);
                    madeAPlay = progress = true;
                    continue;
                }

                // Try forming new groups from hand
                var groups = TabletopAIHelper.FindPlayableGroups(hand);
                if (groups.Count > 0)
                {
                    var best = groups[0];
                    var indices = FindHandIndices(hand, best);
                    if (indices != null)
                    {
                        game.AICreateGroup(indices);
                        madeAPlay = progress = true;
                    }
                }
            }

            return madeAPlay;
        }

        private bool TryEntry(TabletopMeldGame game, TabletopSnapshot snap)
        {
            var entryMelds = TabletopAIHelper.FindEntryMelds(game.AIWorkingHand);
            if (entryMelds == null) return false;

            foreach (var meld in entryMelds)
            {
                var indices = FindHandIndices(game.AIWorkingHand, meld);
                if (indices == null) return false;
                game.AICreateGroup(indices);
            }
            return true;
        }

        private static List<int>? FindHandIndices(IReadOnlyList<Card> hand, List<Card> cards)
        {
            var tempHand = hand.ToList();
            var indices  = new List<int>();
            foreach (var c in cards)
            {
                int idx = tempHand.FindIndex(h => h.Equals(c));
                if (idx < 0) return null;
                // Find original index in full hand
                int origIdx = -1;
                var usedSoFar = new HashSet<int>(indices);
                for (int i = 0; i < hand.Count; i++)
                    if (!usedSoFar.Contains(i) && hand[i].Equals(c)) { origIdx = i; break; }
                if (origIdx < 0) return null;
                indices.Add(origIdx);
                tempHand.RemoveAt(idx);
            }
            return indices;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The Conservator — cautious player; only plays if hand value drops
    // significantly. Prefers not to extend the table unless it clears many cards.
    // ─────────────────────────────────────────────────────────────────────────

    public class TabletopConservatorAI : ITabletopAI
    {
        public bool TakeTurn(TabletopMeldGame game, TabletopSnapshot snap)
        {
            var currentPlayer = snap.Players.First(p => p.Index == snap.CurrentPlayerIdx);

            // --- Entry: requires high-value first move ---
            if (!currentPlayer.HasEntered)
                return TryEntry(game);

            // --- Post-entry: only play if it removes ≥2 cards or a high-value card ---
            var hand      = game.AIWorkingHand;
            int threshold = Math.Max(TabletopMeldGame.EntryMinPoints, 20);
            bool played   = false;

            // Prefer groups that reduce hand value the most
            var groups = TabletopAIHelper.FindPlayableGroups(hand);
            foreach (var group in groups)
            {
                int pts = group.Sum(MeldValidator.TabletopPointValue);
                if (pts >= threshold || group.Count >= 3)
                {
                    var indices = FindHandIndices(game.AIWorkingHand, group);
                    if (indices != null)
                    {
                        game.AICreateGroup(indices);
                        played = true;
                        break; // conservative: one meld per turn
                    }
                }
            }

            // Try one extension if still have high-value cards
            if (!played)
            {
                var ext = TabletopAIHelper.FindExtension(game.AIWorkingHand, game.AIWorkingTable);
                if (ext.HasValue)
                {
                    int pts = MeldValidator.TabletopPointValue(game.AIWorkingHand[ext.Value.handIdx]);
                    if (pts >= 10)
                    {
                        game.AIAddToGroup(ext.Value.handIdx, ext.Value.groupIdx, ext.Value.position);
                        played = true;
                    }
                }
            }

            return played;
        }

        private bool TryEntry(TabletopMeldGame game)
        {
            var entryMelds = TabletopAIHelper.FindEntryMelds(game.AIWorkingHand);
            if (entryMelds == null) return false;
            foreach (var meld in entryMelds)
            {
                var indices = FindHandIndices(game.AIWorkingHand, meld);
                if (indices == null) return false;
                game.AICreateGroup(indices);
            }
            return true;
        }

        private static List<int>? FindHandIndices(IReadOnlyList<Card> hand, List<Card> cards)
        {
            var tempHand = hand.ToList();
            var indices  = new List<int>();
            var usedSet  = new HashSet<int>();
            foreach (var c in cards)
            {
                int origIdx = -1;
                for (int i = 0; i < hand.Count; i++)
                    if (!usedSet.Contains(i) && hand[i].Equals(c)) { origIdx = i; break; }
                if (origIdx < 0) return null;
                indices.Add(origIdx);
                usedSet.Add(origIdx);
                int tempIdx = tempHand.FindIndex(h => h.Equals(c));
                if (tempIdx >= 0) tempHand.RemoveAt(tempIdx);
            }
            return indices;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The Casual — random-ish play; may skip turns, form random groups,
    // ignore optimal strategy. Simulates a beginner human.
    // ─────────────────────────────────────────────────────────────────────────

    public class TabletopCasualAI : ITabletopAI
    {
        private static readonly Random _rng = new Random();

        public bool TakeTurn(TabletopMeldGame game, TabletopSnapshot snap)
        {
            // 20% chance to just pass even if a play is available
            if (_rng.NextDouble() < 0.20) return false;

            var currentPlayer = snap.Players.FirstOrDefault(p => p.Index == snap.CurrentPlayerIdx);
            if (currentPlayer == null) return false;

            if (!currentPlayer.HasEntered)
                return TryEntry(game);

            // Random subset of actions — pick at most 2 plays
            int plays = 0;
            while (plays < 2)
            {
                var hand   = game.AIWorkingHand;
                var table  = game.AIWorkingTable;
                var groups = TabletopAIHelper.FindPlayableGroups(hand);

                // 50% chance to extend, 50% new group
                bool tryExtend = _rng.NextDouble() < 0.5;
                if (tryExtend)
                {
                    var ext = TabletopAIHelper.FindExtension(hand, table);
                    if (ext.HasValue)
                    {
                        game.AIAddToGroup(ext.Value.handIdx, ext.Value.groupIdx, ext.Value.position);
                        plays++;
                        continue;
                    }
                }

                if (groups.Count > 0)
                {
                    // Pick randomly among top 3 groups
                    int pick = _rng.Next(Math.Min(3, groups.Count));
                    var indices = FindHandIndices(game.AIWorkingHand, groups[pick]);
                    if (indices != null)
                    {
                        game.AICreateGroup(indices);
                        plays++;
                        continue;
                    }
                }

                break; // nothing more to do
            }

            return plays > 0;
        }

        private bool TryEntry(TabletopMeldGame game)
        {
            var entryMelds = TabletopAIHelper.FindEntryMelds(game.AIWorkingHand);
            if (entryMelds == null) return false;
            // Casual might fail to find entry — 10% chance to try sub-optimal play
            if (_rng.NextDouble() < 0.10) return false;
            foreach (var meld in entryMelds)
            {
                var indices = FindHandIndices(game.AIWorkingHand, meld);
                if (indices == null) return false;
                game.AICreateGroup(indices);
            }
            return true;
        }

        private static List<int>? FindHandIndices(IReadOnlyList<Card> hand, List<Card> cards)
        {
            var tempHand = hand.ToList();
            var indices  = new List<int>();
            var usedSet  = new HashSet<int>();
            foreach (var c in cards)
            {
                int origIdx = -1;
                for (int i = 0; i < hand.Count; i++)
                    if (!usedSet.Contains(i) && hand[i].Equals(c)) { origIdx = i; break; }
                if (origIdx < 0) return null;
                indices.Add(origIdx);
                usedSet.Add(origIdx);
                int tempIdx = tempHand.FindIndex(h => h.Equals(c));
                if (tempIdx >= 0) tempHand.RemoveAt(tempIdx);
            }
            return indices;
        }
    }
}

namespace Rummy.Logic.TabletopMeld
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Rummy.Logic.Cards;
    using Rummy.Logic.Melds;

    // ── Phase ─────────────────────────────────────────────────────────────────

    public enum TabletopPhase
    {
        Idle,           // No game in progress
        PlayerTurn,     // A player is taking their turn (working state active)
        RoundOver,      // A player emptied their hand OR stock exhausted
        GameOver,       // Reserved for future multi-game match tracking
    }

    // ── Per-player public info (immutable snapshot slice) ─────────────────────

    public class TabletopPlayerInfo
    {
        public int    Index      { get; init; }
        public string Name       { get; init; } = "";
        public bool   IsHuman    { get; init; }
        public int    HandCount  { get; init; }
        public bool   HasEntered { get; init; }
        public int    Score      { get; init; }
    }

    // ── Immutable render snapshot ─────────────────────────────────────────────

    public class TabletopSnapshot
    {
        public TabletopPhase Phase            = TabletopPhase.Idle;
        public int           RoundNumber      = 0;
        public int           CurrentPlayerIdx = 0;
        public bool          IsHumanTurn      = false;

        // Table groups. During the current player's turn this is the WORKING state (may be
        // temporarily invalid mid-turn). Between turns it is the last committed state.
        public List<List<Card>> TableGroups   = new();

        // Per-group validity flags (populated only during PlayerTurn)
        public List<bool> GroupValidity       = new();

        // Current player's in-progress hand (populated only when IsHumanTurn)
        public List<Card> CurrentPlayerHand   = new();

        // All players' public info
        public List<TabletopPlayerInfo> Players = new();

        public int    StockCount     = 0;
        public string StatusMessage  = "Press New Game to start.";

        // Round-end result (valid when Phase == RoundOver)
        public int    WinnerIdx      = -1;
        public string RoundResultMsg = "";
    }

    // ── AI Interface ──────────────────────────────────────────────────────────

    public interface ITabletopAI
    {
        /// <summary>
        /// Execute the AI's complete turn using the game's AI action methods
        /// (AICreateGroup, AIAddToGroup). Return true if the AI made a valid play,
        /// false if it cannot play this turn.
        /// </summary>
        bool TakeTurn(TabletopMeldGame game, TabletopSnapshot snapshot);
    }

    // ── Internal player ───────────────────────────────────────────────────────

    internal sealed class TabletopPlayer
    {
        public int           Index      { get; init; }
        public string        Name       { get; init; } = "";
        public bool          IsHuman    { get; init; }
        public List<Card>    Hand       { get; set; }  = new();
        public bool          HasEntered { get; set; }  = false;
        public int           Score      { get; set; }  = 0;
        public ITabletopAI?  AI         { get; init; }

        public int HandValue() => Hand.Sum(MeldValidator.TabletopPointValue);
    }

    // ── Tabletop Meld Game ────────────────────────────────────────────────────

    public class TabletopMeldGame
    {
        // ── Constants ──────────────────────────────────────────────────
        public const int InitialDeal         = 14;   // cards dealt to each player
        public const int EntryMinPoints      = 30;   // min pts from hand for first entry
        public const int InvalidStatePenalty = 3;    // penalty cards if end-turn state invalid
        public const int CannotPlayPenalty   = 1;    // penalty cards when player passes

        // ── Committed state ─────────────────────────────────────────────
        private List<List<Card>>     _table      = new();
        private List<TabletopPlayer> _players    = new();
        private List<Card>           _stock      = new();
        private int                  _currentIdx = 0;
        private TabletopPhase        _phase      = TabletopPhase.Idle;
        private int                  _roundNumber = 0;

        // ── Working state (valid only when _phase == PlayerTurn) ────────
        private List<List<Card>>? _workingTable;             // mutable table copy
        private List<Card>?       _workingHand;              // current player's working hand
        private List<Card>?       _committedHandSnapshot;    // hand at turn-start (validation)
        private List<List<Card>>? _committedTableSnapshot;   // table at turn-start (validation)

        // ── Thread-safe render snapshot ─────────────────────────────────
        private volatile TabletopSnapshot _snap = new TabletopSnapshot();
        public  TabletopSnapshot Snapshot => _snap;

        // ── Setup ───────────────────────────────────────────────────────

        /// <summary>
        /// Start a new game with 1 human + up to 3 AI opponents.
        /// Pass AI instances (or null to use the default StrategistAI).
        /// </summary>
        public void StartGame(int numAiPlayers, params ITabletopAI[] ais)
        {
            numAiPlayers = Math.Clamp(numAiPlayers, 1, 3);
            _players.Clear();
            _players.Add(new TabletopPlayer { Index = 0, Name = "You", IsHuman = true });

            string[] defaultNames = { "The Strategist", "The Conservator", "The Casual" };
            for (int i = 0; i < numAiPlayers; i++)
            {
                _players.Add(new TabletopPlayer
                {
                    Index   = i + 1,
                    Name    = i < defaultNames.Length ? defaultNames[i] : $"AI {i + 1}",
                    IsHuman = false,
                    AI      = i < ais.Length ? ais[i] : new TabletopStrategistAI(),
                });
            }
            StartNewRound();
        }

        public void StartNewRound()
        {
            _roundNumber++;
            foreach (var p in _players) { p.Hand.Clear(); p.HasEntered = false; }
            _table.Clear();

            // Build and shuffle the 106-card double deck
            var deck = new DoubleDeck();
            _stock = new List<Card>();
            while (true) { try { _stock.Add(deck.GetNextCard()); } catch { break; } }

            // Deal InitialDeal cards to each player
            foreach (var p in _players)
                for (int i = 0; i < InitialDeal; i++)
                    p.Hand.Add(PopStock()!);

            _currentIdx = 0;
            _phase      = TabletopPhase.PlayerTurn;
            SetupWorkingState();
            PublishSnapshot($"Round {_roundNumber} — {CurrentPlayer().Name}'s turn.");
            MaybeRunAI();
        }

        // ── Working-state management ─────────────────────────────────────

        private void SetupWorkingState()
        {
            var p = CurrentPlayer();
            _workingTable           = DeepCopyTable(_table);
            _committedTableSnapshot = DeepCopyTable(_table);
            _workingHand            = new List<Card>(p.Hand);
            _committedHandSnapshot  = new List<Card>(p.Hand);
        }

        private void RollbackWorkingState()
        {
            _workingTable           = null;
            _workingHand            = null;
            _committedHandSnapshot  = null;
            _committedTableSnapshot = null;
        }

        private void CommitWorkingState(TabletopPlayer player)
        {
            _table      = _workingTable!;
            player.Hand = _workingHand!;
            if (!player.HasEntered) player.HasEntered = true;
            RollbackWorkingState();
        }

        // ── Human action API ─────────────────────────────────────────────

        /// <summary>Create a new table group from cards in the working hand.</summary>
        public bool CreateGroup(IList<int> handIndices)
        {
            if (!HumanTurnActive()) return false;
            if (!IndicesValid(handIndices, _workingHand!)) return false;

            var cards = PickFromList(_workingHand!, handIndices);
            _workingTable!.Add(cards);
            PublishWorking();
            return true;
        }

        /// <summary>Add a card from the working hand to an existing table group.</summary>
        public bool AddToGroup(int handIdx, int groupIdx, int position)
        {
            if (!HumanTurnActive()) return false;
            if (!CheckBounds(handIdx, _workingHand!) || !CheckBounds(groupIdx, _workingTable!)) return false;

            var card  = _workingHand![handIdx];
            _workingHand!.RemoveAt(handIdx);
            InsertIntoGroup(_workingTable![groupIdx], card, position);
            PublishWorking();
            return true;
        }

        /// <summary>Move a card from one table group to another (or append if dstPosition == -1).</summary>
        public bool MoveCard(int srcGroup, int srcCard, int dstGroup, int dstPosition)
        {
            if (!HumanTurnActive()) return false;
            if (!CheckBounds(srcGroup, _workingTable!) || !CheckBounds(dstGroup, _workingTable!)) return false;
            if (srcGroup == dstGroup) return false;
            if (!CheckBounds(srcCard, _workingTable![srcGroup])) return false;

            var card = _workingTable![srcGroup][srcCard];
            _workingTable![srcGroup].RemoveAt(srcCard);
            InsertIntoGroup(_workingTable![dstGroup], card, dstPosition);
            PruneEmpty();
            PublishWorking();
            return true;
        }

        /// <summary>Pick a card from a table group into the working hand.</summary>
        public bool PickUpFromTable(int groupIdx, int cardIdx)
        {
            if (!HumanTurnActive()) return false;
            if (!CheckBounds(groupIdx, _workingTable!)) return false;
            if (!CheckBounds(cardIdx, _workingTable![groupIdx])) return false;

            var card = _workingTable![groupIdx][cardIdx];
            _workingTable![groupIdx].RemoveAt(cardIdx);
            PruneEmpty();
            _workingHand!.Add(card);
            PublishWorking();
            return true;
        }

        /// <summary>Split a group at the given position — cards from that index onward form a new group.</summary>
        public bool SplitGroup(int groupIdx, int splitPos)
        {
            if (!HumanTurnActive()) return false;
            if (!CheckBounds(groupIdx, _workingTable!)) return false;
            var group = _workingTable![groupIdx];
            if (splitPos <= 0 || splitPos >= group.Count) return false;

            var newGroup = group.GetRange(splitPos, group.Count - splitPos);
            group.RemoveRange(splitPos, group.Count - splitPos);
            _workingTable!.Add(newGroup);
            PublishWorking();
            return true;
        }

        /// <summary>Append all cards from the source group onto the destination group.</summary>
        public bool MergeGroups(int srcGroupIdx, int dstGroupIdx)
        {
            if (!HumanTurnActive()) return false;
            if (!CheckBounds(srcGroupIdx, _workingTable!) || !CheckBounds(dstGroupIdx, _workingTable!)) return false;
            if (srcGroupIdx == dstGroupIdx) return false;

            _workingTable![dstGroupIdx].AddRange(_workingTable![srcGroupIdx]);

            // Remove src (adjust for possible index shift)
            int removeIdx = srcGroupIdx > dstGroupIdx ? srcGroupIdx : srcGroupIdx;
            _workingTable!.RemoveAt(removeIdx);
            PublishWorking();
            return true;
        }

        // ── Turn resolution ───────────────────────────────────────────────

        /// <summary>
        /// End the current player's turn. If the working state is valid, commits it.
        /// If invalid, rolls back and applies a penalty draw.
        /// </summary>
        public bool EndTurn()
        {
            if (_phase != TabletopPhase.PlayerTurn) return false;
            var player = CurrentPlayer();

            if (!ValidateWorkingState(player, out string error))
            {
                RollbackWorkingState();
                int drawn = DrawPenaltyCards(player, InvalidStatePenalty);
                SetupWorkingState(); // re-setup for penalty cards
                string msg = $"{player.Name}: invalid end — rolled back. Drew {drawn} penalty card(s). ({error})";
                RollbackWorkingState(); // done with working state; advance will re-setup
                PublishSnapshot(msg);
                AdvanceTurn();
                return false;
            }

            CommitWorkingState(player);

            // Win check
            if (player.Hand.Count == 0)
            {
                EndRound(player.Index);
                return true;
            }

            PublishSnapshot($"{player.Name} ended their turn.");
            AdvanceTurn();
            return true;
        }

        /// <summary>
        /// Current player declares they cannot make any valid play.
        /// Draws a penalty card and ends turn. If stock is empty, ends round by exhaustion.
        /// </summary>
        public bool CannotPlay()
        {
            if (_phase != TabletopPhase.PlayerTurn) return false;

            var player = CurrentPlayer();
            RollbackWorkingState();

            if (_stock.Count == 0)
            {
                EndRoundByStockExhaustion();
                return true;
            }

            int drawn = DrawPenaltyCards(player, CannotPlayPenalty);
            PublishSnapshot($"{player.Name} passed. Drew {drawn} card(s).");
            SetupWorkingState(); // re-setup with new card
            RollbackWorkingState();
            AdvanceTurn();
            return true;
        }

        // ── AI path (same logic, no IsHuman guard) ───────────────────────

        public bool AICreateGroup(IList<int> handIndices)
        {
            if (_phase != TabletopPhase.PlayerTurn || CurrentPlayer().IsHuman) return false;
            if (!IndicesValid(handIndices, _workingHand!)) return false;
            var cards = PickFromList(_workingHand!, handIndices);
            _workingTable!.Add(cards);
            return true;
        }

        public bool AIAddToGroup(int handIdx, int groupIdx, int position)
        {
            if (_phase != TabletopPhase.PlayerTurn || CurrentPlayer().IsHuman) return false;
            if (!CheckBounds(handIdx, _workingHand!) || !CheckBounds(groupIdx, _workingTable!)) return false;
            var card = _workingHand![handIdx];
            _workingHand!.RemoveAt(handIdx);
            InsertIntoGroup(_workingTable![groupIdx], card, position);
            return true;
        }

        /// <summary>Expose working hand to AI (read-only view).</summary>
        public IReadOnlyList<Card> AIWorkingHand =>
            (_workingHand as IReadOnlyList<Card>) ?? new List<Card>();

        /// <summary>Expose working table to AI (read-only view).</summary>
        public IReadOnlyList<IReadOnlyList<Card>> AIWorkingTable =>
            _workingTable?.Select(g => (IReadOnlyList<Card>)g).ToList()
            ?? new List<IReadOnlyList<Card>>();

        // ── AI orchestration ─────────────────────────────────────────────

        private void MaybeRunAI()
        {
            var player = CurrentPlayer();
            if (player.IsHuman || _phase != TabletopPhase.PlayerTurn) return;

            bool played = player.AI?.TakeTurn(this, _snap) ?? false;
            if (!played)
                CannotPlay();
            else
                EndTurn();
        }

        // ── Validation ───────────────────────────────────────────────────

        private bool ValidateWorkingState(TabletopPlayer player, out string error)
        {
            error = "";

            // --- 1. Ensure no table cards remain in working hand ---
            // A card "came from the table" if it's in working hand but wasn't in committed hand.
            // Use multiset subtraction to handle duplicates correctly.
            var committedHandCopy = new List<Card>(_committedHandSnapshot!);
            foreach (var c in _workingHand!)
            {
                int idx = committedHandCopy.FindIndex(h => h.Equals(c));
                if (idx >= 0)
                    committedHandCopy.RemoveAt(idx);
                else
                {
                    error = "All table cards that were picked up must be placed back.";
                    return false;
                }
            }

            // --- 2. All table groups must be valid ---
            foreach (var group in _workingTable!)
            {
                if (group.Count == 0) continue;
                if (!MeldValidator.IsValid(group))
                {
                    error = "Every table group must be a valid set or run.";
                    return false;
                }
            }

            // --- 3. Player must have played at least one card from their original hand ---
            // playedFromHand = committedHandSnapshot - workingHand (multiset subtraction)
            var workingHandCopy  = new List<Card>(_workingHand!);
            var playedFromHand   = new List<Card>();
            foreach (var c in _committedHandSnapshot!)
            {
                int idx = workingHandCopy.FindIndex(h => h.Equals(c));
                if (idx >= 0)
                    workingHandCopy.RemoveAt(idx);
                else
                    playedFromHand.Add(c);
            }

            if (playedFromHand.Count == 0)
            {
                error = "You must play at least one card from your hand.";
                return false;
            }

            // --- 4. First-entry check: cards played from hand must total ≥ EntryMinPoints ---
            if (!player.HasEntered)
            {
                int entryPts = playedFromHand.Sum(MeldValidator.TabletopPointValue);
                if (entryPts < EntryMinPoints)
                {
                    error = $"First entry needs ≥{EntryMinPoints} pts from your hand (you have {entryPts}).";
                    return false;
                }
            }

            return true;
        }

        private List<bool> ComputeGroupValidity() =>
            _workingTable?.Select(g => g.Count >= 3 && MeldValidator.IsValid(g)).ToList()
            ?? new List<bool>();

        // ── Round end ────────────────────────────────────────────────────

        private void EndRound(int winnerIdx)
        {
            _phase = TabletopPhase.RoundOver;
            var winner = _players[winnerIdx];
            int winPts = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"🎉 {winner.Name} wins the round!");
            foreach (var p in _players)
            {
                if (p.Index == winnerIdx) continue;
                int hv     = p.HandValue();
                p.Score   -= hv;
                winPts    += hv;
                sb.AppendLine($"  {p.Name}: -{hv} pts ({p.Hand.Count} card(s) remaining)");
            }
            winner.Score += winPts;
            sb.AppendLine($"  {winner.Name} gains +{winPts} pts → total {winner.Score}");
            PublishSnapshot(sb.ToString().Trim(), winnerIdx: winnerIdx);
        }

        private void EndRoundByStockExhaustion()
        {
            _phase = TabletopPhase.RoundOver;
            var winner = _players.OrderBy(p => p.HandValue()).First();
            var sb     = new StringBuilder();
            sb.AppendLine("Stock exhausted! Lowest hand value wins.");
            sb.AppendLine($"  {winner.Name} wins with {winner.HandValue()} pts remaining.");
            foreach (var p in _players)
                sb.AppendLine($"  {p.Name}: {p.HandValue()} pts ({p.Hand.Count} card(s))");
            PublishSnapshot(sb.ToString().Trim(), winnerIdx: winner.Index);
        }

        // ── Turn advancement ─────────────────────────────────────────────

        private void AdvanceTurn()
        {
            if (_phase != TabletopPhase.PlayerTurn) return;
            _currentIdx = (_currentIdx + 1) % _players.Count;
            SetupWorkingState();
            PublishSnapshot($"{CurrentPlayer().Name}'s turn.");
            MaybeRunAI();
        }

        // ── Stock helpers ────────────────────────────────────────────────

        private Card? PopStock()
        {
            if (_stock.Count == 0) return null;
            var c = _stock[^1];
            _stock.RemoveAt(_stock.Count - 1);
            return c;
        }

        private int DrawPenaltyCards(TabletopPlayer player, int count)
        {
            int drawn = 0;
            for (int i = 0; i < count && _stock.Count > 0; i++)
            {
                player.Hand.Add(PopStock()!);
                drawn++;
            }
            return drawn;
        }

        // ── Snapshot publishing ──────────────────────────────────────────

        private void PublishWorking() => PublishSnapshot(BuildStatusMsg());

        private void PublishSnapshot(string statusMsg = "", int winnerIdx = -1)
        {
            var player = CurrentPlayer();
            var snap = new TabletopSnapshot
            {
                Phase             = _phase,
                RoundNumber       = _roundNumber,
                CurrentPlayerIdx  = _currentIdx,
                IsHumanTurn       = player.IsHuman,
                TableGroups       = DeepCopyTable(_workingTable ?? _table),
                GroupValidity     = _phase == TabletopPhase.PlayerTurn
                                        ? ComputeGroupValidity()
                                        : new List<bool>(),
                CurrentPlayerHand = player.IsHuman
                                        ? new List<Card>(_workingHand ?? player.Hand)
                                        : new List<Card>(),
                Players           = _players.Select(p => new TabletopPlayerInfo
                {
                    Index      = p.Index,
                    Name       = p.Name,
                    IsHuman    = p.IsHuman,
                    HandCount  = p.Hand.Count,
                    HasEntered = p.HasEntered,
                    Score      = p.Score,
                }).ToList(),
                StockCount        = _stock.Count,
                StatusMessage     = statusMsg,
                WinnerIdx         = winnerIdx,
                RoundResultMsg    = winnerIdx >= 0 ? statusMsg : "",
            };
            _snap = snap;
        }

        private string BuildStatusMsg()
        {
            var player  = CurrentPlayer();
            var validity = ComputeGroupValidity();
            int invalid = validity.Count(v => !v);
            if (invalid > 0)
                return $"{player.Name}: {_workingHand!.Count} card(s) in hand — {invalid} invalid group(s).";
            return $"{player.Name}: {_workingHand!.Count} card(s) in hand — all groups valid.";
        }

        // ── Utilities ────────────────────────────────────────────────────

        private TabletopPlayer CurrentPlayer() => _players[_currentIdx];

        private bool HumanTurnActive() =>
            _phase == TabletopPhase.PlayerTurn && CurrentPlayer().IsHuman;

        private static bool CheckBounds<T>(int idx, List<T> list) =>
            idx >= 0 && idx < list.Count;

        private static bool IndicesValid(IList<int> indices, List<Card> list) =>
            indices.Count > 0
            && indices.All(i => i >= 0 && i < list.Count)
            && indices.Distinct().Count() == indices.Count;

        private static List<Card> PickFromList(List<Card> list, IList<int> indices)
        {
            var result = indices.Select(i => list[i]).ToList();
            foreach (int i in indices.OrderByDescending(x => x))
                list.RemoveAt(i);
            return result;
        }

        private static void InsertIntoGroup(List<Card> group, Card card, int position)
        {
            if (position < 0 || position > group.Count) position = group.Count;
            group.Insert(position, card);
        }

        private void PruneEmpty() => _workingTable!.RemoveAll(g => g.Count == 0);

        private static List<List<Card>> DeepCopyTable(List<List<Card>> table) =>
            table.Select(g => new List<Card>(g)).ToList();
    }
}

namespace Rummy.Logic.GinRummy
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Cards;
    using Rummy.Logic.Melds;

    // ── Game Phase ────────────────────────────────────────────────────────────

    public enum GinPhase
    {
        Idle,           // No game in progress
        HumanDraw,      // Human must draw (from stock or discard)
        HumanDiscard,   // Human must discard one card
        AITurn,         // AI is taking its turn
        Knocking,       // Human knocked — resolving lay-offs and scoring
        RoundOver,      // Round ended — show results
        GameOver,       // A player reached the target score
    }

    // ── Render Snapshot (thread-safe read by UI) ──────────────────────────────

    public class GinSnapshot
    {
        public GinPhase Phase         = GinPhase.Idle;
        public int      RoundNumber   = 0;
        public int      HumanScore    = 0;
        public int      AIScore       = 0;
        public int      TargetScore   = 100;

        // Hands (human face-up, AI face-down normally)
        public List<Card> HumanHand   = new();
        public List<Card> AIHand      = new();  // revealed at knock/gin resolution

        // Table
        public Card?    TopDiscard    = null;   // top of discard pile (null = empty)
        public int      StockCount    = 0;      // # cards remaining in stock pile
        public bool     DiscardEmpty  = true;

        // State helpers
        public bool     CanKnock      = false;  // deadwood ≤ 10
        public int      HumanDeadwood = 0;
        public string   StatusMessage = "Press New Game to start.";

        // Round result (valid when Phase == RoundOver or GameOver)
        public string   RoundResultMsg = "";
        public int      RoundPoints    = 0;

        // Last drawn card index in hand (for highlight animation)
        public int      LastDrawnIdx   = -1;

        // AI reveal info (valid during Knocking phase)
        public bool     AIRevealed     = false;
        public int      AIDeadwood     = 0;
        public bool     HumanGin       = false; // 0 deadwood
        public bool     Undercut       = false; // AI ≤ Human deadwood at knock
        public List<Card> HumanDeadwoodCards = new(); // human deadwood cards after resolution
        public List<Card> AIDeadwoodCards    = new(); // AI deadwood cards after resolution
        public List<Card> HumanLaidOffCards  = new(); // human cards laid off onto AI's melds
        public List<Card> AILaidOffCards     = new(); // AI cards laid off onto human's melds
    }

    // ── Game Events (dequeued by UI each frame for log/animation) ─────────────

    public enum GinEventType
    {
        RoundStart,
        HumanDrewStock, HumanTookDiscard, HumanDiscarded, HumanKnocked, HumanGin,
        AIDrewStock,    AITookDiscard,    AIDiscarded,    AIKnocked,    AIGin,
        RoundResult,    GameResult,
    }

    public record struct GinGameEvent(
        GinEventType Type,
        string       Label,
        Card?        Card        = null,
        Card?        NextDiscard = null);

    // ── AI Strategy Interface ─────────────────────────────────────────────────

    public interface IGinAI
    {
        /// <summary>
        /// Given the current hand and top discard, decide whether to take the discard.
        /// </summary>
        bool TakeDiscard(IList<Card> hand, Card topDiscard);

        /// <summary>
        /// Choose which card to discard from the hand. Returns the index.
        /// </summary>
        int ChooseDiscard(IList<Card> hand);

        /// <summary>
        /// Decide whether to knock when eligible (deadwood ≤ 10).
        /// </summary>
        bool ShouldKnock(IList<Card> hand);
    }

    // ── Gin Rummy Game ────────────────────────────────────────────────────────

    public class GinRummyGame
    {
        // ── Config ────────────────────────────────────────────────
        public int TargetScore { get; set; } = 100;

        // ── Public snapshot (atomic swap) ─────────────────────────
        private volatile GinSnapshot _snapshot = new GinSnapshot();
        public  GinSnapshot Snapshot => _snapshot;

        // ── Private state ─────────────────────────────────────────
        private Deck            _deck       = new Deck();
        private List<Card>      _stock      = new();
        private List<Card>      _discard    = new();
        private List<Card>      _humanHand  = new();
        private List<Card>      _aiHand     = new();
        private int             _round      = 0;
        private int             _humanScore = 0;
        private int             _aiScore    = 0;
        private GinPhase        _phase      = GinPhase.Idle;
        private IGinAI          _ai;
        private Card?           _lastDiscard = null; // top of discard pile

        // Pending draw from discard (human took top card — move to hand)
        private Card?           _pendingDraw = null;

        // ── Game event queue (drained by UI each frame) ───────────────────────
        private readonly System.Collections.Concurrent.ConcurrentQueue<GinGameEvent> _events = new();
        public bool TryDequeueEvent(out GinGameEvent ev) => _events.TryDequeue(out ev);

        private void Emit(GinEventType t, string label, Card? card = null, Card? nextDiscard = null)
            => _events.Enqueue(new GinGameEvent(t, label, card, nextDiscard));

        public GinRummyGame(IGinAI ai)
        {
            _ai = ai;
        }

        // ── Public API (called by UI/render thread) ───────────────

        public void StartNewGame(int targetScore = 100)
        {
            TargetScore  = targetScore;
            _humanScore  = 0;
            _aiScore     = 0;
            _round       = 0;
            StartNewRound();
        }

        public void StartNewRound()
        {
            _round++;
            _deck    = new Deck();
            _stock   = new List<Card>();
            _discard = new List<Card>();
            _humanHand.Clear();
            _aiHand.Clear();

            // Fill stock with all deck cards
            while (true)
            {
                try { _stock.Add(_deck.GetNextCard()); }
                catch { break; }
            }
            // Shuffle stock (deck already shuffles, but we store as list)
            // (Deck constructor already shuffles — stock is just in deck order)

            // Deal 10 cards each
            for (int i = 0; i < 10; i++) _humanHand.Add(DrawFromStock()!);
            for (int i = 0; i < 10; i++) _aiHand.Add(DrawFromStock()!);

            // Turn over first discard
            _discard.Add(DrawFromStock()!);
            _lastDiscard = _discard[^1];

            _phase = GinPhase.HumanDraw;
            Emit(GinEventType.RoundStart, $"── Round {_round} started ── (initial discard: {_lastDiscard})");
            PublishSnapshot(statusMsg: "Your turn - draw from stock or take the discard.");
        }

        // ── Human actions ─────────────────────────────────────────

        public bool HumanDrawFromStock()
        {
            if (_phase != GinPhase.HumanDraw) return false;
            var card = DrawFromStock();
            if (card == null) { ReshuffleDiscard(); card = DrawFromStock(); }
            if (card == null) return false; // stock exhausted

            _humanHand.Add(card);
            Emit(GinEventType.HumanDrewStock, $"You drew from stock: {card}", card);
            _phase = GinPhase.HumanDiscard;
            int lastIdx = _humanHand.Count - 1;
            PublishSnapshot(lastDrawnIdx: lastIdx, statusMsg: "Pick a card to discard.");
            return true;
        }

        public bool HumanTakeDiscard()
        {
            if (_phase != GinPhase.HumanDraw) return false;
            if (_discard.Count == 0) return false;

            var card = _discard[^1];
            _discard.RemoveAt(_discard.Count - 1);
            _humanHand.Add(card);
            _lastDiscard = _discard.Count > 0 ? _discard[^1] : null;
            Emit(GinEventType.HumanTookDiscard, $"You took discard: {card}", card, _lastDiscard);
            _phase = GinPhase.HumanDiscard;
            int lastIdx = _humanHand.Count - 1;
            PublishSnapshot(lastDrawnIdx: lastIdx, statusMsg: "Pick a card to discard.");
            return true;
        }

        public bool HumanDiscard(int cardIndex)
        {
            if (_phase != GinPhase.HumanDiscard) return false;
            if (cardIndex < 0 || cardIndex >= _humanHand.Count) return false;

            var card = _humanHand[cardIndex];
            _humanHand.RemoveAt(cardIndex);
            _discard.Add(card);
            _lastDiscard = card;
            Emit(GinEventType.HumanDiscarded, $"You discarded: {card}", card);

            // Check if human has Gin (0 deadwood after discarding)
            int dw = MeldValidator.MinDeadwood(_humanHand, out _, out _);
            if (dw == 0)
            {
                Emit(GinEventType.HumanGin, "GIN! You have 0 deadwood!");
                ResolveKnock(isGin: true);
                return true;
            }

            // Transition to AI turn
            _phase = GinPhase.AITurn;
            PublishSnapshot(statusMsg: "AI is thinking...");
            RunAITurn();
            return true;
        }

        public bool HumanKnock(int discardIndex)
        {
            if (_phase != GinPhase.HumanDiscard) return false;
            int dw = MeldValidator.MinDeadwood(_humanHand, out _, out _);
            if (dw > 10) return false; // not eligible

            // Discard the chosen card first
            if (discardIndex >= 0 && discardIndex < _humanHand.Count)
            {
                var card = _humanHand[discardIndex];
                _humanHand.RemoveAt(discardIndex);
                _discard.Add(card);
                _lastDiscard = card;
                Emit(GinEventType.HumanDiscarded, $"You discarded: {card}", card);
            }

            int dw2 = MeldValidator.MinDeadwood(_humanHand, out _, out _);
            if (dw2 == 0)
            {
                Emit(GinEventType.HumanGin, "GIN! You have 0 deadwood!");
                ResolveKnock(isGin: true);
            }
            else
            {
                Emit(GinEventType.HumanKnocked, $"You knocked! ({dw2} deadwood)");
                ResolveKnock(isGin: false);
            }
            return true;
        }

        // ── AI Turn ───────────────────────────────────────────────

        private void RunAITurn()
        {
            // Draw
            bool takeDiscard = _discard.Count > 0 && _ai.TakeDiscard(_aiHand, _discard[^1]);
            Card? aiDrewCard = null;
            if (takeDiscard)
            {
                aiDrewCard = _discard[^1];
                _aiHand.Add(aiDrewCard);
                _discard.RemoveAt(_discard.Count - 1);
                _lastDiscard = _discard.Count > 0 ? _discard[^1] : null;
                Emit(GinEventType.AITookDiscard, $"AI took discard: {aiDrewCard}", aiDrewCard, _lastDiscard);
            }
            else
            {
                var card = DrawFromStock();
                if (card == null) { ReshuffleDiscard(); card = DrawFromStock(); }
                if (card != null) { aiDrewCard = card; _aiHand.Add(card); }
                Emit(GinEventType.AIDrewStock, "AI drew from stock", aiDrewCard);
            }

            // Always discard first (correct Gin Rummy rules: draw → discard → check outcome)
            int di = _ai.ChooseDiscard(_aiHand);
            if (di >= 0 && di < _aiHand.Count)
            {
                var aiDiscard = _aiHand[di];
                _aiHand.RemoveAt(di);
                _discard.Add(aiDiscard);
                _lastDiscard = _discard[^1];
                Emit(GinEventType.AIDiscarded, $"AI discarded: {aiDiscard}", aiDiscard);
            }

            // Check outcome on 10-card hand
            int dw = MeldValidator.MinDeadwood(_aiHand, out _, out _);
            if (dw == 0)
            {
                Emit(GinEventType.AIGin, "AI GIN!");
                ResolveKnock(isGin: true, aiKnocks: true);
                return;
            }

            if (dw <= 10 && _ai.ShouldKnock(_aiHand))
            {
                Emit(GinEventType.AIKnocked, $"AI knocked! ({dw} deadwood)");
                ResolveKnock(isGin: false, aiKnocks: true);
                return;
            }

            // Human's turn again
            _phase = GinPhase.HumanDraw;
            PublishSnapshot(statusMsg: "Your turn - draw or take the discard.");
        }

        // ── Round resolution ──────────────────────────────────────

        private void ResolveKnock(bool isGin, bool aiKnocks = false)
        {
            _phase = GinPhase.Knocking;

            int humanDw = MeldValidator.MinDeadwood(_humanHand, out var humanMelds, out var humanLeft);
            int aiDw    = MeldValidator.MinDeadwood(_aiHand,    out var aiMelds,    out var aiLeft);

            // Reorder both hands by melds so cards are visually grouped when revealed.
            // Within runs: ascending rank (Ace low = 1). Within sets: ascending suit.
            // Deadwood: ascending rank then suit.
            static int RunRank(Card c) => c.Type == CardType.Ace ? 1 : (int)c.Type;
            static IEnumerable<Card> SortMeld(Meld m)
                => m.Type == MeldType.Run
                    ? m.Cards.OrderBy(RunRank)
                    : m.Cards.OrderBy(c => (int)c.Suit);
            static IEnumerable<Card> SortDead(List<Card> dead)
                => dead.OrderBy(c => c.Type == CardType.Ace ? 1 : (int)c.Type).ThenBy(c => (int)c.Suit);

            var aiOrdered = aiMelds.SelectMany(SortMeld).Concat(SortDead(aiLeft)).ToList();
            _aiHand.Clear();
            _aiHand.AddRange(aiOrdered);

            var humanOrdered = humanMelds.SelectMany(SortMeld).Concat(SortDead(humanLeft)).ToList();
            _humanHand.Clear();
            _humanHand.AddRange(humanOrdered);

            bool humanGin    = humanDw == 0 && !aiKnocks;
            bool aiGin       = aiDw == 0 && aiKnocks;
            bool undercut    = false;
            int  roundPoints = 0;
            string msg;
            List<Card> humanDeadwoodCards;
            List<Card> aiDeadwoodCards;
            List<Card> humanLaidOffCards;
            List<Card> aiLaidOffCards;

            if (humanGin || (isGin && !aiKnocks))
            {
                // Human Gin — wins all dead wood + 25 bonus
                roundPoints  = aiDw + 25;
                _humanScore += roundPoints;
                msg = $"GIN! You win {roundPoints} pts (AI had {aiDw} deadwood + 25 bonus).";
                humanDeadwoodCards = new List<Card>();
                aiDeadwoodCards    = aiLeft;
                humanLaidOffCards  = new List<Card>();
                aiLaidOffCards     = new List<Card>();
            }
            else if (aiGin || (isGin && aiKnocks && aiDw == 0))
            {
                roundPoints  = humanDw + 25;
                _aiScore    += roundPoints;
                msg = $"AI GIN! AI wins {roundPoints} pts (you had {humanDw} deadwood + 25 bonus).";
                humanDeadwoodCards = humanLeft;
                aiDeadwoodCards    = new List<Card>();
                humanLaidOffCards  = new List<Card>();
                aiLaidOffCards     = new List<Card>();
            }
            else if (!aiKnocks)
            {
                // Human knocked — AI lays off only its own deadwood onto human melds
                var aiRemaining = LayOffCards(aiLeft, humanMelds, humanLeft);
                aiDw = aiRemaining.Sum(c => MeldValidator.GinPointValue(c));
                humanDeadwoodCards = humanLeft;
                aiDeadwoodCards    = aiRemaining;
                humanLaidOffCards  = new List<Card>();
                aiLaidOffCards     = aiLeft.Where(c => !aiRemaining.Any(r => ReferenceEquals(r, c))).ToList();

                if (aiDw <= humanDw)
                {
                    undercut     = true;
                    roundPoints  = humanDw - aiDw + 25;
                    _aiScore    += roundPoints;
                    msg = $"UNDERCUT! AI wins {roundPoints} pts (your {humanDw} vs AI {aiDw} deadwood).";
                }
                else
                {
                    roundPoints  = aiDw - humanDw;
                    _humanScore += roundPoints;
                    msg = $"You knocked! You win {roundPoints} pts (AI had {aiDw} vs your {humanDw} deadwood).";
                }
            }
            else
            {
                // AI knocked — human lays off only its own deadwood onto AI melds
                var humanRemaining = LayOffCards(humanLeft, aiMelds, aiLeft);
                humanDw = humanRemaining.Sum(c => MeldValidator.GinPointValue(c));
                humanDeadwoodCards = humanRemaining;
                aiDeadwoodCards    = aiLeft;
                humanLaidOffCards  = humanLeft.Where(c => !humanRemaining.Any(r => ReferenceEquals(r, c))).ToList();
                aiLaidOffCards     = new List<Card>();

                if (humanDw <= aiDw)
                {
                    undercut     = true;
                    roundPoints  = aiDw - humanDw + 25;
                    _humanScore += roundPoints;
                    msg = $"UNDERCUT! You win {roundPoints} pts (AI {aiDw} vs your {humanDw} deadwood).";
                }
                else
                {
                    roundPoints  = humanDw - aiDw;
                    _aiScore    += roundPoints;
                    msg = $"AI knocked! AI wins {roundPoints} pts (your {humanDw} vs AI {aiDw} deadwood).";
                }
            }

            bool gameOver = _humanScore >= TargetScore || _aiScore >= TargetScore;
            _phase = gameOver ? GinPhase.GameOver : GinPhase.RoundOver;
            if (gameOver)
            {
                msg += _humanScore >= TargetScore ? $"  YOU WIN the game!" : $"  AI WINS the game!";
                Emit(GinEventType.GameResult, msg);
            }
            else
            {
                Emit(GinEventType.RoundResult, msg);
            }

            PublishSnapshot(
                aiRevealed:          true,
                humanDw:             humanDw,
                aiDw:                aiDw,
                humanGin:            humanGin,
                undercut:            undercut,
                roundMsg:            msg,
                roundPts:            roundPoints,
                statusMsg:           msg,
                humanDeadwoodCards:  humanDeadwoodCards,
                aiDeadwoodCards:     aiDeadwoodCards,
                humanLaidOffCards:   humanLaidOffCards,
                aiLaidOffCards:      aiLaidOffCards);
        }

        // Lay off cards from 'hand' onto existing 'melds'; returns the cards that remain as deadwood.
        private static List<Card> LayOffCards(List<Card> hand, List<Meld> melds, List<Card> leftover)
        {
            // Simple greedy lay-off: try to extend each meld with each leftover card
            var remaining = new List<Card>(hand);
            // Use mutable card lists so chained lay-offs onto the same meld work correctly
            var meldCardLists = melds.Select(m => new List<Card>(m.Cards)).ToList();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int m = 0; m < meldCardLists.Count; m++)
                {
                    for (int i = remaining.Count - 1; i >= 0; i--)
                    {
                        var extended = meldCardLists[m].Append(remaining[i]).ToList();
                        if (MeldValidator.IsValid(extended))
                        {
                            meldCardLists[m] = extended; // track extension for chaining
                            remaining.RemoveAt(i);
                            changed = true;
                            break;
                        }
                    }
                }
            }
            return remaining;
        }

        // ── Stock/Discard helpers ─────────────────────────────────

        private Card? DrawFromStock()
        {
            if (_stock.Count == 0) return null;
            var card = _stock[^1];
            _stock.RemoveAt(_stock.Count - 1);
            return card;
        }

        private void ReshuffleDiscard()
        {
            if (_discard.Count <= 1) return;
            // Keep top discard; shuffle rest back into stock
            var keep = _discard[^1];
            var shuffle = _discard.Take(_discard.Count - 1).OrderBy(_ => Guid.NewGuid()).ToList();
            _stock.AddRange(shuffle);
            _discard.Clear();
            _discard.Add(keep);
        }

        // ── Snapshot publishing ───────────────────────────────────

        private void PublishSnapshot(
            int         lastDrawnIdx        = -1,
            bool        aiRevealed          = false,
            int         humanDw             = -1,
            int         aiDw                = -1,
            bool        humanGin            = false,
            bool        undercut            = false,
            string      roundMsg            = "",
            int         roundPts            = 0,
            string      statusMsg           = "",
            List<Card>? humanDeadwoodCards   = null,
            List<Card>? aiDeadwoodCards      = null,
            List<Card>? humanLaidOffCards    = null,
            List<Card>? aiLaidOffCards       = null)
        {
            int dw   = humanDw >= 0 ? humanDw
                     : (_humanHand.Count > 0 ? MeldValidator.MinDeadwood(_humanHand, out _, out _) : 0);
            bool canKnock = dw <= 10 && _humanHand.Count == 11 && _phase == GinPhase.HumanDiscard;

            _snapshot = new GinSnapshot
            {
                Phase         = _phase,
                RoundNumber   = _round,
                HumanScore    = _humanScore,
                AIScore       = _aiScore,
                TargetScore   = TargetScore,
                HumanHand     = new List<Card>(_humanHand),
                AIHand        = new List<Card>(_aiHand),
                TopDiscard    = _lastDiscard,
                StockCount    = _stock.Count,
                DiscardEmpty  = _discard.Count == 0,
                CanKnock      = canKnock,
                HumanDeadwood = dw,
                StatusMessage = statusMsg,
                RoundResultMsg = roundMsg,
                RoundPoints   = roundPts,
                LastDrawnIdx  = lastDrawnIdx,
                AIRevealed    = aiRevealed,
                AIDeadwood    = aiDw >= 0 ? aiDw : 0,
                HumanGin          = humanGin,
                Undercut           = undercut,
                HumanDeadwoodCards = humanDeadwoodCards ?? new List<Card>(),
                AIDeadwoodCards    = aiDeadwoodCards    ?? new List<Card>(),
                HumanLaidOffCards  = humanLaidOffCards  ?? new List<Card>(),
                AILaidOffCards     = aiLaidOffCards     ?? new List<Card>(),
            };
        }
    }
}

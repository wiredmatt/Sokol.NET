namespace TexasHoldem.AI.SmartPlayer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.AI.SmartPlayer.Helpers;
    using TexasHoldem.Logic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Extensions;
    using TexasHoldem.Logic.Helpers;
    using TexasHoldem.Logic.Players;

    public enum PlayerStyle { Tight, Balanced, Aggressive, LAG }

    public class SmartPlayer : BasePlayer
    {
        private static readonly HandEvaluator Evaluator = new HandEvaluator();

        public override string Name { get; } = "SmartPlayer_" + Guid.NewGuid();

        public override int BuyIn { get; } = -1;

        // Per-hand state
        private CardValuationType _preFlopStrength = CardValuationType.Unplayable;
        private bool _raisedPreFlop;

        private readonly PlayerStyle _style;

        public SmartPlayer(PlayerStyle style = PlayerStyle.Balanced) { _style = style; }

        // ─── Style-based thresholds ──────────────────────────────────────────────
        // Preflop: open raise sizing (BB multiples, exclusive upper bound for Next())
        private int PfRaiseMinBB  => _style == PlayerStyle.Aggressive ? 3 : 2;
        private int PfRaiseMaxBB  => _style switch { PlayerStyle.Tight => 3, PlayerStyle.Aggressive => 6, _ => 4 };
        // Preflop: 3-bet sizing (multiplier of the facing raise, exclusive upper bound)
        private int PfReraiseMin  => _style == PlayerStyle.Aggressive ? 3 : 2;
        private int PfReraiseMax  => _style switch { PlayerStyle.Tight => 3, PlayerStyle.Aggressive => 5, _ => 4 };
        // Risky hands: open-raise frequency
        private int RiskyOpenPct    => _style switch { PlayerStyle.Tight => 30, PlayerStyle.Aggressive => 75, PlayerStyle.LAG => 80, _ => 55 };
        // Risky hands: max pot-odds to call a raise (0–1 fraction)
        private float RiskyCallOdds => _style switch { PlayerStyle.Tight => 0.30f, PlayerStyle.Aggressive => 0.50f, PlayerStyle.LAG => 0.55f, _ => 0.40f };
        // NotRecommended: steal-raise frequency
        private int NRStealPct      => _style switch { PlayerStyle.Tight => 5, PlayerStyle.Aggressive => 30, PlayerStyle.LAG => 40, _ => 15 };
        // NotRecommended: limp-in frequency when no raise
        private int NRLimpPct       => _style switch { PlayerStyle.Tight => 25, PlayerStyle.Aggressive => 70, PlayerStyle.LAG => 85, _ => 50 };
        // NotRecommended: max pot-odds to call a raise
        private float NRCallOdds    => _style switch { PlayerStyle.Tight => 0.15f, PlayerStyle.Aggressive => 0.30f, PlayerStyle.LAG => 0.35f, _ => 0.22f };
        // Postflop: river bluff with missed draw
        private int BluffRiverPct   => _style switch { PlayerStyle.Tight => 10, PlayerStyle.Aggressive => 40, PlayerStyle.LAG => 55, _ => 25 };
        // Postflop: c-bet bluff on flop/turn
        private int BluffCBetPct    => _style switch { PlayerStyle.Tight => 25, PlayerStyle.Aggressive => 65, PlayerStyle.LAG => 75, _ => 50 };
        // Postflop: semi-bluff with a strong draw
        private int BluffSemiPct    => _style switch { PlayerStyle.Tight => 25, PlayerStyle.Aggressive => 60, PlayerStyle.LAG => 70, _ => 45 };
        // Postflop pair: c-bet frequency / normal lead frequency
        private int PairCBetPct     => _style switch { PlayerStyle.Tight => 40, PlayerStyle.Aggressive => 75, PlayerStyle.LAG => 80, _ => 60 };
        private int PairLeadPct     => _style switch { PlayerStyle.Tight => 15, PlayerStyle.Aggressive => 50, PlayerStyle.LAG => 60, _ => 30 };

        public override void StartHand(IStartHandContext context)
        {
            base.StartHand(context);
            _preFlopStrength = CardValuationType.Unplayable;
            _raisedPreFlop = false;
        }

        public override PlayerAction PostingBlind(IPostingBlindContext context)
            => context.BlindAction;

        public override PlayerAction GetTurn(IGetTurnContext context)
            => context.RoundType == GameRoundType.PreFlop
                ? GetPreFlopAction(context)
                : GetPostFlopAction(context);

        // ────────────────────────────────────────────────────────────
        // PRE-FLOP
        // ────────────────────────────────────────────────────────────

        private PlayerAction GetPreFlopAction(IGetTurnContext context)
        {
            _preFlopStrength = HandStrengthValuation.PreFlop(FirstCard, SecondCard);
            int bigBlind = context.SmallBlind * 2;
            // "facing a raise" means someone bet more than one BB above the BB (i.e., a real raise,
            // not just a call/limp at the BB level)
            bool facingRaise = context.MoneyToCall > bigBlind;

            switch (_preFlopStrength)
            {
                case CardValuationType.Recommended:
                    // Premium hands (AA, KK, QQ, JJ, AK): always raise; 3-bet when facing a raise.
                    // Keep sizing to 2.5-3x to encourage action rather than driving everyone out.
                    _raisedPreFlop = true;
                    if (context.CanRaise)
                    {
                        int raiseTo = facingRaise
                            ? context.MoneyToCall * RandomProvider.Next(PfReraiseMin, PfReraiseMax)   // style-based re-raise
                            : bigBlind * RandomProvider.Next(PfRaiseMinBB, PfRaiseMaxBB);             // style-based BB open
                        return BetAmount(context, raiseTo);
                    }
                    return PlayerAction.CheckOrCall();

                case CardValuationType.Risky:
                    // Suited connectors, medium pairs, broadway hands
                    if (!facingRaise)
                    {
                        // Open-raise at style-dependent frequency, limp the rest — smaller sizing to build pots
                        if (context.CanRaise && RandomProvider.Next(0, 100) < RiskyOpenPct)
                        {
                            _raisedPreFlop = true;
                            return BetAmount(context, bigBlind * RandomProvider.Next(2, 3));
                        }
                        return PlayerAction.CheckOrCall();
                    }
                    else
                    {
                        // Count raises already in this preflop round.
                        // Risky hands only call a single open raise — fold to any 3-bet or higher.
                        int riskyRaiseCount = context.PreviousRoundActions.Count(a => a.Action.Type == PlayerActionType.Raise);
                        if (riskyRaiseCount >= 2)
                            return PlayerAction.Fold();
                        // Call a single raise only if pot odds are within style-based threshold
                        float potOdds = (float)context.MoneyToCall / (context.CurrentPot + context.MoneyToCall);
                        return potOdds <= RiskyCallOdds ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
                    }

                case CardValuationType.NotRecommended:
                    if (context.CanCheck)
                        return PlayerAction.CheckOrCall();
                    if (!facingRaise)
                    {
                        // Occasional steal-raise from late position — frequency depends on style
                        if (context.CanRaise && RandomProvider.Next(0, 100) < NRStealPct)
                        {
                            _raisedPreFlop = true;
                            return BetAmount(context, bigBlind * RandomProvider.Next(2, 3));
                        }
                        // Limp in at style-dependent frequency when it's just one BB to call
                        // marginal but playable hands (K8s, A4o, 98o, etc.)
                        return RandomProvider.Next(0, 100) < NRLimpPct
                            ? PlayerAction.CheckOrCall()
                            : PlayerAction.Fold();
                    }
                    else
                    {
                        // After a steal-raise, always fold to re-raises — no 3-bet calling with marginal hands.
                        if (_raisedPreFlop)
                            return PlayerAction.Fold();
                        // Fold to raises unless price is within style-based threshold
                        float odds = (float)context.MoneyToCall / (context.CurrentPot + context.MoneyToCall);
                        return odds <= NRCallOdds ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
                    }

                default: // Unplayable
                    return context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
            }
        }

        // ────────────────────────────────────────────────────────────
        // POST-FLOP (Flop / Turn / River)
        // ────────────────────────────────────────────────────────────

        private PlayerAction GetPostFlopAction(IGetTurnContext context)
        {
            var allCards = BuildAllCards();
            if (allCards.Count < 5)
                return PlayerAction.CheckOrCall();

            var bestHand = Evaluator.GetBestHand(allCards);
            HandRankType rank = bestHand.RankType;

            bool facingBet = context.MoneyToCall > 0;
            bool isRiver = context.RoundType == GameRoundType.River;
            int pot = context.CurrentPot;
            float potOdds = facingBet ? (float)context.MoneyToCall / (pot + context.MoneyToCall) : 0f;

            // C-bet opportunity: raised preflop and first to act (no bets behind us)
            bool isCBet = _raisedPreFlop && !facingBet && context.PreviousRoundActions.Count == 0;

            // ── Monster: Full House / Quads / Straight Flush ─────────
            if (rank >= HandRankType.FullHouse)
            {
                if (facingBet)
                {
                    // Re-raise for maximum value
                    if (context.CanRaise)
                        return BetAmount(context, (int)(pot * 0.85));
                    return PlayerAction.CheckOrCall();
                }
                // River: no more streets — always bet (95%) to extract maximum value
                // Flop/Turn: mix slow-play (30%) and leading out (70%) to trap
                int leadFreq = isRiver ? 95 : 70;
                if (context.CanRaise && RandomProvider.Next(0, 100) < leadFreq)
                    return BetAmount(context, (int)(pot * 0.65));
                return PlayerAction.CheckOrCall();
            }

            // ── Strong: Straight / Flush / Three-of-a-Kind ──────────
            if (rank >= HandRankType.ThreeOfAKind)
            {
                if (facingBet)
                {
                    // River: draw is complete — re-raise ~85% for maximum value; flat-call 15% to mix
                    // Flop/Turn: re-raise 55% for value, flat-call 45% to balance range
                    int reraiseFreq = isRiver ? 85 : 55;
                    if (context.CanRaise && RandomProvider.Next(0, 100) < reraiseFreq)
                        return BetAmount(context, (int)(pot * 0.70));
                    return PlayerAction.CheckOrCall();
                }
                // Lead out with a 55-75% pot bet
                if (context.CanRaise)
                    return BetAmount(context, (int)(pot * (0.55 + RandomProvider.Next(0, 20) / 100.0)));
                return PlayerAction.CheckOrCall();
            }

            // ── Two Pair ─────────────────────────────────────────────
            if (rank == HandRankType.TwoPairs)
            {
                if (facingBet)
                {
                    if (potOdds <= 0.40f)
                        return PlayerAction.CheckOrCall();
                    // Fold to large over-bets (> 75% pot)
                    float betRatio = pot > 0 ? (float)context.MoneyToCall / pot : 1f;
                    if (betRatio > 0.75f)
                        return context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
                    return PlayerAction.CheckOrCall();
                }
                if (context.CanRaise)
                    return BetAmount(context, (int)(pot * (0.45 + RandomProvider.Next(0, 15) / 100.0)));
                return PlayerAction.CheckOrCall();
            }

            // ── One Pair ─────────────────────────────────────────────
            if (rank == HandRankType.Pair)
            {
                // Pair + strong draw (flush or OESD) = premium combo: semi-raise aggressively
                int drawPctWithPair = EstimateDrawEquity(allCards, context.RoundType);
                bool hasPairPlusDraw = drawPctWithPair >= 19; // flush draw (19%) or OESD (17%+)

                if (hasPairPlusDraw)
                {
                    if (facingBet)
                    {
                        // Pair + draw: ~40-50% total equity — re-raise 70%, call 30%
                        if (context.CanRaise && RandomProvider.Next(0, 100) < 70)
                            return BetAmount(context, (int)(pot * 0.70));
                        return PlayerAction.CheckOrCall();
                    }
                    // Lead out as a semi-raise 75% of the time
                    if (context.CanRaise && RandomProvider.Next(0, 100) < 75)
                        return BetAmount(context, (int)(pot * 0.65));
                    return PlayerAction.CheckOrCall();
                }

                if (facingBet)
                {
                    float betRatio = pot > 0 ? (float)context.MoneyToCall / pot : 1f;
                    if (betRatio <= 0.40f)
                        return PlayerAction.CheckOrCall();
                    return context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
                }
                // C-bet with pair: style-based frequency; normal lead also style-based
                int betFreq = isCBet ? PairCBetPct : PairLeadPct;
                if (context.CanRaise && RandomProvider.Next(0, 100) < betFreq)
                    return BetAmount(context, (int)(pot * 0.50));
                return PlayerAction.CheckOrCall();
            }

            // ── Weak: High Card / nothing ─────────────────────────────
            {
                int drawPct = EstimateDrawEquity(allCards, context.RoundType);
                float equity = drawPct / 100f;

                if (!facingBet)
                {
                    // River bluff with missed draw — frequency depends on style
                    if (isRiver && context.CanRaise && IsLikelyMissedDraw(allCards) && RandomProvider.Next(0, 100) < BluffRiverPct)
                        return BetAmount(context, (int)(pot * 0.55));
                    // Flop/Turn c-bet bluff when we raised preflop — frequency depends on style
                    if (isCBet && context.CanRaise && RandomProvider.Next(0, 100) < BluffCBetPct)
                        return BetAmount(context, (int)(pot * 0.50));
                    // Semi-bluff a strong draw (flush / OESD) — frequency depends on style
                    if (equity >= 0.30f && context.CanRaise && RandomProvider.Next(0, 100) < BluffSemiPct)
                        return BetAmount(context, (int)(pot * 0.55));
                    return PlayerAction.CheckOrCall();
                }
                else
                {
                    // Call if draw equity beats pot odds with margin
                    if (equity > potOdds + 0.04f)
                        return PlayerAction.CheckOrCall();
                    return context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // HELPERS
        // ────────────────────────────────────────────────────────────

        private List<Card> BuildAllCards()
        {
            var cards = new List<Card> { FirstCard, SecondCard };
            if (CommunityCards != null)
                cards.AddRange(CommunityCards);
            return cards;
        }

        /// <summary>
        /// Returns estimated draw-completion probability in percent (0-100).
        /// Checks for 4-card flush draws and open-ended / gutshot straight draws.
        /// </summary>
        private static int EstimateDrawEquity(List<Card> allCards, GameRoundType round)
        {
            int cardsToSee = round == GameRoundType.Flop ? 2 : round == GameRoundType.Turn ? 1 : 0;
            if (cardsToSee == 0) return 0;

            // Flush draw: exactly 4 cards of the same suit
            var suitCounts = new int[4];
            foreach (var c in allCards) suitCounts[(int)c.Suit]++;
            if (suitCounts.Any(x => x == 4))
                return cardsToSee == 2 ? 35 : 19;

            // Straight draws: find the longest run of consecutive distinct ranks
            var ranks = allCards.Select(c => (int)c.Type).Distinct().OrderBy(r => r).ToList();
            // Treat Ace as low (rank 1) for wheel draws (A-2-3-4-5)
            if (ranks.Contains((int)CardType.Ace))
                ranks.Insert(0, 1);

            int maxRun = 1, run = 1;
            for (int i = 1; i < ranks.Count; i++)
            {
                if (ranks[i] == ranks[i - 1] + 1) { run++; if (run > maxRun) maxRun = run; }
                else run = 1;
            }

            if (maxRun >= 4) return cardsToSee == 2 ? 32 : 17; // OESD / completed draw
            if (maxRun == 3) return cardsToSee == 2 ? 14 : 7;  // Gutshot-ish

            return 0;
        }

        /// <summary>
        /// On the river with a high-card hand, detect if we had a strong but missed draw
        /// (useful for deciding whether to bluff).
        /// </summary>
        private static bool IsLikelyMissedDraw(List<Card> allCards)
        {
            var suitCounts = new int[4];
            foreach (var c in allCards) suitCounts[(int)c.Suit]++;
            if (suitCounts.Any(x => x == 4)) return true;

            var ranks = allCards.Select(c => (int)c.Type).Distinct().OrderBy(r => r).ToList();
            int maxRun = 1, run = 1;
            for (int i = 1; i < ranks.Count; i++)
            {
                if (ranks[i] == ranks[i - 1] + 1) { run++; if (run > maxRun) maxRun = run; }
                else run = 1;
            }
            return maxRun >= 4;
        }

        /// <summary>
        /// Places a bet of <paramref name="amount"/> additional chips (beyond the call).
        /// Respects MinRaise and goes all-in when necessary.
        /// </summary>
        private static PlayerAction BetAmount(IGetTurnContext context, int amount)
        {
            if (!context.CanRaise) return PlayerAction.CheckOrCall();
            int raise = Math.Max(amount, context.MinRaise);
            int maxRaise = context.MoneyLeft - context.MoneyToCall;
            if (raise >= maxRaise) return PlayerAction.Raise(maxRaise); // all-in
            return PlayerAction.Raise(raise);
        }
    }
}


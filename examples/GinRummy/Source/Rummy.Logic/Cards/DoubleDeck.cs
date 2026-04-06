namespace Rummy.Logic.Cards
{
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Extensions;

    /// <summary>
    /// 106-card double deck for Tabletop Meld mode:
    /// two full 52-card decks + 2 Jester wildcards = 106 cards, shuffled.
    /// </summary>
    public class DoubleDeck : IDeck
    {
        private readonly IList<Card> _cards;
        private int _index;

        private static readonly Card _jester1 = new Card(CardSuit.Joker, CardType.Joker);
        private static readonly Card _jester2 = new Card(CardSuit.Joker, CardType.Joker);

        public DoubleDeck()
        {
            var all = new List<Card>(106);
            // Two copies of the standard 52-card deck
            for (int copy = 0; copy < 2; copy++)
                foreach (var card in Deck.AllCards)
                    all.Add(card.DeepClone());
            // Two Jester wildcards
            all.Add(_jester1.DeepClone());
            all.Add(_jester2.DeepClone());

            _cards = EnumerableExtensions.Shuffle(all).ToList();
            _index = _cards.Count;
        }

        public int RemainingCount => _index;

        public Card GetNextCard()
        {
            if (_index == 0)
                throw new InternalGameException("DoubleDeck is empty!");
            return _cards[--_index];
        }

        /// <summary>Returns the top card without removing it (for peeking at the discard pile setup).</summary>
        public Card PeekNextCard()
        {
            if (_index == 0)
                throw new InternalGameException("DoubleDeck is empty!");
            return _cards[_index - 1];
        }
    }
}

namespace Rummy.Logic.Melds
{
    using System.Collections.Generic;
    using System.Linq;
    using Rummy.Logic.Cards;

    /// <summary>
    /// Represents a validated group of cards on the table (set or run).
    /// </summary>
    public class Meld
    {
        public MeldType Type { get; }
        public IReadOnlyList<Card> Cards { get; }

        public Meld(MeldType type, IEnumerable<Card> cards)
        {
            Type  = type;
            Cards = cards.ToList().AsReadOnly();
        }

        public override string ToString()
        {
            string cardsStr = string.Join(" ", Cards.Select(c => c.ToString()));
            return $"[{Type}: {cardsStr}]";
        }
    }
}

// Card, Hand and Deck logic ported from Examples/Blackjack Strategy (Models/CardUtils.cs)
namespace EvolutionaryStudio.Model.Blackjack
{
    public sealed class GameRandomizer
    {
        private readonly Random random = new(Guid.NewGuid().GetHashCode());

        public int IntBetween(int lower, int upper) => random.Next(lower, upper);
        public int IntLessThan(int upper) => random.Next(upper);
        public float GetFloatFromZeroToOne() => (float)random.NextDouble();
    }

    public sealed class Card
    {
        public enum Suits { Hearts, Spades, Clubs, Diamonds }
        public enum Ranks { Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King, Ace }
        public const int HighestRankIndex = 9;

        public Ranks Rank { get; }
        public Suits Suit { get; }

        public Card(Ranks rank, Suits suit)
        {
            Rank = rank;
            Suit = suit;
        }

        public static readonly List<Ranks> ListOfRanks = Enum.GetValues<Ranks>().ToList();
        public static readonly List<Suits> ListOfSuits = Enum.GetValues<Suits>().ToList();

        public int RankValueHigh => Rank switch
        {
            Ranks.Ace => 11,
            Ranks.King or Ranks.Queen or Ranks.Jack or Ranks.Ten => 10,
            _ => (int)Rank
        };

        public int RankValueLow => Rank switch
        {
            Ranks.Ace => 1,
            Ranks.King or Ranks.Queen or Ranks.Jack or Ranks.Ten => 10,
            _ => (int)Rank
        };

        public static string RankText(Ranks rank) => "  23456789TJQKA"[(int)rank].ToString();

        public override string ToString() => RankText(Rank) + Suit;
    }

    public sealed class Hand
    {
        public List<Card> Cards { get; } = new();

        public void AddCard(Card card) => Cards.Add(card);

        public bool IsPair()
        {
            if (Cards.Count > 2) return false;
            return Cards[0].Rank == Cards[1].Rank;
        }

        public bool HasSoftAce()
        {
            int numAces = Cards.Count(c => c.Rank == Card.Ranks.Ace);
            if (numAces == 0) return false;

            // try one ace as 11 and the rest as 1
            int total = 11 +
                Cards.Where(c => c.Rank != Card.Ranks.Ace).Sum(c => c.RankValueLow) +
                (numAces - 1);
            return total <= 21;
        }

        public int HandValue()
        {
            int highValue = 0, lowValue = 0;
            bool aceUsedAsHigh = false;
            foreach (var card in Cards)
            {
                if (card.Rank == Card.Ranks.Ace && !aceUsedAsHigh)
                {
                    highValue += card.RankValueHigh;
                    lowValue += card.RankValueLow;
                    aceUsedAsHigh = true;
                }
                else
                {
                    highValue += card.RankValueLow;
                    lowValue += card.RankValueLow;
                }
            }

            if (lowValue > 21) return lowValue;
            if (highValue > 21) return lowValue;
            return highValue;
        }

        public override string ToString() => string.Join(",", Cards) + " = " + HandValue();
    }

    public sealed class Deck
    {
        private readonly List<Card> cards;
        private readonly GameRandomizer randomizer = new();
        private int currentCard;

        public Deck(int numDecks)
        {
            cards = new List<Card>(52 * numDecks);
            for (int i = 0; i < numDecks; i++)
                foreach (var rank in Card.ListOfRanks)
                    foreach (var suit in Card.ListOfSuits)
                        cards.Add(new Card(rank, suit));
            Shuffle();
        }

        public int CardsRemaining => cards.Count - currentCard;

        public Card DealCard()
        {
            ShuffleIfNeeded();
            return cards[currentCard++];
        }

        public void ForceNextCardToBe(Card.Ranks rank)
        {
            int foundAt = -1;
            for (int i = currentCard; i < cards.Count; i++)
                if (cards[i].Rank == rank) { foundAt = i; break; }
            if (foundAt == -1)
                for (int i = 0; i < currentCard; i++)
                    if (cards[i].Rank == rank) { foundAt = i; break; }

            (cards[foundAt], cards[currentCard]) = (cards[currentCard], cards[foundAt]);
        }

        public void EnsureNextCardIsnt(Card.Ranks rank)
        {
            while (cards[currentCard].Rank == rank)
            {
                currentCard++;
                ShuffleIfNeeded();
            }
        }

        public void Shuffle()
        {
            for (int i = cards.Count - 1; i > 1; i--)
            {
                int swapWith = randomizer.IntLessThan(i);
                (cards[i], cards[swapWith]) = (cards[swapWith], cards[i]);
            }
            currentCard = 0;
        }

        private void ShuffleIfNeeded()
        {
            if (CardsRemaining < 20)
                Shuffle();
        }
    }
}

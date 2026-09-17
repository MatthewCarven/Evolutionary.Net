// Strategy table and game simulation ported from Examples/Blackjack Strategy
// (Models/StrategyBase.cs and Models/StrategyTester.cs)
namespace EvolutionaryStudio.Model.Blackjack
{
    public enum ActionToTake { Stand, Hit, Double, Split }

    /// <summary>
    /// A complete Blackjack playing strategy: for every (dealer upcard, player holding)
    /// combination, the action to take.  Filled in by querying an evolved tree.
    /// </summary>
    public sealed class Strategy
    {
        public const int LowestSoftHandRemainder = 2;
        public const int HighestSoftHandRemainder = 9;
        public const int LowestHardHandValue = 5;
        public const int HighestHardHandValue = 20;

        private readonly ActionToTake[,] pairs = new ActionToTake[10, 10];
        private readonly ActionToTake[,] soft = new ActionToTake[10, HighestSoftHandRemainder + 1];
        private readonly ActionToTake[,] hard = new ActionToTake[10, HighestHardHandValue + 1];

        public void SetActionForPair(Card.Ranks upcard, Card.Ranks pairRank, ActionToTake action)
            => pairs[IndexFromRank(upcard), IndexFromRank(pairRank)] = action;
        public ActionToTake GetActionForPair(int upcardIndex, int pairRankIndex)
            => pairs[upcardIndex, pairRankIndex];

        public void SetActionForSoftHand(Card.Ranks upcard, int softRemainder, ActionToTake action)
            => soft[IndexFromRank(upcard), softRemainder] = action;
        public ActionToTake GetActionForSoftHand(int upcardIndex, int softRemainder)
            => soft[upcardIndex, softRemainder];

        public void SetActionForHardHand(Card.Ranks upcard, int hardTotal, ActionToTake action)
            => hard[IndexFromRank(upcard), hardTotal] = action;
        public ActionToTake GetActionForHardHand(int upcardIndex, int hardTotal)
            => hard[upcardIndex, hardTotal];

        public ActionToTake GetActionForHand(Hand hand, Card dealerUpcard)
        {
            if (hand.HandValue() >= 21) return ActionToTake.Stand;

            int upcardIndex = IndexFromRank(dealerUpcard.Rank);

            if (hand.IsPair())
                return pairs[upcardIndex, IndexFromRank(hand.Cards[0].Rank)];

            if (hand.HasSoftAce())
            {
                int howManyAces = hand.Cards.Count(c => c.Rank == Card.Ranks.Ace);
                int total = hand.Cards
                    .Where(c => c.Rank != Card.Ranks.Ace)
                    .Sum(c => c.RankValueHigh) + (howManyAces - 1);
                return soft[upcardIndex, total];
            }

            return hard[upcardIndex, hand.HandValue()];
        }

        // ten through king share a column, so ten distinct columns: 2..9, T, A
        public static int IndexFromRank(Card.Ranks rank) => rank switch
        {
            Card.Ranks.Ace => 9,
            Card.Ranks.King or Card.Ranks.Queen or Card.Ranks.Jack or Card.Ranks.Ten => 8,
            _ => (int)rank - 2
        };
    }

    public sealed class BlackjackConfig
    {
        public Evolutionary.EngineParameters EngineParams = new();
        public int NumDecks = 4;
        public int HandsPerEval = 25000;
        public int BetSize = 2;
        public int BlackjackPayoffSize = 3;   // 3:2 payoff on a 2-chip bet
        public bool StackTheDeck;
    }

    /// <summary>Plays hands of Blackjack with a strategy and reports chips won or lost.</summary>
    public sealed class StrategyTester
    {
        private readonly Strategy strategy;
        private readonly BlackjackConfig conditions;

        public StrategyTester(Strategy strategy, BlackjackConfig conditions)
        {
            this.strategy = strategy;
            this.conditions = conditions;
        }

        public int PlayHands(int numHandsToPlay)
        {
            int playerChips = 0;
            var deck = new Deck(conditions.NumDecks);
            var randomizer = new GameRandomizer();

            var dealerHand = new Hand();
            var playerHand = new Hand();
            var playerHands = new List<Hand>();
            var betAmountPerHand = new List<int>();

            for (int handNum = 0; handNum < numHandsToPlay; handNum++)
            {
                dealerHand.Cards.Clear();
                playerHand.Cards.Clear();

                dealerHand.AddCard(deck.DealCard());
                dealerHand.AddCard(deck.DealCard());
                playerHand.AddCard(deck.DealCard());

                if (conditions.StackTheDeck)
                {
                    // even out pair / soft / hard frequencies so all strategy cells get exercised
                    var rand = randomizer.GetFloatFromZeroToOne();
                    if (rand < 0.33F)
                    {
                        deck.ForceNextCardToBe(playerHand.Cards[0].Rank);
                    }
                    else if (rand < 0.66F)
                    {
                        if (playerHand.Cards[0].Rank != Card.Ranks.Ace)
                            deck.ForceNextCardToBe(Card.Ranks.Ace);
                        else
                            deck.EnsureNextCardIsnt(Card.Ranks.Ace);
                    }
                }
                playerHand.AddCard(deck.DealCard());

                playerHands.Clear();
                playerHands.Add(playerHand);
                betAmountPerHand.Clear();
                betAmountPerHand.Add(conditions.BetSize);
                playerChips -= conditions.BetSize;

                // 1. player Blackjack
                if (playerHand.HandValue() == 21)
                {
                    if (dealerHand.HandValue() != 21)
                        playerChips += conditions.BetSize + conditions.BlackjackPayoffSize;
                    else
                        playerChips += conditions.BetSize;   // push: bet returned
                    continue;
                }

                // 2. dealer Blackjack: bet already lost
                if (dealerHand.HandValue() == 21) continue;

                // 3. play out each player hand (splits append to the list)
                for (int handIndex = 0; handIndex < playerHands.Count; handIndex++)
                {
                    playerHand = playerHands[handIndex];

                    bool playerDrawing = true;
                    while (playerDrawing)
                    {
                        if (playerHand.HandValue() == 21)
                        {
                            if (playerHand.Cards.Count == 2)   // post-split Blackjack pays off immediately
                            {
                                playerChips += betAmountPerHand[handIndex] +
                                    conditions.BlackjackPayoffSize * betAmountPerHand[handIndex] / conditions.BetSize;
                                betAmountPerHand[handIndex] = 0;
                            }
                            break;
                        }

                        var action = strategy.GetActionForHand(playerHand, dealerHand.Cards[0]);
                        if (action == ActionToTake.Double && playerHand.Cards.Count > 2)
                            action = ActionToTake.Hit;

                        switch (action)
                        {
                            case ActionToTake.Hit:
                                playerHand.AddCard(deck.DealCard());
                                if (playerHand.HandValue() == 21)
                                    playerDrawing = false;
                                if (playerHand.HandValue() > 21)
                                {
                                    betAmountPerHand[handIndex] = 0;
                                    playerDrawing = false;
                                }
                                break;

                            case ActionToTake.Stand:
                                playerDrawing = false;
                                break;

                            case ActionToTake.Double:
                                playerChips -= conditions.BetSize;
                                betAmountPerHand[handIndex] += conditions.BetSize;
                                playerHand.AddCard(deck.DealCard());
                                if (playerHand.HandValue() > 21)
                                    betAmountPerHand[handIndex] = 0;
                                playerDrawing = false;
                                break;

                            case ActionToTake.Split:
                                var newHand = new Hand();
                                newHand.AddCard(playerHand.Cards[1]);
                                playerHand.Cards[1] = deck.DealCard();
                                newHand.AddCard(deck.DealCard());
                                playerHands.Add(newHand);

                                playerChips -= conditions.BetSize;
                                betAmountPerHand.Add(conditions.BetSize);
                                break;
                        }
                    }
                }

                // 4. dealer draws to 17+ if any player hands remain live
                bool playerHandsAvailable = betAmountPerHand.Sum() > 0;
                if (playerHandsAvailable)
                {
                    bool dealerBusted = false;
                    while (dealerHand.HandValue() < 17)
                    {
                        dealerHand.AddCard(deck.DealCard());
                        if (dealerHand.HandValue() > 21)
                        {
                            for (int handIndex = 0; handIndex < playerHands.Count; handIndex++)
                                playerChips += betAmountPerHand[handIndex] * 2;
                            dealerBusted = true;
                            break;
                        }
                    }

                    // 5. showdown
                    if (!dealerBusted)
                    {
                        int dealerValue = dealerHand.HandValue();
                        for (int handIndex = 0; handIndex < playerHands.Count; handIndex++)
                        {
                            int playerValue = playerHands[handIndex].HandValue();
                            if (playerValue == dealerValue)
                                playerChips += betAmountPerHand[handIndex];        // push
                            else if (playerValue > dealerValue)
                                playerChips += betAmountPerHand[handIndex] * 2;    // win
                        }
                    }
                }
            }

            return playerChips;
        }
    }
}

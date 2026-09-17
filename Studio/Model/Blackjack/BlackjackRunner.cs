using Evolutionary;

namespace EvolutionaryStudio.Model.Blackjack
{
    /// <summary>State data each candidate sees: the current hand, plus action votes.</summary>
    public class BlackjackState
    {
        public Hand PlayerHand;
        public Card DealerUpcard;
        public int VotesForHit;
        public int VotesForStand;
        public int VotesForDoubleDown;
        public int VotesForSplit;
    }

    public sealed class BlackjackSnapshot : SnapshotBase
    {
        public CandidateSolution<bool, BlackjackState> Candidate;
        public Strategy Strategy;

        public override string FitnessText => Fitness.ToString("+0;-0;0") + " chips";
    }

    /// <summary>
    /// Runs the Blackjack strategy evolution, ported from the Blackjack Strategy example's
    /// SolutionSingle: boolean trees vote for Hit/Stand/Double/Split via stateful functions,
    /// and fitness is the chips won playing thousands of simulated hands.
    /// </summary>
    public sealed class BlackjackRunner
    {
        private volatile bool stopRequested;

        // both events fire on the engine's worker thread
        public event Action<GenerationStat> GenerationCompleted;
        public event Action<BlackjackSnapshot> NewBest;

        public void RequestStop() => stopRequested = true;

        public Task<BlackjackSnapshot> RunAsync(BlackjackConfig config) => Task.Run(() => Run(config));

        private BlackjackSnapshot Run(BlackjackConfig config)
        {
            stopRequested = false;

            // fitness is money won, so higher is better by definition
            config.EngineParams.IsLowerFitnessBetter = false;
            var engine = new Engine<bool, BlackjackState>(config.EngineParams);

            AddPrimitives(engine);

            engine.AddFitnessFunction(candidate =>
            {
                var strategy = BuildStrategy(candidate);
                return new StrategyTester(strategy, config).PlayHands(config.HandsPerEval);
            });

            float lastBest = float.MinValue;
            engine.AddProgressFunction((progress, bestThisGen) =>
            {
                GenerationCompleted?.Invoke(new GenerationStat
                {
                    Generation = progress.GenerationNumber,
                    BestThisGen = progress.BestFitnessThisGen,
                    AvgThisGen = progress.AvgFitnessThisGen,
                    BestSoFar = progress.BestFitnessSoFar,
                    Milliseconds = progress.TimeForGeneration.TotalMilliseconds
                });

                if (bestThisGen != null &&
                    progress.BestFitnessSoFar > lastBest &&
                    progress.BestFitnessThisGen == progress.BestFitnessSoFar)
                {
                    lastBest = progress.BestFitnessSoFar;
                    NewBest?.Invoke(MakeSnapshot(bestThisGen.Clone(), progress.GenerationNumber, false));
                }

                return !stopRequested;
            });

            var best = engine.FindBestSolution();
            return MakeSnapshot(best, -1, true);
        }

        private static BlackjackSnapshot MakeSnapshot(CandidateSolution<bool, BlackjackState> candidate, int generation, bool isFinal)
        {
            return new BlackjackSnapshot
            {
                Generation = generation,
                Fitness = candidate.Fitness,
                IsFinal = isFinal,
                Candidate = candidate,
                Strategy = BuildStrategy(candidate),
                Tree = candidate.GetTreeInfo(),
                RawExpression = candidate.ToString()
            };
        }

        // ----- primitive set (same names as the original example) ----------------

        private static void AddPrimitives(Engine<bool, BlackjackState> engine)
        {
            // boolean operators
            engine.AddFunction((a, b) => a || b, "Or");
            engine.AddFunction((a, b, c) => a || b || c, "Or3");
            engine.AddFunction((a, b) => a && b, "And");
            engine.AddFunction((a, b, c) => a && b && c, "And3");
            engine.AddFunction(a => !a, "Not");

            // stateful functions register an action vote and pass their argument through
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesForHit++; return v; }, "HitIf");
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesForStand++; return v; }, "StandIf");
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesForDoubleDown++; return v; }, "DoubleIf");
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesForSplit++; return v; }, "SplitIf");

            // terminal functions query the game state

            // soft hands: ace + 2..9
            for (int i = 2; i <= 9; i++)
            {
                int other = i;
                engine.AddTerminalFunction(
                    s => s.PlayerHand.HasSoftAce() && s.PlayerHand.HandValue() - 11 == other,
                    "AcePlus" + other);
            }

            // pairs of 2..9 by rank, tens by value (covers T/J/Q/K), and aces
            for (int r = 2; r <= 9; r++)
            {
                var rank = (Card.Ranks)r;
                engine.AddTerminalFunction(s => HasPairOf(s.PlayerHand, rank), "HasPair" + Card.RankText(rank));
            }
            engine.AddTerminalFunction(
                s => s.PlayerHand.Cards.Count == 2 &&
                     s.PlayerHand.Cards[0].RankValueHigh == 10 &&
                     s.PlayerHand.Cards[1].RankValueHigh == 10,
                "HasPairT");
            engine.AddTerminalFunction(s => HasPairOf(s.PlayerHand, Card.Ranks.Ace), "HasPairA");

            // hard hand totals 5..20
            for (int i = 5; i <= 20; i++)
            {
                int total = i;
                engine.AddTerminalFunction(s => s.PlayerHand.HandValue() == total, "Hard" + total);
            }

            // dealer upcards 2..9 by rank, tens by value, and ace
            for (int r = 2; r <= 9; r++)
            {
                var rank = (Card.Ranks)r;
                engine.AddTerminalFunction(s => s.DealerUpcard.Rank == rank, "Dlr" + Card.RankText(rank));
            }
            engine.AddTerminalFunction(s => s.DealerUpcard.RankValueHigh == 10, "Dlr10");
            engine.AddTerminalFunction(s => s.DealerUpcard.Rank == Card.Ranks.Ace, "DlrA");
        }

        private static bool HasPairOf(Hand hand, Card.Ranks rank)
        {
            return hand.Cards.Count == 2 &&
                   hand.Cards[0].Rank == rank &&
                   hand.Cards[1].Rank == rank;
        }

        // ----- tree → strategy table (ported from StrategyFactory) ---------------

        public static Strategy BuildStrategy(CandidateSolution<bool, BlackjackState> candidate)
        {
            var result = new Strategy();

            foreach (var upcardRank in Card.ListOfRanks)
            {
                if (upcardRank is Card.Ranks.Jack or Card.Ranks.Queen or Card.Ranks.King)
                    continue;

                var dealerCard = new Card(upcardRank, Card.Suits.Diamonds);

                // pairs
                for (var pairedRank = Card.Ranks.Ace; pairedRank >= Card.Ranks.Two; pairedRank--)
                {
                    var hand = new Hand();
                    hand.AddCard(new Card(pairedRank, Card.Suits.Hearts));
                    hand.AddCard(new Card(pairedRank, Card.Suits.Spades));
                    result.SetActionForPair(upcardRank, pairedRank, AskCandidate(candidate, hand, dealerCard));
                }

                // soft hands: A + 2..9 (A-A is a pair, A-10 is blackjack)
                for (int otherCard = 9; otherCard > 1; otherCard--)
                {
                    var hand = new Hand();
                    hand.AddCard(new Card(Card.Ranks.Ace, Card.Suits.Hearts));
                    hand.AddCard(new Card((Card.Ranks)otherCard, Card.Suits.Spades));
                    result.SetActionForSoftHand(upcardRank, otherCard, AskCandidate(candidate, hand, dealerCard));
                }

                // hard hands 5..20
                for (int hardTotal = 20; hardTotal > 4; hardTotal--)
                {
                    var hand = new Hand();
                    int firstCardRank = (hardTotal % 2 != 0) ? (hardTotal + 1) / 2 : hardTotal / 2;
                    int secondCardRank = hardTotal - firstCardRank;

                    // 20 would be T-T (a pair), so use a three-card 20 instead
                    if (hardTotal == 20)
                    {
                        hand.AddCard(new Card(Card.Ranks.Ten, Card.Suits.Diamonds));
                        firstCardRank = 6;
                        secondCardRank = 4;
                    }
                    if (firstCardRank == secondCardRank)
                    {
                        firstCardRank++;
                        secondCardRank--;
                    }

                    hand.AddCard(new Card((Card.Ranks)firstCardRank, Card.Suits.Diamonds));
                    hand.AddCard(new Card((Card.Ranks)secondCardRank, Card.Suits.Spades));
                    result.SetActionForHardHand(upcardRank, hardTotal, AskCandidate(candidate, hand, dealerCard));
                }
            }

            return result;
        }

        private static ActionToTake AskCandidate(CandidateSolution<bool, BlackjackState> candidate, Hand hand, Card dealerUpcard)
        {
            var state = candidate.StateData;
            state.PlayerHand = hand;
            state.DealerUpcard = dealerUpcard;
            state.VotesForHit = 0;
            state.VotesForStand = 0;
            state.VotesForDoubleDown = 0;
            state.VotesForSplit = 0;

            candidate.Evaluate();

            int votesForSplit = hand.IsPair() ? state.VotesForSplit : int.MinValue;
            int votesForDouble = hand.Cards.Count <= 2 ? state.VotesForDoubleDown : int.MinValue;

            var best = ActionToTake.Double;
            int bestVotes = votesForDouble;
            if (state.VotesForStand > bestVotes) { bestVotes = state.VotesForStand; best = ActionToTake.Stand; }
            if (state.VotesForHit > bestVotes) { bestVotes = state.VotesForHit; best = ActionToTake.Hit; }
            if (votesForSplit > bestVotes) { best = ActionToTake.Split; }
            return best;
        }
    }
}

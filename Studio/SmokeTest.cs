using EvolutionaryStudio.Model;
using Evolutionary;

namespace EvolutionaryStudio
{
    /// <summary>
    /// Headless build-verification run: evolve the Koza polynomial for a few
    /// generations and sanity-check the snapshot/inspection pipeline.
    /// Launched via "EvolutionaryStudio --smoke"; returns a process exit code.
    /// </summary>
    internal static class SmokeTest
    {
        public static int Run()
        {
            try
            {
                var preset = ProblemPresets.All[0];
                var dataset = preset.Factory();
                var problem = ProblemBuilder.Prepare(dataset, dataset.Columns.IndexOf("Y"), new List<int> { 0 }, 0.8);

                var functions = FunctionCatalog.CreateAll().Where(f => f.IsSelected).ToList();
                var config = new RunConfig
                {
                    Functions = functions,
                    Constants = new List<float> { 0, 1, 2, 3, -1 },
                    Problem = problem,
                    UseRmse = false,
                    EngineParams = new EngineParameters
                    {
                        PopulationSize = 150,
                        MinGenerations = 5,
                        MaxGenerations = 12,
                        StagnantGenerationLimit = 5,
                        ElitismRate = 0.1,
                        CrossoverRate = 0.95,
                        MutationRate = 0.05,
                        RandomTreeMinDepth = 3,
                        RandomTreeMaxDepth = 6,
                        TourneySize = 4,
                        SelectionStyle = SelectionStyle.Tourney
                    }
                };

                var runner = new GpRunner();
                BestSnapshot lastImprovement = null;
                runner.NewBest += snap => lastImprovement = snap;
                runner.GenerationCompleted += stat =>
                    Console.WriteLine($"  gen {stat.Generation}: best-so-far {stat.BestSoFar}");

                var final = runner.RunAsync(config).GetAwaiter().GetResult();

                var defs = functions.ToDictionary(f => f.Name);
                Console.WriteLine("raw:   " + final.RawExpression);
                Console.WriteLine("infix: " + ExpressionPrinter.ToInfix(final.Tree, defs));

                int nodes = TreeStats.CountNodes(final.Tree);
                int depth = TreeStats.Depth(final.Tree);
                final.Candidate.SetVariableValue("X", 0.5f);
                float atHalf = final.Candidate.Evaluate();
                Console.WriteLine($"nodes={nodes} depth={depth} f(0.5)={atHalf} (true value 0.9375)");

                // exercises the Clone() owner-reference fix: mid-run snapshots are clones
                bool cloneOk = true;
                if (lastImprovement != null)
                {
                    lastImprovement.Candidate.SetVariableValue("X", 0.25f);
                    float cloneValue = lastImprovement.Candidate.Evaluate();
                    cloneOk = !float.IsNaN(cloneValue);
                    Console.WriteLine($"clone snapshot f(0.25)={cloneValue}");
                }

                // quick Blackjack evolution: bool trees, vote functions, strategy extraction
                var bjConfig = new Model.Blackjack.BlackjackConfig
                {
                    HandsPerEval = 2000,
                    EngineParams = new EngineParameters
                    {
                        PopulationSize = 50,
                        MinGenerations = 1,
                        MaxGenerations = 3,
                        StagnantGenerationLimit = 3,
                        ElitismRate = 0,
                        CrossoverRate = 1.0,
                        MutationRate = 0,
                        RandomTreeMinDepth = 4,
                        RandomTreeMaxDepth = 7,
                        TourneySize = 3,
                        SelectionStyle = SelectionStyle.Tourney
                    }
                };
                var bjRunner = new Model.Blackjack.BlackjackRunner();
                var bjFinal = bjRunner.RunAsync(bjConfig).GetAwaiter().GetResult();
                var sampleHand = new Model.Blackjack.Hand();
                sampleHand.AddCard(new Model.Blackjack.Card(Model.Blackjack.Card.Ranks.Ten, Model.Blackjack.Card.Suits.Hearts));
                sampleHand.AddCard(new Model.Blackjack.Card(Model.Blackjack.Card.Ranks.Six, Model.Blackjack.Card.Suits.Spades));
                var sampleAction = bjFinal.Strategy.GetActionForHand(
                    sampleHand, new Model.Blackjack.Card(Model.Blackjack.Card.Ranks.Ace, Model.Blackjack.Card.Suits.Clubs));
                bool blackjackOk = bjFinal.Strategy != null && bjFinal.Tree != null && TreeStats.CountNodes(bjFinal.Tree) >= 1;
                Console.WriteLine($"blackjack: fitness {bjFinal.FitnessText}, hard-16-vs-A action {sampleAction}, ok={blackjackOk}");

                // quick Snake evolution: vote-tree steering, headless replay
                var snakeConfig = new Model.Snake.SnakeConfig
                {
                    Board = 10,
                    GamesPerEval = 2,
                    EngineParams = new EngineParameters
                    {
                        PopulationSize = 60,
                        MinGenerations = 1,
                        MaxGenerations = 3,
                        StagnantGenerationLimit = 3,
                        ElitismRate = 0.05,
                        CrossoverRate = 0.95,
                        MutationRate = 0.05,
                        RandomTreeMinDepth = 4,
                        RandomTreeMaxDepth = 6,
                        TourneySize = 3,
                        SelectionStyle = SelectionStyle.Tourney
                    }
                };
                var snakeRunner = new Model.Snake.SnakeRunner();
                var snakeFinal = snakeRunner.RunAsync(snakeConfig).GetAwaiter().GetResult();
                var (snakeApples, snakeSteps) = Model.Snake.SnakeRunner.PlayGame(snakeFinal.Candidate, 777, 10);
                bool snakeOk = snakeFinal.Tree != null && snakeSteps > 0 &&
                               TreeStats.CountNodes(snakeFinal.Tree) >= 1;
                Console.WriteLine($"snake: fitness {snakeFinal.FitnessText}, " +
                                  $"replay {snakeApples} apples / {snakeSteps} steps, ok={snakeOk}");

                // bundled bike-sharing CSV must load: numeric columns kept, the date column dropped
                var bike = ProblemPresets.All.Single(p => p.DisplayName.StartsWith("Bike")).Factory();
                bool bikeOk = bike.Rows.Count > 700 && bike.Columns.Contains("cnt") && !bike.Columns.Contains("dteday");
                Console.WriteLine($"bike csv: {bike.Rows.Count} rows, {bike.Columns.Count} numeric columns, ok={bikeOk}");

                if (final.Tree == null || nodes < 1 || float.IsNaN(atHalf) || !cloneOk || !bikeOk || !blackjackOk || !snakeOk)
                {
                    Console.WriteLine("SMOKE FAIL");
                    return 1;
                }
                Console.WriteLine("SMOKE OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SMOKE FAIL: " + ex);
                return 1;
            }
        }
    }
}

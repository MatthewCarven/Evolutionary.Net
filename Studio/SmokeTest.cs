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

                if (final.Tree == null || nodes < 1 || float.IsNaN(atHalf) || !cloneOk)
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

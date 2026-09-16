using Evolutionary;

namespace EvolutionaryStudio.Model
{
    public sealed class RunConfig
    {
        public EngineParameters EngineParams = new();
        public List<FunctionDef> Functions = new();
        public List<float> Constants = new();
        public PreparedProblem Problem;
        public bool UseRmse;
    }

    public sealed class GenerationStat
    {
        public int Generation { get; set; }
        public float BestThisGen { get; set; }
        public float AvgThisGen { get; set; }
        public float BestSoFar { get; set; }
        public double Milliseconds { get; set; }
    }

    /// <summary>A cloned best-of-run candidate, safe to inspect and evaluate while evolution continues.</summary>
    public sealed class BestSnapshot
    {
        public int Generation;
        public float Fitness;
        public bool IsFinal;
        public CandidateSolution<float, ProblemState> Candidate;
        public TreeNodeInfo Tree;
        public string RawExpression;

        public string Label => (IsFinal ? "★ Final best" : $"Gen {Generation}") + $"   {Fitness:G6}";
    }

    public sealed class GpRunner
    {
        private volatile bool stopRequested;

        // both events fire on the engine's worker thread
        public event Action<GenerationStat> GenerationCompleted;
        public event Action<BestSnapshot> NewBest;

        public void RequestStop() => stopRequested = true;

        public Task<BestSnapshot> RunAsync(RunConfig config) => Task.Run(() => Run(config));

        private BestSnapshot Run(RunConfig config)
        {
            stopRequested = false;

            // fitness is always an error metric here, so lower is better by definition
            config.EngineParams.IsLowerFitnessBetter = true;
            var engine = new Engine<float, ProblemState>(config.EngineParams);

            foreach (var name in config.Problem.VariableNames)
                engine.AddVariable(name);
            foreach (var c in config.Constants)
                engine.AddConstant(c);
            foreach (var f in config.Functions)
            {
                switch (f.Arity)
                {
                    case 1: engine.AddFunction((Func<float, float>)f.Fn, f.Name); break;
                    case 2: engine.AddFunction((Func<float, float, float>)f.Fn, f.Name); break;
                    case 3: engine.AddFunction((Func<float, float, float, float>)f.Fn, f.Name); break;
                    default: throw new InvalidOperationException($"Unsupported arity {f.Arity} for {f.Name}");
                }
            }

            var train = config.Problem.Train;
            var vars = config.Problem.VariableNames;
            bool rmse = config.UseRmse;
            engine.AddFitnessFunction(candidate => EvaluateFitness(candidate, train, vars, rmse));

            float lastBest = float.MaxValue;
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

                // snapshot the record holder whenever the all-time best improves
                if (bestThisGen != null &&
                    progress.BestFitnessSoFar < lastBest &&
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

        private static BestSnapshot MakeSnapshot(CandidateSolution<float, ProblemState> candidate, int generation, bool isFinal)
        {
            return new BestSnapshot
            {
                Generation = generation,
                Fitness = candidate.Fitness,
                IsFinal = isFinal,
                Candidate = candidate,
                Tree = candidate.GetTreeInfo(),
                RawExpression = candidate.ToString()
            };
        }

        public static float EvaluateFitness(CandidateSolution<float, ProblemState> candidate,
                                            List<Sample> rows, string[] vars, bool rmse)
        {
            double total = 0;
            foreach (var sample in rows)
            {
                for (int i = 0; i < vars.Length; i++)
                    candidate.SetVariableValue(vars[i], sample.Inputs[i]);

                float predicted;
                try { predicted = candidate.Evaluate(); }
                catch { predicted = float.NaN; }

                double err = float.IsNaN(predicted) || float.IsInfinity(predicted)
                    ? 1e6
                    : Math.Abs(predicted - sample.Target);
                total += rmse ? err * err : err;
            }

            double metric = total / rows.Count;
            if (rmse) metric = Math.Sqrt(metric);
            if (double.IsNaN(metric) || double.IsInfinity(metric)) metric = 1e9;

            // truncate so tiny float noise doesn't defeat stagnation-based termination
            return (float)(Math.Truncate(metric * 10000.0) / 10000.0);
        }
    }
}

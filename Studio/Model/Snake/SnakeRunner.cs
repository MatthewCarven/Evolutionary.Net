using Evolutionary;

namespace EvolutionaryStudio.Model.Snake
{
    /// <summary>State data each candidate sees: the current game, plus steering votes.</summary>
    public class SnakeState
    {
        public SnakeGame Game;
        public int VotesStraight;
        public int VotesLeft;
        public int VotesRight;
    }

    public sealed class SnakeConfig
    {
        public EngineParameters EngineParams = new();
        public int Board = 12;
        public int GamesPerEval = 4;
    }

    public sealed class SnakeSnapshot : SnapshotBase
    {
        public CandidateSolution<bool, SnakeState> Candidate;
        public int Board;

        public override string FitnessText => Fitness.ToString("0") + " pts";
    }

    /// <summary>
    /// Evolves a snake player with the GP engine. Boolean trees steer in the
    /// snake's own frame of reference: stateful vote functions (GoStraightIf /
    /// TurnLeftIf / TurnRightIf) cast votes, terminal functions sense danger
    /// and food relative to the current heading. Fitness is apples-first
    /// (apples x 1000 + steps) with a hunger rule so circling forever loses.
    /// </summary>
    public sealed class SnakeRunner
    {
        private const int MaxSteps = 5000;

        private volatile bool stopRequested;

        // both events fire on the engine's worker thread
        public event Action<GenerationStat> GenerationCompleted;
        public event Action<SnakeSnapshot> NewBest;

        public void RequestStop() => stopRequested = true;

        public Task<SnakeSnapshot> RunAsync(SnakeConfig config) => Task.Run(() => Run(config));

        private SnakeSnapshot Run(SnakeConfig config)
        {
            stopRequested = false;

            config.EngineParams.IsLowerFitnessBetter = false;
            var engine = new Engine<bool, SnakeState>(config.EngineParams);
            AddPrimitives(engine);

            // every candidate in a generation faces the same fruit sequences;
            // the progress callback rolls fresh seeds for the next generation
            var seedRng = new Random(1234567);
            int[] seeds = NextSeeds(seedRng, config.GamesPerEval);
            int board = config.Board;

            engine.AddFitnessFunction(candidate =>
            {
                double total = 0;
                foreach (int seed in seeds)
                {
                    var (apples, steps) = PlayGame(candidate, seed, board);
                    total += apples * 1000 + steps;
                }
                return (float)(total / seeds.Length);
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
                    NewBest?.Invoke(MakeSnapshot(bestThisGen.Clone(), progress.GenerationNumber, false, board));
                }

                seeds = NextSeeds(seedRng, config.GamesPerEval);
                return !stopRequested;
            });

            var best = engine.FindBestSolution();
            return MakeSnapshot(best, -1, true, board);
        }

        private static int[] NextSeeds(Random rng, int count)
        {
            var seeds = new int[count];
            for (int i = 0; i < count; i++)
                seeds[i] = rng.Next(1_000_000_000);
            return seeds;
        }

        private static SnakeSnapshot MakeSnapshot(CandidateSolution<bool, SnakeState> candidate,
                                                  int generation, bool isFinal, int board)
        {
            return new SnakeSnapshot
            {
                Generation = generation,
                Fitness = candidate.Fitness,
                IsFinal = isFinal,
                Candidate = candidate,
                Board = board,
                Tree = candidate.GetTreeInfo(),
                RawExpression = candidate.ToString()
            };
        }

        // ----- playing --------------------------------------------------------

        /// <summary>One headless game. Returns (apples, steps survived).</summary>
        public static (int Apples, int Steps) PlayGame(CandidateSolution<bool, SnakeState> candidate,
                                                       int seed, int board)
        {
            var game = new SnakeGame(board, board, seed);
            int hungerLimit = board * board;
            int steps = 0, hunger = 0;
            while (game.Alive && !game.Won && steps < MaxSteps && hunger < hungerLimit)
            {
                game.SetDirection(Decide(candidate, game));
                int before = game.Score;
                if (!game.Step())
                    break;
                steps++;
                hunger = game.Score != before ? 0 : hunger + 1;
            }
            return (game.Score, steps);
        }

        /// <summary>Evaluate the tree once and turn its votes into an absolute direction.</summary>
        public static (int Dy, int Dx) Decide(CandidateSolution<bool, SnakeState> candidate, SnakeGame game)
        {
            var state = candidate.StateData;
            state.Game = game;
            state.VotesStraight = 0;
            state.VotesLeft = 0;
            state.VotesRight = 0;
            candidate.Evaluate();

            var (dy, dx) = game.Direction;
            if (state.VotesLeft > state.VotesStraight && state.VotesLeft >= state.VotesRight)
                return (-dx, dy);                       // left of heading
            if (state.VotesRight > state.VotesStraight && state.VotesRight > state.VotesLeft)
                return (dx, -dy);                       // right of heading
            return (dy, dx);                            // straight (wins ties)
        }

        // ----- primitive set ----------------------------------------------------

        private static void AddPrimitives(Engine<bool, SnakeState> engine)
        {
            // boolean operators
            engine.AddFunction((a, b) => a || b, "Or");
            engine.AddFunction((a, b, c) => a || b || c, "Or3");
            engine.AddFunction((a, b) => a && b, "And");
            engine.AddFunction((a, b, c) => a && b && c, "And3");
            engine.AddFunction(a => !a, "Not");

            // steering votes: pass the value through, register the vote
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesStraight++; return v; }, "GoStraightIf");
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesLeft++; return v; }, "TurnLeftIf");
            engine.AddStatefulFunction((v, s) => { if (v) s.VotesRight++; return v; }, "TurnRightIf");

            // senses, all relative to the snake's current heading
            engine.AddTerminalFunction(s => DangerAt(s.Game, Ahead(s.Game, 1)), "DangerAhead");
            engine.AddTerminalFunction(s => DangerAt(s.Game, LeftOf(s.Game)), "DangerLeft");
            engine.AddTerminalFunction(s => DangerAt(s.Game, RightOf(s.Game)), "DangerRight");
            engine.AddTerminalFunction(s => DangerAt(s.Game, Ahead(s.Game, 2)), "Danger2Ahead");
            engine.AddTerminalFunction(s => FoodDot(s.Game, alongHeading: true) > 0, "FoodAhead");
            engine.AddTerminalFunction(s => FoodDot(s.Game, alongHeading: true) < 0, "FoodBehind");
            engine.AddTerminalFunction(s => FoodDot(s.Game, alongHeading: false) > 0, "FoodLeft");
            engine.AddTerminalFunction(s => FoodDot(s.Game, alongHeading: false) < 0, "FoodRight");
            engine.AddTerminalFunction(s => s.Game.Body.Count > s.Game.Width, "LongBody");
        }

        private static (int Y, int X) Ahead(SnakeGame game, int distance)
        {
            var (hy, hx) = game.Head;
            return (hy + game.Direction.Dy * distance, hx + game.Direction.Dx * distance);
        }

        private static (int Y, int X) LeftOf(SnakeGame game)
        {
            var (hy, hx) = game.Head;
            return (hy - game.Direction.Dx, hx + game.Direction.Dy);
        }

        private static (int Y, int X) RightOf(SnakeGame game)
        {
            var (hy, hx) = game.Head;
            return (hy + game.Direction.Dx, hx - game.Direction.Dy);
        }

        private static bool DangerAt(SnakeGame game, (int Y, int X) cell)
        {
            if (cell.Y < 0 || cell.Y >= game.Height || cell.X < 0 || cell.X >= game.Width)
                return true;
            if (cell == game.Tail)              // the tail vacates next tick
                return false;
            return game.BodySet.Contains(cell);
        }

        /// <summary>Fruit offset projected on the heading (true) or the left vector (false).</summary>
        private static int FoodDot(SnakeGame game, bool alongHeading)
        {
            if (!game.HasFruit) return 0;
            var (hy, hx) = game.Head;
            var (fy, fx) = game.Fruit;
            int ry = fy - hy, rx = fx - hx;
            var (dy, dx) = game.Direction;
            return alongHeading
                ? ry * dy + rx * dx              // + ahead, - behind
                : ry * -dx + rx * dy;            // + left of heading, - right
        }
    }
}

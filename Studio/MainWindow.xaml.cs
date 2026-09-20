using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Evolutionary;
using EvolutionaryStudio.Controls;
using EvolutionaryStudio.Model;
using EvolutionaryStudio.Model.Blackjack;
using EvolutionaryStudio.Model.Snake;
using Microsoft.Win32;

namespace EvolutionaryStudio
{
    public partial class MainWindow : Window
    {
        private readonly List<FunctionDef> functionDefs = FunctionCatalog.CreateAll();
        private readonly Dictionary<string, FunctionDef> defsByName;
        private readonly List<ProblemPreset> presets = ProblemPresets.All;
        private readonly ObservableCollection<GenerationStat> history = new();
        private readonly ObservableCollection<SnapshotBase> snapshots = new();
        private readonly ObservableCollection<ColumnChoice> columnChoices = new();

        private Dataset currentDataset;
        private PreparedProblem lastRunProblem;
        private GpRunner runner;
        private BlackjackRunner bjRunner;
        private BlackjackConfig lastBjConfig;
        private SnakeRunner snakeRunner;
        private SnakeConfig lastSnakeConfig;
        private PlotSeries bestSeries, avgSeries;
        private List<VarRow> playgroundRows;
        private bool suppressPresetEvent;
        private string loadedCsvPath;

        // snake replay (Watch tab)
        private System.Windows.Threading.DispatcherTimer replayTimer;
        private CandidateSolution<bool, SnakeState> replayCandidate;
        private Model.Snake.SnakeGame replayGame;
        private readonly Random replayRng = new();
        private int replaySteps, replayHunger, replayGames, replayBest, replayDeadTicks;

        private bool IsBlackjackMode => cboMode.SelectedIndex == 1;
        private bool IsSnakeMode => cboMode.SelectedIndex == 2;

        public MainWindow()
        {
            InitializeComponent();
            defsByName = functionDefs.ToDictionary(f => f.Name);

            icFunctions.ItemsSource = functionDefs;
            icColumns.ItemsSource = columnChoices;
            gridHistory.ItemsSource = history;
            lstSnapshots.ItemsSource = snapshots;
            cboPreset.ItemsSource = presets;

            plotFitness.XLabel = "Generation";
            plotFitness.YLabel = "Fitness (error)";
            plotFit.Title = "Predicted vs actual";
            plotFit.XLabel = "Actual";
            plotFit.YLabel = "Predicted";
            plotFit.ShowYEqualsX = true;

            UpdateTrainLabel();
            cboPreset.SelectedIndex = 0;
        }

        // ----- mode switching ----------------------------------------------------

        private void CboMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (grpBlackjack == null || grpSnake == null) return;   // still initializing

            int mode = cboMode.SelectedIndex;
            bool regression = mode == 0, blackjack = mode == 1, snake = mode == 2;
            grpProblem.Visibility = regression ? Visibility.Visible : Visibility.Collapsed;
            grpFunctions.Visibility = regression ? Visibility.Visible : Visibility.Collapsed;
            grpConstants.Visibility = regression ? Visibility.Visible : Visibility.Collapsed;
            grpBlackjack.Visibility = blackjack ? Visibility.Visible : Visibility.Collapsed;
            grpSnake.Visibility = snake ? Visibility.Visible : Visibility.Collapsed;

            SetEngineParamDefaults(mode);
            plotFitness.YLabel = blackjack ? "Fitness (chips won)"
                               : snake ? "Fitness (points)"
                               : "Fitness (error)";

            // chip scores are negative, so a log axis only makes sense elsewhere;
            // regression error and snake points are non-negative
            if (blackjack) chkLogScale.IsChecked = false;
            chkLogScale.IsEnabled = !blackjack;
            plotFitness.LogY = chkLogScale.IsChecked == true;

            txtRunStatus.Text = regression
                ? "Regression mode — engine parameters set to the regression defaults."
                : blackjack
                    ? "Blackjack mode — engine parameters set to the Blackjack defaults. Press Run."
                    : "Snake mode — evolve a self-driving snake, then watch it play in the Examiner. Press Run.";
        }

        private void SetEngineParamDefaults(int mode)
        {
            // modes: 0 = regression, 1 = blackjack, 2 = snake
            txtPopulation.Text = mode switch { 1 => "250", 2 => "250", _ => "500" };
            txtMinGen.Text = mode switch { 0 => "20", _ => "1" };
            txtMaxGen.Text = "100";
            txtStagnant.Text = mode switch { 1 => "10", _ => "15" };
            txtElitism.Text = mode switch { 0 => "0.10", 1 => "0", _ => "0.05" };
            txtCrossover.Text = mode switch { 1 => "1.0", _ => "0.95" };
            txtMutation.Text = mode switch { 1 => "0", _ => "0.05" };
            txtMinDepth.Text = mode switch { 0 => "3", _ => "4" };
            txtMaxDepth.Text = mode switch { 0 => "6", _ => "7" };
            cboSelection.SelectedIndex = 0;
            txtTourney.Text = mode switch { 1 => "3", _ => "4" };
        }

        // ----- problem selection -------------------------------------------------

        private void CboPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (suppressPresetEvent || cboPreset.SelectedItem is not ProblemPreset preset)
                return;
            try
            {
                ApplyDataset(preset.Factory(), preset);
                loadedCsvPath = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load this preset:\n\n" + ex.Message, "Preset failed",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnLoadCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Delimited data (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
                Title = "Load a dataset"
            };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                var ds = CsvLoader.Load(dialog.FileName);
                suppressPresetEvent = true;
                cboPreset.SelectedIndex = -1;
                suppressPresetEvent = false;
                ApplyDataset(ds, null);
                loadedCsvPath = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load this file:\n\n" + ex.Message, "CSV load failed",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyDataset(Dataset ds, ProblemPreset preset)
        {
            currentDataset = ds;
            txtDatasetInfo.Text = $"{ds.Name} — {ds.Rows.Count} rows, {ds.Columns.Count} numeric columns.";

            cboTarget.ItemsSource = ds.Columns;
            string target = preset?.DefaultTarget != null && ds.Columns.Contains(preset.DefaultTarget)
                ? preset.DefaultTarget
                : ds.Columns[^1];
            cboTarget.SelectedItem = target;

            var excluded = new HashSet<string>(preset?.ExcludedInputs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            columnChoices.Clear();
            foreach (var col in ds.Columns)
                columnChoices.Add(new ColumnChoice { Name = col, IsSelected = col != target && !excluded.Contains(col) });
        }

        private void CboTarget_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // the target can't also be an input
            if (cboTarget.SelectedItem is string target)
                foreach (var choice in columnChoices.Where(c => c.Name == target))
                    choice.IsSelected = false;
        }

        private void SliderTrain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTrainLabel();

        private void UpdateTrainLabel()
        {
            if (lblTrain != null)
                lblTrain.Text = $"{(int)sliderTrain.Value}% train / {100 - (int)sliderTrain.Value}% test";
        }

        // ----- run control -------------------------------------------------------

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (IsSnakeMode)
                await RunSnakeAsync();
            else if (IsBlackjackMode)
                await RunBlackjackAsync();
            else
                await RunEvolutionAsync();
        }

        private void ResetRunViews()
        {
            history.Clear();
            snapshots.Clear();
            txtInfix.Text = "";
            txtSnapshotStats.Text = "";
            treeDiagram.Root = null;
            plotFit.SetSeries();
            txtFitMetrics.Text = "";
            strategyGrid.Strategy = null;
            txtStrategyInfo.Text = "";
            txtEvalResult.Text = "";
            StopReplay();
        }

        private void StartFitnessChart(string bestName)
        {
            bestSeries = new PlotSeries { Name = bestName, Color = Color.FromRgb(0x2D, 0x6C, 0xB5), Thickness = 2.2 };
            avgSeries = new PlotSeries { Name = "Average (gen)", Color = Color.FromRgb(0xD8, 0x8A, 0x2A), AutoScale = false };
            ApplyFitnessSeries();
        }

        private async Task RunEvolutionAsync()
        {
            var config = BuildConfig(out string error);
            if (config == null)
            {
                MessageBox.Show(this, error, "Can't start the run", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResetRunViews();
            lastRunProblem = config.Problem;
            StartFitnessChart("Best so far");

            runner = new GpRunner();
            runner.GenerationCompleted += stat => Dispatcher.BeginInvoke(() => OnGeneration(stat));
            runner.NewBest += snap => Dispatcher.BeginInvoke(() => AddSnapshot(snap));

            SetRunningUi(true);
            txtRunStatus.Text = "Evolving…";
            try
            {
                var final = await runner.RunAsync(config);
                AddSnapshot(final);
                txtRunStatus.Text = $"Done — best fitness {final.Fitness:G6} after {history.Count} generations.  " +
                                    "Open the Examiner tab to inspect it.";
            }
            catch (Exception ex)
            {
                txtRunStatus.Text = "Run failed.";
                MessageBox.Show(this, ex.ToString(), "Run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetRunningUi(false);
            }
        }

        private async Task RunBlackjackAsync()
        {
            var config = BuildBlackjackConfig(out string error);
            if (config == null)
            {
                MessageBox.Show(this, error, "Can't start the run", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResetRunViews();
            lastRunProblem = null;
            lastBjConfig = config;
            StartFitnessChart("Best so far (chips)");

            bjRunner = new BlackjackRunner();
            bjRunner.GenerationCompleted += stat => Dispatcher.BeginInvoke(() => OnGeneration(stat));
            bjRunner.NewBest += snap => Dispatcher.BeginInvoke(() => AddSnapshot(snap));

            SetRunningUi(true);
            txtRunStatus.Text = "Evolving a Blackjack strategy…";
            try
            {
                var final = await bjRunner.RunAsync(config);
                AddSnapshot(final);
                txtRunStatus.Text = $"Done — best strategy scored {final.FitnessText} over {config.HandsPerEval} hands.  " +
                                    "Open the Examiner tab to see how it plays.";
            }
            catch (Exception ex)
            {
                txtRunStatus.Text = "Run failed.";
                MessageBox.Show(this, ex.ToString(), "Run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetRunningUi(false);
            }
        }

        private async Task RunSnakeAsync()
        {
            var config = BuildSnakeConfig(out string error);
            if (config == null)
            {
                MessageBox.Show(this, error, "Can't start the run", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResetRunViews();
            lastRunProblem = null;
            lastSnakeConfig = config;
            StartFitnessChart("Best so far (points)");

            snakeRunner = new SnakeRunner();
            snakeRunner.GenerationCompleted += stat => Dispatcher.BeginInvoke(() => OnGeneration(stat));
            snakeRunner.NewBest += snap => Dispatcher.BeginInvoke(() => AddSnapshot(snap));

            SetRunningUi(true);
            txtRunStatus.Text = "Evolving a snake player…";
            try
            {
                var final = await snakeRunner.RunAsync(config);
                AddSnapshot(final);
                txtRunStatus.Text = $"Done — best snake scored {final.FitnessText}.  " +
                                    "Open the Examiner's Watch tab to see it play.";
            }
            catch (Exception ex)
            {
                txtRunStatus.Text = "Run failed.";
                MessageBox.Show(this, ex.ToString(), "Run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetRunningUi(false);
            }
        }

        private SnakeConfig BuildSnakeConfig(out string error)
        {
            error = null;
            if (!TryParseInt(txtSnakeBoard.Text, 6, 30, "Board size", out int board, ref error)) return null;
            if (!TryParseInt(txtSnakeGames.Text, 1, 20, "Games per fitness eval", out int games, ref error)) return null;
            if (!TryParseEngineParams(out var engineParams, ref error)) return null;

            return new SnakeConfig { EngineParams = engineParams, Board = board, GamesPerEval = games };
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            runner?.RequestStop();
            bjRunner?.RequestStop();
            snakeRunner?.RequestStop();
            btnStop.IsEnabled = false;
            txtRunStatus.Text = "Stopping after this generation…";
        }

        private void SetRunningUi(bool running)
        {
            btnRun.IsEnabled = !running;
            btnStop.IsEnabled = running;
            panelConfig.IsEnabled = !running;
        }

        private RunConfig BuildConfig(out string error)
        {
            error = null;
            if (currentDataset == null) { error = "Load a dataset or pick a preset first."; return null; }
            if (cboTarget.SelectedItem is not string target) { error = "Pick a target column."; return null; }

            var inputIndexes = new List<int>();
            for (int i = 0; i < currentDataset.Columns.Count; i++)
            {
                string col = currentDataset.Columns[i];
                if (col != target && columnChoices.Any(c => c.Name == col && c.IsSelected))
                    inputIndexes.Add(i);
            }
            if (inputIndexes.Count == 0) { error = "Select at least one input column."; return null; }

            var selectedFunctions = functionDefs.Where(f => f.IsSelected).ToList();
            if (selectedFunctions.Count == 0) { error = "Select at least one function."; return null; }

            var constants = new List<float>();
            foreach (var part in txtConstants.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseFloat(part, out float value)) { error = $"Constant \"{part}\" is not a number."; return null; }
                constants.Add(value);
            }

            if (!TryParseEngineParams(out var engineParams, ref error)) return null;

            var problem = ProblemBuilder.Prepare(currentDataset, currentDataset.Columns.IndexOf(target),
                                                 inputIndexes, sliderTrain.Value / 100.0);

            return new RunConfig
            {
                Functions = selectedFunctions,
                Constants = constants,
                Problem = problem,
                UseRmse = cboMetric.SelectedIndex == 1,
                EngineParams = engineParams
            };
        }

        private BlackjackConfig BuildBlackjackConfig(out string error)
        {
            error = null;
            if (!TryParseInt(txtBjHands.Text, 500, 1000000, "Hands per fitness eval", out int hands, ref error)) return null;
            if (!TryParseInt(txtBjDecks.Text, 1, 8, "Number of decks", out int decks, ref error)) return null;
            if (!TryParseEngineParams(out var engineParams, ref error)) return null;

            return new BlackjackConfig
            {
                EngineParams = engineParams,
                HandsPerEval = hands,
                NumDecks = decks,
                StackTheDeck = chkBjStack.IsChecked == true
            };
        }

        private bool TryParseEngineParams(out EngineParameters engineParams, ref string error)
        {
            engineParams = null;
            if (!TryParseInt(txtPopulation.Text, 10, 100000, "Population size", out int population, ref error)) return false;
            if (!TryParseInt(txtMinGen.Text, 1, 100000, "Min generations", out int minGen, ref error)) return false;
            if (!TryParseInt(txtMaxGen.Text, minGen, 1000000, "Max generations", out int maxGen, ref error)) return false;
            if (!TryParseInt(txtStagnant.Text, 1, 100000, "Stagnant generations", out int stagnant, ref error)) return false;
            if (!TryParseDouble(txtElitism.Text, 0, 0.9, "Elitism rate", out double elitism, ref error)) return false;
            if (!TryParseDouble(txtCrossover.Text, 0, 1, "Crossover rate", out double crossover, ref error)) return false;
            if (!TryParseDouble(txtMutation.Text, 0, 1, "Mutation rate", out double mutation, ref error)) return false;
            if (!TryParseInt(txtMinDepth.Text, 1, 10, "Tree min depth", out int minDepth, ref error)) return false;
            if (!TryParseInt(txtMaxDepth.Text, minDepth, 10, "Tree max depth", out int maxDepth, ref error)) return false;
            if (!TryParseInt(txtTourney.Text, 2, population, "Tourney size", out int tourney, ref error)) return false;

            engineParams = new EngineParameters
            {
                PopulationSize = population,
                MinGenerations = minGen,
                MaxGenerations = maxGen,
                StagnantGenerationLimit = stagnant,
                ElitismRate = elitism,
                CrossoverRate = crossover,
                MutationRate = mutation,
                RandomTreeMinDepth = minDepth,
                RandomTreeMaxDepth = maxDepth,
                TourneySize = tourney,
                SelectionStyle = cboSelection.SelectedIndex switch
                {
                    1 => SelectionStyle.RouletteWheel,
                    2 => SelectionStyle.Ranked,
                    _ => SelectionStyle.Tourney
                }
            };
            return true;
        }

        // ----- live progress -----------------------------------------------------

        private void OnGeneration(GenerationStat stat)
        {
            history.Insert(0, stat);
            bestSeries?.Points.Add(new Point(stat.Generation, stat.BestSoFar));
            avgSeries?.Points.Add(new Point(stat.Generation, stat.AvgThisGen));
            plotFitness.Redraw();
            txtRunStatus.Text = $"Gen {stat.Generation} — best so far {stat.BestSoFar:G6}, " +
                                $"gen avg {stat.AvgThisGen:G6}, {stat.Milliseconds:0} ms/gen";
        }

        private void ChkShowAvg_Changed(object sender, RoutedEventArgs e) => ApplyFitnessSeries();

        private void ApplyFitnessSeries()
        {
            if (bestSeries == null) return;
            if (chkShowAvg.IsChecked == true)
                plotFitness.SetSeries(bestSeries, avgSeries);
            else
                plotFitness.SetSeries(bestSeries);
        }

        // ----- examiner ----------------------------------------------------------

        private void AddSnapshot(SnapshotBase snap)
        {
            // the final best is usually the same tree as the last improvement — merge them
            if (snap.IsFinal && snapshots.Count > 0 && snapshots[0].RawExpression == snap.RawExpression)
                snapshots.RemoveAt(0);
            snapshots.Insert(0, snap);

            if (chkFollow.IsChecked == true || snap.IsFinal)
                lstSnapshots.SelectedIndex = 0;
        }

        private void LstSnapshots_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstSnapshots.SelectedItem is SnapshotBase snap)
                ShowSnapshot(snap);
        }

        private void ShowSnapshot(SnapshotBase snap)
        {
            txtInfix.Text = ExpressionPrinter.ToInfix(snap.Tree, defsByName);

            int nodes = TreeStats.CountNodes(snap.Tree);
            int depth = TreeStats.Depth(snap.Tree);
            var usage = TreeStats.UsageCounts(snap.Tree)
                                 .OrderByDescending(kv => kv.Value)
                                 .Take(6)
                                 .Select(kv => $"{kv.Key}×{kv.Value}");
            string origin = snap.IsFinal ? "final best of run" : $"new best at generation {snap.Generation}";
            txtSnapshotStats.Text = $"{origin}   ·   fitness {snap.FitnessText}   ·   {nodes} nodes, depth {depth}   ·   {string.Join(", ", usage)}";

            treeDiagram.Root = snap.Tree;
            txtEvalResult.Text = "";

            bool blackjack = snap is BlackjackSnapshot;
            bool snake = snap is SnakeSnapshot;
            bool regressionMode = !blackjack && !snake;
            tabStrategy.Visibility = blackjack ? Visibility.Visible : Visibility.Collapsed;
            tabWatch.Visibility = snake ? Visibility.Visible : Visibility.Collapsed;
            tabFit.Visibility = regressionMode ? Visibility.Visible : Visibility.Collapsed;
            tabPlayground.Visibility = regressionMode ? Visibility.Visible : Visibility.Collapsed;
            if (tabsExaminer.SelectedItem is System.Windows.Controls.TabItem current && current.Visibility != Visibility.Visible)
                tabsExaminer.SelectedItem = blackjack ? tabStrategy : snake ? tabWatch : tabTree;

            if (snap is BestSnapshot regression)
            {
                StopReplay();
                UpdateFitPlot(regression);
                UpdatePlayground();
            }
            else if (snap is BlackjackSnapshot bj)
            {
                StopReplay();
                ShowBlackjackSnapshot(bj);
            }
            else if (snap is SnakeSnapshot snakeSnap)
            {
                StartReplay(snakeSnap);
            }
        }

        // ----- snake replay (Watch tab) ------------------------------------------

        private void StartReplay(SnakeSnapshot snap)
        {
            replayCandidate = snap.Candidate;
            replayGames = 0;
            replayBest = 0;
            NewReplayGame(snap.Board);

            if (replayTimer == null)
            {
                replayTimer = new System.Windows.Threading.DispatcherTimer();
                replayTimer.Tick += (_, _) => ReplayTick();
            }
            replayTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / sliderReplaySpeed.Value);
            replayTimer.Start();
            btnReplayPause.Content = "Pause";
        }

        private void StopReplay()
        {
            replayTimer?.Stop();
            replayCandidate = null;
            replayGame = null;
            if (snakeBoard != null) snakeBoard.Game = null;
            if (txtReplayInfo != null) txtReplayInfo.Text = "";
        }

        private void NewReplayGame(int board)
        {
            replayGame = new Model.Snake.SnakeGame(board, board, replayRng.Next(1_000_000_000));
            replaySteps = replayHunger = replayDeadTicks = 0;
            snakeBoard.Game = replayGame;
        }

        private void ReplayTick()
        {
            if (replayCandidate == null || replayGame == null)
                return;

            int board = replayGame.Width;
            if (replayGame.Alive && !replayGame.Won && replayHunger < board * board)
            {
                replayGame.SetDirection(SnakeRunner.Decide(replayCandidate, replayGame));
                int before = replayGame.Score;
                replayGame.Step();
                replaySteps++;
                replayHunger = replayGame.Score != before ? 0 : replayHunger + 1;
            }
            else
            {
                // linger on the corpse for a moment, then deal a fresh board
                replayDeadTicks++;
                if (replayDeadTicks > Math.Max(6, (int)sliderReplaySpeed.Value))
                {
                    replayGames++;
                    replayBest = Math.Max(replayBest, replayGame.Score);
                    NewReplayGame(board);
                }
            }

            snakeBoard.Refresh();
            txtReplayInfo.Text = $"apples {replayGame.Score}   steps {replaySteps}   " +
                                 $"game {replayGames + 1}   best {replayBest}";
        }

        private void BtnReplayPause_Click(object sender, RoutedEventArgs e)
        {
            if (replayTimer == null) return;
            if (replayTimer.IsEnabled)
            {
                replayTimer.Stop();
                btnReplayPause.Content = "Play";
            }
            else if (replayCandidate != null)
            {
                replayTimer.Start();
                btnReplayPause.Content = "Pause";
            }
        }

        private void BtnReplayNew_Click(object sender, RoutedEventArgs e)
        {
            if (replayGame != null)
            {
                replayGames++;
                replayBest = Math.Max(replayBest, replayGame.Score);
                NewReplayGame(replayGame.Width);
            }
        }

        private void SliderReplaySpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (replayTimer != null)
                replayTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, e.NewValue));
        }

        private void ShowBlackjackSnapshot(BlackjackSnapshot snap)
        {
            strategyGrid.Strategy = snap.Strategy;
            if (lastBjConfig == null)
            {
                txtStrategyInfo.Text = $"Training score: {snap.FitnessText}";
                return;
            }

            // replay the strategy on freshly shuffled decks to show an honest out-of-training score
            int validation = new StrategyTester(snap.Strategy, lastBjConfig).PlayHands(lastBjConfig.HandsPerEval);
            double wagered = (double)lastBjConfig.HandsPerEval * lastBjConfig.BetSize;
            txtStrategyInfo.Text =
                $"Training score: {snap.FitnessText} over {lastBjConfig.HandsPerEval} hands    ·    " +
                $"fresh-deal validation: {validation:+0;-0;0} chips  (≈{100.0 * validation / wagered:F2}% of chips wagered)";
        }

        private void UpdateFitPlot(BestSnapshot snap)
        {
            if (lastRunProblem == null) return;

            var trainPoints = EvaluateSplit(snap, lastRunProblem.Train, out string trainMetrics);
            var testPoints = EvaluateSplit(snap, lastRunProblem.Test, out string testMetrics);

            plotFit.SetSeries(
                new PlotSeries { Name = $"Train ({lastRunProblem.Train.Count})", Color = Color.FromArgb(150, 0x2D, 0x6C, 0xB5), Scatter = true, Points = trainPoints },
                new PlotSeries { Name = $"Test ({lastRunProblem.Test.Count})", Color = Color.FromArgb(190, 0xD8, 0x5A, 0x2A), Scatter = true, Points = testPoints });

            txtFitMetrics.Text = $"Train:  {trainMetrics}\nTest:   {testMetrics}";
        }

        private List<Point> EvaluateSplit(BestSnapshot snap, List<Sample> rows, out string metrics)
        {
            var points = new List<Point>();
            if (rows.Count == 0) { metrics = "(no rows)"; return points; }

            int step = Math.Max(1, rows.Count / 2000);   // cap plotted points for huge datasets
            double absSum = 0, sqSum = 0, targetSum = 0;
            int n = 0;
            var residualsBase = new List<(double actual, double predicted)>();

            foreach (var sample in rows)
            {
                for (int i = 0; i < lastRunProblem.VariableNames.Length; i++)
                    snap.Candidate.SetVariableValue(lastRunProblem.VariableNames[i], sample.Inputs[i]);
                float predicted;
                try { predicted = snap.Candidate.Evaluate(); }
                catch { predicted = float.NaN; }
                if (float.IsNaN(predicted) || float.IsInfinity(predicted))
                    continue;

                double err = predicted - sample.Target;
                absSum += Math.Abs(err);
                sqSum += err * err;
                targetSum += sample.Target;
                residualsBase.Add((sample.Target, predicted));
                if (n % step == 0)
                    points.Add(new Point(sample.Target, predicted));
                n++;
            }

            if (n == 0) { metrics = "(all evaluations were NaN/∞)"; return points; }

            double mae = absSum / n;
            double rmse = Math.Sqrt(sqSum / n);
            double mean = targetSum / n;
            double ssTot = residualsBase.Sum(r => (r.actual - mean) * (r.actual - mean));
            double r2 = ssTot < 1e-12 ? double.NaN : 1 - sqSum / ssTot;
            metrics = $"MAE {mae,10:F4}    RMSE {rmse,10:F4}    R² {r2,7:F4}    ({n} rows)";
            return points;
        }

        private void UpdatePlayground()
        {
            if (lastRunProblem == null) return;
            var vars = lastRunProblem.VariableNames;

            // keep the user's values if the variable set hasn't changed
            if (playgroundRows != null && playgroundRows.Select(r => r.Variable).SequenceEqual(vars))
                return;

            var defaults = lastRunProblem.Test.FirstOrDefault() ?? lastRunProblem.Train.FirstOrDefault();
            playgroundRows = vars.Select((v, i) => new VarRow
            {
                Variable = v,
                Value = defaults != null ? defaults.Inputs[i].ToString("G6", CultureInfo.InvariantCulture) : "0"
            }).ToList();
            gridPlayground.ItemsSource = playgroundRows;
        }

        private void BtnEvaluate_Click(object sender, RoutedEventArgs e)
        {
            if (lstSnapshots.SelectedItem is not BestSnapshot snap || playgroundRows == null)
                return;

            gridPlayground.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            foreach (var row in playgroundRows)
            {
                if (!TryParseFloat(row.Value, out float value))
                {
                    txtEvalResult.Text = $"\"{row.Value}\" is not a number ({row.Variable}).";
                    return;
                }
                snap.Candidate.SetVariableValue(row.Variable, value);
            }

            try
            {
                float result = snap.Candidate.Evaluate();
                txtEvalResult.Text = $"= {result:G7}";
            }
            catch (Exception ex)
            {
                txtEvalResult.Text = "Evaluation failed: " + ex.Message;
            }
        }

        private void BtnCopyInfix_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtInfix.Text)) Clipboard.SetText(txtInfix.Text);
        }

        private void BtnCopyRaw_Click(object sender, RoutedEventArgs e)
        {
            if (lstSnapshots.SelectedItem is SnapshotBase snap) Clipboard.SetText(snap.RawExpression);
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (treeDiagram != null)
                treeDiagram.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
        }

        // ----- exports -------------------------------------------------------------

        private void ChkLogScale_Changed(object sender, RoutedEventArgs e)
        {
            if (plotFitness == null) return;
            plotFitness.LogY = chkLogScale.IsChecked == true;
            plotFitness.Redraw();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (history.Count == 0)
            {
                txtRunStatus.Text = "Nothing to export yet — run the engine first.";
                return;
            }
            var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = "generation-history.csv" };
            if (dialog.ShowDialog(this) != true) return;

            var lines = new List<string> { "generation,best_this_gen,avg_this_gen,best_so_far,ms_per_gen" };
            foreach (var s in history.OrderBy(h => h.Generation))
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{s.Generation},{s.BestThisGen},{s.AvgThisGen},{s.BestSoFar},{s.Milliseconds:0}"));
            System.IO.File.WriteAllLines(dialog.FileName, lines);
            txtRunStatus.Text = "Wrote " + dialog.FileName;
        }

        private void BtnSaveChart_Click(object sender, RoutedEventArgs e) => SaveElementPng(plotFitness, "fitness-chart.png");

        private void BtnSaveTree_Click(object sender, RoutedEventArgs e)
        {
            if (treeDiagram.Root == null) { txtRunStatus.Text = "No tree selected yet."; return; }
            SaveElementPng(treeDiagram, "expression-tree.png");
        }

        private void BtnSaveStrategy_Click(object sender, RoutedEventArgs e)
        {
            if (strategyGrid.Strategy == null) { txtRunStatus.Text = "No strategy yet — run a Blackjack evolution first."; return; }
            SaveElementPng(strategyGrid, "blackjack-strategy.png");
        }

        private void SaveElementPng(FrameworkElement element, string suggestedName)
        {
            if (element.ActualWidth < 4 || element.ActualHeight < 4)
            {
                txtRunStatus.Text = "Nothing to save yet.";
                return;
            }
            if (element.ActualWidth * element.ActualHeight > 40_000_000)
            {
                txtRunStatus.Text = "Too large to export as a PNG — use the expression text instead.";
                return;
            }
            var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = suggestedName };
            if (dialog.ShowDialog(this) != true) return;

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(dialog.FileName);
            encoder.Save(stream);
            txtRunStatus.Text = "Wrote " + dialog.FileName;
        }

        // ----- setup save / load ----------------------------------------------------

        private sealed class StudioSetup
        {
            public int Mode { get; set; }
            public int PresetIndex { get; set; } = -1;
            public string CsvPath { get; set; }
            public string TargetColumn { get; set; }
            public List<string> InputColumns { get; set; } = new();
            public double TrainPercent { get; set; } = 75;
            public int MetricIndex { get; set; }
            public List<string> Functions { get; set; } = new();
            public string Constants { get; set; }
            public string Population { get; set; }
            public string MinGenerations { get; set; }
            public string MaxGenerations { get; set; }
            public string StagnantLimit { get; set; }
            public string ElitismRate { get; set; }
            public string CrossoverRate { get; set; }
            public string MutationRate { get; set; }
            public string TreeMinDepth { get; set; }
            public string TreeMaxDepth { get; set; }
            public int SelectionIndex { get; set; }
            public string TourneySize { get; set; }
            public string BlackjackHands { get; set; }
            public string BlackjackDecks { get; set; }
            public bool BlackjackStack { get; set; }
        }

        private void BtnSaveSetup_Click(object sender, RoutedEventArgs e)
        {
            var setup = new StudioSetup
            {
                Mode = cboMode.SelectedIndex,
                PresetIndex = cboPreset.SelectedIndex,
                CsvPath = loadedCsvPath,
                TargetColumn = cboTarget.SelectedItem as string,
                InputColumns = columnChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList(),
                TrainPercent = sliderTrain.Value,
                MetricIndex = cboMetric.SelectedIndex,
                Functions = functionDefs.Where(f => f.IsSelected).Select(f => f.Name).ToList(),
                Constants = txtConstants.Text,
                Population = txtPopulation.Text,
                MinGenerations = txtMinGen.Text,
                MaxGenerations = txtMaxGen.Text,
                StagnantLimit = txtStagnant.Text,
                ElitismRate = txtElitism.Text,
                CrossoverRate = txtCrossover.Text,
                MutationRate = txtMutation.Text,
                TreeMinDepth = txtMinDepth.Text,
                TreeMaxDepth = txtMaxDepth.Text,
                SelectionIndex = cboSelection.SelectedIndex,
                TourneySize = txtTourney.Text,
                BlackjackHands = txtBjHands.Text,
                BlackjackDecks = txtBjDecks.Text,
                BlackjackStack = chkBjStack.IsChecked == true
            };

            var dialog = new SaveFileDialog { Filter = "Studio setup (*.json)|*.json", FileName = "studio-setup.json" };
            if (dialog.ShowDialog(this) != true) return;
            System.IO.File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(setup, new JsonSerializerOptions { WriteIndented = true }));
            txtRunStatus.Text = "Wrote " + dialog.FileName;
        }

        private void BtnLoadSetup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Studio setup (*.json)|*.json", Title = "Load a Studio setup" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var setup = JsonSerializer.Deserialize<StudioSetup>(System.IO.File.ReadAllText(dialog.FileName));
                ApplySetup(setup);
                txtRunStatus.Text = "Loaded setup from " + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load this setup:\n\n" + ex.Message, "Setup load failed",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplySetup(StudioSetup setup)
        {
            // mode first — switching it resets the engine-parameter boxes to that mode's defaults
            if (setup.Mode is 0 or 1)
                cboMode.SelectedIndex = setup.Mode;

            // dataset: a saved CSV path wins; otherwise the preset index
            if (!string.IsNullOrEmpty(setup.CsvPath) && System.IO.File.Exists(setup.CsvPath))
            {
                suppressPresetEvent = true;
                cboPreset.SelectedIndex = -1;
                suppressPresetEvent = false;
                ApplyDataset(CsvLoader.Load(setup.CsvPath), null);
                loadedCsvPath = setup.CsvPath;
            }
            else if (setup.PresetIndex >= 0 && setup.PresetIndex < presets.Count && cboPreset.SelectedIndex != setup.PresetIndex)
            {
                cboPreset.SelectedIndex = setup.PresetIndex;
            }

            if (setup.TargetColumn != null && currentDataset != null && currentDataset.Columns.Contains(setup.TargetColumn))
                cboTarget.SelectedItem = setup.TargetColumn;
            if (setup.InputColumns is { Count: > 0 })
                foreach (var choice in columnChoices)
                    choice.IsSelected = setup.InputColumns.Contains(choice.Name);

            if (setup.TrainPercent is >= 50 and <= 95) sliderTrain.Value = setup.TrainPercent;
            if (setup.MetricIndex is 0 or 1) cboMetric.SelectedIndex = setup.MetricIndex;

            if (setup.Functions is { Count: > 0 })
            {
                foreach (var def in functionDefs)
                    def.IsSelected = setup.Functions.Contains(def.Name);
                // checkbox bindings don't observe plain properties, so rebind
                icFunctions.ItemsSource = null;
                icFunctions.ItemsSource = functionDefs;
            }
            if (setup.Constants != null) txtConstants.Text = setup.Constants;

            static void Set(System.Windows.Controls.TextBox box, string value)
            {
                if (!string.IsNullOrEmpty(value)) box.Text = value;
            }
            Set(txtPopulation, setup.Population);
            Set(txtMinGen, setup.MinGenerations);
            Set(txtMaxGen, setup.MaxGenerations);
            Set(txtStagnant, setup.StagnantLimit);
            Set(txtElitism, setup.ElitismRate);
            Set(txtCrossover, setup.CrossoverRate);
            Set(txtMutation, setup.MutationRate);
            Set(txtMinDepth, setup.TreeMinDepth);
            Set(txtMaxDepth, setup.TreeMaxDepth);
            if (setup.SelectionIndex is >= 0 and <= 2) cboSelection.SelectedIndex = setup.SelectionIndex;
            Set(txtTourney, setup.TourneySize);
            Set(txtBjHands, setup.BlackjackHands);
            Set(txtBjDecks, setup.BlackjackDecks);
            chkBjStack.IsChecked = setup.BlackjackStack;
        }

        // ----- screenshot demo mode (used by "--screenshot <dir>") ---------------

        internal async Task<bool> RunScreenshotDemoAsync(string outputDir)
        {
            try
            {
                System.IO.Directory.CreateDirectory(outputDir);

                // small, quick demo run on the trig-blend preset
                cboPreset.SelectedIndex = 1;
                txtPopulation.Text = "250";
                txtMinGen.Text = "15";
                txtMaxGen.Text = "35";
                txtStagnant.Text = "10";
                await RunEvolutionAsync();

                tabsMain.SelectedIndex = 0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_evolution.png"));

                chkLogScale.IsChecked = true;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_evolution_log.png"));
                chkLogScale.IsChecked = false;

                tabsMain.SelectedIndex = 1;
                tabsExaminer.SelectedIndex = 0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_examiner_tree.png"));

                tabsExaminer.SelectedIndex = 1;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_examiner_fit.png"));

                // a quick Blackjack run for the strategy view
                cboMode.SelectedIndex = 1;
                txtPopulation.Text = "120";
                txtMaxGen.Text = "8";
                txtStagnant.Text = "8";
                txtBjHands.Text = "5000";
                await RunBlackjackAsync();

                tabsMain.SelectedIndex = 1;
                tabsExaminer.SelectedItem = tabStrategy;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_blackjack.png"));

                // a quick Snake run for the watch tab
                cboMode.SelectedIndex = 2;
                txtPopulation.Text = "150";
                txtMaxGen.Text = "12";
                txtStagnant.Text = "12";
                await RunSnakeAsync();

                tabsMain.SelectedIndex = 1;
                tabsExaminer.SelectedItem = tabWatch;
                await Task.Delay(1800);   // let the replay animate a few ticks
                CapturePng(System.IO.Path.Combine(outputDir, "studio_snake.png"));

                Console.WriteLine("screenshots written to " + outputDir);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SCREENSHOT FAIL: " + ex);
                return false;
            }
        }

        private void CapturePng(string path)
        {
            var root = (FrameworkElement)Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(path);
            encoder.Save(stream);
        }

        // ----- parsing helpers ---------------------------------------------------

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseInt(string text, int min, int max, string name, out int value, ref string error)
        {
            if (!int.TryParse(text, out value) || value < min || value > max)
            {
                error = $"{name} must be a whole number between {min} and {max}.";
                return false;
            }
            return true;
        }

        private static bool TryParseDouble(string text, double min, double max, string name, out double value, ref string error)
        {
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                 && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                || value < min || value > max)
            {
                error = $"{name} must be a number between {min} and {max}.";
                return false;
            }
            return true;
        }
    }

    public sealed class ColumnChoice : INotifyPropertyChanged
    {
        private bool isSelected;
        public string Name { get; set; }
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class VarRow
    {
        public string Variable { get; set; }
        public string Value { get; set; }
    }
}

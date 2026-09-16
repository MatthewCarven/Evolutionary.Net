using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Evolutionary;
using EvolutionaryStudio.Controls;
using EvolutionaryStudio.Model;
using Microsoft.Win32;

namespace EvolutionaryStudio
{
    public partial class MainWindow : Window
    {
        private readonly List<FunctionDef> functionDefs = FunctionCatalog.CreateAll();
        private readonly Dictionary<string, FunctionDef> defsByName;
        private readonly List<ProblemPreset> presets = ProblemPresets.All;
        private readonly ObservableCollection<GenerationStat> history = new();
        private readonly ObservableCollection<BestSnapshot> snapshots = new();
        private readonly ObservableCollection<ColumnChoice> columnChoices = new();

        private Dataset currentDataset;
        private PreparedProblem lastRunProblem;
        private GpRunner runner;
        private PlotSeries bestSeries, avgSeries;
        private List<VarRow> playgroundRows;
        private bool suppressPresetEvent;

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

        // ----- problem selection -------------------------------------------------

        private void CboPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (suppressPresetEvent || cboPreset.SelectedItem is not ProblemPreset preset)
                return;
            try
            {
                ApplyDataset(preset.Factory(), preset);
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

        private async void BtnRun_Click(object sender, RoutedEventArgs e) => await RunEvolutionAsync();

        private async Task RunEvolutionAsync()
        {
            var config = BuildConfig(out string error);
            if (config == null)
            {
                MessageBox.Show(this, error, "Can't start the run", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            history.Clear();
            snapshots.Clear();
            txtInfix.Text = "";
            txtSnapshotStats.Text = "";
            treeDiagram.Root = null;
            plotFit.SetSeries();
            txtFitMetrics.Text = "";
            lastRunProblem = config.Problem;

            bestSeries = new PlotSeries { Name = "Best so far", Color = Color.FromRgb(0x2D, 0x6C, 0xB5), Thickness = 2.2 };
            avgSeries = new PlotSeries { Name = "Average (gen)", Color = Color.FromRgb(0xD8, 0x8A, 0x2A), AutoScale = false };
            ApplyFitnessSeries();

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

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            runner?.RequestStop();
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

            if (!TryParseInt(txtPopulation.Text, 10, 100000, "Population size", out int population, ref error)) return null;
            if (!TryParseInt(txtMinGen.Text, 1, 100000, "Min generations", out int minGen, ref error)) return null;
            if (!TryParseInt(txtMaxGen.Text, minGen, 1000000, "Max generations", out int maxGen, ref error)) return null;
            if (!TryParseInt(txtStagnant.Text, 1, 100000, "Stagnant generations", out int stagnant, ref error)) return null;
            if (!TryParseDouble(txtElitism.Text, 0, 0.9, "Elitism rate", out double elitism, ref error)) return null;
            if (!TryParseDouble(txtCrossover.Text, 0, 1, "Crossover rate", out double crossover, ref error)) return null;
            if (!TryParseDouble(txtMutation.Text, 0, 1, "Mutation rate", out double mutation, ref error)) return null;
            if (!TryParseInt(txtMinDepth.Text, 1, 10, "Tree min depth", out int minDepth, ref error)) return null;
            if (!TryParseInt(txtMaxDepth.Text, minDepth, 10, "Tree max depth", out int maxDepth, ref error)) return null;
            if (!TryParseInt(txtTourney.Text, 2, population, "Tourney size", out int tourney, ref error)) return null;

            var problem = ProblemBuilder.Prepare(currentDataset, currentDataset.Columns.IndexOf(target),
                                                 inputIndexes, sliderTrain.Value / 100.0);

            return new RunConfig
            {
                Functions = selectedFunctions,
                Constants = constants,
                Problem = problem,
                UseRmse = cboMetric.SelectedIndex == 1,
                EngineParams = new EngineParameters
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
                }
            };
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

        private void AddSnapshot(BestSnapshot snap)
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
            if (lstSnapshots.SelectedItem is BestSnapshot snap)
                ShowSnapshot(snap);
        }

        private void ShowSnapshot(BestSnapshot snap)
        {
            txtInfix.Text = ExpressionPrinter.ToInfix(snap.Tree, defsByName);

            int nodes = TreeStats.CountNodes(snap.Tree);
            int depth = TreeStats.Depth(snap.Tree);
            var usage = TreeStats.UsageCounts(snap.Tree)
                                 .OrderByDescending(kv => kv.Value)
                                 .Take(6)
                                 .Select(kv => $"{kv.Key}×{kv.Value}");
            string origin = snap.IsFinal ? "final best of run" : $"new best at generation {snap.Generation}";
            txtSnapshotStats.Text = $"{origin}   ·   fitness {snap.Fitness:G6}   ·   {nodes} nodes, depth {depth}   ·   {string.Join(", ", usage)}";

            treeDiagram.Root = snap.Tree;
            UpdateFitPlot(snap);
            UpdatePlayground();
            txtEvalResult.Text = "";
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
            if (lstSnapshots.SelectedItem is BestSnapshot snap) Clipboard.SetText(snap.RawExpression);
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (treeDiagram != null)
                treeDiagram.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
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

                tabsMain.SelectedIndex = 1;
                tabsExaminer.SelectedIndex = 0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_examiner_tree.png"));

                tabsExaminer.SelectedIndex = 1;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CapturePng(System.IO.Path.Combine(outputDir, "studio_examiner_fit.png"));

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

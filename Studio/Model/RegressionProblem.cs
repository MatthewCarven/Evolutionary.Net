using System.Globalization;
using System.IO;

namespace EvolutionaryStudio.Model
{
    // state-data type required by the engine's generic signature; regression needs none
    public class ProblemState { }

    public sealed class Sample
    {
        public float[] Inputs;
        public float Target;
    }

    /// <summary>A table of numeric columns, ready to be turned into a regression problem.</summary>
    public sealed class Dataset
    {
        public string Name;
        public List<string> Columns = new();
        public List<float[]> Rows = new();
    }

    /// <summary>Train/test samples plus the variable names fed to the engine.</summary>
    public sealed class PreparedProblem
    {
        public string[] VariableNames;
        public List<Sample> Train = new();
        public List<Sample> Test = new();
    }

    public static class ProblemBuilder
    {
        /// <summary>Split a dataset into train/test samples using the chosen target and input columns.</summary>
        public static PreparedProblem Prepare(Dataset ds, int targetIndex, IList<int> inputIndexes, double trainFraction)
        {
            var problem = new PreparedProblem
            {
                VariableNames = inputIndexes.Select(i => ds.Columns[i]).ToArray()
            };

            // deterministic shuffle so runs are comparable
            var order = Enumerable.Range(0, ds.Rows.Count).ToArray();
            var rng = new Random(12345);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int trainCount = Math.Max(1, (int)(ds.Rows.Count * trainFraction));
            for (int k = 0; k < order.Length; k++)
            {
                var row = ds.Rows[order[k]];
                var sample = new Sample
                {
                    Inputs = inputIndexes.Select(i => row[i]).ToArray(),
                    Target = row[targetIndex]
                };
                (k < trainCount ? problem.Train : problem.Test).Add(sample);
            }
            return problem;
        }
    }

    public static class CsvLoader
    {
        /// <summary>
        /// Loads a delimited text file, keeping only columns that are (almost entirely) numeric.
        /// Non-numeric columns like dates are dropped; rows with unparseable values are skipped.
        /// </summary>
        public static Dataset Load(string path)
        {
            var lines = File.ReadAllLines(path)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();
            if (lines.Count < 2)
                throw new InvalidDataException("File has fewer than two non-empty lines.");

            char delimiter = DetectDelimiter(lines[0]);
            var firstFields = SplitLine(lines[0], delimiter);

            bool hasHeader = firstFields.Any(f => !TryParse(f, out _));
            var headers = hasHeader
                ? firstFields.Select(Sanitize).ToList()
                : Enumerable.Range(1, firstFields.Length).Select(i => "C" + i).ToList();
            DedupeNames(headers);

            int firstDataLine = hasHeader ? 1 : 0;
            var rawRows = new List<string[]>();
            for (int i = firstDataLine; i < lines.Count; i++)
            {
                var fields = SplitLine(lines[i], delimiter);
                if (fields.Length == headers.Count)
                    rawRows.Add(fields);
            }
            if (rawRows.Count == 0)
                throw new InvalidDataException("No data rows matched the header column count.");

            // keep columns where at least 95% of values parse as numbers
            var keptColumns = new List<int>();
            for (int col = 0; col < headers.Count; col++)
            {
                int parseable = rawRows.Count(r => TryParse(r[col], out _));
                if (parseable >= rawRows.Count * 0.95)
                    keptColumns.Add(col);
            }
            if (keptColumns.Count < 2)
                throw new InvalidDataException("Need at least two numeric columns (inputs and a target).");

            var ds = new Dataset { Name = Path.GetFileName(path) };
            ds.Columns = keptColumns.Select(c => headers[c]).ToList();
            foreach (var raw in rawRows)
            {
                var row = new float[keptColumns.Count];
                bool ok = true;
                for (int k = 0; k < keptColumns.Count; k++)
                {
                    if (!TryParse(raw[keptColumns[k]], out row[k])) { ok = false; break; }
                }
                if (ok)
                    ds.Rows.Add(row);
            }
            if (ds.Rows.Count == 0)
                throw new InvalidDataException("No fully-numeric data rows found.");
            return ds;
        }

        private static char DetectDelimiter(string headerLine)
        {
            int commas = headerLine.Count(c => c == ',');
            int semis = headerLine.Count(c => c == ';');
            int tabs = headerLine.Count(c => c == '\t');
            if (tabs >= commas && tabs >= semis) return tabs > 0 ? '\t' : ',';
            return semis > commas ? ';' : ',';
        }

        private static string[] SplitLine(string line, char delimiter)
        {
            return line.Split(delimiter).Select(f => f.Trim().Trim('"')).ToArray();
        }

        private static bool TryParse(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Sanitize(string name)
        {
            var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            var result = new string(chars);
            if (result.Length == 0 || char.IsDigit(result[0]))
                result = "V" + result;
            return result;
        }

        private static void DedupeNames(List<string> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++)
            {
                string candidate = names[i];
                int suffix = 2;
                while (!seen.Add(candidate))
                    candidate = names[i] + "_" + suffix++;
                names[i] = candidate;
            }
        }
    }

    public sealed class ProblemPreset
    {
        public string DisplayName;
        public Func<Dataset> Factory;
        public string DefaultTarget;
        public string[] ExcludedInputs = Array.Empty<string>();

        public override string ToString() => DisplayName;
    }

    public static class ProblemPresets
    {
        public static List<ProblemPreset> All => new()
        {
            new ProblemPreset
            {
                DisplayName = "Koza polynomial:  y = x⁴ + x³ + x² + x",
                Factory = () => FromFunction("Koza polynomial", x => x * x * x * x + x * x * x + x * x + x, -1, 1, 201),
                DefaultTarget = "Y"
            },
            new ProblemPreset
            {
                DisplayName = "Trig blend:  y = x² + 5·sin(3x)",
                Factory = () => FromFunction("Trig blend", x => x * x + 5.0 * Math.Sin(3 * x), -5, 5, 251),
                DefaultTarget = "Y"
            },
            new ProblemPreset
            {
                DisplayName = "Damped sine:  y = 10·e^(−0.3x)·sin(2x)",
                Factory = () => FromFunction("Damped sine", x => 10.0 * Math.Exp(-0.3 * x) * Math.Sin(2 * x), 0, 10, 251),
                DefaultTarget = "Y"
            },
            new ProblemPreset
            {
                DisplayName = "Two variables:  z = x·y + sin(x)",
                Factory = TwoVariableSurface,
                DefaultTarget = "Z"
            },
            new ProblemPreset
            {
                DisplayName = "Bike sharing demo (real CSV data)",
                Factory = LoadBikeData,
                DefaultTarget = "cnt",
                // instant is a row index; casual+registered sum to cnt, which makes the problem trivial
                ExcludedInputs = new[] { "instant", "casual", "registered" }
            }
        };

        private static Dataset FromFunction(string name, Func<double, double> f, double lo, double hi, int points)
        {
            var ds = new Dataset { Name = name };
            ds.Columns.Add("X");
            ds.Columns.Add("Y");
            for (int i = 0; i < points; i++)
            {
                double x = lo + (hi - lo) * i / (points - 1);
                ds.Rows.Add(new[] { (float)x, (float)f(x) });
            }
            return ds;
        }

        private static Dataset TwoVariableSurface()
        {
            var ds = new Dataset { Name = "Two-variable surface" };
            ds.Columns.Add("X");
            ds.Columns.Add("Y");
            ds.Columns.Add("Z");
            for (int i = 0; i <= 15; i++)
            {
                for (int j = 0; j <= 15; j++)
                {
                    double x = -3 + 6.0 * i / 15;
                    double y = -3 + 6.0 * j / 15;
                    double z = x * y + Math.Sin(x);
                    ds.Rows.Add(new[] { (float)x, (float)y, (float)z });
                }
            }
            return ds;
        }

        private static Dataset LoadBikeData()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "bike-sharing-by-day.csv");
            var ds = CsvLoader.Load(path);
            ds.Name = "Bike sharing (daily rentals)";
            return ds;
        }
    }
}

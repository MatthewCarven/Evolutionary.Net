using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EvolutionaryStudio.Controls
{
    public sealed class PlotSeries
    {
        public string Name;
        public Color Color;
        public List<Point> Points = new();
        public bool Scatter;
        public double Thickness = 1.8;
        public bool AutoScale = true;   // series excluded from autoscale are drawn clipped
    }

    /// <summary>
    /// Lightweight dependency-free chart: line and scatter series, auto-scaled axes,
    /// optional y = x reference line, legend.
    /// </summary>
    public class PlotCanvas : FrameworkElement
    {
        private readonly List<PlotSeries> series = new();
        private static readonly Typeface Font = new("Segoe UI");

        public string Title { get; set; }
        public string XLabel { get; set; }
        public string YLabel { get; set; }
        public bool ShowYEqualsX { get; set; }
        public bool LogY { get; set; }   // log₁₀ Y axis; non-positive values are clamped to the decade below the smallest positive

        public void SetSeries(params PlotSeries[] newSeries)
        {
            series.Clear();
            series.AddRange(newSeries);
            InvalidateVisual();
        }

        public void Redraw() => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Brushes.White, null, bounds);
            if (ActualWidth < 80 || ActualHeight < 60) return;

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            double left = 58, right = 14, top = string.IsNullOrEmpty(Title) ? 14 : 32, bottom = 42;
            var plot = new Rect(left, top, Math.Max(10, ActualWidth - left - right), Math.Max(10, ActualHeight - top - bottom));

            bool hasData = false;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var s in series.Where(s => s.AutoScale))
            {
                foreach (var p in s.Points)
                {
                    if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y)) continue;
                    hasData = true;
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            }

            if (!hasData)
            {
                var empty = Text("No data yet", 13, Brushes.Gray, pixelsPerDip);
                dc.DrawText(empty, new Point((ActualWidth - empty.Width) / 2, (ActualHeight - empty.Height) / 2));
                return;
            }

            if (ShowYEqualsX)
            {
                double lo = Math.Min(minX, minY), hi = Math.Max(maxX, maxY);
                minX = minY = lo; maxX = maxY = hi;
            }

            // optionally move the Y axis into log10 space (bounds and drawing both use TY)
            bool logActive = false;
            double logFloor = 0;
            if (LogY)
            {
                double minPositive = double.MaxValue;
                foreach (var s in series)
                    foreach (var p in s.Points)
                        if (!double.IsNaN(p.Y) && !double.IsInfinity(p.Y) && p.Y > 0 && p.Y < minPositive)
                            minPositive = p.Y;
                if (minPositive < double.MaxValue)
                {
                    logActive = true;
                    logFloor = minPositive / 10;
                    minY = double.MaxValue; maxY = double.MinValue;
                    foreach (var s in series.Where(s => s.AutoScale))
                        foreach (var p in s.Points)
                        {
                            if (double.IsNaN(p.Y) || double.IsInfinity(p.Y)) continue;
                            double logValue = Math.Log10(Math.Max(p.Y, logFloor));
                            minY = Math.Min(minY, logValue);
                            maxY = Math.Max(maxY, logValue);
                        }
                }
            }
            double TY(double y) => logActive ? Math.Log10(Math.Max(y, logFloor)) : y;

            if (maxX - minX < 1e-9) { minX -= 1; maxX += 1; }
            if (maxY - minY < 1e-9) { minY -= 1; maxY += 1; }

            // a little breathing room on Y
            double padY = (maxY - minY) * 0.05;
            minY -= padY; maxY += padY;

            double ScaleX(double x) => plot.Left + (x - minX) / (maxX - minX) * plot.Width;
            double ScaleY(double y) => plot.Bottom - (TY(y) - minY) / (maxY - minY) * plot.Height;
            double ScaleYAxis(double transformed) => plot.Bottom - (transformed - minY) / (maxY - minY) * plot.Height;

            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 235, 240)), 1);
            var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 155, 165)), 1);
            gridPen.Freeze(); axisPen.Freeze();

            foreach (double tx in NiceTicks(minX, maxX, 6))
            {
                double px = ScaleX(tx);
                dc.DrawLine(gridPen, new Point(px, plot.Top), new Point(px, plot.Bottom));
                var label = Text(FormatTick(tx), 10, Brushes.DimGray, pixelsPerDip);
                dc.DrawText(label, new Point(px - label.Width / 2, plot.Bottom + 4));
            }
            foreach (double ty in logActive ? LogTicks(minY, maxY) : NiceTicks(minY, maxY, 5))
            {
                double py = ScaleYAxis(ty);
                dc.DrawLine(gridPen, new Point(plot.Left, py), new Point(plot.Right, py));
                var label = Text(FormatTick(logActive ? Math.Pow(10, ty) : ty), 10, Brushes.DimGray, pixelsPerDip);
                dc.DrawText(label, new Point(plot.Left - label.Width - 6, py - label.Height / 2));
            }

            dc.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            dc.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);

            if (!string.IsNullOrEmpty(Title))
            {
                var t = Text(Title, 13, Brushes.Black, pixelsPerDip, bold: true);
                dc.DrawText(t, new Point(plot.Left + (plot.Width - t.Width) / 2, 8));
            }
            if (!string.IsNullOrEmpty(XLabel))
            {
                var t = Text(XLabel, 11, Brushes.DimGray, pixelsPerDip);
                dc.DrawText(t, new Point(plot.Left + (plot.Width - t.Width) / 2, plot.Bottom + 20));
            }
            if (!string.IsNullOrEmpty(YLabel))
            {
                var t = Text(YLabel, 11, Brushes.DimGray, pixelsPerDip);
                dc.PushTransform(new RotateTransform(-90, 14, plot.Top + (plot.Height + t.Width) / 2));
                dc.DrawText(t, new Point(14, plot.Top + (plot.Height + t.Width) / 2));
                dc.Pop();
            }

            dc.PushClip(new RectangleGeometry(plot));

            if (ShowYEqualsX)
            {
                var refPen = new Pen(Brushes.Silver, 1) { DashStyle = DashStyles.Dash };
                refPen.Freeze();
                dc.DrawLine(refPen, new Point(ScaleX(minX), ScaleY(minX)), new Point(ScaleX(maxX), ScaleY(maxX)));
            }

            foreach (var s in series)
            {
                var brush = new SolidColorBrush(s.Color);
                brush.Freeze();
                var valid = s.Points.Where(p => !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)).ToList();
                if (valid.Count == 0) continue;

                if (s.Scatter)
                {
                    foreach (var p in valid)
                        dc.DrawEllipse(brush, null, new Point(ScaleX(p.X), ScaleY(p.Y)), 2.4, 2.4);
                }
                else
                {
                    var pen = new Pen(brush, s.Thickness) { LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(ScaleX(valid[0].X), ScaleY(valid[0].Y)), false, false);
                        for (int i = 1; i < valid.Count; i++)
                            ctx.LineTo(new Point(ScaleX(valid[i].X), ScaleY(valid[i].Y)), true, false);
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                }
            }

            dc.Pop();

            // legend, top-right inside the plot
            double legendY = plot.Top + 6;
            foreach (var s in series.Where(s => !string.IsNullOrEmpty(s.Name)))
            {
                var label = Text(s.Name, 10.5, Brushes.Black, pixelsPerDip);
                double lx = plot.Right - label.Width - 26;
                var chip = new SolidColorBrush(s.Color);
                chip.Freeze();
                dc.DrawRectangle(chip, null, new Rect(lx, legendY + label.Height / 2 - 4, 14, 8));
                dc.DrawText(label, new Point(lx + 20, legendY));
                legendY += label.Height + 3;
            }
        }

        private static IEnumerable<double> NiceTicks(double min, double max, int targetCount)
        {
            double span = max - min;
            double rawStep = span / Math.Max(1, targetCount);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double residual = rawStep / magnitude;
            double step = residual < 1.5 ? 1 : residual < 3.5 ? 2 : residual < 7.5 ? 5 : 10;
            step *= magnitude;
            double tick = Math.Ceiling(min / step) * step;
            for (; tick <= max + step * 1e-6; tick += step)
                yield return tick;
        }

        private static IEnumerable<double> LogTicks(double min, double max)
        {
            // one tick per decade; fall back to generic ticks when the range is under a decade
            var ticks = new List<double>();
            for (double e = Math.Ceiling(min); e <= Math.Floor(max) + 1e-9; e += 1)
                ticks.Add(e);
            if (ticks.Count < 2)
            {
                ticks.Clear();
                ticks.AddRange(NiceTicks(min, max, 5));
            }
            return ticks;
        }

        private static string FormatTick(double v)
        {
            double abs = Math.Abs(v);
            if (abs >= 100000 || (abs > 0 && abs < 0.001))
                return v.ToString("0.#e0", CultureInfo.InvariantCulture);
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static FormattedText Text(string text, double size, Brush brush, double pixelsPerDip, bool bold = false)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                bold ? new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Font,
                size, brush, pixelsPerDip);
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EvolutionaryStudio.Model.Blackjack;

namespace EvolutionaryStudio.Controls
{
    /// <summary>
    /// Renders a Blackjack strategy as the classic three color-coded tables:
    /// hard totals, soft hands, and pairs, one column per dealer upcard.
    /// </summary>
    public class StrategyGrid : FrameworkElement
    {
        private const double CellW = 27, CellH = 21, HeaderW = 38, TitleH = 22, Gap = 26, Pad = 12, LegendH = 34;

        private static readonly string[] UpcardLabels = { "2", "3", "4", "5", "6", "7", "8", "9", "T", "A" };
        private static readonly Typeface Font = new("Segoe UI");
        private static readonly Typeface BoldFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private Strategy strategy;

        public Strategy Strategy
        {
            get => strategy;
            set { strategy = value; InvalidateVisual(); }
        }

        private static double TableH(int rows) => TitleH + CellH * (rows + 1);
        private static double TableW => HeaderW + CellW * 10;

        private Size Extent => new(
            Pad * 2 + TableW + Gap + TableW,
            Pad * 2 + Math.Max(TableH(16), TableH(8) + Gap + TableH(10)) + LegendH);

        protected override Size MeasureOverride(Size availableSize) => Extent;
        protected override Size ArrangeOverride(Size finalSize) => Extent;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(new Point(0, 0), Extent));
            if (strategy == null) return;

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // hard totals, 20 down to 5
            DrawTable(dc, dpi, Pad, Pad, "Hard totals", 16,
                r => (20 - r).ToString(),
                (col, r) => strategy.GetActionForHardHand(col, 20 - r));

            // soft hands, A-9 down to A-2
            double rightX = Pad + TableW + Gap;
            DrawTable(dc, dpi, rightX, Pad, "Soft hands", 8,
                r => "A-" + (9 - r),
                (col, r) => strategy.GetActionForSoftHand(col, 9 - r));

            // pairs, A-A down to 2-2 (rank index 9 = A, 8 = T, 7 = 9, ...)
            double pairsY = Pad + TableH(8) + Gap;
            DrawTable(dc, dpi, rightX, pairsY, "Pairs", 10,
                r => { string t = RankLabel(9 - r); return t + "-" + t; },
                (col, r) => strategy.GetActionForPair(col, 9 - r));

            DrawLegend(dc, dpi, Pad, Extent.Height - Pad - LegendH + 8);
        }

        private static string RankLabel(int rankIndex) => rankIndex switch
        {
            9 => "A",
            8 => "T",
            _ => (rankIndex + 2).ToString()
        };

        private void DrawTable(DrawingContext dc, double dpi, double x, double y, string title,
                               int rows, Func<int, string> rowLabel, Func<int, int, ActionToTake> action)
        {
            var titleText = Text(title, 12.5, Brushes.Black, dpi, bold: true);
            dc.DrawText(titleText, new Point(x, y));
            y += TitleH;

            var headerBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF6));
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0xB8, 0xBE, 0xC8)), 1);
            headerBrush.Freeze(); borderPen.Freeze();

            // top-left corner + upcard header row
            DrawCell(dc, dpi, new Rect(x, y, HeaderW, CellH), headerBrush, borderPen, "dlr:", Brushes.DimGray, false);
            for (int col = 0; col < 10; col++)
                DrawCell(dc, dpi, new Rect(x + HeaderW + col * CellW, y, CellW, CellH), headerBrush, borderPen,
                         UpcardLabels[col], Brushes.Black, true);

            for (int r = 0; r < rows; r++)
            {
                double rowY = y + CellH * (r + 1);
                DrawCell(dc, dpi, new Rect(x, rowY, HeaderW, CellH), headerBrush, borderPen, rowLabel(r), Brushes.Black, true);

                for (int col = 0; col < 10; col++)
                {
                    var (fill, letter, textBrush) = ActionStyle(action(col, r));
                    DrawCell(dc, dpi, new Rect(x + HeaderW + col * CellW, rowY, CellW, CellH), fill, borderPen, letter, textBrush, true);
                }
            }
        }

        private static (Brush fill, string letter, Brush text) ActionStyle(ActionToTake action) => action switch
        {
            ActionToTake.Hit => (HitBrush, "H", Brushes.Black),
            ActionToTake.Stand => (StandBrush, "S", Brushes.White),
            ActionToTake.Double => (DoubleBrush, "D", Brushes.Black),
            _ => (SplitBrush, "P", Brushes.White)
        };

        private static readonly Brush HitBrush = Frozen(0x8F, 0xD8, 0x8F);
        private static readonly Brush StandBrush = Frozen(0xE2, 0x4A, 0x4A);
        private static readonly Brush DoubleBrush = Frozen(0xF2, 0xD0, 0x4E);
        private static readonly Brush SplitBrush = Frozen(0x93, 0x70, 0xC8);

        private void DrawLegend(DrawingContext dc, double dpi, double x, double y)
        {
            var items = new[]
            {
                (HitBrush, "H = Hit"), (StandBrush, "S = Stand"),
                (DoubleBrush, "D = Double down"), (SplitBrush, "P = Split")
            };
            foreach (var (brush, label) in items)
            {
                dc.DrawRoundedRectangle(brush, null, new Rect(x, y + 2, 16, 12), 2, 2);
                var text = Text(label, 11.5, Brushes.Black, dpi);
                dc.DrawText(text, new Point(x + 22, y));
                x += 22 + text.Width + 22;
            }
        }

        private static void DrawCell(DrawingContext dc, double dpi, Rect rect, Brush fill, Pen pen,
                                     string label, Brush textBrush, bool bold)
        {
            dc.DrawRectangle(fill, pen, rect);
            if (string.IsNullOrEmpty(label)) return;
            var text = Text(label, 11, textBrush, dpi, bold);
            dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
        }

        private static FormattedText Text(string s, double size, Brush brush, double dpi, bool bold = false)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     bold ? BoldFont : Font, size, brush, dpi);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

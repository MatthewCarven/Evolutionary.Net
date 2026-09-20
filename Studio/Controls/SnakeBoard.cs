using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EvolutionaryStudio.Model.Snake;

namespace EvolutionaryStudio.Controls
{
    /// <summary>Draws one SnakeGame state: dark board, red fruit, green snake.</summary>
    public class SnakeBoard : FrameworkElement
    {
        private const double Cell = 26;

        private static readonly Brush Background = Frozen(0x14, 0x16, 0x1C);
        private static readonly Brush GridLine = Frozen(0x20, 0x24, 0x2E);
        private static readonly Brush FruitBrush = Frozen(0xDC, 0x46, 0x46);
        private static readonly Brush BodyBrush = Frozen(0x78, 0xDC, 0x78);
        private static readonly Brush HeadBrush = Frozen(0xF0, 0xFF, 0xF0);

        private SnakeGame game;

        public SnakeGame Game
        {
            get => game;
            set
            {
                bool sizeChanged = (game?.Width, game?.Height) != (value?.Width, value?.Height);
                game = value;
                if (sizeChanged) InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public void Refresh() => InvalidateVisual();

        private Size Extent => game == null
            ? new Size(12 * Cell, 12 * Cell)
            : new Size(game.Width * Cell, game.Height * Cell);

        protected override Size MeasureOverride(Size availableSize) => Extent;
        protected override Size ArrangeOverride(Size finalSize) => Extent;

        protected override void OnRender(DrawingContext dc)
        {
            var extent = Extent;
            dc.DrawRectangle(Background, null, new Rect(new Point(0, 0), extent));

            if (game == null)
            {
                var hint = new FormattedText(
                    "Run a Snake evolution, then pick a snapshot to watch it play.",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12.5, Brushes.Gray,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(hint, new Point(14, extent.Height / 2 - 8));
                return;
            }

            var pen = new Pen(GridLine, 1);
            pen.Freeze();
            for (int y = 1; y < game.Height; y++)
                dc.DrawLine(pen, new Point(0, y * Cell), new Point(extent.Width, y * Cell));
            for (int x = 1; x < game.Width; x++)
                dc.DrawLine(pen, new Point(x * Cell, 0), new Point(x * Cell, extent.Height));

            if (game.HasFruit)
            {
                var (fy, fx) = game.Fruit;
                dc.DrawRoundedRectangle(FruitBrush, null,
                    new Rect(fx * Cell + 3, fy * Cell + 3, Cell - 6, Cell - 6), 6, 6);
            }

            bool isHead = true;
            foreach (var (sy, sx) in game.Body)
            {
                dc.DrawRoundedRectangle(isHead ? HeadBrush : BodyBrush, null,
                    new Rect(sx * Cell + 1.5, sy * Cell + 1.5, Cell - 3, Cell - 3), 4, 4);
                isHead = false;
            }
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

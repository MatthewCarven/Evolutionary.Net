using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Evolutionary;

namespace EvolutionaryStudio.Controls
{
    /// <summary>
    /// Draws an expression tree as a classic node-link diagram: parents centered
    /// over their children, nodes color-coded by kind.
    /// </summary>
    public class TreeDiagram : FrameworkElement
    {
        private const double HGap = 12, VGap = 30, NodeHeight = 26, TextPadX = 9, OuterMargin = 18;
        private const int MaxRenderableNodes = 4000;

        private static readonly Typeface Font = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush FunctionBrush = Frozen(Color.FromRgb(0x3A, 0x6E, 0xB5));
        private static readonly Brush VariableBrush = Frozen(Color.FromRgb(0x3E, 0x9B, 0x4F));
        private static readonly Brush ConstantBrush = Frozen(Color.FromRgb(0xD8, 0x8A, 0x2A));
        private static readonly Brush TerminalFuncBrush = Frozen(Color.FromRgb(0x8E, 0x5B, 0xC0));
        private static readonly Pen EdgePen = FrozenPen(Color.FromRgb(0xB0, 0xB8, 0xC4), 1.4);

        private sealed class LayoutNode
        {
            public TreeNodeInfo Info;
            public double X, Y, Width, SubtreeWidth;
            public List<LayoutNode> Children = new();
        }

        private TreeNodeInfo root;
        private LayoutNode layoutRoot;
        private Size extent = new(0, 0);
        private bool tooLarge;
        private int nodeCount;

        public TreeNodeInfo Root
        {
            get => root;
            set
            {
                root = value;
                BuildLayout();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        private void BuildLayout()
        {
            layoutRoot = null;
            tooLarge = false;
            nodeCount = 0;
            extent = new Size(0, 0);
            if (root == null) return;

            nodeCount = Count(root);
            if (nodeCount > MaxRenderableNodes)
            {
                tooLarge = true;
                extent = new Size(420, 80);
                return;
            }

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            layoutRoot = Build(root, pixelsPerDip);
            int depth = Depth(layoutRoot);
            Assign(layoutRoot, OuterMargin, 0);
            extent = new Size(layoutRoot.SubtreeWidth + OuterMargin * 2,
                              depth * (NodeHeight + VGap) - VGap + OuterMargin * 2);
        }

        private static int Count(TreeNodeInfo n) => 1 + n.Children.Sum(Count);
        private static int Depth(LayoutNode n) => 1 + (n.Children.Count == 0 ? 0 : n.Children.Max(Depth));

        private LayoutNode Build(TreeNodeInfo info, double pixelsPerDip)
        {
            var text = MakeText(info.Label, pixelsPerDip);
            var node = new LayoutNode { Info = info, Width = Math.Max(30, text.Width + TextPadX * 2) };
            foreach (var child in info.Children)
                node.Children.Add(Build(child, pixelsPerDip));
            node.SubtreeWidth = node.Children.Count == 0
                ? node.Width + HGap
                : Math.Max(node.Width + HGap, node.Children.Sum(c => c.SubtreeWidth));
            return node;
        }

        private void Assign(LayoutNode node, double left, int depth)
        {
            node.Y = OuterMargin + depth * (NodeHeight + VGap);
            if (node.Children.Count == 0)
            {
                node.X = left + (node.SubtreeWidth - node.Width) / 2;
                return;
            }

            double childLeft = left;
            if (node.Children.Sum(c => c.SubtreeWidth) < node.SubtreeWidth)
                childLeft += (node.SubtreeWidth - node.Children.Sum(c => c.SubtreeWidth)) / 2;
            foreach (var child in node.Children)
            {
                Assign(child, childLeft, depth + 1);
                childLeft += child.SubtreeWidth;
            }

            double firstCenter = node.Children[0].X + node.Children[0].Width / 2;
            double lastCenter = node.Children[^1].X + node.Children[^1].Width / 2;
            node.X = (firstCenter + lastCenter) / 2 - node.Width / 2;
        }

        protected override Size MeasureOverride(Size availableSize) => extent;
        protected override Size ArrangeOverride(Size finalSize) => extent;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Math.Max(ActualWidth, extent.Width), Math.Max(ActualHeight, extent.Height)));
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            if (tooLarge)
            {
                var msg = new FormattedText(
                    $"Tree has {nodeCount} nodes — too large to draw. See the expression text instead.",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 12, Brushes.DimGray, pixelsPerDip);
                dc.DrawText(msg, new Point(16, 30));
                return;
            }
            if (layoutRoot == null) return;

            DrawEdges(dc, layoutRoot);
            DrawNodes(dc, layoutRoot, pixelsPerDip);
        }

        private static void DrawEdges(DrawingContext dc, LayoutNode node)
        {
            var from = new Point(node.X + node.Width / 2, node.Y + NodeHeight);
            foreach (var child in node.Children)
            {
                dc.DrawLine(EdgePen, from, new Point(child.X + child.Width / 2, child.Y));
                DrawEdges(dc, child);
            }
        }

        private static void DrawNodes(DrawingContext dc, LayoutNode node, double pixelsPerDip)
        {
            var brush = node.Info.Kind switch
            {
                TreeNodeKind.Function => FunctionBrush,
                TreeNodeKind.Variable => VariableBrush,
                TreeNodeKind.TerminalFunction => TerminalFuncBrush,
                _ => ConstantBrush
            };
            dc.DrawRoundedRectangle(brush, null, new Rect(node.X, node.Y, node.Width, NodeHeight), 6, 6);
            var text = MakeText(node.Info.Label, pixelsPerDip);
            dc.DrawText(text, new Point(node.X + (node.Width - text.Width) / 2, node.Y + (NodeHeight - text.Height) / 2));

            foreach (var child in node.Children)
                DrawNodes(dc, child, pixelsPerDip);
        }

        private static FormattedText MakeText(string label, double pixelsPerDip)
        {
            return new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     Font, 12, Brushes.White, pixelsPerDip);
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Pen FrozenPen(Color c, double thickness)
        {
            var p = new Pen(new SolidColorBrush(c), thickness);
            p.Freeze();
            return p;
        }
    }
}

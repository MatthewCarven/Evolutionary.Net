using System.Text;
using Evolutionary;

namespace EvolutionaryStudio.Model
{
    public static class ExpressionPrinter
    {
        /// <summary>
        /// Renders a tree snapshot as readable infix math where the function catalog defines
        /// an operator symbol (Add → +, etc.); everything else stays as Name(args).
        /// </summary>
        public static string ToInfix(TreeNodeInfo node, IReadOnlyDictionary<string, FunctionDef> defs)
        {
            var sb = new StringBuilder();
            Walk(node, defs, 0, sb);
            return sb.ToString();
        }

        private static void Walk(TreeNodeInfo node, IReadOnlyDictionary<string, FunctionDef> defs,
                                 int parentPrecedence, StringBuilder sb)
        {
            if (node.Kind != TreeNodeKind.Function)
            {
                sb.Append(node.Label);
                return;
            }

            if (defs.TryGetValue(node.Label, out var def) && def.InfixSymbol != null && node.Children.Count == 2)
            {
                bool needParens = def.Precedence < parentPrecedence;
                if (needParens) sb.Append('(');
                Walk(node.Children[0], defs, def.Precedence, sb);
                sb.Append(' ').Append(def.InfixSymbol).Append(' ');
                Walk(node.Children[1], defs, def.Precedence + (def.RightNeedsParens ? 1 : 0), sb);
                if (needParens) sb.Append(')');
                return;
            }

            if (node.Label == "Neg" && node.Children.Count == 1)
            {
                sb.Append('-');
                Walk(node.Children[0], defs, int.MaxValue, sb);
                return;
            }

            sb.Append(node.Label).Append('(');
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                Walk(node.Children[i], defs, 0, sb);
            }
            sb.Append(')');
        }
    }

    public static class TreeStats
    {
        public static int CountNodes(TreeNodeInfo node)
        {
            int count = 1;
            foreach (var child in node.Children)
                count += CountNodes(child);
            return count;
        }

        public static int Depth(TreeNodeInfo node)
        {
            int deepest = 0;
            foreach (var child in node.Children)
                deepest = Math.Max(deepest, Depth(child));
            return deepest + 1;
        }

        public static Dictionary<string, int> UsageCounts(TreeNodeInfo root)
        {
            var counts = new Dictionary<string, int>();
            void Visit(TreeNodeInfo n)
            {
                string key = n.Kind == TreeNodeKind.Function ? n.Label : n.Kind.ToString();
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                foreach (var child in n.Children) Visit(child);
            }
            Visit(root);
            return counts;
        }
    }
}

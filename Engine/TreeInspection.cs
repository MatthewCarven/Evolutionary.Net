using System.Collections.Generic;

namespace Evolutionary
{
    public enum TreeNodeKind { Function, TerminalFunction, Variable, Constant }

    /// <summary>
    /// A read-only snapshot of one node in a candidate's expression tree, safe to hand
    /// to UI / analysis code without exposing the live tree.
    /// </summary>
    public sealed class TreeNodeInfo
    {
        public string Label { get; internal set; }
        public TreeNodeKind Kind { get; internal set; }
        public List<TreeNodeInfo> Children { get; } = new List<TreeNodeInfo>();
    }

    public static class TreeInspection
    {
        /// <summary>
        /// Returns a snapshot of the candidate's expression tree for display or analysis.
        /// </summary>
        public static TreeNodeInfo GetTreeInfo<T, S>(this CandidateSolution<T, S> candidate) where S : new()
        {
            return Convert(candidate.Root);
        }

        private static TreeNodeInfo Convert<T, S>(NodeBaseType<T, S> node) where S : new()
        {
            var info = new TreeNodeInfo();

            var functionNode = node as FunctionNode<T, S>;
            if (functionNode != null)
            {
                info.Kind = TreeNodeKind.Function;
                info.Label = functionNode.FunctionName;
                foreach (var child in functionNode.Children)
                    info.Children.Add(Convert(child));
                return info;
            }

            if (node is TerminalFunctionNode<T, S>)
                info.Kind = TreeNodeKind.TerminalFunction;
            else if (node is VariableNode<T, S>)
                info.Kind = TreeNodeKind.Variable;
            else
                info.Kind = TreeNodeKind.Constant;

            info.Label = node.ToString();
            return info;
        }
    }
}

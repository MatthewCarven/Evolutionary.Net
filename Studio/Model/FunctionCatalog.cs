namespace EvolutionaryStudio.Model
{
    public sealed class FunctionDef
    {
        public string Name;
        public int Arity;
        public Delegate Fn;
        public string InfixSymbol;       // null → rendered as Name(args)
        public int Precedence;           // only meaningful for infix operators
        public bool RightNeedsParens;    // non-associative operators: a-b-c, a/b/c
        public string Description;
        public bool IsSelected { get; set; }

        public string CheckboxLabel => $"{Name} ({Arity})";
    }

    public static class FunctionCatalog
    {
        public static List<FunctionDef> CreateAll()
        {
            return new List<FunctionDef>
            {
                Def("Add", (Func<float, float, float>)((a, b) => a + b), "+", 1, false, "a + b", true),
                Def("Sub", (Func<float, float, float>)((a, b) => a - b), "-", 1, true, "a − b", true),
                Def("Mult", (Func<float, float, float>)((a, b) => a * b), "*", 2, false, "a × b", true),
                Def("Div", (Func<float, float, float>)((a, b) => b == 0 ? 1f : a / b), "/", 2, true, "protected divide: a / b, 1 when b = 0", true),
                Def("Sin", (Func<float, float>)(a => (float)Math.Sin(a)), null, 0, false, "sin(a)", true),
                Def("Cos", (Func<float, float>)(a => (float)Math.Cos(a)), null, 0, false, "cos(a)", true),
                Def("Square", (Func<float, float>)(a => a * a), null, 0, false, "a²", true),
                Def("Cube", (Func<float, float>)(a => a * a * a), null, 0, false, "a³", false),
                Def("Sqrt", (Func<float, float>)(a => (float)Math.Sqrt(Math.Abs(a))), null, 0, false, "protected: √|a|", false),
                Def("Log", (Func<float, float>)(a => a <= 0 ? 0f : (float)Math.Log(a)), null, 0, false, "protected: ln(a), 0 when a ≤ 0", false),
                Def("Exp", (Func<float, float>)(a => (float)Math.Exp(Math.Min(a, 20f))), null, 0, false, "e^a, clamped at e^20", false),
                Def("Abs", (Func<float, float>)(a => Math.Abs(a)), null, 0, false, "|a|", false),
                Def("Neg", (Func<float, float>)(a => -a), null, 0, false, "−a", false),
                Def("Tanh", (Func<float, float>)(a => (float)Math.Tanh(a)), null, 0, false, "tanh(a)", false),
                Def("Min", (Func<float, float, float>)Math.Min, null, 0, false, "min(a, b)", false),
                Def("Max", (Func<float, float, float>)Math.Max, null, 0, false, "max(a, b)", false),
                Def("IfPos", (Func<float, float, float, float>)((a, b, c) => a > 0 ? b : c), null, 0, false, "a > 0 ? b : c", false),
            };
        }

        private static FunctionDef Def(string name, Delegate fn, string infix, int prec, bool rightParens, string description, bool on)
        {
            int arity = fn.Method.GetParameters().Length;
            return new FunctionDef
            {
                Name = name,
                Arity = arity,
                Fn = fn,
                InfixSymbol = infix,
                Precedence = prec,
                RightNeedsParens = rightParens,
                Description = description,
                IsSelected = on
            };
        }
    }
}

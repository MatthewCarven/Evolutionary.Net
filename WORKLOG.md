# Worklog — Evolutionary.Net GUI & Examiner

## 2026-09-16

- Cloned https://github.com/GregSommerville/Evolutionary.Net (genetic programming engine, C#, .NET Framework 4.6.1 class library, zero dependencies).
- Surveyed the engine: `Engine<T,S>` drives evolution; `CandidateSolution<T,S>` holds an expression tree of `FunctionNode` / `VariableNode` / `ConstantNode` / `TerminalFunctionNode`. Tree internals (`Root`, node types) are all `internal`, so an external examiner can't walk trees without an API addition.
- Findings / decisions:
  - `CandidateSolution.Clone()` does not re-point cloned nodes' owner-candidate references at the new clone — `Evaluate()` on a clone reads the *original* candidate's variables. The engine works around this internally (calls `SetCandidateRef` after crossover and on the final result), but it bites any external caller. Fixed in `Clone()` itself.
  - Added `Engine/TreeInspection.cs`: public `TreeNodeKind`, `TreeNodeInfo` (label/kind/children) and `GetTreeInfo()` extension on `CandidateSolution` — snapshot API for examiners. Registered in the legacy csproj too.
  - GUI tech: WPF on net10.0-windows (SDK 10.0.401 is installed). The old-style Engine csproj is left untouched for VS users; the Studio project compiles `..\Engine\*.cs` directly (same assembly ⇒ examiner also has internal access if ever needed).
  - Examples still reference the legacy csproj and are unaffected.
- Built `Studio\` (Evolutionary Studio): problem presets + CSV loader (bundled bike-sharing data), primitive set / engine parameter panels, background run with live fitness chart + generation history, Examiner tab (tree diagram, infix expression, predicted-vs-actual + MAE/RMSE/R², variable playground). Custom chart + tree-layout controls, no external packages (matching the repo's zero-dependency spirit).
- `EvolutionaryStudio.exe --smoke` runs a tiny headless evolution (Koza polynomial) and exits 0/1 — used as the build-verification harness.

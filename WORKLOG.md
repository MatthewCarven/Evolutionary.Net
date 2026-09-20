# Worklog — Evolutionary.Net GUI & Examiner

## 2026-09-20

- "Bells and whistles" pass to make the fork useful to others:
  - GitHub Actions CI (`.github/workflows/build.yml`): builds Studio and runs `--smoke` on every push/PR; badge added to Readme.
  - Exports: generation history → CSV; fitness chart / tree diagram / strategy tables → PNG (guarded against oversized trees).
  - Log-scale fitness axis (`PlotCanvas.LogY`): decade ticks, sub-decade fallback, non-positives clamped; auto-disabled in Blackjack mode (negative chip scores).
  - Save/Load setup: full configuration (mode, dataset preset or CSV path, target/input columns, primitive set, constants, engine params, blackjack settings) as JSON via toolbar buttons.
  - Screenshot demo now also captures a log-scale frame (`images/studio_evolution_log.png`); all screenshots refreshed.
- Verified: build clean, smoke passes, GUI launch check, log-scale render inspected.

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

## 2026-09-17

- User asked for a tutorial on "implementing a goal". Wrote `TUTORIAL.md`: goal = scorecard concept, preset warm-up, worked kinetic-energy example (`Examples\kinetic-energy.csv`, 100 rows, KE = ½·m·v²), tuning cheat-sheet, code-path fitness-function example, troubleshooting.
- Verified the worked example headlessly (scratchpad console harness referencing EvolutionaryStudio.dll) before documenting it:
  - Default primitive set (with Sin/Cos): MAE 6.09 after 100 gens, bloated formula — kept in the tutorial as the "choose primitives to fit the goal" lesson.
  - Add/Sub/Mult/Div/Square only: exact solve (MAE 0) at generation 5 — `(m/2 + zero-junk) * Square(v)`.
- Linked the tutorial from the Readme's Studio section.
- Fork sync: user forked to MatthewCarven/Evolutionary.Net and ran the initial push. Remotes reconfigured: `origin` → the fork, `upstream` → GregSommerville's repo.
- **Blackjack mode in Studio** (user request). Ported the Blackjack Strategy example's single-tree approach (`SolutionSingle`) into `Studio\Model\Blackjack\`: Card/Hand/Deck, Strategy table, StrategyTester simulation, and BlackjackRunner (bool trees, HitIf/StandIf/DoubleIf/SplitIf vote functions, 44 terminal functions generated in loops with the original names). New `StrategyGrid` control renders the classic hard/soft/pairs tables. MainWindow gained a Mode dropdown that swaps config panels, sets per-mode engine defaults, and shows a Strategy examiner tab with a fresh-deal validation score. Snapshots refactored to a shared `SnapshotBase`.
- Port note: the original example's player-Blackjack payoff branches are inverted (winning blackjack only returned the bet; a push paid the bonus). The port implements correct 3:2 rules. The per-upcard variant (`SolutionByUpcard`, 10 sequential runs) was not ported.
- Verified: smoke test extended (blackjack mini-run + strategy query), screenshot demo captures the strategy view (`images/studio_blackjack.png`) — 8-gen demo run scored −980 chips/5000 hands with sane emergent patterns (double on 11, hit 10).

### Blackjack evolution experiments (user request: "let it evolve 60+ generations, tune as you like")

Ran three 101-generation experiments via a scratchpad harness referencing the Studio dll, each validated on 10 × 100,000 *fresh-deal* hands, benchmarked against the example's hand-coded basic strategy (ported; its rendered table matches published basic strategy). Scores are % of chips wagered:

| Experiment | Setup | Train (best-so-far) | Fresh-deal validation |
|---|---|---|---|
| A defaults | pop 250, 25k hands/eval, no mutation/elitism | −2.76% | **−5.37% ± 0.10** |
| B tuned | pop 500, 40k hands/eval, mutation 0.10, elitism 0.04 | −0.43% | **−2.36% ± 0.15** |
| C stacked deck | as A, StackTheDeck=true | +20.18% (!) | **−7.33% ± 0.12** |
| Basic strategy | hand-coded benchmark | — | **+0.02% ± 0.09** |

Findings (images: `images/blackjack_lab_curves.png`, `images/blackjack_lab_best.png`):
- Train ≫ validation across the board: with noisy fitness (25k-hand evals have a std of roughly ±1.2% of wagered), "best-so-far" is partly deal-luck. Bigger eval samples (B) shrink the gap and produce genuinely better play.
- Stacking the deck is a trap: C posted +20% on its stacked distribution but transferred worst of all. Train on the distribution you'll be tested on.
- Champion B learned real blackjack: always double 10/11, stand 15+, stand soft A-7+, hit stiffs vs strong upcards — but never learned "stand on 12–16 vs weak dealer" or "always split A-A/8-8" (it splits everything vs dealer 4, a comic local optimum). A single tree struggles to condition on the dealer upcard — quantifies why the example's per-upcard variant (not ported) exists.
- This simulator's rules (3:2, dealer stands soft 17, peek, DAS, post-split 21 pays 3:2) make basic strategy ≈ break-even, so evolved-vs-basic gaps read directly as % of wagered.

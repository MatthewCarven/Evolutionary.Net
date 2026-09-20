# TODO — Evolutionary.Net GUI & Examiner

## Done
- [x] Clone repo, survey engine architecture
- [x] Engine: public tree-inspection API (`TreeInspection.cs`)
- [x] Engine: fix stale owner refs in `CandidateSolution.Clone()`
- [x] Studio: WPF project skeleton (net10.0-windows, engine sources linked)
- [x] Studio: regression problem model (presets, CSV loader, train/test split)
- [x] Studio: GP runner (background thread, stop, per-gen stats, best snapshots)
- [x] Studio: live fitness chart + generation history grid
- [x] Studio: examiner — tree diagram, infix printer, predicted-vs-actual, playground
- [x] Smoke test mode (`--smoke`) and passing build
- [x] TUTORIAL.md with verified kinetic-energy worked example
- [x] Blackjack mode in Studio (vote trees, strategy tables, validation score)
- [x] Fork configured: origin = MatthewCarven/Evolutionary.Net, upstream = GregSommerville

- [x] Exports: history CSV, chart/tree/strategy PNGs (2026-09-20)
- [x] Log-scale toggle on the fitness chart (2026-09-20)
- [x] Save/load full setup as JSON (2026-09-20)
- [x] GitHub Actions CI: build + smoke test on every push, Readme badge (2026-09-20)

## Later / ideas
- [ ] Port the per-upcard Blackjack variant (10 small evolutions; should approach basic strategy)
- [ ] Examiner: side-by-side compare of two snapshots
- [ ] Boolean-typed problems (engine supports any T; Studio is float-only for now)
- [ ] Population diversity metrics (needs engine support to expose the population per generation)
- [ ] Upstream PR to GregSommerville/Evolutionary.Net with just the engine fixes (Clone owner-ref fix + TreeInspection API) — ask Matthew first

## For meatthread0
- [x] Fork created and initial push done; remotes configured (origin = fork).
- [ ] If a future `git push` from a Claude session is blocked by permissions, run it by hand.

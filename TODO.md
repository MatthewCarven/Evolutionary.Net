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

## Later / ideas
- [ ] Export: save best expression / per-gen stats to file from the GUI
- [ ] Examiner: side-by-side compare of two snapshots
- [ ] Optional log-scale toggle on the fitness chart
- [ ] Boolean-typed problems (engine supports any T; Studio is float-only for now)
- [ ] Population diversity metrics (unique expression count per generation)

## For meatthread0
- [x] Fork created and initial push done; remotes configured (origin = fork).
- [ ] If a future `git push` from a Claude session is blocked by permissions, run it by hand.

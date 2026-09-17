# Evolutionary Studio Tutorial — how to give the engine a goal

This is a beginner-friendly walkthrough of **Evolutionary Studio** (`dotnet run --project Studio -c Release`), ending with the two ways to implement a goal of your own.

## 1. The one idea everything hangs on

A genetic program doesn't take instructions — it takes a **scorecard**. You never tell the engine *how* to solve anything. You only tell it *how well a random guess did*, and it breeds better guesses over generations.

So "implementing a goal" always means the same thing: **turning your goal into a number the engine can minimize.**

- In the **GUI**, that number is already wired up for you: it's the prediction error. Your goal becomes a *table of examples* — some input columns, and one column holding the answer you want. The engine's job: evolve a formula that reproduces the answer column from the input columns.
- In **code**, you can score candidates any way you like (simulate a game, run a physics model, whatever) — that's section 5.

## 2. Warm-up: run a preset (2 minutes)

1. Start the app. The **Problem** panel already has *"Trig blend: y = x² + 5·sin(3x)"* style presets — pick any.
2. Press **▶ Run**. Watch the blue *Best so far* line fall on the Evolution tab. Falling = learning (fitness here is error, so lower is better).
3. It stops by itself when it hasn't improved for a while (the *Stagnant gens* setting), or press **■ Stop**.
4. Open the **Examiner** tab. Every time the engine found a new personal best, it saved a snapshot — click through them to literally watch the formula evolve. The **Tree** view shows the program as the engine sees it; **Fit quality** shows predicted-vs-actual dots (a perfect formula puts every dot on the diagonal line).

## 3. Worked example: implement a real goal

**Goal: "discover the physics formula for kinetic energy from raw measurements."** (We know the answer — KE = ½·m·v² — so we can check the engine's work.)

### Step 1 — turn the goal into a table

Any goal you can tabulate is fair game: lab measurements, spreadsheet rows, simulation outputs, game logs. One row per example, one column with the desired answer:

```
m,v,KE
1,1,0.5
1,2,2
1,3,4.5
...
```

A ready-made version with 100 rows is included at **`Examples\kinetic-energy.csv`**.

### Step 2 — load it

Click **Load CSV…** and pick the file. Set **Target column** to `KE` (the thing to predict) and leave `m` and `v` checked as **Input columns** (the variables the formula may use).

### Step 3 — choose building blocks that fit the goal ⭐

This is the step that separates a clean result from a mess. The **Functions** checklist is the engine's vocabulary — it can only build formulas out of what you check.

For this goal, **uncheck `Sin` and `Cos`**, keeping `Add`, `Sub`, `Mult`, `Div`, `Square`. Physics formulas like this one are products and powers; trig is dead weight here.

Real results from this exact dataset, same settings, only the checkboxes differing:

| Primitive set | Result after 100 generations |
|---|---|
| Defaults (with Sin/Cos) | error 6.09, a ten-line monster full of nested `Cos(...)` |
| Add/Sub/Mult/Div/Square only | **exact answer at generation 5** |

Rule of thumb: start with `Add, Sub, Mult, Div`, add `Square`/`Cube` for polynomial-ish data, add `Sin`/`Cos` only if the data visibly oscillates, `Log`/`Exp` only for growth/decay shapes.

The **Constants** box matters the same way — the default list already contains `0.5` and `2`, either of which gives the engine a route to the ½.

### Step 4 — run and read the result

Press **▶ Run**. Expect the best-fitness line to crash to (or near) **0** within a handful of generations. Then in the **Examiner**:

- The formula came out as `(m / 2 + Square(Square(Square(0))) - 0) * Square(v)` in our verified run. Don't panic at the junk: `Square(Square(Square(0)))` is just 0, and `- 0` does nothing. Read past the harmless zero-terms and it's **(m / 2) · v²** — exactly ½·m·v². Leftover do-nothing fragments ("bloat") are normal in GP; the Tree view makes them easy to spot.
- **Fit quality** should show every dot on the diagonal, R² = 1.0 on both train and test.
- Sanity-check it in the **Playground**: set `m = 4`, `v = 10` → should evaluate to `200`.
- **Copy infix** puts the formula on the clipboard, ready to paste into code, Excel, or a paper napkin.

### The train/test split, and why you care

Studio holds back 25% of your rows (the slider) and never trains on them. If **Train** R² is great but **Test** R² is poor, the formula memorized your data instead of learning the pattern — use more rows, fewer primitives, or a smaller tree depth.

## 4. Tuning cheat-sheet

| Knob | Raise it when… | Lower it when… |
|---|---|---|
| Population size | results are inconsistent between runs | each generation is too slow |
| Max generations | fitness was still falling when it stopped | you're just exploring |
| Stagnant gens before stop | it gives up too early | runs drag on with no progress |
| Tree max depth | the pattern seems too complex for small trees | formulas are bloated / overfitting |
| Mutation rate | evolution gets stuck on a plateau | good solutions keep getting wrecked |
| Elitism rate | best score sometimes gets *worse* | population converges too fast to one idea |
| Metric: RMSE instead of MAE | big misses hurt you more than small ones | outliers in the data shouldn't dominate |

## 5. Goals that aren't "fit this table" (the code path)

Some goals can't be a CSV — "win at Blackjack", "balance this pole", "control this rocket". For those you skip the GUI and write the scorecard yourself as a **fitness function** in C#:

```csharp
using Evolutionary;

var engine = new Engine<float, ProblemState>(new EngineParameters
{
    PopulationSize = 500,
    IsLowerFitnessBetter = true      // false if your score is "points earned"
});

// vocabulary, same idea as the Functions checklist
engine.AddVariable("X");
engine.AddConstant(0.5f);
engine.AddFunction((a, b) => a + b, "Add");
engine.AddFunction((a, b) => a * b, "Mult");

// THE GOAL LIVES HERE: score one candidate however you like —
// run a simulation, play 1000 hands, anything that returns a number
engine.AddFitnessFunction(candidate =>
{
    float totalError = 0;
    foreach (var example in myExamples)
    {
        candidate.SetVariableValue("X", example.Input);
        totalError += Math.Abs(candidate.Evaluate() - example.Answer);
    }
    return totalError;
});

engine.AddProgressFunction((progress, best) => true);   // false = stop early
var winner = engine.FindBestSolution();
Console.WriteLine(winner.ToString());
```

For a full non-regression goal, read `Examples\Blackjack Strategy` — its fitness function plays thousands of hands and returns the money won, and it uses *terminal functions* plus state data so trees can ask questions like "is my hand a pair?".  You can also watch this goal evolve without writing any code: switch Studio's **Mode** dropdown to *Blackjack strategy* and press Run — the examiner shows the evolved strategy as the classic hit/stand/double/split tables.  Notice the fitness is negative and rising: the goal there isn't "be right", it's "lose the least against the house edge", and the scorecard-not-instructions idea is exactly the same.

Two engine extras added alongside Studio that help here: `candidate.GetTreeInfo()` gives you a walkable snapshot of the evolved tree (it's what powers the tree diagram), and `candidate.Clone()` now produces independently evaluable copies.

## 6. Troubleshooting

- **Fitness stuck high from generation 0** — the vocabulary probably can't express the answer (e.g. no `Mult`/`Square` for a squared relationship), or a needed input column is unchecked.
- **Formula is enormous** — fewer functions checked, lower *Tree max depth*, and let *Stagnant gens* stop the run earlier; also just read past zero-terms like `+ 0` and `* 1`.
- **Great on train, bad on test** — overfitting; see section 3's split note.
- **Every run gives a different formula** — normal! Evolution is random. If they all score similarly, they're usually algebraically equivalent or equally valid fits.

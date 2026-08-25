# RouletteSim — Design Doc

## Goal
A tool for testing personal betting/bankroll strategies against statistically
correct European roulette odds — fast iteration and volume matter more than
visual fidelity. (Superseded an earlier "pure PhysX, no RNG" version — see
DECISIONS.md for why that was dropped.)

## Wheel
European single-zero, 37 pockets (0–36), physical pocket order per
`Core/WheelLayout.cs`. Used both for red/black lookup and for laying out
numbers visually around the rim — no longer used to derive the result.

## Result generation
`Core/SpinResultGenerator.cs` draws a uniform random pocket number (0–36) via
an injectable `IRandomSource` — no physics, no bias. This is the entire
fairness contract: every pocket equally likely, exactly like a real wheel.

## Spin animation
`Presentation/SpinAnimator.cs` is purely cosmetic. The winning number is
already known before the animation starts; it tweens the wheel disc and ball
transform (via `AnimationCurve` ease-out, no Rigidbody/collision) to land
exactly on the pre-determined result. Can't hang, tunnel, or misbehave — there
is no physics simulation to go wrong.

## Betting
`Core/Bet.cs`, `BetType.cs`, `BetResolver.cs` implement standard European
odds: straight-up 35:1, dozens/columns 2:1, red/black/odd/even/low/high 1:1,
zero loses all outside bets. Chip denominations: 25/100/500
(`Core/ChipDenominations.cs`).

## Bankroll & sessions
`Core/Bankroll.cs` tracks balance with insufficient-funds rejection (never
goes negative). `Core/SessionHistory.cs` / `SpinRecord.cs` track per-spin
results and running stats (win rate, max drawdown).

## Batch simulation
`Core/BatchSimulator.cs` runs many spins synchronously against a fixed bet
set with no animation — the actual point of the tool: seeing how a flat
betting strategy behaves over hundreds/thousands of spins in well under a
second. Exposed via `Presentation/BatchSimUI.cs`.

## Architecture
Core/Presentation split — `Assets/Scripts/Core/` has zero UnityEngine
dependency (uses `System.Random` behind `IRandomSource`, not
`UnityEngine.Random`), so all betting/payout/RNG logic is covered by EditMode
unit tests (`Assets/Scripts/Tests/EditMode/`) that run in under a second, no
Play mode required. `Assets/Scripts/Presentation/` holds all MonoBehaviours
and contains no betting/payout logic itself.

## Scope
Wheel + betting UI (number grid, outside bets, chip selector, bet tray) +
bankroll + batch simulation. No multiplayer, no persistence between sessions
yet, no split/street/corner bets (straight-up + standard outside bets only).

## Controls
Click chips to select denomination, click number grid / outside-bet buttons
to place bets, SPIN to resolve, or use the batch panel to simulate many spins
against the current bet tray at once.

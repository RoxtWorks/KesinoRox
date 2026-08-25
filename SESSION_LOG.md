# Session Log

## 2026-08-09
Built from scratch.

**Done:**
- Project skeleton (ProjectSettings, Packages/manifest.json) hand-created —
  headless CLI project creation blocked by license entitlement, use Unity Hub GUI.
- `WheelLayout.cs` — European 37-pocket order + red/black lookup
- `WheelSpinner.cs` — Rigidbody torque-driven rotor with angular drag
- `BallController.cs` — orbit (friction decay) → free-fall/settle (real PhysX)
- `PocketResultReader.cs` — reads final pocket from ball position vs wheel rotation
- `RouletteSceneBuilder.cs` — builds bowl, deflectors, wheel, pocket dividers,
  ball procedurally at runtime (primitives — no art yet)
- `RouletteManager.cs` — spin trigger (SPACE / button), settle detection, result UI
- `Assets/Scenes/Main.unity` — one GameManager object running everything

**Not done / next session:**
- Open in Unity Hub, verify it imports clean, press Play, tune physics
  (bounciness/friction/torque ranges) against real-wheel feel
- Real meshes for wheel/ball/deflectors (currently primitives)
- Betting table (out of scope for this session, see DECISIONS.md)

**To open:** Unity Hub → Add → select `RouletteSim` folder → open with 6000.1.10f1.

## 2026-08-09 (later)
- Fixed ball-launch-off-table bug: default Cylinder primitive collider is
  CapsuleCollider, breaks under non-uniform scale — swapped disc/floor/deflectors
  to BoxCollider (see DECISIONS.md).
- Fixed center-pull bug from the first attempted fix (MeshCollider convex warps
  on flat shapes) — also resolved by the BoxCollider swap.
- Ball Rigidbody now forced kinematic/no-gravity in Awake() so it doesn't fall
  before the first spin.
- Added colours (table felt, wheel, deflectors, red/black/green pockets).
- Installed Unity MCP (CoplayDev/unity-mcp) — package added to manifest.json,
  `uv` installed, local server running, registered with Claude Code. Session
  active as "RouletteSim". Future sessions can drive the Editor directly via
  MCP tools instead of guessing collider/physics behaviour from screenshots.

## 2026-08-09 (pivot session — RNG betting simulator)

Unity MCP live this session (registered as HTTP server, `127.0.0.1:8080`).
Debugged the physics build fully — found and fixed three real bugs live
(ball spinning in place forever from near-zero angular damping, tunneling
through thin floor colliders, ejecting past the bowl rim through gaps
between discrete deflector pins). Also hit and fixed an environment issue:
Unity's domain-reload-on-Play was hanging, traced to a write-permission
failure under the Program Files install path — worked around via
Edit → Project Settings → Editor → Enter Play Mode Settings → "Do not
reload Domain or Scene".

Mid-session, clarified the actual goal with the user: a tool to test
personal gambling/bankroll strategies over many spins, not a physics demo.
Pivoted the whole project — see DECISIONS.md "pivot" entry for full
rationale.

**Done:**
- Deleted `BallController.cs`, `WheelSpinner.cs`, `PocketResultReader.cs`,
  `RouletteManager.cs`, old `RouletteSceneBuilder.cs`.
- New `Assets/Scripts/Core/` (zero UnityEngine dependency): `WheelLayout.cs`
  (moved), `IRandomSource.cs`, `SpinResultGenerator.cs`, `BetType.cs`,
  `Bet.cs`, `BetResolver.cs`, `Bankroll.cs`, `ChipDenominations.cs`,
  `SpinRecord.cs`, `SessionHistory.cs`, `BatchSimulator.cs`, plus
  `RouletteSim.Core.asmdef`.
- New `Assets/Scripts/Tests/EditMode/`: `BetResolverTests.cs`,
  `SpinResultGeneratorTests.cs`, `BankrollTests.cs`, `BatchSimulatorTests.cs`
  — 20 tests, all passing, run in under a second.
- New `Assets/Scripts/Presentation/`: `RouletteTableBuilder.cs` (trimmed,
  visual-only), `SpinAnimator.cs` (tween-based spin, no physics),
  `UIFactory.cs`, `BankrollHudUI.cs`, `ChipSelectorUI.cs`,
  `BettingUIController.cs` (number grid + outside bets + bet tray + spin),
  `HistoryPanelUI.cs`, `BatchSimUI.cs`, `GameManager.cs` (thin orchestrator).
- Wired `GameManager.cs` onto the scene's GameManager object, verified via
  Play mode: chip selection, straight-up + outside bet placement, spin
  animation + correct RNG/payout resolution, bankroll deduction/deposit,
  history logging, and batch simulate (ran 987/1000 spins before busting a
  1000-budget flat-25 strategy, stats all correct) — all confirmed working
  end-to-end via live clicks + screenshots.
- Fixed a HUD layout overlap (budget input was covering the balance text).

**Not done / next session:**
- Split/street/corner bets (only straight-up + standard outside bets so far).
- No persistence between sessions (bankroll/history resets on restart).
- Batch mode's "strategy" is just "repeat whatever's in the bet tray" — a
  pluggable `IBettingStrategy` interface would be needed to script actual
  progression systems (e.g. the user's "Calculated Sniper" system) for
  automated batch testing.
- Minor: number grid still visually overlaps the wheel graphic a bit: could
  reposition the betting felt to sit fully below/beside the wheel instead of
  underneath it.

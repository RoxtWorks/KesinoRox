# Decisions Log

- **3D + real physics over 2D/simplified RNG.** User asked for "most realistic
  results" — result must emerge from physics, not Random.Range on the final number.
- **European single-zero (37 pockets), not American double-zero.** More common
  reference wheel; easy to add a 00 pocket later if wanted.
- **Wheel-only scope, no betting table yet.** Keeps the hard realism problem
  (physics) isolated before layering UI/economy on top.
- **Ball orbit phase is kinematic, fall/settle phase is dynamic PhysX.** A free
  Rigidbody ball on a bare radius (no authored track mesh) is unstable at
  orbit speeds. Kinematic orbit still derives its deceleration and drop point
  from friction physics — only the *final* result (fall, bounce, settle) needed
  to be true PhysX, and it is.
- **Whole rig built procedurally at runtime (`RouletteSceneBuilder.cs`)**
  instead of hand-authored scene/prefab, since no mesh art was provided yet.
  Swap primitives for real meshes later without touching the physics/manager code.
- **Project created by hand-writing the Unity project skeleton**, not via
  `Unity.exe -batchmode -createProject` — headless CLI creation failed
  (Code 404, no entitlement for the headless license on this machine). Open
  via Unity Hub normally instead; interactive license works fine there.
- **Replaced default colliders on all squashed Cylinder primitives (wheel disc,
  floor, deflector pins) with `MeshCollider`.** Unity's default collider for a
  `PrimitiveType.Cylinder` is a `CapsuleCollider`, which does not scale
  correctly under heavy non-uniform localScale — the resulting collider
  ballooned far past the visible flat-disc mesh, and PhysX resolving that
  hidden overlap every frame was violently launching the ball off the table.
  MeshCollider (convex on the dynamic wheel disc, non-convex on static pieces)
  hugs the actual visible shape.
- **Switched wheel disc / floor / deflector colliders from `MeshCollider(convex)`
  to `BoxCollider`.** Convex hull generation on a very flat, squashed shape
  (30:1 aspect ratio) produced a warped, non-planar hull that pulled the ball
  toward the wheel center instead of letting it settle wherever it landed.
  BoxCollider.size scales correctly under non-uniform localScale and stays
  perfectly flat, so it's the reliable choice for squashed primitives here.
- **Ball Rigidbody forced to kinematic/no-gravity in `Awake()`.** It defaults
  to dynamic+gravity, so before the first spin it was free-falling to the
  floor at the origin immediately at scene start — compounding the
  center-of-wheel symptom.
- **WheelSpinner now sets angular velocity directly instead of `AddTorque(impulse)`.**
  A torque impulse of 40-90 was reverse-engineered against an unknown moment of
  inertia and turned out to barely move the disc (~0.2 rad/s) — imperceptible
  spin. Setting velocity directly guarantees the intended launch speed;
  angularDamping still provides real physics-based deceleration afterward.
- **Result no longer waits for the wheel to slow down.** `CheckForResult` was
  gated on both ball-settled AND wheel-angular-speed-near-zero. In real
  roulette the number is fixed the instant the ball settles into a moving
  pocket — waiting for the wheel itself to stop (which can take a very long
  time under realistic drag) was the main reason no result ever appeared
  during testing, even on runs where the ball had genuinely settled correctly.
- **Ball settle detection rewritten to not depend on `OnCollisionStay`.** That
  callback stops firing once contacts go quiet even though the ball hasn't
  moved, silently stranding the sim in `Falling` forever. Now it's a plain
  sustained-low-velocity timer checked every `FixedUpdate`, independent of
  whether a collision event happens to be active that frame.
- **Added a live on-screen debug HUD** (wheel angular speed, ball phase,
  tangential/linear speed) plus `Debug.Log` at every phase transition
  (Launch, BeginFall, Settled, TrySpin, Result) — there was previously no way
  to tell whether the sim was progressing or silently stuck.
- **Unity MCP registered but not usable this session** — `claude mcp add` was
  run by the in-editor plugin mid-session; new MCP servers only attach when
  Claude Code starts, so this session is still working blind via direct file
  edits + reasoning. Next session should have live Editor access via MCP —
  use it to inspect actual runtime Rigidbody/collider state instead of
  deriving physics behaviour by hand.
- **Added flat colours** (table felt green, wheel black, gold deflectors/dividers,
  red/black/green pocket floors matching real wheel colours) — primitives were
  unreadable white-on-white before.

## 2026-08-09 (pivot — dropped physics entirely)

- **Abandoned the PhysX ball simulation, rebuilt as RNG + betting UI.** With
  live Unity MCP access this session, the physics build was fully debugged
  (ball spinning forever from near-zero angular damping, tunneling through
  thin floor colliders, ejecting past the bowl rim with no containment wall —
  all found and fixed one at a time via live Play-mode inspection). But it
  turned out to be solving the wrong problem: the user's actual goal is
  testing personal gambling/bankroll strategies over many spins, which needs
  statistically correct odds and fast iteration, not a bouncing ball. Every
  physics tweak cost a 10-20s Play-mode round trip per spin to verify: no
  path to the volume-testing workflow the user actually wants.
- **Result now comes from `SpinResultGenerator`** — a uniform RNG draw over
  the 37 pockets via an injectable `IRandomSource`, not physics. Same fairness
  guarantee (every pocket equally likely), zero simulation risk.
- **Spin is now a cosmetic tween (`SpinAnimator`), not a Rigidbody.** The
  winning number is decided before the animation plays; the animation just
  has to look right. This structurally eliminates the entire class of bugs
  above — there's no physics step left to hang, tunnel, or misbehave.
- **Split code into `Core/` (zero UnityEngine dependency) and
  `Presentation/`.** Core holds all betting/payout/bankroll/RNG logic and is
  covered by EditMode unit tests that run in under a second — the fast
  iteration loop the physics approach never had. Presentation holds
  MonoBehaviours only, no game logic.
- **Deleted `BallController.cs`, `WheelSpinner.cs`, `PocketResultReader.cs`,
  `RouletteManager.cs`.** `RouletteSceneBuilder.cs` was trimmed to
  `Presentation/RouletteTableBuilder.cs` — same procedural-primitive visuals,
  no Rigidbody/Collider anywhere. `WheelLayout.cs` moved into `Core/`
  unchanged; still used for the wheel's visual number layout and red/black
  lookup, no longer for result generation.
- **Added betting: chip denominations (25/100/500), straight-up + standard
  outside bets (red/black/odd/even/low/high/dozens/columns), bankroll with
  insufficient-funds rejection, session history, and batch simulation**
  (`BatchSimulator`) to run hundreds/thousands of spins against a fixed bet
  set synchronously with no animation — this is the actual point of the tool.
- **`execute_code` (Unity MCP's inline C# eval) triggers a domain reload each
  call**, which silently nulls any plain (non-serialized) C# fields on live
  MonoBehaviours — e.g. a `Bankroll` field set only via runtime `Build()`
  calls. Caused a confusing "bet placement does nothing" false alarm
  mid-session. Lesson: don't interleave `execute_code` state inspection with
  manual UI testing in the same Play session — verify via clicks/screenshots
  first, only use `execute_code` to inspect *after* the interaction sequence
  is done, or expect state to reset.

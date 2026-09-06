# Normal Craps — Reference Layout Tracker

Reference image: real casino craps table (user-provided, see memory/reference_normalcraps_layout.md)

## Status key: ✅ done | 🔧 in progress | ❌ missing

---

## LEFT FELT

| Element | Status | Notes |
|---|---|---|
| Number cells (4/5/6/8/9/10) split LAY/PLACE | ✅ | Top=LOSE red, center=number, bottom=WIN green |
| Number cell payout label (e.g. 7:6) | ❌ | Reference has NO payout in cell — should remove |
| COME area (large gold "COME" text) | ✅ | Exists, visual only |
| DON'T COME BAR (left-anchored inside come area) | ❌ | Currently separate, reference has it left-edge of come bar |
| FIELD bar — 2/12 large gold | 🔧 | Code has PAYS DBL but 2/12 not big enough |
| FIELD bar — center numbers "3·4·9·10·11" small | 🔧 | Current shows these but layout not matching |
| FIELD bar — "FIELD" label centered below numbers | ❌ | Currently FIELD label is on LEFT edge |
| FIELD bar — "PAYS DOUBLE" near 2 and 12 | 🔧 | Shows as "PAYS DBL", positioning off |
| DON'T PASS BAR (thin red bar) | ✅ | Exists |
| PASS LINE (large gold text, green bg) | 🔧 | Exists but could be more prominent |

## RIGHT PANEL

| Element | Status | Notes |
|---|---|---|
| HARDWAYS header | ✅ | |
| Hardway cells — text labels "HARD 4" etc | 🔧 | Reference uses dice pip icons, we use text |
| ONE ROLL section — SEVEN / CRAPS / ELEVEN / HORN | ✅ | Present |
| C·E label | ✅ | |

## CORE / LOGIC

| Element | Status | Notes |
|---|---|---|
| Lay bets enum (Lay4–Lay10) | ✅ | |
| Lay bet resolver payout | ✅ | |
| Lay bet round resolution | ✅ | |
| Lay bet UI (click, chips, clear) | ✅ | |
| Lay bet unit tests | ❌ | Not written yet |
| Odds modal (PassOdds/DontPassOdds) | ✅ | |

## IMMEDIATE PRIORITY (to match reference)
1. Fix FIELD bar layout — move "FIELD" to center, 2/12 gold+large, PAYS DOUBLE flanking them
2. Remove payout ratio from number cells (reference shows none)
3. DON'T COME BAR left-anchored inside come area
4. Add lay bet unit tests

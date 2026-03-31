
# SmartPlayer — Player Styles & Decision Thresholds

## Overview

`SmartPlayer` supports four distinct playing personalities controlled by the `PlayerStyle` enum. Each style adjusts thresholds throughout the decision pipeline — preflop hand selection, raise sizing, postflop aggression, and bluff frequency.

```csharp
public enum PlayerStyle { Tight, Balanced, Aggressive, LAG }
```

| Style | Description |
|---|---|
| `Tight` | Conservative. Folds marginal hands preflop, rarely enters pots with weak holdings, minimal bluffing. |
| `Balanced` | Default. Plays a solid, straightforward game — the baseline behaviour. |
| `Aggressive` | Wider opens, larger sizing, frequent c-bets and bluffs. Will 3-bet light. |
| `LAG` | Loose-Aggressive. Widest call range, highest bluff frequency, large 3-bets. Plays many pots. |

---

## Preflop Thresholds

### Recommended hands (AA, KK, QQ, JJ, AK, AQs…)
Always raise. 3-bets when facing a raise.

| Sizing | Tight | Balanced | Aggressive | LAG |
|---|---|---|---|---|
| Open raise (BB multiples) | 2–3x | 2–4x | 3–6x | 2–4x |
| Facing a raise: re-raise multiplier | 2–3x raise | 2–4x raise | 3–5x raise | 2–4x raise |

### Risky hands (medium pairs, suited connectors, broadway)

| Threshold | Tight | Balanced | Aggressive | LAG |
|---|---|---|---|---|
| Open-raise frequency (no raise in front) | 30% | 55% | 75% | 80% |
| Max pot-odds to call a raise | 30% | 40% | 50% | 55% |

When not open-raising, these hands limp in (call).  
Risky open-raises use 2x BB sizing regardless of style.

### NotRecommended hands (K8o, A4o, 98o…)

| Threshold | Tight | Balanced | Aggressive | LAG |
|---|---|---|---|---|
| Steal-raise frequency (no raise in front) | 5% | 15% | 30% | 40% |
| Limp-in frequency (no raise, no steal) | 25% | 50% | 70% | 85% |
| Max pot-odds to call a raise | 15% | 22% | 30% | 35% |

`CanCheck` always checks (BB / free play). Steal-raises use 2x BB sizing.

### Unplayable hands
Always fold unless able to check for free.

---

## Postflop Thresholds

### Strong made hands (pair or better)

#### Pair — c-bet / lead-bet

| Frequency | Tight | Balanced | Aggressive | LAG |
|---|---|---|---|---|
| C-bet (raised preflop, first to act) | 40% | 60% | 75% | 80% |
| Lead-bet (did not raise preflop) | 15% | 30% | 50% | 60% |

Bet sizing: 50% of pot. Pair + draw: leads out 75% at 65% pot.

#### Two-pair
Bet 45–60% pot. Calls bets up to 40% pot-odds.

#### Strong (Three-of-a-kind / Straight / Flush)
Re-raises 55–85% depending on street; leads 55–75% pot.

#### Monster (Full House / Quads / Straight Flush)
Bet 65–85% pot. Slow-plays 30% on non-river streets.

---

## Bluffing Thresholds

| Bluff Type | Tight | Balanced | Aggressive | LAG |
|---|---|---|---|---|
| River bluff with missed draw | 10% | 25% | 40% | 55% |
| Flop/Turn c-bet bluff (raised preflop) | 25% | 50% | 65% | 75% |
| Semi-bluff with strong draw (≥30% equity) | 25% | 45% | 60% | 70% |

Bluff bet sizing: 50–55% of pot.  
Draw equity threshold for semi-bluffs: `EstimateDrawEquity ≥ 30%` (flush draw or OESD).

---

## Style Assignment In-Game

Styles are assigned at game setup and cycle through the array to ensure variety at the table.

**Simulation mode** (AI-only game, cycles):

```
Tight → Balanced → Aggressive → LAG → Balanced → Tight → Aggressive → Balanced → LAG → Tight
```

**Normal mode** (human + AI opponents, cycles from seat 1):

```
Aggressive → Tight → LAG → Balanced → Aggressive → Tight → LAG → Balanced → Balanced
```

---

## Implementation Notes

- `_raisedPreFlop` flag enables c-bet detection postflop (`isCBet = _raisedPreFlop && !facingBet && PreviousRoundActions.Count == 0`).
- Draw equity is estimated via `EstimateDrawEquity()`: flush draw (4 suited) = 35%/19% (flop/turn); OESD (4 consecutive) = 32%/17%; gutshot = 14%/7%.
- `IsLikelyMissedDraw()` detects river bluff opportunities when holding 4-flush or 4-straight that didn't complete.
- `BetAmount()` respects `MinRaise` and goes all-in when the computed raise exceeds remaining stack.



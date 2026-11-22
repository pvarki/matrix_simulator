# 0013. The order the systems run in is behaviour

- Status: Accepted
- Date: 2026-11-22

## Context
`Simulation.Step` runs a fixed list of systems (`src/Twin.Sim/Systems`) once per tick. Each system reads state that
earlier systems in the same tick have already written, so swapping two systems changes what agents do and what the event
log records, without any single system's code changing. The order was only stated in a constructor comment and in
scattered "runs after/before" sentences in each system's doc comment. Orders were applied by hand-written code inside
`Step`, outside the list.

[ADR 0005](0005-determinism.md) (Decision, second bullet) still holds: orders are validated when issued and applied at
the start of the next tick, in issue order.

## Decision
The order of the systems is part of the simulation's behaviour. It is the table below, it is pinned by
`tests/Twin.Sim.Tests/Core/SystemOrderTests.cs` (`Systems_run_in_the_documented_order`), and changing it means updating
that test and this table in the same pull request, with the reason.

Order handling is a system too: `OrderSystem` runs first. `Simulation.Issue` validates an order (`OrderValidator`) and
queues it on that same `OrderSystem` instance, which applies the queue after `Tick++` and before every other system.

Each tick: `Tick++`, then the systems in this order, then the state-change events are logged.

| # | System | Reads | Writes | Runs after / why |
|---|---|---|---|---|
| 1 | `OrderSystem` | orders queued by `Issue`; the unit's current order, squad membership | `CurrentOrder`, posture wanted, drill end/answer, watch and alert state, squad detach/rejoin, squad command state (`MovementOrder`, `OrderRequestedAt`, halt, brief) | First, so every other system sees this tick's orders (ADR 0005). |
| 2 | `CombatStatusSystem` | pin and suppression timers, fire pressure, posture wanted | health (pin ends wounded), fire pressure, suppression, posture | Before movement, so an agent that may move again does so in the same tick. |
| 3 | `SquadFollowSystem` | leader position, `squad.MovementStarted`, `squad.Corridor`, `squad.TrailLeaderId`, slots, member state | member positions and velocity, leader trail | Before the leader moves: members head for slots around where the leader is at the start of the tick. |
| 4 | `SquadCohesionSystem` | leader's current order, `squad.MovementOrder`, member distances to slots, member permission requests | `squad.MovementStarted`, `squad.Corridor`, `LeaderHeld`; every member's `HeldBySquad` and `SquadSpeedFactor`; leader trail reset and `TrailLeaderId`; clears a finished `MovementOrder`; answers member permission requests (squad permits) | After the members have moved, before the leader moves, so it judges where the members are now. |
| 5 | `MovementSystem` | `MoveTo` orders, `HeldBySquad`, `SpeedFactor` (includes `SquadSpeedFactor`) | position, velocity, activity | After cohesion, so a held leader does not move. |
| 6 | `CorridorMovementSystem` | `MoveThroughCorridor` orders, terrain, permits, `HeldBySquad`, `SpeedFactor` (includes `SquadSpeedFactor`) | route, position, velocity, permission requests, edge deployment | After cohesion, as `MovementSystem`. |
| 7 | `ContactDrillSystem` | fire received, attackers, cover | drill stage, posture, drill moves, contact reports | After movement and before `FireSystem`. |
| 8 | `SpottingSystem` | positions, `WatchDirection`, `ObservationSectors`, muzzle flashes | `WatchDirection`, `WatchesSectors`, spot progress, spotted/known enemies | After movement and the drill, before firing. |
| 9 | `SpotReportSystem` | spotted enemies, contact reports, squad mates in voice range | pending reports, known enemies, squad halt, `EnemyEstimate` | After spotting, before firing. |
| 10 | `FireSystem` | targets, known enemies, rules of engagement, line of sight | shots, rounds, fire pressure, hits (pin, out of action), drill start, muzzle flash | After spotting and reports, so only known enemies are targets. |
| 11 | `SquadSystem` | command deliveries, health of leaders, suppression | members act on commands, team and squad takeovers | After firing, so a leader shot out of action leaves its place in the same tick. |
| 12 | `ObservationSystem` | velocity, squad formation and deployment | `Heading`, `ObservationSectors`, `WatchesAir` | Last, after everyone has moved. |

The reads and writes are summarised from each system's own `<summary>`; that comment stays the detailed description.

## Open questions
Recorded only; this ADR changes nothing about them.

- **Q1: followers lag their leader's movement order by one tick.** `SquadFollowSystem` reads `squad.MovementStarted`
  (`SquadMovementSystems.cs:212`) and `squad.Corridor` (`:293`) before `SquadCohesionSystem` sets them from the leader's
  order (`:44–51`). `Squad.NewMovementOrder` (`Squad.cs:113`), called by `OrderSystem`, sets `MovementOrder` but not
  `MovementStarted`, and nothing sets `MovementStarted` back to false (`:46` and `:50` are its only writes). So:
  - on a squad's **first** movement order, its members do not follow at all on the tick the order is applied;
  - on every **later** movement order, they follow on that tick but with the previous order's `squad.Corridor` (none
    after a `MoveTo`), and use the new corridor from the next tick.
- **Q2: spotting uses the previous tick's sectors.** `SpottingSystem` reads `ObservationSectors`
  (`SpottingSystems.cs:66`, `:161–164`), which `ObservationSystem` writes last in the tick.

## Alternatives considered
- Call `OrderSystem` explicitly from `Step` before the loop: works the same, but the pin test would not cover it and
  order handling would stay a special case outside "behaviour goes in a system".
- Document the table in a doc comment on `Simulation`: it is long, and it records a decision plus open questions, which
  is what ADRs are for.
- Let each system declare its dependencies and sort them: more machinery than twelve systems need, and the order would
  still have to be reviewed as behaviour.

## Consequences
- A change to the order fails `SystemOrderTests` until the test and this table are updated together, so it is visible in
  review.
- `Simulation` holds world state, `Issue`, `Step`, queries and the state hash; order rules live in `OrderSystem`,
  `OrderValidator` and `SquadValidator`.
- Renaming, splitting or adding a system means a one-line update here and in the pin test.
- Q1 and Q2 stay one-tick lags until someone decides whether they are intended.

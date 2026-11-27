@AGENTS.md

# RaceDay WebAPI — Claude Code notes

`AGENTS.md` (imported above) and `.cursor/rules/` hold the architecture rules. This file records feature notes that are not obvious from the code.

## Running

```bash
dotnet build RaceDay.slnx
dotnet test RaceDay.slnx
dotnet test tests/RaceDay.Modules.RaceMonitoring.UnitTests
```

Local Docker stack: `deploy/docker-compose.yml` (service `api` → container `deploy-api-1`, `http://localhost:5080`, Swagger at `/swagger`); `deploy/dev-up.ps1` starts it. The frontends (admin-fe on 3001, UserWeb on 3000) call this API. After backend changes, rebuild the `api` image (`docker compose -f deploy/docker-compose.yml up -d --build api`) or the frontends keep hitting the old code. EF migrations apply at startup.

## Real-time: SSE vs SignalR

- **SSE** (`Results.ServerSentEvents`, `System.Net.ServerSentEvents`) is used for one-way server → browser feeds: Positioning live positions/alerts (`PositioningModule.cs`), race event log timeline (`RaceEventLogSse`), and cut-off warnings (`CutoffWarningSse`). Browsers open them through the frontend's same-origin proxy, which adds the JWT from the session cookie (`EventSource` cannot send headers).
- **SignalR** (WebSocket) is only used by the Simulation module (`SimulationHub`).
- SSE items are serialized with the default HTTP JSON options (camelCase). The SSE `event:` name is what the browser subscribes to, so renaming a kind is a breaking change for admin-fe.
- Send a heartbeat (15 s) so proxies don't drop idle streams, and set `Cache-Control: no-store` + `X-Accel-Buffering: no`.

## Cut-off monitoring & Emergency (added 2026-11-23)

Warns Admin and Race-staff when a started runner has not passed a checkpoint before its cut-off. Module: `src/Modules/RaceMonitoring`. UI: admin-fe Emergency page ("Khẩn cấp").

**Flow**

1. `CutoffWarningMonitor` (BackgroundService, Infrastructure) sends `DetectCutoffWarningsCommand` every `IntervalSeconds`. The first tick waits one interval because the seeder migrates the schema at startup.
2. `DetectCutoffWarningsCommandHandler` reads race-day events (today and yesterday in the race time zone) via `IEventCutoffScheduleQuery` (Event.Contracts, implemented in `Event.Infrastructure/Events/EventCutoffScheduleQuery.cs`) and first passages from checkpoint scans.
   - Raises one warning per (registration, checkpoint) when cut-off + grace period is over, the runner has started (scan, start time or status Running, when `RequireStartEvidence`), and there is no passage at that checkpoint or any later one.
   - Skips runners with race status Finished, DNS, DNF, DQ.
   - Auto-resolves active warnings: `PassedCheckpoint`, `PassedLaterCheckpoint`, or `RunnerStatusChanged`.
   - One failing event is logged and does not stop the others.
3. `CutoffWarningPassageResolver` is also called from `CheckpointScanRecorder` so a late scan closes the warning immediately instead of on the next tick.
4. Changes are saved, then published to `InMemoryCutoffWarningBroker` (singleton, registered as both `ICutoffWarningBroker` and `ICutoffWarningPublisher`), and pushed to open SSE streams.

**Cut-off times**: checkpoint cut-off is the organizer's local "HH:mm" (also `H'h'mm`, or a full ISO timestamp). `CutoffClock.ToUtc` converts it with the event date and `TimeZoneId`; a time earlier than the distance start is treated as next day (overnight ultras).

**Data**: table `race_monitoring.cutoff_warnings` (entity `Domain/Entities/RunnerMonitoring/CutoffWarning.cs`, migration `20261123102433_AddCutoffWarnings`). Unique index on (RegistrationId, CheckpointId). Names, BIB and last passage are snapshots; no cross-schema foreign keys. Rows are never deleted: status is `Active` → `Resolved` (system) or `Dismissed` (staff, with optional note ≤ 500 chars).

**Endpoints** (`RaceMonitoringModule.cs`, group `/api/v1/race-monitoring`, policy `OrganizerOrRaceStaff`):

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/cutoff-warnings` | `eventId?`, `status` = Active (default) / Resolved / Dismissed / All, `search` (BIB, name, checkpoint), `page`, `pageSize`; returns `events` with counts per event |
| GET | `/cutoff-warnings/stream` | SSE: `ready`, `heartbeat` (15 s), `CutoffWarningRaised`, `CutoffWarningResolved`, `CutoffWarningDismissed`; closes when the JWT expires |
| GET | `/cutoff-warnings/{id}/passages` | last passage + up to 50 scans, newest first |
| POST | `/cutoff-warnings/{id}/dismiss` | body `{ note? }`; only changes status, never touches registrations, scans or timeline |

**Access** (`CutoffWarningAccess`): Admin sees every event; others only events where they are organizer or Active Race-staff (same rule as the SOS dashboard, `IEventSosOpsAccessQuery`). The SSE stream re-checks access per event (cached 30 s) and fails closed.

**Config** (`appsettings.json` → `RaceMonitoring:CutoffMonitoring`): `Enabled` (true), `IntervalSeconds` (30, min 5), `GracePeriodSeconds` (120, lets late RFID uploads land first), `LookBackHours` (12), `RequireStartEvidence` (true), `TimeZoneId` (`Asia/Ho_Chi_Minh`).

**Limit**: the broker is in-memory, so live notifications only reach clients connected to the same API instance. Running more than one API instance needs a shared channel (e.g. Redis pub/sub). The list endpoint is always correct because it reads the database.

**Tests** (`tests/RaceDay.Modules.RaceMonitoring.UnitTests`): `Domain/CutoffWarningTests.cs`, `Application/CutoffClockTests.cs`, `Application/DetectCutoffWarningsHandlerTests.cs`, `Application/CutoffWarningPassageResolverTests.cs`, `Api/CutoffWarningSseTests.cs` (wire format: event names and camelCase fields admin-fe reads, per-event filtering). The test project references the RaceMonitoring Api project and `Microsoft.AspNetCore.App` for the SSE tests.

## Simulator → race timeline (added 2026-11-25)

Simulated checkpoint crossings appear on the Race-staff "Timeline trực tiếp" (admin-fe) as RFID rows marked simulated.

- `SimulationTimelineFeed` (Simulation.Application) is a second `ISimulationBroadcaster` behind `CompositeSimulationBroadcaster`: it diffs each snapshot's splits and queues only new crossings. `SimulationTimelineWorker` (Simulation.Api, BackgroundService) writes the queue to `ISimulationTimelineSink` (RaceMonitoring.Contracts), outside the clock tick.
- `SimulationTimelineSink` (RaceMonitoring.Application) writes `RaceEventLog` rows only — `SourceType = "Simulation"`, SourceId stable per (run, runner, course checkpoint), metadata `scanMethod: RFID` + `simulationRunId`. **No `checkpoint_scans` row**, so cut-off monitoring ignores simulated passes. OccurredAtUtc = official start (event date + distance/event start, `Simulation:StartTimeZone`, same anchor as the result commit) + interpolated sim time (microseconds).
- The first snapshot of a run on a distance (create, Reset, New Run, or after an API restart) deletes every other simulated run's rows for that distance and sends SSE `RaceEventLogsCleared { eventId, eventDistanceId, simulationRunId }`.
- Chip time: `RaceEventLogChipStarts` adds `chipStartAtUtc` (first pass at a Start-type checkpoint, simulated and real kept apart) to list/get/SSE; admin-fe shows `occurredAt − chipStart` as `HH:MM:SS.mmm`.
- Map course checkpoints → `event_checkpoints`: saving the map (`UpsertCourseCommandHandler`) calls `IEventCheckpointSync` (Event.Contracts). Match by ExternalId, then same type within 1 m; removing a checkpoint that has scans, timeline rows or passages (`ICheckpointActivityQuery` + Event passages) throws and the course is not saved.
- Tests: `CheckpointCourseSyncTests` (Event), `SimulationTimelineSinkTests` (RaceMonitoring), `SimulationTimelineFeedTests` (Simulation).

## Race times are Vietnam wall-clock (since 2026-11-27)

`EventDate`, `StartTime`, `EndDate` and distance `StartAt`/`EndAt` hold what the organizer types in UserWeb (no offset, never converted by the frontends), which is Vietnam time. Compare them with UTC instants only through `RaceDay.Shared.Application.Time.RaceLocalTime` (`ToUtc(date, time)`, `Today(utcNow)`, `TryFindZone`; falls back to a fixed UTC+07 zone when the host has no tz data).

- Uses it: finish result time (`FinishCommands`), DNS gate (`ChangeRaceStatusCommand`), refunds/payout phase (`LedgerCalculator`), dispute "last month" window, event-date validation, positioning race-day window (`EventOrganizerAccessQuery.StartsAtUtc`), simulation official start (`Simulation:StartTimeZone` = `Asia/Ho_Chi_Minh` in docker-compose and appsettings.Development). Cut-off monitoring uses `CutoffClock` with the same zone.
- Before this, the first four read the clock as UTC (7 h late: a 06:00 race could not record a finish before 13:00 and refunds counted UTC dates). Results already stored are not recomputed.
- `deploy/sql/seed_system_test_event.sql` stores the Vietnam clock of `-StartAt`.
- **Not changed (watch is out of scope):** `RunnerRaceDayQuery.GetPrimaryAsync`, used only by `POST /api/v1/device/sos` (wear token), still takes "today" as the UTC date and picks the runner's registration whose `EventDate` is closest. Between 00:00 and 07:00 Vietnam time a runner with races on consecutive days can have an SOS attached to the wrong race (ties are unordered). Fix it before bringing the watch back: use `RaceLocalTime.Today`, prefer the race whose race-day window contains now, and add a tie-breaker.

## Staff QR check-in & out-of-zone alerts (added 2026-11-27)

Staff and Photographers share GPS (Positioning module) only after checking in with the organizer's rotating QR.

- `StaffCheckInCodes` (Application) is stateless: QR text = `raceday-staff-checkin:{eventId:N}:{window}:{hmac}` with 60 s windows (unix seconds / 60), plus 5 s grace after rotation. Key: `Positioning:CheckInSecret`, else derived from `Jwt:SigningKey`, else random per process.
- Endpoints (group `/api/v1/positioning/events/{eventId}`): `GET /check-in-code` (organizer only → `{ payload, issuedAtUtc, expiresAtUtc, serverNowUtc }`), `POST /check-in` `{ code }` (active Staff/Photographer; wrong code → 400 with `StaffCheckInRules.WrongCode`), `DELETE /check-in`.
- Check-in state lives in Redis (`CachedStaffCheckInStore`, key `positioning:checkin:{event}:{user}`, 24 h). `POST /positions` from a staff member who has not checked in returns 403 `{ error, zoneStatus: "not-checked-in" }`; organizers are exempt.
- Window: check-in and out-of-zone alerts open 1 h before the race start (`PositioningAccess.RaceDayOpensAt`), closed for Cancelled/Completed/Suspended.
- Out-of-zone alerts (`PositionAlert.UserId`) are delivered by `PositionEventStream` only to the listener whose user left the zone, on both `/alerts` and `/positions/live`. Organizers and other staff no longer get them; the live map still shows positions.
- UI: UserWeb organizer "Quản lý nhân sự" → "QR check-in nhân sự" (`StaffCheckInQrDialog`); StaffMobile drawer Check-in toggle → `StaffCheckInScanScreen`.
- Test data: `deploy/sql/seed_staff_checkin_outzone.sql` (re-run to move the start to now + 30 min).

## Known pre-existing issues (as of 2026-11-23)

- `Event.IntegrationTests`: 27 failures because Postgres-specific migrations run on SQLite. Not caused by the cut-off work.
- Build warning MSB3277 (EF Core Relational 10.0.4 vs 10.0.12) from the RaceMonitoring Api project.

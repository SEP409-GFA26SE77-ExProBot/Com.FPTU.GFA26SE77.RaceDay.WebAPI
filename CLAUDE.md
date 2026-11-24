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

## Known pre-existing issues (as of 2026-11-23)

- `Event.IntegrationTests`: 27 failures because Postgres-specific migrations run on SQLite. Not caused by the cut-off work.
- Build warning MSB3277 (EF Core Relational 10.0.4 vs 10.0.12) from the RaceMonitoring Api project.

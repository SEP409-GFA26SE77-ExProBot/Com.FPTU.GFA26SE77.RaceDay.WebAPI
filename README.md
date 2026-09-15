# RaceDay WebAPI — Modular Monolith

.NET 10 modular monolith for RaceDay. Shared kernel + feature modules + API Gateway.

## Solution layout

```text
src/
  Shared/                 # Cross-cutting (Domain/Application/Infrastructure/Web)
  Modules/
    Iam/                  # Auth & identity (PostgreSQL)
    AuditLog/             # Audit trail (MongoDB)
    Payment/              # Skeleton (implement later)
    _Template/            # Copy-paste starter for new modules
  Hosts/RaceDay.Api/      # Composition root (monolith host)
  Gateways/RaceDay.ApiGateway/  # YARP gateway (public vs admin)
tests/
deploy/docker-compose.yml # Postgres, MongoDB, Redis, RabbitMQ
```

Open `RaceDay.slnx` (not the old `WebApplication1` template).

## Patterns included

| Area | What |
|------|------|
| Repository + Unit of Work | EF/Postgres (IAM) |
| Mongo repository | AuditLog (no classic UoW) |
| Caching | Redis (`ICacheService`) |
| Messaging | RabbitMQ scaffold (`IEventBus`) |
| CQRS-lite | MediatR commands/queries |
| Rate limiting | Shared.Web + Gateway |
| Auth policies | `PublicUser`, `AdminOnly` |
| Admin isolation | Gateway blocks `/admin/**` in Production |

## Quick start

```bash
docker compose -f deploy/docker-compose.yml up -d
dotnet run --project src/Hosts/RaceDay.Api
dotnet run --project src/Gateways/RaceDay.ApiGateway
dotnet test RaceDay.slnx
```

- Host: `http://localhost:5080`
- Gateway: `http://localhost:5000`
- Public API prefix: `/api/v1/...`
- Admin API prefix: `/admin/v1/...` (blocked on public gateway in Production)

## Add a new module

See `src/Modules/_Template/README.md`.

## Notes

- JWT bearer is wired but signing config must be completed before production auth.
- RabbitMQ publisher is a scaffold logger stub — replace with real client publish.
- Old folder `Com.FPTU.GFA26SE77.RaceDay.WebAPI/WebApplication1` is leftover template; prefer `src/`.
- `Directory.Build.props` pins patched transitive packages (`SharpCompress`, `Snappier`, `System.Security.Cryptography.Xml`) for NuGet audit on Infrastructure projects.

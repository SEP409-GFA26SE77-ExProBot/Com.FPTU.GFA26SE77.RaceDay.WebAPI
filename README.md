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

## Cursor / agent conventions

- `AGENTS.md` — short non-negotiables for AI/human contributors
- `.cursor/rules/*.mdc` — architecture, C# standards, modules, API/security, Shared, testing

## Database (PostgreSQL)

- Database `postgres` — schemas `iam` / `event` / `payment`
- Secrets in **repo-root `.env`** (gitignored). Copy from `.env.example`.
- One SQL script: [`deploy/sql/raceday_init.sql`](deploy/sql/raceday_init.sql) (schema + seed)
- Dev seed password: `MatKhau@123` (`admin@raceday.vn`, `cuong.le@raceday.vn`, …)
- EF also migrates on Host startup (history stamped in the SQL file)

```bash
cp .env.example .env   # then fill Password + Jwt__SigningKey
docker compose -f deploy/docker-compose.yml up -d
# OR: psql -h localhost -U postgres -d postgres -f deploy/sql/raceday_init.sql
dotnet run --project src/Hosts/RaceDay.Api
```

Seed admin: `admin@raceday.vn` / `MatKhau@123` · Event/Payment API: `docs/RaceDay_Event_API_Guide.docx`

## Notes

- Secrets (DB password, JWT signing key) must be in `.env` — never commit `.env`.
- Prefer **one** of: run `raceday_init.sql` **or** let EF migrate on startup.
- JWT via `Jwt__*` env vars. In-process audit: IAM → AuditLog (Mongo).
- Prefer `src/` modular monolith; ignore legacy `WebApplication1` template.

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

## IAM database (PostgreSQL)

- Server: PostgreSQL 18 — database `postgres`, schema `iam`
- Secrets live in **repo-root `.env`** (gitignored). Copy from `.env.example`.
- SQL script: `deploy/sql/iam_init.sql`
- Dev seed password: `MatKhau@123` (users like `admin@raceday.vn`)
- EF migrates + seeds on Host startup

```bash
cp .env.example .env   # then fill Password + Jwt__SigningKey
docker compose -f deploy/docker-compose.yml up -d   # optional Mongo/Redis
# OR run SQL: psql -h localhost -U postgres -d postgres -f deploy/sql/iam_init.sql
dotnet run --project src/Hosts/RaceDay.Api
```

### IAM API

| Method | Path | Auth |
|--------|------|------|
| POST | `/api/v1/iam/auth/register` | public |
| POST | `/api/v1/iam/auth/login` | public |
| POST | `/api/v1/iam/auth/reset-password` | public |
| GET | `/admin/v1/iam/users` | Admin JWT |
| GET | `/admin/v1/iam/users/{id}` | Admin JWT |
| PUT | `/admin/v1/iam/users/{id}` | Admin JWT |
| DELETE | `/admin/v1/iam/users/{id}` | Admin JWT |
| GET | `/admin/v1/audit-logs` | Admin JWT |

Seed admin: `admin@raceday.vn` / `MatKhau@123`

## Notes

- Secrets (DB password, JWT signing key) must be in `.env` — never commit `.env`.
- If local PostgreSQL password differs, set `ConnectionStrings__Postgres` in `.env`.
- Prefer **one** of: run `iam_init.sql` **or** let EF migrate on startup (SQL script already inserts EF migration history).
- JWT is configured via `Jwt__*` env vars.
- In-process audit: IAM Contracts events → `MediatorEventBus` → AuditLog (Mongo).
- Old folder `Com.FPTU.GFA26SE77.RaceDay.WebAPI/WebApplication1` is leftover template; prefer `src/`.
- `Directory.Build.props` pins patched transitive packages for NuGet audit.

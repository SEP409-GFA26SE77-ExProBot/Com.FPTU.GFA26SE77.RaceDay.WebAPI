# RaceDay WebAPI — Modular Monolith

.NET 10 modular monolith: Shared kernel + feature modules (IAM, Event, Payment, AuditLog, RaceMonitoring) + YARP API Gateway.

Open **`RaceDay.slnx`** (không dùng legacy `WebApplication1`).

---

## Cấu trúc thư mục (file quan trọng ở đâu?)

```text
SEP_WebAPI/
├── RaceDay.slnx                 # Solution chính
├── .env.example                 # Mẫu cấu hình — copy → .env (gitignored)
├── .env                         # Secrets thật (KHÔNG commit)
├── AGENTS.md / .cursor/rules/   # Quy ước kiến trúc cho contributor / AI
│
├── docs/                        # ★ Tài liệu
│   ├── RaceDay_API_Guide.docx          # ★ Hướng dẫn API gộp 1 file (IAM+Event+Payment)
│   │                                   #    View → Navigation Pane: click Heading nhảy API
│   └── _gen_api_guide_docx.py          # Regenerate: python docs/_gen_api_guide_docx.py
│
├── deploy/                      # ★ Hạ tầng local (+ optional API container)
│   ├── docker-compose.yml       # Postgres, MongoDB, Redis, RabbitMQ, profile `api`
│   ├── Dockerfile               # RaceDay.Api image (compose profile api)
│   └── sql/
│       └── raceday_init.sql     # ★ Schema + seed IAM/Event/Payment (1 file)
│
├── src/
│   ├── Shared/                  # Cross-cutting (Domain/Application/Infrastructure/Web)
│   ├── Modules/
│   │   ├── Iam/                 # Auth — PostgreSQL schema `iam`
│   │   ├── Event/               # Giải + đăng ký — schema `event`
│   │   ├── Payment/             # PayOS checkout — schema `payment`
│   │   ├── AuditLog/            # Audit — MongoDB
│   │   ├── RaceMonitoring/      # Giám sát giải — scaffold, chưa có schema
│   │   └── _Template/           # Copy khi tạo module mới
│   ├── Hosts/RaceDay.Api/       # Composition root (chạy API :5080)
│   └── Gateways/RaceDay.ApiGateway/  # YARP public edge (:5000)
│
└── tests/                       # Unit tests theo module
```

| Bạn cần… | Mở file / thư mục |
|----------|-------------------|
| SQL tạo bảng + seed | [`deploy/sql/raceday_init.sql`](deploy/sql/raceday_init.sql) |
| Docker DB/cache/queue | [`deploy/docker-compose.yml`](deploy/docker-compose.yml) |
| Hướng dẫn API + body mẫu | [`docs/RaceDay_API_Guide.docx`](docs/RaceDay_API_Guide.docx) — mở Navigation Pane (mục lục trái) |
| Biến môi trường | [`.env.example`](.env.example) → copy thành `.env` |
| Chạy API | `src/Hosts/RaceDay.Api` |
| Gateway | `src/Gateways/RaceDay.ApiGateway` |

---

## Quick start

```bash
# 1) Secrets
cp .env.example .env
# Sửa: Postgres password, Jwt__SigningKey, Iam__SeedPassword (= MatKhau@123 nếu dùng seed SQL)

# 2) Infra (không gồm API — mặc định)
docker compose -f deploy/docker-compose.yml up -d
# Postgres map host :5433 → container :5432 (tránh conflict PostgreSQL Windows :5432).
# Volume Postgres trống → tự chạy deploy/sql/raceday_init.sql lần đầu.
# Reset volume Docker: docker compose -f deploy/docker-compose.yml down -v && up -d

# 3a) API local (dev thường)
dotnet run --project src/Hosts/RaceDay.Api --launch-profile http

# 3b) API trong Docker (cùng file compose, profile api)
# docker compose -f deploy/docker-compose.yml --profile api up -d --build
# → http://localhost:5080/swagger  (connection string nội bộ: Host=postgres …)

# Optional gateway:
dotnet run --project src/Gateways/RaceDay.ApiGateway

dotnet test RaceDay.slnx
```

| Service | URL |
|---------|-----|
| API Host | http://localhost:5080 |
| Swagger | http://localhost:5080/swagger |
| Gateway | http://localhost:5000 |
| RabbitMQ UI | http://localhost:15672 (`guest`/`guest`) |

- Public API: `/api/v1/{module}/...`
- Admin: `/api/v1/{module}/admin/...` hoặc policy `AdminOnly` (Gateway Production chặn `/admin/**`)

**Seed login** (ưu tiên **username**, password chung Active: `MatKhau@123` — nguồn `deploy/sql/raceday_init.sql` / mục 0 trong `docs/RaceDay_API_Guide.docx`):

| Role | Username | Email |
|------|----------|-------|
| Admin | `admin` | `admin@raceday.vn` |
| Organizer | `cuong.le` | `cuong.le@raceday.vn` |
| Runner | `an.nguyen` | `an.nguyen@raceday.vn` |
| Runner | `binh.tran` | `binh.tran@raceday.vn` |

---

## Setup từng thành phần

Tất cả cấu hình qua **`.env`** ở root (ASP.NET: `Section__Key`). Host đọc `.env` khi start.

### 1. PostgreSQL (IAM + Event + Payment)

| Key | Ví dụ |
|-----|--------|
| `ConnectionStrings__Postgres` | `Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres` |

- Docker compose: user/password/db = `postgres` / `postgres` / `postgres`, **host port `5433`** (tránh đụng PostgreSQL Windows trên `5432`).
- Schemas: `iam`, `event`, `payment`.
- Schema + seed: **một file** [`deploy/sql/raceday_init.sql`](deploy/sql/raceday_init.sql).
- App cũng `MigrateAsync` khi start; file SQL đã stamp `__EFMigrationsHistory` để không tạo trùng.

Re-seed Event/Payment sạch (giữ user IAM): xem comment `TRUNCATE` trong đầu file `raceday_init.sql`.

### 2. MongoDB (AuditLog)

| Key | Ví dụ |
|-----|--------|
| `ConnectionStrings__MongoDb` | `mongodb://localhost:27017` |
| `MongoDb__Database` | `raceday` |

- Docker: port `27017`. Không cần SQL init — AuditLog tạo collection khi ghi.
- OTP / login / approve… emit integration events → ghi audit Mongo.

### 3. Redis (cache)

| Key | Ví dụ |
|-----|--------|
| `ConnectionStrings__Redis` | `localhost:6379` |

Docker: port `6379`. Dùng qua `ICacheService`.

### 4. RabbitMQ (messaging scaffold)

| Key | Ví dụ |
|-----|--------|
| `ConnectionStrings__RabbitMq` | `amqp://guest:guest@localhost:5672` |

Docker: AMQP `5672`, management UI `15672`. Exchange cấu hình trong `appsettings` (`RabbitMq:Exchange`).

### 5. JWT

| Key | Ghi chú |
|-----|---------|
| `Jwt__Issuer` | `raceday` |
| `Jwt__Audience` | `raceday-clients` |
| `Jwt__SigningKey` | Chuỗi random ≥ 32 ký tự (Base64 khuyến nghị) |

### 6. SMTP (OTP email, ticket mail)

| Key | Ghi chú |
|-----|---------|
| `Smtp__Host` | `smtp.gmail.com` — **để trống** = chỉ log OTP ra console (dev) |
| `Smtp__Port` | `587` |
| `Smtp__Username` / `Smtp__From` | Gmail của bạn |
| `Smtp__Password` | **App Password** 16 ký tự (Google Account → Security → 2-Step → App passwords) |
| `Smtp__FromName` | `RaceDay` |
| `Smtp__UseSsl` | `true` |

Gmail: không dùng mật khẩu đăng nhập thường — bắt buộc App Password.

### 7. File storage — Local hoặc Cloudflare R2

Dùng cho: banner giải, hồ sơ organizer, giấy khám VĐV (`POST .../uploads`).

**Dev — Local (mặc định)**

```env
Storage__Provider=Local
Storage__PublicBaseUrl=http://localhost:5080
```

File nằm dưới `wwwroot/uploads/...` của Host; URL = `{PublicBaseUrl}/uploads/...`.

**Cloudflare R2**

1. Cloudflare Dashboard → R2 → tạo bucket (vd `raceday-uploads`).
2. Manage R2 API Tokens → Access Key Id + Secret.
3. Bật **public access** / custom domain (hoặc `pub-xxxx.r2.dev`) — FE cần URL public, **không** dùng URL dashboard.
4. `.env`:

```env
Storage__Provider=R2
Storage__PublicBaseUrl=https://pub-xxxx.r2.dev
Storage__R2__AccountId=your_account_id
Storage__R2__AccessKeyId=...
Storage__R2__SecretAccessKey=...
Storage__R2__Bucket=raceday-uploads
```

Flow FE: **upload trước** → lấy `fileUrl` → mới gọi create event / registration (chi tiết trong Event API Guide).

### 8. PayOS (thanh toán)

| Key | Ghi chú |
|-----|---------|
| `PayOS__UseMock` | `true` = cổng giả (dev, không cần account) |
| `PayOS__ClientId` / `ApiKey` / `ChecksumKey` | Lấy từ [my.payos.vn](https://my.payos.vn) khi live |
| `PayOS__BaseUrl` | `https://api-merchant.payos.vn` |
| `PayOS__PublicApiBaseUrl` | Base API để build mock checkout link (`http://localhost:5080`) |

**Dev:** `PayOS__UseMock=true` → `POST /api/v1/payment/checkout` trả HTML mock → bấm Thanh toán = PAID → registration `pending_approval`.

**Live:** `UseMock=false` + 3 key + webhook public tới  
`POST /api/v1/payment/webhooks/payos` (cần tunnel/ngrok nếu local).

### 9. IAM seed password

| Key | Ghi chú |
|-----|---------|
| `Iam__SeedPassword` | Chỉ khi bảng `iam.users` **trống** — app seeder tạo user. Nên trùng password trong SQL seed (`MatKhau@123`). |

---

## Modules & persistence

| Module | Engine | Schema / DB |
|--------|--------|-------------|
| IAM | PostgreSQL + EF + UoW | `iam` |
| Event | PostgreSQL + EF + UoW | `event` |
| Payment | PostgreSQL + EF + UoW | `payment` |
| AuditLog | MongoDB | database `raceday` |
| RaceMonitoring | PostgreSQL packages sẵn, chưa gắn DbContext | schema chưa tạo |
| Cache | Redis | — |
| Bus | RabbitMQ (+ MediatR in-process) | — |

---

## Patterns

| Area | What |
|------|------|
| Repository + UoW | Postgres modules |
| Mongo repo | AuditLog (không classic UoW) |
| CQRS-lite | MediatR |
| Rate limiting | Shared.Web + Gateway |
| Auth | JWT policies `PublicUser`, `AdminOnly` |
| Admin isolation | Gateway Production chặn `/admin/**` |

---

## Thêm module mới

Xem `src/Modules/_Template/README.md` — copy template, đăng ký trong `Program.cs` + Gateway + tests.

---

## Ghi chú

- **Không commit** `.env`, keys R2/PayOS/SMTP.
- Ưu tiên `src/` modular monolith; bỏ qua folder legacy template.
- Doc API chi tiết (field, thứ tự gọi upload → create → checkout): thư mục **`docs/`**.

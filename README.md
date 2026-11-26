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
│   ├── docker-compose.yml       # Postgres, Redis, RabbitMQ, profile `api`
│   ├── Dockerfile               # RaceDay.Api image (compose profile api)
│   ├── clear-and-seed.ps1       # Truncate + seed demo
│   └── sql/
│       └── clear_and_seed.sql   # ★ Demo data (ONE FILE)
│
├── src/
│   ├── Shared/                  # Cross-cutting (Domain/Application/Infrastructure/Web)
│   ├── Modules/
│   │   ├── Iam/                 # Auth — PostgreSQL schema `iam`
│   │   ├── Event/               # Giải + đăng ký — schema `event`
│   │   ├── Payment/             # PayOS checkout — schema `payment`
│   │   ├── AuditLog/            # Audit — PostgreSQL schema `audit` (append-only)
│   │   ├── RaceMonitoring/      # Giám sát giải
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
| Workspace staff checkpoint / simulation, nhật ký lưu lâu dài và chốt báo cáo | [`docs/staff-checkpoint-workspace.md`](docs/staff-checkpoint-workspace.md) |
| Dashboard Admin / Organizer — 5 API, luồng gọi và ý nghĩa số liệu | [`docs/dashboard-api.md`](docs/dashboard-api.md) |
| Giao diện dashboard — đường dẫn, luồng sử dụng, bộ lọc và kiểm thử | [`docs/dashboard-ui.md`](docs/dashboard-ui.md) |
| Dashboard — nhóm trạng thái và xuất Excel/PDF | [`docs/dashboard-export.md`](docs/dashboard-export.md) |

Trong simulation, staff phụ trách **sau CP trước đến hết CP của mình**: `(km CP trước, km CP được giao]`; Start phụ trách vị trí xuất phát. DNF theo chặng hiện tại; DQ được báo trong chặng hoặc khi đã có ghi nhận qua trạm trong lượt, kể cả đã về đích, trước khi checkpoint chốt/lượt khóa. Ví dụ CP1 km 3 và CP2 km 5: `(3, 5]` thuộc CP2. Xem tài liệu checkpoint để test trên AdminWeb. Thay đổi phạm vi không cần migration; triển khai backend và AdminWeb cùng đợt.

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

# Bộ dữ liệu test staff checkpoint + 12 runner (Postgres localhost:5433)
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/seed-staff-workspace.ps1

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

### Chạy AdminWeb và UserWeb bằng Docker

Đặt hai repo `admin-fe` và `UserWeb` cạnh repo WebAPI. Từ thư mục gốc WebAPI, chạy API ở cổng 5080 rồi build và chạy hai frontend.

```powershell
docker compose -f deploy/docker-compose.yml --profile api up -d --build
docker compose -f deploy/docker-compose.frontends.yml up -d --build
```

- UserWeb mở tại [localhost:3000](http://localhost:3000).
- AdminWeb mở tại [localhost:3001](http://localhost:3001).
- Dừng hai frontend bằng `docker compose -f deploy/docker-compose.frontends.yml down`.

Compose dùng `RACEDAY_BACKEND_API_URL` cho các request từ container đến API, mặc định là `http://host.docker.internal:5080`. Trình duyệt dùng `RACEDAY_PUBLIC_API_URL` cho SignalR và các URL API trực tiếp, mặc định là `http://localhost:5080`. Nếu đổi URL public hoặc CDN qua `RACEDAY_STORAGE_PUBLIC_BASE_URL`, chạy lại `up -d --build` để cập nhật bundle. API cần cho phép các origin `http://localhost:3000` và `http://localhost:3001` trong `ALLOWS_CORS`.

**Seed login** (ưu tiên **username**, password chung Active: `MatKhau@123` — nguồn `deploy/sql/raceday_init.sql` / mục 0 trong `docs/RaceDay_API_Guide.docx`):

| Role | Username | Email |
|------|----------|-------|
| Admin | `admin` | `admin@raceday.vn` |
| Organizer | `cuong.le` | `cuong.le@raceday.vn` |
| Runner | `an.nguyen` | `an.nguyen@raceday.vn` |
| Runner | `binh.tran` | `binh.tran@raceday.vn` |

---

## Setup từng thành phần

### Positioning

- `POST /api/v1/positioning/events/{eventId}/positions` nhận `latitude`, `longitude`, `accuracyMeters` và `capturedAtUtc` từ tài khoản đang có phân công Active hoặc organizer của giải. Phản hồi `zoneStatus` là `inside`, `outside`, `uncertain` (sai số GPS chồng lên ranh giới) hoặc `unconfigured`.
- `GET /api/v1/positioning/events/{eventId}/alerts` mở stream SSE xác thực. Sự kiện `outside` chứa tên, vai trò và mã phân công của nhân sự rời vùng. Người xem phải là organizer của giải hoặc nhân sự có phân công Active tại giải.
- `GET /api/v1/positioning/events/{eventId}/positions` trả vị trí của Staff và Photographer có mẫu GPS trong 20 giây gần nhất. Quyền xem giống stream cảnh báo. Dùng `nextOffset` để đọc trang tiếp theo nếu có.
- `GET /api/v1/positioning/events/{eventId}/positions/live` mở SSE với danh sách ban đầu (`snapshot`), theo sau bởi vị trí mới (`position`) và cảnh báo rời vùng (`outside`). Khi kết nối lại, client nhận danh sách mới nhất.
- Phân công **Di động** dùng vùng đệm 100 m dọc các tuyến của giải. Checkpoint dùng bán kính 500 m quanh tọa độ đã lưu. Với nhiều checkpoint được giao, nhân sự có thể hoạt động trong bất kỳ vùng nào trong số đó. Nhiệm vụ chưa có tọa độ chỉ báo vị trí, chưa phát cảnh báo.
- Mẫu mới nhất hết hạn sau 2 phút. Cảnh báo chỉ bắt đầu từ một giờ trước giờ xuất phát và vẫn gửi trong lúc giải diễn ra. Lấy giờ xuất phát của giải hoặc giờ sớm nhất trong các cự ly nếu giải chưa đặt giờ. Cảnh báo realtime được phát đến các kết nối trên cùng instance WebAPI.

Tất cả cấu hình qua **`.env`** ở root (ASP.NET: `Section__Key`). Host đọc `.env` khi start.

### 1. PostgreSQL (IAM + Event + Payment)

| Key | Ví dụ |
|-----|--------|
| `ConnectionStrings__Postgres` | `Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres` |

- Docker compose: user/password/db = `postgres` / `postgres` / `postgres`, **host port `5433`** (tránh đụng PostgreSQL Windows trên `5432`).
- Schemas: `iam`, `event`, `payment`, `finance`, `map`, `photo`, `device`, `audit`, …
- Schema: API `MigrateAsync` khi start. Demo data: [`deploy/clear-and-seed.ps1`](deploy/clear-and-seed.ps1) → [`deploy/sql/clear_and_seed.sql`](deploy/sql/clear_and_seed.sql).

#### Seed riêng cho system test

Khởi động API để chạy migration trước. Từ thư mục WebAPI, chạy lệnh phù hợp với kịch bản test.

```powershell
# Xóa dữ liệu PostgreSQL của RaceDay ở Docker local và nạp bộ system test mới.
.\deploy\seed-system-test-event.ps1 -ResetDatabase

# Nạp hoặc đổi lịch giải Bien Dong System Test 5K.
.\deploy\seed-system-test-event.ps1 -StartAt '2026-11-18T06:30:00+07:00' -DurationMinutes 90

# Đặt giờ xuất phát lùi 30 phút để thử luồng DNS sau giờ xuất phát.
.\deploy\seed-system-test-event.ps1 -StartAt ([DateTimeOffset]::Now.AddMinutes(-30).ToString('yyyy-MM-ddTHH:mm:sszzz'))
```

Thay timestamp trong lệnh bằng giờ test cần dùng. Nếu bỏ `-StartAt`, giờ xuất phát mặc định là sau 24 giờ. Seed lưu ngày và giờ theo UTC cho cả sự kiện và cự ly 5K, rồi in giờ UTC và UTC+07:00. Đăng ký đóng 15 phút trước giờ xuất phát. Khi thời gian kết thúc qua ngày UTC kế tiếp, seed lưu ngày kết thúc của giải và để trống giờ cutoff của cự ly.

Chạy lại lệnh import để đổi lịch trên cùng event ID `5eed2026-0000-4000-8000-000000000100`. Các đăng ký, BIB, check-in và kết quả đã có được giữ lại. File [`seed_system_test_event.sql`](deploy/sql/seed_system_test_event.sql) dùng trực tiếp với `psql -f`. Có thể sửa mặc định ở đầu file hoặc truyền `-v event_start=2026-11-18T06:30:00+07:00 -v duration_minutes=90`. File [`clear_and_seed_system_test.sql`](deploy/sql/clear_and_seed_system_test.sql) nạp lại toàn bộ dữ liệu và cần để cùng thư mục với file seed một sự kiện.

Mật khẩu chung của các tài khoản test là `MatKhau@123`.

| Kịch bản | Tài khoản |
|----------|-----------|
| Quản lý giải Bien Dong System Test 5K | `systest.organizer` |
| Checkpoint, check-in và upload ảnh | `systest.staff`, `systest.photo` |
| VĐV có BIB 50001 và 50002 | `systest.runner1`, `systest.runner2` |
| VĐV chưa đăng ký | `systest.runner3` |
| Duyệt Marble Mountains Trail 10K sau khi reset | `manager` |
| Xem kết quả và chứng nhận Han River Results 5K sau khi reset | `systest.runner1` |
| Quản trị hệ thống sau khi reset | `systest.admin` |

### 2. AuditLog (Postgres schema `audit`)

- Append-only bảng `audit.audit_entries` (EF, không classic UoW).
- OTP / login / approve… emit integration events → ghi audit Postgres.
- Admin FE: `GET /api/v1/audit-logs?page=&pageSize=` (không đổi path).

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
| AuditLog | PostgreSQL + EF (append-only, no UoW) | `audit` |
| RaceMonitoring | PostgreSQL + EF | `race_monitoring` (module schema) |
| Cache | Redis | — |
| Bus | RabbitMQ (+ MediatR in-process) | — |

---

## Patterns

| Area | What |
|------|------|
| Repository + UoW | Postgres transactional modules |
| Append-only repo | AuditLog Postgres (SaveChanges on insert) |
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

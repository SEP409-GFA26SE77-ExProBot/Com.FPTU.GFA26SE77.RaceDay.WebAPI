# Device — Wear OS Device Flow + SOS (RaceDay)

> **Nguồn:** thiết kế [Claude share](https://claude.ai/share/afd04cd2-7c98-4889-a379-8589d8dde652) (OAuth 2.0 Device Authorization — RFC 8628 + SOS Wear)  
> **Word:** `docs/Device_Wear_SOS_API_Guide.docx` ← sync từ file này (`python docs/_device_md_to_docx.py`)  
> **Ánh xạ:** modular monolith RaceDay (`.cursor/rules` / `AGENTS.md`) — module `Device`, prefix public `/api/v1/device`  
> **JSON:** camelCase. Lỗi domain/validation → `400` `{ "error": "…" }` (trừ lỗi poll token theo RFC — xem mục 2).  
> **Base URL local:** `http://localhost:5080`

---

## 0. Phạm vi module Device

Chỉ **Wear Device Flow (RFC 8628) + SOS** theo Claude share.  
RFID gate / `X-Device-Key` ingest đã **gỡ bỏ** — không còn trong code.

**Quy ước RaceDay vs snippet Claude:**

| Claude (ví dụ) | RaceDay |
|----------------|---------|
| `POST /device/code` | `POST /api/v1/device/auth/code` |
| `POST /device/token` | `POST /api/v1/device/auth/token` |
| `GET /device/link/{userCode}` | `GET /api/v1/device/link/{userCode}` |
| `POST /sos` | `POST /api/v1/device/sos` |
| `int RunnerId` | `Guid` = IAM `userId` (JWT `sub`) |
| `int? RaceId` | `Guid? EventId` (giải đang diễn ra) |
| `AppDb` monolithic | Schema EF riêng `device` + `IEventOrganizerAccessQuery` / Contracts — **không** join cross-module EF |

---

## 1. Happy path — thứ tự gọi API

```text
[Watch] 1. POST /api/v1/device/auth/code
           ↓ hiển thị QR (verificationUrlComplete) + userCode
[Phone] 2. Runner đã login IAM → mở /device?code=…
[Phone] 3. GET  /api/v1/device/link/{userCode}     (xem thiết bị)
[Phone] 4. POST /api/v1/device/link/{userCode}/approve
[Watch] 5. Poll POST /api/v1/device/auth/token     (mỗi ~3s)
           ↓ nhận accessToken + refreshToken (scope sos:create)
[Watch] 6. POST /api/v1/device/sos                 (giữ nút / countdown)
[Watch] 7. POST /api/v1/device/sos/{id}/locations  (cập nhật GPS mỗi 10–15s)
[Staff] 8. GET  /api/v1/device/events/{eventId}/sos
[Staff] 9. POST /api/v1/device/sos/{id}/ack | /resolve
[Runner] GET /api/v1/device/linked-devices + DELETE …/{id} (revoke)
```

**Thứ tự implement đề xuất (khớp Claude §5):**

1. SOS mock + schema (endpoint tạo SOS + location)  
2. Device Flow (auth/code + auth/token + link approve + trang FE)  
3. Gắn JWT scope `sos:create` vào SOS + retry/idempotency  
4. Organizer dashboard list/ack/resolve (+ SignalR optional)  
5. Runner list/revoke linked devices  

---

## 2. Watch — Device Authorization (không JWT user)

### 2.1 `POST /api/v1/device/auth/code`

**Ai gọi:** Wear OS (anonymous).  
**Rate limit:** policy riêng (vd. 10 req/IP/phút) — `device-code`.

**Input**

```json
{
  "deviceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```

| Field | Bắt buộc | Validation |
|-------|----------|------------|
| deviceId | ✓ | UUID không rỗng; watch tự sinh, lưu local |

**Xử lý server**

- Sinh `deviceCode` (random 32 bytes hex) — **chỉ trả plaintext 1 lần**, DB lưu **hash**.  
- Sinh `userCode` 8 ký tự dễ đọc (bỏ `0/O/1/I`), unique trong cửa sổ còn hạn.  
- `expiresAt` = now + 5 phút; `failedAttempts = 0`; `consumed = false`.

**Output `200`**

```json
{
  "deviceCode": "A1B2…(hex)",
  "userCode": "K7QM92XP",
  "verificationUrl": "https://app.raceday.vn/device",
  "verificationUrlComplete": "https://app.raceday.vn/device?code=K7QM92XP",
  "expiresIn": 300,
  "interval": 3
}
```

| Trường hợp lỗi | HTTP | Body |
|----------------|------|------|
| Thiếu/sai `deviceId` | 400 | `{ "error": "…" }` |
| Quá rate limit | 429 | — |

Watch: QR = `verificationUrlComplete`; text = `userCode`; poll sau mỗi `interval` giây.

---

### 2.2 `POST /api/v1/device/auth/token`

**Ai gọi:** Wear poll (anonymous).  
**Rate limit:** rộng hơn (vd. 30/IP/phút) — `device-token`.  
**Lỗi theo RFC 8628** (khác convention `{error}` message tiếng Việt — giữ `error` code chuẩn để client Flutter parse).

**Input**

```json
{
  "deviceCode": "A1B2…",
  "grantType": "urn:ietf:params:oauth:grant-type:device_code"
}
```

| Field | Bắt buộc | Validation |
|-------|----------|------------|
| deviceCode | ✓ | Khớp hash trong DB |
| grantType | | Nếu gửi: đúng device_code grant |

**Output khi đã approve `200`**

```json
{
  "accessToken": "eyJ…",
  "refreshToken": "…",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "scope": "sos:create"
}
```

Claims JWT watch gợi ý: `sub` = runner userId, `device_id` = `LinkedDevice.Id`, `scope` = `sos:create`.  
Sau khi cấp token: `DeviceAuthRequest.Consumed = true`; tạo/ cập nhật `LinkedDevice`.

| Trường hợp | HTTP | Body `error` |
|------------|------|----------------|
| Chưa approve | 400 | `authorization_pending` → watch **tiếp tục poll** |
| Hết hạn | 400 | `expired_token` → watch xin `/auth/code` mới |
| Sai / đã consume | 400 | `invalid_grant` |
| Thiếu deviceCode | 400 | `invalid_request` |

---

## 3. Runner phone — xác nhận liên kết (JWT PublicUser)

Prefix: `/api/v1/device/link` — `RequireAuthorization(PublicUser)`.

### 3.1 `GET /api/v1/device/link/{userCode}`

**Input:** path `userCode` (chuẩn hóa: bỏ dấu `-`/space, upper-case).

**Validation / bảo mật**

- Mã không tồn tại / hết hạn / đã consume → `404`.  
- Sai mã nhiều lần (theo IP hoặc user): tăng `FailedAttempts`; ≥ 5 → khóa tạm (vd. 15 phút) → `429` hoặc `400` `"Quá số lần thử"`.

**Output `200`**

```json
{
  "deviceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "userCode": "K7QM92XP",
  "expiresAtUtc": "2026-10-17T16:20:00Z",
  "displayName": "Wear OS watch"
}
```

FE: “Liên kết đồng hồ này với tài khoản {fullName}?”

---

### 3.2 `POST /api/v1/device/link/{userCode}/approve`

**Input:** path `userCode`; body optional:

```json
{ "deviceName": "Galaxy Watch 6" }
```

**Validation**

| Case | HTTP | Ghi chú |
|------|------|---------|
| Chưa login | 401 | |
| userCode invalid/expired/consumed | 404 | |
| Quá failed attempts | 400/429 | |
| OK | 204 | Set `ApprovedAt`, `RunnerUserId` = JWT sub |

**Output:** `204 No Content` (watch poll token sẽ nhận JWT).

---

### 3.3 `GET /api/v1/device/linked-devices`

**Auth:** JWT runner.  
**Output `200`:** danh sách đồng hồ đã link, chưa revoke.

```json
[
  {
    "id": "…",
    "deviceId": "…",
    "name": "Wear OS watch",
    "linkedAtUtc": "…",
    "revokedAtUtc": null
  }
]
```

---

### 3.4 `DELETE /api/v1/device/linked-devices/{id}`

**Auth:** JWT runner (chỉ device của mình) hoặc Admin.  
**Output:** `204`. Set `RevokedAtUtc`; access token sau đó fail khi check revoke (cache ngắn hạn OK).

---

## 4. Watch — SOS (JWT scope `sos:create`)

Policy: `SosWrite` = claim `scope` chứa `sos:create` + `LinkedDevice.RevokedAt == null`.  
**Không** rate-limit chặt (chỉ chống spam rất rộng).

### 4.1 `POST /api/v1/device/sos`

**Idempotent** theo `id` (= `eventId` do watch sinh — Claude gọi `event_id`).

**Input**

```json
{
  "id": "11111111-2222-4333-8444-555555555555",
  "lat": 10.8231,
  "lng": 106.6297,
  "accuracy": 12.5,
  "recordedAtUtc": "2026-10-15T08:30:00Z",
  "battery": 42
}
```

| Field | Bắt buộc | Validation |
|-------|----------|------------|
| id | ✓ | UUID; PK chống trùng retry |
| lat | ✓ | −90…90 |
| lng | ✓ | −180…180 |
| accuracy | | ≥ 0 nếu có |
| recordedAtUtc | ✓ | UTC |
| battery | | 0…100 nếu có |

**Server gắn:** `runnerUserId` + `linkedDeviceId` từ token; `eventId` (race) = registration confirmed đang trong cửa sổ race-day (query qua **Event.Contracts**, không join EF Event).

**Output `200`**

```json
{
  "id": "11111111-2222-4333-8444-555555555555",
  "status": "Open",
  "eventId": "bad0f19e-c875-4412-9c8c-e731e25fce9e",
  "createdAtUtc": "…"
}
```

| Case | HTTP |
|------|------|
| Thiếu/sai scope / device revoked | 401/403 |
| Validation toạ độ | 400 |
| Retry cùng `id` | 200 — append location, không tạo SOS mới |

---

### 4.2 `POST /api/v1/device/sos/{id}/locations`

Cập nhật vị trí sau SOS (mỗi 10–15s hoặc batch khi mất mạng).

**Input (1 điểm hoặc batch)**

```json
{
  "locations": [
    {
      "lat": 10.8240,
      "lng": 106.6301,
      "accuracy": 8.0,
      "recordedAtUtc": "2026-10-15T08:30:15Z",
      "battery": 41
    }
  ]
}
```

| Case | HTTP |
|------|------|
| SOS không tồn tại / không phải của runner | 404 |
| `status == Resolved` | 409 Conflict |
| OK | 204 |

---

### 4.3 (Optional) `POST /api/v1/device/auth/refresh`

Gia hạn access token bằng refresh token (hash lưu DB). Scope vẫn `sos:create`.

---

## 5. Organizer / Staff — dashboard SOS

**Auth:** JWT PublicUser + là organizer của giải / staff Active / Admin.

### 5.1 `GET /api/v1/device/events/{eventId}/sos?status=Open`

**Output `200`**

```json
[
  {
    "id": "…",
    "runnerUserId": "…",
    "bib": "1001",
    "status": "Open",
    "createdAtUtc": "…",
    "lastLat": 10.8231,
    "lastLng": 106.6297,
    "battery": 42
  }
]
```

`bib` lấy qua Event.Contracts (registration), không join cross-module.

### 5.2 `POST /api/v1/device/sos/{id}/ack`

Status → `Acknowledged`. Output: SOS DTO cập nhật.

### 5.3 `POST /api/v1/device/sos/{id}/resolve`

Status → `Resolved` (watch location update → 409). Output: SOS DTO.

**Realtime (optional):** SignalR hub group `event-{eventId}` event `sos` — không bắt buộc cho MVP API.

---

## 6. Schema gợi ý (EF schema `device`)

| Entity | Ý nghĩa |
|--------|---------|
| `DeviceAuthRequest` | deviceId, deviceCodeHash, userCode, expiresAt, approvedAt, runnerUserId?, failedAttempts, consumed |
| `LinkedDevice` | runnerUserId, clientDeviceId, name, linkedAt, revokedAt?, refreshTokenHash? |
| `SosAlert` | Id = client event id; runnerUserId; eventId?; linkedDeviceId; status Open/Acknowledged/Resolved; createdAt |
| `SosLocation` | sosAlertId, lat, lng, accuracy?, battery?, recordedAt |

*(Tên `SosAlert` tránh nhầm với MediatR/integration `IIntegrationEvent`.)*

Integration (optional): `SosOpenedIntegrationEvent` → AuditLog / push.

---

## 7. Auth matrix tóm tắt (nhóm B)

| API | Auth |
|-----|------|
| `POST …/auth/code` | Anonymous (+ rate limit) |
| `POST …/auth/token` | Anonymous (+ rate limit) |
| `GET/POST …/link/…` | JWT runner (`PublicUser`) |
| `GET/DELETE …/linked-devices` | JWT runner |
| `POST …/sos`, `…/sos/{id}/locations` | JWT **watch** scope `sos:create` |
| `GET …/events/{id}/sos`, ack, resolve | JWT organizer/staff/Admin |

---

## 8. Trạng thái code

BE Wear Device Flow + SOS **đã implement** trong `src/Modules/Device` (Swagger tag **11. Device — Wear link + SOS**).  
SignalR realtime / SMS / refresh-token rotate đầy đủ = optional follow-up.

---

## 9. Checklist FE / Wear (ngoài API)

| Bước | Ghi chú |
|------|---------|
| Wear UI SOS | Giữ 2–3s hoặc countdown 5s + Hủy |
| GPS | Gửi last-known ngay; tracking chỉ sau khi SOS mở |
| Queue offline | Retry exponential backoff |
| Next.js `/device` | 1 page + gọi link GET/approve |
| AndroidManifest | `ACCESS_FINE_LOCATION`; foreground service nếu track khi màn tắt |
| Giả định mạng | Watch chỉ BT/Wi‑Fi cần phone gần; LTE độc lập — chốt với BTC |

---

## 10. Ghi chú kiến trúc

Code nằm trong `src/Modules/Device/...` — Domain/Application/Infrastructure/Api/Contracts; Host chỉ `AddDeviceModule` / `MapDeviceEndpoints`; cross-module chỉ qua **Contracts + events** (`IRunnerRaceDayQuery`, `IEventOrganizerAccessQuery`).

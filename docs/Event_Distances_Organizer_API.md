# Event Distances API — Organizer setup & Public filter

> **Word:** `docs/Event_Distances_Organizer_API.docx` ← sync từ file này (`python docs/_event_distances_md_to_docx.py`)

Tài liệu mô tả API **cự ly + giá vé** cho trang *Thiết lập cự ly và giá vé*, và API public tối giản dùng filter/leaderboard.

Base URL (local Docker): `http://localhost:5080`

---

## 0. Vì sao không làm PUT distance riêng / PUT ticketPricing riêng?

| Ý kiến | Thực tế RaceDay |
|--------|-----------------|
| “Cần PUT update từng distance” | Đã có **một** `PUT .../organizer/{eventId}/distances` — upsert **cả list** cự ly (create / update / xóa) trong một request. |
| “Cần API update giá riêng” | Giá theo đợt (`types`) nằm trong **`ticketPricing`** gửi **cùng** PUT trên — cùng trang UI, cùng lần Lưu. |
| “Public GET thiếu giá / start time” | **Đúng chủ đích.** Public chỉ filter. Setup dùng GET/PUT **organizer**. |

**Lý do thiết kế (giải thích teammate):**

1. UI organizer là **một form**: list cự ly + setup giá theo từng cự ly → một snapshot save.
2. Key giá = **`name` cự ly** (`ticketPricing.byDistanceName["5K"]`). Sửa tên + giá trong **cùng PUT** tránh lệch key / orphan pricing.
3. Tách 2 PUT (distances vs pricing) → 2 round-trip, dễ một cái thành công một cái fail, FE phải sync thêm.
4. PATCH từng `distanceId` chỉ hợp khi sau này có màn/ quyền chỉ sửa một field — hiện chưa cần.

**Luồng UI:** `GET organizer` → đổ form → user sửa → `PUT` full `distances` + `ticketPricing` (kể cả field không đổi).

---

## 1. GET Public distances (filter / leaderboard)

| | |
|--|--|
| **Tên** | Lấy danh sách cự ly tối giản (public) |
| **Method / Path** | `GET /api/v1/event/distances?eventId={eventId}` |
| **Auth / Role** | Có thể gọi khi giải đã public (Approved / RegistrationOpen / Closed / Completed). User đăng nhập xem draft của mình / Admin qua policy view. **Không** dùng cho trang setup organizer. |
| **Rate limit** | Public API |

### Response `200` — `RaceEventDistanceDto[]`

```json
[
  {
    "id": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
    "name": "Fun Run 5K",
    "distanceKm": 5
  }
]
```

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `id` | uuid | có (response) | Id cự ly — dùng filter / leaderboard |
| `name` | string | có | Tên hiển thị filter |
| `distanceKm` | number | có | Km |

**Không trả:** `price`, `ticketPricing`, `startAt`, `endAt`, `capacity` — tránh thừa cho filter.

---

## 2. GET Organizer distances detail

| | |
|--|--|
| **Tên** | Chi tiết cự ly + ticketPricing (organizer setup) |
| **Method / Path** | `GET /api/v1/event/organizer/{eventId}/distances` |
| **Auth / Role** | Header `Authorization: Bearer {accessToken}`. Policy **`PublicUser`** (user đã login, không phải wear token). **Business:** phải là **chủ giải** (`organizerUserId`) hoặc **Admin**. |
| **Rate limit** | `authenticated` |

### Path

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `eventId` | uuid | **required** | Id giải |

### Response `200` — `OrganizerDistancesDetailDto`

```json
{
  "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "status": "draft",
  "distances": [
    {
      "id": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
      "name": "5K",
      "distanceKm": 5,
      "capacity": 1000,
      "registeredCount": 12,
      "price": 100000,
      "currency": "VND",
      "startAt": "05:30:00",
      "endAt": "09:00:00"
    }
  ],
  "ticketPricing": {
    "byDistanceName": {
      "5K": {
        "basePrice": 100000,
        "types": [
          {
            "key": "early",
            "name": "Early Bird",
            "dateFrom": "2026-01-01",
            "dateTo": "2026-03-31",
            "price": 80000
          }
        ]
      }
    }
  }
}
```

#### Root

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `eventId` | uuid | có | Id giải |
| `status` | string | có | `draft` / `pending` / `approved` / `rejected` / `registration-open` / … |
| `distances` | array | có | List cự ly (có thể `[]` nếu chưa tạo) |
| `ticketPricing` | object \| null | optional (response) | JSON giá; `null` nếu chưa setup |

#### `distances[]`

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `id` | uuid | có | **Id cự ly** (không phải `eventId`) — FE giữ để PUT update |
| `name` | string | có | Tên cự ly; khớp key trong `ticketPricing.byDistanceName` |
| `distanceKm` | number | có | Quãng đường (km) |
| `capacity` | int | có | Slot tối đa |
| `registeredCount` | int | có | Số ĐK hiện có (read-only) |
| `price` | decimal | có | Giá fallback / base khi không có type đang mở |
| `currency` | string | có | Luôn `"VND"` (nền tảng VN) |
| `startAt` | time \| null | optional | Giờ xuất phát cự ly (`HH:mm:ss`) |
| `endAt` | time \| null | optional | Giờ cutoff / kết thúc cự ly |

#### `ticketPricing.byDistanceName[name]`

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `basePrice` | number | optional | Giá gốc theo tên cự ly (ưu tiên hơn khi resolve cùng `distance.price`) |
| `types` | array | optional | Các đợt giá (Early / Late / …) — **tên tự đặt**, không hardcode |
| `types[].key` | string | optional | Mã đợt (FE thường dùng id ổn định) |
| `types[].name` | string | optional | Tên hiển thị (Early Bird, Late, …) |
| `types[].dateFrom` | date string | optional | Ngày bắt đầu (`yyyy-MM-dd`, lịch VN) |
| `types[].dateTo` | date string | optional | Ngày kết thúc |
| `types[].price` | number | optional | Giá đợt đó |

> Legacy: BE vẫn đọc `phases` nếu data cũ; FE mới **ghi `types`**.

**Resolve giá (runtime đăng ký):** type đang mở & không phải standard → standard đang mở → `basePrice` / `distance.price`.

---

## 3. PUT Organizer distances (+ ticketPricing) — upsert

| | |
|--|--|
| **Tên** | Cập nhật / thay thế danh sách cự ly + giá vé |
| **Method / Path** | `PUT /api/v1/event/organizer/{eventId}/distances` |
| **Auth / Role** | `Bearer` + policy **`PublicUser`**. **Business:** chủ giải hoặc Admin. Domain: giải phải **editable** (thường `draft` / `rejected` — theo `EnsureEditable`). |
| **Content-Type** | `application/json` |

### Path

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `eventId` | uuid | **required** | Id giải (trên URL, **không** nhầm với `distances[].id`) |

### Request body

```json
{
  "distances": [
    {
      "id": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
      "name": "5K",
      "distanceKm": 5,
      "capacity": 1000,
      "price": 100000,
      "startAt": "05:30:00",
      "endAt": "09:00:00"
    },
    {
      "name": "10K",
      "distanceKm": 10,
      "capacity": 800,
      "price": 150000
    }
  ],
  "ticketPricing": {
    "byDistanceName": {
      "5K": {
        "basePrice": 100000,
        "types": [
          {
            "key": "early",
            "name": "Early Bird",
            "dateFrom": "2026-01-01",
            "dateTo": "2026-03-31",
            "price": 80000
          },
          {
            "key": "late",
            "name": "Late",
            "dateFrom": "2026-06-01",
            "dateTo": "2026-06-30",
            "price": 120000
          }
        ]
      },
      "10K": {
        "basePrice": 150000,
        "types": []
      }
    }
  }
}
```

#### Root body

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `distances` | array | **required** (≥ 1) | Snapshot **toàn bộ** cự ly muốn giữ. Không gửi = validation lỗi. Không thể “chỉ gửi ticketPricing”. |
| `ticketPricing` | object \| omit/null | **optional** | Có object → ghi đè JSON giá. `null` / không gửi → **giữ nguyên** giá cũ. |

#### `distances[]` — từng phần tử

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `id` | uuid | **optional** | **Id cự ly.** Có + thuộc giải → **update** (giữ map/staff/reg). Không có → **create** mới. Cự ly cũ không còn trong list → **xóa** (chặn nếu đã có VĐV ĐK). **Không phải eventId.** |
| `name` | string | **required** | Tên cự ly (max 100). Phải khớp key trong `byDistanceName` nếu có setup giá. |
| `distanceKm` | number | **required** | `0.1` … `500` |
| `capacity` | int | **required** | `1` … `500_000`. Tổng capacity ≤ quy mô gói nếu đã mua package. |
| `price` | decimal | **required** | `≥ 0`. Fallback / base; thường = `basePrice` trên UI. |
| `startAt` | time string | **optional** | Giờ XP (`HH:mm:ss`). Nếu cả `startAt` + `endAt` → `endAt ≥ startAt`. |
| `endAt` | time string | **optional** | Giờ cutoff. |

**Không nhận trên request (đã bỏ / cố định server):**

- `currency` — luôn VND  
- `type` / `color` — giữ giá trị cũ khi update; mặc định khi create  
- `startLocation` / `finishLocation` / elevation — đã gỡ (map course lo tọa độ)  
- `registeredCount` — read-only  

#### `ticketPricing` (khi gửi)

| Thuộc tính | Kiểu | Bắt buộc | Giải thích |
|------------|------|----------|------------|
| `byDistanceName` | object | recommended | Map `tên cự ly` → cấu hình giá |
| `byDistanceName[name].basePrice` | number | optional | Giá gốc |
| `byDistanceName[name].types` | array | optional | Đợt giá; để `[]` nếu chỉ dùng base |
| `types[].key` | string | optional | Mã đợt |
| `types[].name` | string | optional | Tên đợt (tự đặt) |
| `types[].dateFrom` / `dateTo` | date | optional | Cửa sổ ngày |
| `types[].price` | number | optional | Giá đợt |

### Hành vi upsert (tóm tắt)

| Payload distance | Kết quả |
|------------------|---------|
| Có `id` hợp lệ | Update cự ly đó |
| Không `id` | Create cự ly mới |
| Cự ly DB không có trong list | Xóa (fail nếu đã có registration) |
| `ticketPricing` có body | Ghi đè pricing JSON |
| `ticketPricing` null/omit | Không đổi pricing |

### Response

- `200` — `RaceEventDto` (chi tiết giải sau khi lưu; FE có thể `GET organizer/distances` lại để sync form).
- Lỗi thường gặp: chưa login; không phải chủ giải; validation field; xóa cự ly đã có ĐK; tổng slot vượt gói; giải không còn editable.

---

## 4. Cách FE lấy ra UI để sửa

1. Mở trang setup → `GET /api/v1/event/organizer/{eventId}/distances`.
2. Map mỗi `distances[i]` → row form (`id` giữ làm key).
3. Map `ticketPricing.byDistanceName[row.name]` → `basePrice` + bảng `types`.
4. User sửa trên UI.
5. Lưu → build lại **full** `distances` (mọi row, kèm `id` nếu đã persist) + `ticketPricing` → `PUT` cùng path.
6. Không gửi form trống / không PATCH từng field trên server.

---

## 5. So sánh nhanh 3 endpoint

| Endpoint | Ai dùng | Có giá / giờ XP? |
|----------|---------|------------------|
| `GET /api/v1/event/distances?eventId=` | Public / filter | Không |
| `GET /api/v1/event/organizer/{eventId}/distances` | Chủ giải / Admin | Có |
| `PUT /api/v1/event/organizer/{eventId}/distances` | Chủ giải / Admin | Ghi distances + (optional) ticketPricing |

---

## 6. Sample curl (organizer)

```bash
# Đọc form
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5080/api/v1/event/organizer/$EVENT_ID/distances"

# Lưu (upsert list + giá)
curl -s -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "http://localhost:5080/api/v1/event/organizer/$EVENT_ID/distances" \
  -d @distances-put-body.json
```

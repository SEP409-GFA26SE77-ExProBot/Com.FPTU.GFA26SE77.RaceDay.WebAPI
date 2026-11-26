# RFID Phase 2 — Hướng dẫn sử dụng API

Hướng dẫn gọi API gán thẻ EPC và chuyển đầu đọc sang quét checkpoint. Phạm vi là RaceDay WebAPI, gồm các route RFID của RaceMonitoring và hai API không đổi mà luồng này dùng tiếp: `timing-reads` và `race-logs`.

JSON là **camelCase**. Lỗi nghiệp vụ và validation trả `{ "error": "…" }`. HTTP **400** là quy tắc hoặc body không hợp lệ. HTTP **409** là xung đột gán hoặc đầu đọc đang bận. HTTP **401** là thiếu hoặc sai xác thực.

Yêu cầu đọc nằm trong bộ nhớ của process API. Khởi động lại host thì yêu cầu đang mở và chế độ `await-checkpoint` mất. Dòng gán EPC trong `race_monitoring.rfid_tag_assignments` vẫn còn.

---

## 1. Chuẩn bị

### 1.1 Base URL

Gọi thẳng host API:

```text
http://localhost:5080
```

Nguồn: profile `http` trong `src/Hosts/RaceDay.Api/Properties/launchSettings.json`.

Prefix RFID: `/api/v1/race-monitoring`.

Trang demo cùng origin:

```text
http://localhost:5080/rfid-demo/
```

### 1.2 Hai loại xác thực

| Người gọi | Header | Ai được vào |
| --- | --- | --- |
| Organizer | `Authorization: Bearer {accessToken}` | JWT IAM, policy `PublicUser`. Handler chỉ cho admin hoặc `OrganizerUserId` của đúng giải. |
| Gateway | `X-Service-Key: {serviceKey}` | Khớp `RfidGateway:ServiceKey` của API. Route gateway là `AllowAnonymous`. |

`{serviceKey}` phải trùng `Api:ServiceKey` của `RaceDay.RfidGateway`. Trang demo không nhận và không gửi service key.

Token Wear (`token_use=wear`) không dùng cho các route Organizer trong tài liệu này.

### 1.3 Biến

```text
{baseUrl}          = http://localhost:5080
{accessToken}      = accessToken từ POST /api/v1/iam/auth/login
{serviceKey}       = RfidGateway:ServiceKey
{eventId}          = GUID giải
{registrationId}   = GUID đăng ký Confirmed, có BIB, thuộc đúng giải
{readerId}         = ReaderId trong RfidGateway:Readers của đúng giải
{requestId}        = GUID yêu cầu đọc, server tạo khi POST read-requests
{tagId}            = EPC hex. Server bỏ khoảng trắng và dấu :, viết hoa
```

Đầu đọc phải có đúng một dòng cấu hình: `ReaderId` khớp, `EventId` khớp giải đang chọn, `CheckpointId` khác GUID rỗng. Nhiều checkpoint cho cùng reader trả lỗi.

### 1.4 Lấy token, giải, đăng ký và đầu đọc

Đăng nhập tài khoản chủ giải. Seed local trong README: username `cuong.le`, mật khẩu `MatKhau@123`.

```http
POST {baseUrl}/api/v1/iam/auth/login
Content-Type: application/json

{
  "username": "cuong.le",
  "password": "MatKhau@123"
}
```

Response `200 OK`:

```json
{
  "userId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "email": "cuong.le@raceday.vn",
  "username": "cuong.le",
  "fullName": "Lê Hoàng Cường",
  "role": "Organizer",
  "accessToken": "<jwt>",
  "mustChangePassword": false
}
```

Các gọi Organizer phía dưới dùng:

```http
Authorization: Bearer {accessToken}
Accept: application/json
```

Lấy giải của tài khoản đang đăng nhập:

```http
GET {baseUrl}/api/v1/event/organizer/mine
Authorization: Bearer {accessToken}
```

Mỗi phần tử có `id` và `name`. `id` là `{eventId}`.

Lấy đăng ký của giải:

```http
GET {baseUrl}/api/v1/event/organizer/{eventId}/registrations
Authorization: Bearer {accessToken}
```

Chỉ chọn dòng `status` là `Confirmed` và `bibNumber` khác rỗng. `id` của dòng đó là `{registrationId}`. `distanceLabel` và `distanceKm` là cự ly hiển thị. `checkedInAtUtc` khác `null` nghĩa là đã check-in.

Lấy đầu đọc đã cấu hình cho giải:

```http
GET {baseUrl}/api/v1/race-monitoring/rfid/readers?eventId={eventId}
Authorization: Bearer {accessToken}
```

`readerId` trong response là giá trị đưa vào các lệnh RFID. Response không có service key.

### 1.5 Trang demo

Mở `http://localhost:5080/rfid-demo/`, đăng nhập, chọn giải, đầu đọc và một đăng ký Confirmed có BIB. Trang hiện BIB và cự ly.

1. **Đọc thẻ** tạo yêu cầu và poll trạng thái mỗi 1,2 giây.
2. Khi trạng thái là `Observed` và chỉ có một EPC, **Xác nhận gán** mới bật.
3. **Hủy yêu cầu** chỉ bật khi yêu cầu chưa kết thúc.
4. Sau khi xác nhận, lấy thẻ ra khỏi vùng đọc, check-in và phát BIB trên UserWeb, rồi bấm **Bắt đầu quét checkpoint** và đưa thẻ lại.
5. **Xem timeline** gọi race-logs theo BIB đang chọn. Sau khi bắt đầu quét, trang tải lại timeline mỗi 3 giây.

Token demo nằm trong `sessionStorage` của trình duyệt, khóa `raceday-rfid-demo-token`.

---

## 2. Luồng gọi

Một reader chỉ có một yêu cầu chưa kết thúc. Thời hạn yêu cầu là 60 giây, tính từ lúc tạo. Hết hạn chuyển `Expired` ở lần đọc hoặc ghi tiếp theo.

1. Organizer **POST** `/rfid/read-requests` với `eventId`, `registrationId`, `readerId`. Trạng thái đầu là `Pending`.
2. Gateway **GET** `/rfid/read-requests/next?readerId=…`. Lệnh `assign` đổi `Pending` thành `Reading`.
3. Gateway **POST** `/rfid/read-requests/{requestId}/observations`. Không EPC là `NoTag`, một EPC là `Observed`, nhiều EPC khác nhau là `Ambiguous`. Bước này không gọi `timing-reads` và không tạo timeline.
4. Organizer **GET** `/rfid/read-requests/{requestId}` cho đến khi `Observed` và `tagIds` có đúng một EPC.
5. Organizer **POST** `/rfid/assignments` với `requestId` và EPC đó. `created: true` là dòng mới. `created: false` là cùng cặp đã có.
6. Gateway poll tiếp và nhận `command: "hold"` cho đến khi Organizer bấm bắt đầu quét.
7. Organizer **POST** `/rfid/readers/{readerId}/checkpoint-scan`.
8. Lần poll `checkpoint` đầu trả `releaseTagIds` rồi xóa danh sách đó. Gateway dùng danh sách này để bỏ cửa sổ lọc 15 giây của lượt gán, nên EPC vừa gán đi tới `timing-reads` ngay.
9. Gateway **POST** `/timing-reads`. Timeline đã lưu đọc bằng **GET** race-logs.

Lượt đọc để gán và lượt quét checkpoint là hai đường. Chỉ lượt checkpoint tạo checkpoint scan và dòng timeline.

---

## 3. API Organizer

Mọi route dưới đây cần JWT mục 1.2. Path `{requestId}` không phải GUID thì routing trả **404** trước handler.

| Method | Path | Body / query | Khi gọi | Thành công |
| --- | --- | --- | --- | --- |
| POST | `/rfid/read-requests` | `{ eventId, registrationId, readerId }` | Bắt đầu đọc thẻ để gán | **200** yêu cầu `Pending` |
| GET | `/rfid/read-requests/{requestId}` | — | Poll trạng thái | **200** yêu cầu hiện tại |
| POST | `/rfid/read-requests/{requestId}/cancel` | — | Người tạo hủy khi chưa `Confirmed` | **200** yêu cầu `Cancelled`, hoặc snapshot nếu đã `Cancelled` / `Expired` |
| POST | `/rfid/assignments` | `{ requestId, tagId }` | Chỉ khi `Observed` và đúng một EPC | **200** dòng gán |
| GET | `/rfid/assignments` | `eventId`, `registrationId` | Xem EPC đã lưu | **200** dòng gán. **404** khi chưa gán |
| GET | `/rfid/readers` | `eventId` | Chọn đầu đọc | **200** mảng reader |
| POST | `/rfid/readers/{readerId}/checkpoint-scan` | — | Sau khi đã xác nhận gán | **200** mode `checkpoint-scan` |

### 3.1 Tạo yêu cầu đọc

```http
POST {baseUrl}/api/v1/race-monitoring/rfid/read-requests
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo"
}
```

| Field | Type | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `eventId` | GUID | Có | Giải mà user là chủ, hoặc user là admin |
| `registrationId` | GUID | Có | Đăng ký thuộc giải, `Confirmed`, có BIB |
| `readerId` | string | Có | Đầu đọc đã gắn một checkpoint của giải này |

Response `200 OK`:

```json
{
  "requestId": "33333333-3333-4333-8333-333333333333",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo",
  "status": "Pending",
  "tagIds": [],
  "expiresAtUtc": "2026-11-24T05:09:00Z",
  "createdByUserId": "dddddddd-dddd-dddd-dddd-dddddddddddd"
}
```

`expiresAtUtc` là 60 giây sau lúc tạo. Reader đã có yêu cầu `Pending`, `Reading`, `Observed`, `Ambiguous` hoặc `NoTag` thì request mới trả **409**:

```json
{ "error": "Đầu đọc đang có yêu cầu đọc chưa kết thúc." }
```

### 3.2 Xem trạng thái yêu cầu

```http
GET {baseUrl}/api/v1/race-monitoring/rfid/read-requests/{requestId}
Authorization: Bearer {accessToken}
```

Response `200 OK` cùng dạng mục 3.1. `tagIds` là EPC đã chuẩn hóa của lần observation gần nhất. Không tìm thấy yêu cầu:

```json
{ "error": "Không tìm thấy yêu cầu đọc." }
```

HTTP **400**. User không phải chủ giải của yêu cầu:

```json
{ "error": "Bạn không phải chủ giải này." }
```

### 3.3 Hủy yêu cầu

```http
POST {baseUrl}/api/v1/race-monitoring/rfid/read-requests/{requestId}/cancel
Authorization: Bearer {accessToken}
```

Không có body. Chỉ `createdByUserId` của yêu cầu được hủy, và user đó phải là chủ giải hoặc admin. Đã `Cancelled` hoặc `Expired` thì trả snapshot hiện tại. Đã `Confirmed`:

```json
{ "error": "Yêu cầu đã xác nhận, không hủy được." }
```

Người khác tạo yêu cầu:

```json
{ "error": "Chỉ người tạo yêu cầu mới hủy được." }
```

Response khi hủy được, `200 OK`:

```json
{
  "requestId": "33333333-3333-4333-8333-333333333333",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo",
  "status": "Cancelled",
  "tagIds": [],
  "expiresAtUtc": "2026-11-24T05:09:00Z",
  "createdByUserId": "dddddddd-dddd-dddd-dddd-dddddddddddd"
}
```

Sau `Cancelled` hoặc `Expired`, reader nhận yêu cầu mới.

### 3.4 Xác nhận gán EPC

Chỉ gọi khi GET yêu cầu trả `status: "Observed"` và `tagIds` có đúng một phần tử. `tagId` trong body phải là EPC đó.

```http
POST {baseUrl}/api/v1/race-monitoring/rfid/assignments
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "requestId": "33333333-3333-4333-8333-333333333333",
  "tagId": "E20000112233445566778899"
}
```

| Field | Type | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `requestId` | GUID | Có | Yêu cầu đang `Observed` |
| `tagId` | string | Có | Đúng EPC duy nhất của yêu cầu. `E200 0011…` và `e200:0011…` được chuẩn hóa thành cùng một chuỗi |

EPC hợp lệ là hex, độ dài chẵn, từ 4 ký tự trở lên, sau khi bỏ khoảng trắng và dấu `:`. Scheme lưu luôn là `EPC`.

Response `200 OK` khi tạo dòng mới:

```json
{
  "assignmentId": "44444444-4444-4444-8444-444444444444",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "tagScheme": "EPC",
  "tagId": "E20000112233445566778899",
  "assignedByUserId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "assignedAtUtc": "2026-11-24T05:08:12Z",
  "created": true
}
```

Gửi lại cùng cặp giải + đăng ký + EPC trả dòng hiện có, `created: false`, và yêu cầu vẫn `Confirmed`. Không tạo dòng thứ hai.

Xung đột là HTTP **409**, body `{ "error": "…" }`:

| `error` | Khi nào |
| --- | --- |
| `EPC đã được gán cho đăng ký khác trong giải này.` | EPC đã thuộc đăng ký khác trong DB |
| `Đăng ký đã được gán một EPC khác trong giải này.` | Đăng ký đã có EPC khác trong DB |
| `EPC này đã được gán cho vận động viên khác trong cấu hình đầu đọc.` | `RfidGateway:Tags` đã map EPC này sang đăng ký khác |
| `Đăng ký này đã được gán EPC khác trong cấu hình đầu đọc.` | `RfidGateway:Tags` đã map đăng ký này sang EPC khác |
| `EPC hoặc đăng ký đã được gán trong giải này.` | Hai request cùng lúc đụng unique index |

Cùng cặp trong `RfidGateway:Tags` được ghi vào DB. Kiểm tra cấu hình đi cả hai chiều trước khi insert. Lệnh này không tạo checkpoint scan.

Chưa `Observed`, nhiều EPC, EPC không khớp, đã hủy, đã hết hạn, hoặc đã xác nhận bằng EPC khác:

```json
{ "error": "Chỉ xác nhận khi yêu cầu đọc được đúng một EPC." }
```

Các câu **400** khác của bước này: `Yêu cầu đọc đã xác nhận.`, `Yêu cầu đọc đã hủy.`, `Yêu cầu đọc đã hết hạn.`, `EPC không hợp lệ.`, `Chỉ gán thẻ cho đăng ký Confirmed.`, `Đăng ký chưa có số BIB.`, `Đăng ký không thuộc giải này.`

### 3.5 Xem EPC đã gán

```http
GET {baseUrl}/api/v1/race-monitoring/rfid/assignments?eventId={eventId}&registrationId={registrationId}
Authorization: Bearer {accessToken}
```

Response `200 OK` cùng dạng mục 3.4, với `created: false`.

Chưa có dòng:

```json
{ "error": "Chưa gán EPC cho đăng ký này." }
```

HTTP **404**.

### 3.6 Danh sách đầu đọc

```http
GET {baseUrl}/api/v1/race-monitoring/rfid/readers?eventId={eventId}
Authorization: Bearer {accessToken}
```

Response `200 OK`:

```json
[
  {
    "readerId": "ypd-202-demo",
    "eventId": "11111111-1111-4111-8111-111111111111",
    "checkpointId": "f1d10000-0000-4000-8000-000000000001",
    "mode": "checkpoint-scan"
  }
]
```

`mode` là `assign` khi reader đang có yêu cầu chưa kết thúc, `await-checkpoint` sau khi gán đã `Confirmed` và chưa bấm quét, `checkpoint-scan` khi reader đang ở chế độ gửi `timing-reads`. Mảng rỗng nghĩa là giải chưa có reader hợp lệ trong cấu hình.

### 3.7 Bắt đầu quét checkpoint

```http
POST {baseUrl}/api/v1/race-monitoring/rfid/readers/{readerId}/checkpoint-scan
Authorization: Bearer {accessToken}
```

`{readerId}` là chuỗi, cần encode khi có ký tự đặc biệt. Không có body. User phải là chủ giải của reader đó.

Response `200 OK`:

```json
{
  "readerId": "ypd-202-demo",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "mode": "checkpoint-scan"
}
```

Reader chưa gắn checkpoint:

```json
{ "error": "Đầu đọc chưa được gán checkpoint." }
```

Reader gắn nhiều checkpoint:

```json
{ "error": "Đầu đọc được gán nhiều hơn một checkpoint." }
```

Sau lệnh này, poll của gateway chuyển từ `hold` sang `checkpoint`. Lần `checkpoint` đầu mang `releaseTagIds` của EPC lượt gán.

---

## 4. API Gateway

Hai route này dùng `X-Service-Key`. Sai hoặc thiếu key trả **401**, không có JSON nghiệp vụ.

Gateway kéo lệnh. Organizer không gọi hai route này.

| Method | Path | Body / query | Kết quả |
| --- | --- | --- | --- |
| GET | `/rfid/read-requests/next` | `readerId`, `requestId?` | `{ command, requestId, status, eventId, registrationId, readerId, releaseTagIds }` |
| POST | `/rfid/read-requests/{requestId}/observations` | `{ readerId, epcs }` | Cập nhật yêu cầu. Không ghi `timing-reads`, không tạo timeline |

`command` chỉ là `assign`, `hold` hoặc `checkpoint`.

### 4.1 Kéo lệnh

```http
GET {baseUrl}/api/v1/race-monitoring/rfid/read-requests/next?readerId=ypd-202-demo&requestId={requestId}
X-Service-Key: {serviceKey}
Accept: application/json
```

`requestId` là tùy chọn. Gửi id yêu cầu vừa xử lý để server giữ ngữ cảnh reader khi không còn yêu cầu đang hoạt động.

Đang có yêu cầu của đúng reader, `200 OK`:

```json
{
  "command": "assign",
  "requestId": "33333333-3333-4333-8333-333333333333",
  "status": "Reading",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo",
  "releaseTagIds": []
}
```

Lần poll này nhận yêu cầu `Pending` và đổi thành `Reading` trong cùng một khóa. Observation gửi khi còn `Pending` bị từ chối.

Sau `Confirmed`, trước checkpoint-scan:

```json
{
  "command": "hold",
  "requestId": "33333333-3333-4333-8333-333333333333",
  "status": "Confirmed",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo",
  "releaseTagIds": ["E20000112233445566778899"]
}
```

`hold` nghĩa là chưa gửi `timing-reads`. `releaseTagIds` vẫn nằm trong poll `hold`.

Sau checkpoint-scan, lần poll đầu:

```json
{
  "command": "checkpoint",
  "requestId": "33333333-3333-4333-8333-333333333333",
  "status": "Confirmed",
  "eventId": "11111111-1111-4111-8111-111111111111",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "readerId": "ypd-202-demo",
  "releaseTagIds": ["E20000112233445566778899"]
}
```

Server xóa `releaseTagIds` sau lần trả `checkpoint` này. Poll `checkpoint` sau đó có `releaseTagIds: []`. Gateway xóa các EPC đó khỏi cửa sổ lọc 15 giây rồi mới gửi `timing-reads`.

Không có yêu cầu và reader không ở `await-checkpoint` thì `command` vẫn là `checkpoint`. `requestId`, `status`, `eventId`, `registrationId` có thể `null` nếu reader chưa từng có yêu cầu trong process hiện tại.

### 4.2 Nộp EPC của lượt gán

```http
POST {baseUrl}/api/v1/race-monitoring/rfid/read-requests/{requestId}/observations
X-Service-Key: {serviceKey}
Content-Type: application/json

{
  "readerId": "ypd-202-demo",
  "epcs": [
    {
      "tagId": "E20000112233445566778899",
      "observedAtUtc": "2026-11-24T05:08:10Z"
    }
  ]
}
```

| Field | Type | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `readerId` | string | Có | Phải trùng reader của yêu cầu |
| `epcs` | array | Không | Bỏ trống hoặc `[]` là không thấy thẻ |
| `epcs[].tagId` | string | Có, nếu có phần tử | EPC. Trùng nhau sau chuẩn hóa chỉ tính một lần |
| `epcs[].observedAtUtc` | ISO-8601 UTC | Có, nếu có phần tử | Thời điểm quan sát. Không được là giá trị mặc định |

Response `200 OK` là yêu cầu sau khi cập nhật.

| Số EPC khác nhau | `status` | `tagIds` |
| --- | --- | --- |
| 0 | `NoTag` | `[]` |
| 1 | `Observed` | một EPC |
| từ 2 | `Ambiguous` | mọi EPC đã chuẩn hóa |

Gửi observation mới khi yêu cầu còn `Reading`, `Observed`, `NoTag` hoặc `Ambiguous` thì thay `tagIds` và trạng thái bằng kết quả mới.

| Tình huống | HTTP | `error` |
| --- | --- | --- |
| `readerId` không phải reader của yêu cầu | 400 | `Yêu cầu đọc không thuộc đầu đọc này.` |
| Còn `Pending`, gateway chưa poll `next` | 400 | `Đầu đọc chưa nhận yêu cầu đọc.` |
| `Confirmed`, `Cancelled` hoặc `Expired` | 400 | `Yêu cầu đọc không còn nhận kết quả.` |
| Thiếu `tagId` hoặc `observedAtUtc` trong một phần tử | 400 | `Mã thẻ không hợp lệ.` hoặc `Thời điểm quan sát không hợp lệ.` |
| EPC không phải hex hợp lệ | 400 | `EPC không hợp lệ.` |

---

## 5. API không đổi, dùng sau khi quét

Contract hai API này không đổi ở phase 2. Chúng chỉ chạy sau khi reader đã ở `checkpoint`.

### 5.1 Gửi timing read

Gateway gọi. Header `X-Service-Key`.

```http
POST {baseUrl}/api/v1/race-monitoring/timing-reads
X-Service-Key: {serviceKey}
Content-Type: application/json

{
  "readId": "55555555-5555-4555-8555-555555555555",
  "readerId": "ypd-202-demo",
  "tagScheme": "EPC",
  "tagId": "E20000112233445566778899",
  "observedAtUtc": "2026-11-24T05:12:00Z",
  "timestampSource": "GatewayClock"
}
```

| Field | Type | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `readId` | GUID | Có | Id một lượt đọc. Gửi lại cùng id trả `Duplicate` |
| `readerId` | string | Có | Reader trong `RfidGateway:Readers`, đúng một checkpoint |
| `tagScheme` | string | Có | `EPC` |
| `tagId` | string | Có | EPC đã gán |
| `observedAtUtc` | ISO-8601 UTC | Có | Mốc quan sát |
| `timestampSource` | string | Có | Gateway thật gửi `GatewayClock`. Nguồn test gửi `TestClock` |

Resolver lấy checkpoint từ cấu hình reader. EPC có dòng trong `rfid_tag_assignments` của đúng giải thì dùng dòng đó ngay sau khi lưu. Chưa có dòng thì dùng `RfidGateway:Tags`.

Response `200 OK` khi ghi mới:

```json
{
  "outcome": "Accepted",
  "readId": "55555555-5555-4555-8555-555555555555",
  "scanId": "55555555-5555-4555-8555-555555555555",
  "raceEventLogId": "66666666-6666-4666-8666-666666666666",
  "sequence": 12,
  "eventId": "11111111-1111-4111-8111-111111111111",
  "checkpointId": "f1d10000-0000-4000-8000-000000000001",
  "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"
}
```

Cùng `readId` trả `outcome: "Duplicate"` và không tạo dòng timeline thứ hai. `scanId` của lượt RFID là chính `readId`. API này không ghi passage chính thức và không gọi Start/Finish của module Event.

Thẻ chưa có trong DB và cũng không có trong cấu hình:

```json
{ "error": "Thẻ chưa được gán cho vận động viên trong phạm vi giải này." }
```

### 5.2 Đọc timeline

Organizer hoặc Race-staff Active của giải. Policy là `OrganizerOrRaceStaff`. Admin không đi tắt route này. Trang demo gọi bằng JWT chủ giải và lọc theo BIB.

```http
GET {baseUrl}/api/v1/race-monitoring/events/{eventId}/race-logs?bibNumber=A001&page=1&pageSize=20
Authorization: Bearer {accessToken}
Accept: application/json
```

Query tùy chọn: `eventDistanceId`, `eventType`, `bibNumber`, `fromUtc`, `toUtc`, `afterSequence`, `page`, `pageSize`. `pageSize` ngoài 1–100 thì server dùng 20. Thứ tự là `occurredAtUtc` rồi `sequence`.

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "66666666-6666-4666-8666-666666666666",
      "sequence": 12,
      "eventId": "11111111-1111-4111-8111-111111111111",
      "eventDistanceId": "22222222-2222-4222-8222-222222222222",
      "eventType": "RunnerCheckpointPassed",
      "occurredAtUtc": "2026-11-24T05:12:00Z",
      "recordedAtUtc": "2026-11-24T05:12:01Z",
      "bibNumber": "A001",
      "checkpointId": "f1d10000-0000-4000-8000-000000000001",
      "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "sourceType": "CheckpointScan",
      "sourceId": "55555555-5555-4555-8555-555555555555",
      "scanMethod": "RFID"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 20
}
```

`eventType` của lượt qua checkpoint là `RunnerCheckpointPassed`. `scanMethod` của lượt RFID là `RFID`.

Chi tiết một dòng:

```http
GET {baseUrl}/api/v1/race-monitoring/events/{eventId}/race-logs/{logId}
Authorization: Bearer {accessToken}
```

Log phải thuộc đúng `eventId`. Không có quyền:

```json
{ "error": "Chỉ chủ giải hoặc Race-staff Active mới xem timeline." }
```

Theo dõi realtime, nếu client cần stream:

```http
GET {baseUrl}/api/v1/race-monitoring/events/{eventId}/race-logs/stream
Authorization: Bearer {accessToken}
Accept: text/event-stream
```

Event tên `RaceEventLogCreated`. Không gửi `Last-Event-ID` thì chỉ nhận log mới từ lúc kết nối.

---

## 6. Trạng thái, lệnh và mode

Trạng thái yêu cầu đọc:

| Status | Ý nghĩa | Việc client làm |
| --- | --- | --- |
| `Pending` | Đã tạo, gateway chưa nhận | Gateway poll `next`. Chưa nộp observation |
| `Reading` | Gateway đã nhận lệnh `assign` | Nộp observation |
| `Observed` | Đúng một EPC | Organizer xác nhận bằng EPC đó |
| `Ambiguous` | Từ hai EPC | Đưa một thẻ vào vùng đọc và nộp lại, hoặc hủy |
| `NoTag` | Không thấy EPC | Đưa thẻ vào vùng đọc và nộp lại, hoặc hủy |
| `Confirmed` | Đã xác nhận gán | Poll trả `hold` cho đến checkpoint-scan |
| `Cancelled` | Người tạo đã hủy | Tạo yêu cầu mới nếu cần |
| `Expired` | Quá 60 giây khi còn chưa kết thúc | Tạo yêu cầu mới |

`Observed`, `Ambiguous` và `NoTag` vẫn là yêu cầu đang mở: reader chưa nhận yêu cầu thứ hai, và gateway vẫn nộp observation mới được. `Confirmed`, `Cancelled`, `Expired` là trạng thái kết thúc.

Lệnh gateway nhận từ poll:

| `command` | Gateway làm gì |
| --- | --- |
| `assign` | Đọc EPC và POST observations. Không POST `timing-reads` |
| `hold` | Giữ, không gửi `timing-reads`, chờ Organizer bấm quét |
| `checkpoint` | Xóa cửa sổ lọc theo `releaseTagIds` ở lần đầu, rồi POST `timing-reads` |

`mode` trên GET readers và POST checkpoint-scan:

| `mode` | Khi nào |
| --- | --- |
| `assign` | Reader đang có yêu cầu chưa kết thúc |
| `await-checkpoint` | Yêu cầu vừa `Confirmed`, chưa bấm quét |
| `checkpoint-scan` | Reader đang được phép gửi `timing-reads` |

---

## 7. Dữ liệu gán được lưu

Bảng `race_monitoring.rfid_tag_assignments`:

| Cột | Ý nghĩa |
| --- | --- |
| `Id` | `assignmentId` trong response |
| `EventId` | Giải |
| `RegistrationId` | Đăng ký |
| `TagScheme` | Luôn `EPC` |
| `TagId` | EPC đã chuẩn hóa |
| `AssignedByUserId` | User xác nhận |
| `AssignedAtUtc` | UTC lúc lưu |

Unique theo `(EventId, TagScheme, TagId)` và `(EventId, RegistrationId)`. Không có khóa ngoại sang schema event. Một EPC và một đăng ký mỗi giải.

---

## 8. Lỗi thường gặp

| HTTP | `error` hoặc tình huống |
| --- | --- |
| 401 | Thiếu JWT, JWT hết hạn, hoặc `X-Service-Key` không khớp |
| 400 | `Cần đăng nhập.` |
| 400 | `Bạn không phải chủ giải này.` |
| 400 | `Không tìm thấy giải.` |
| 400 | `Không tìm thấy yêu cầu đọc.` |
| 400 | `Tham chiếu giải không hợp lệ.` / `Tham chiếu đăng ký không hợp lệ.` / `Mã đầu đọc không hợp lệ.` |
| 400 | `Đăng ký không thuộc giải này.` |
| 400 | `Chỉ gán thẻ cho đăng ký Confirmed.` |
| 400 | `Đăng ký chưa có số BIB.` |
| 400 | `Đầu đọc chưa được gán cho giải này.` |
| 400 | `Đầu đọc chưa được gán checkpoint.` |
| 400 | `Đầu đọc được gán nhiều hơn một checkpoint.` |
| 400 | `EPC không hợp lệ.` |
| 400 | `Chỉ xác nhận khi yêu cầu đọc được đúng một EPC.` |
| 400 | `Yêu cầu đã xác nhận, không hủy được.` |
| 400 | `Chỉ người tạo yêu cầu mới hủy được.` |
| 400 | `Yêu cầu đọc không thuộc đầu đọc này.` |
| 400 | `Đầu đọc chưa nhận yêu cầu đọc.` |
| 400 | `Yêu cầu đọc không còn nhận kết quả.` |
| 400 | `Thẻ chưa được gán cho vận động viên trong phạm vi giải này.` |
| 400 | `Chỉ chủ giải hoặc Race-staff Active mới xem timeline.` |
| 404 | GET assignments khi đăng ký chưa có EPC: `Chưa gán EPC cho đăng ký này.` |
| 404 | `{requestId}` hoặc `{eventId}` trên path không phải GUID |
| 409 | `Đầu đọc đang có yêu cầu đọc chưa kết thúc.` |
| 409 | EPC hoặc đăng ký đã thuộc cặp khác, trong DB hoặc trong `RfidGateway:Tags` |

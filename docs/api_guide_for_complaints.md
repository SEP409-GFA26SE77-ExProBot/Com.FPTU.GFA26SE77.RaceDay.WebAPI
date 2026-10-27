# Hướng dẫn Sử dụng 5 API Khiếu Nại (Complaint Management) trong RaceDay

Hệ thống hỗ trợ quy trình khiếu nại hoàn chỉnh giữa **VĐV (Runner)** và **Ban Tổ Chức (Organizer / Admin)** với 3 trạng thái chính: `Pending` (Chưa xử lý), `Accepted` (Đã chấp nhận) và `Rejected` (Đã từ chối).

---

## Tổng quan 5 API

| STT | Phương thức | Đường dẫn Endpoint | Quyền hạn (Auth) | Mô tả ngắn |
| :--- | :--- | :--- | :--- | :--- |
| **1** | `GET` | `/api/v1/event/organizer/complaints` | Organizer / Admin | Xem danh sách khiếu nại (có lọc & phân trang) |
| **2** | `POST` | `/api/v1/event/organizer/complaints/{complaintId}/reply` | Organizer / Admin | Phản hồi và ra quyết định (Accepted / Rejected) |
| **3** | `POST` | `/api/v1/event/complaints` | VĐV (Runner) | Gửi đơn khiếu nại mới kèm link bằng chứng |
| **4** | `GET` | `/api/v1/event/complaints/mine` | VĐV (Runner) | Xem danh sách các đơn khiếu nại của chính mình |
| **5** | `GET` | `/api/v1/event/complaints/detail?complaintId={id}` | Người gửi / Chủ giải / Admin | Xem chi tiết khiếu nại và phản hồi |

---

## Chi tiết từng API

---

### 1. API — Organizer xem danh sách đơn khiếu nại
* **Method:** `GET`
* **Endpoint:** `/api/v1/event/organizer/complaints`
* **Auth:** Bắt buộc (`Authorization: Bearer <accessToken>`) — Phải là Organizer của giải hoặc Admin.
* **Mô tả:** Cho phép Organizer/Admin xem, tìm kiếm và lọc danh sách khiếu nại thuộc các sự kiện họ quản lý (Admin để trống `eventId` sẽ xem được tất cả khiếu nại trong hệ thống).

#### Query Parameters (Tùy chọn):
| Tham số | Kiểu dữ liệu | Mặc định | Mô tả |
| :--- | :--- | :--- | :--- |
| `eventId` | Guid | `null` | Lọc theo ID cụ thể của giải đấu |
| `status` | string | `null` | Lọc theo trạng thái: `Pending`, `Accepted`, `Rejected` |
| `fromDate` | DateTime | `null` | Lọc từ ngày gửi (UTC) |
| `toDate` | DateTime | `null` | Lọc đến ngày gửi (UTC) |
| `page` | int | `1` | Số trang hiện tại |
| `pageSize` | int | `10` | Số lượng bản ghi mỗi trang (tối đa 100) |

#### Request Mẫu:
```http
GET /api/v1/event/organizer/complaints?status=Pending&page=1&pageSize=10
Authorization: Bearer eyJhbGciOi...
```

#### Response Mẫu (`200 OK`):
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "eventName": "VnExpress Marathon Quy Nhon 2026",
      "distanceId": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
      "distanceName": "City 10K",
      "userId": "35bd0476-8072-47fa-a982-7d31d9c13c2b",
      "title": "Chưa xác nhận thanh toán lệ phí",
      "content": "Tôi đã thanh toán nhưng hệ thống vẫn báo pending_payment.",
      "evidenceLinks": [
        "https://drive.google.com/file/d/example-proof-id/view"
      ],
      "status": "Pending",
      "processingStatus": "Unprocessed",
      "responseContent": null,
      "createdAtUtc": "2026-10-27T07:00:00Z",
      "repliedAtUtc": null
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "pendingCount": 1
}
```

---

### 2. API — Organizer phản hồi và quyết định khiếu nại
* **Method:** `POST`
* **Endpoint:** `/api/v1/event/organizer/complaints/{complaintId}/reply`
* **Auth:** Bắt buộc — Phải là Organizer sở hữu giải hoặc Admin.
* **Mô tả:** Organizer tiến hành xem xét đơn, viết nội dung phản hồi và đưa ra quyết định chấp nhận (`Accepted`) hoặc từ chối (`Rejected`). Đơn đã xử lý rồi sẽ không thể phản hồi lại lần nữa.

#### Request Body (`application/json`):
```json
{
  "responseContent": "Ban tổ chức đã kiểm tra sao kê ngân hàng và xác nhận thanh toán của bạn.",
  "decision": "Accepted"
}
```
* **Lưu ý quy tắc:** 
  * `responseContent`: Bắt buộc, tối đa 2000 ký tự.
  * `decision`: Chỉ chấp nhận đúng 2 giá trị chuỗi là `"Accepted"` hoặc `"Rejected"`.

#### Response Mẫu (`200 OK`):
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "eventName": "VnExpress Marathon Quy Nhon 2026",
  "distanceId": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
  "distanceName": "City 10K",
  "userId": "35bd0476-8072-47fa-a982-7d31d9c13c2b",
  "title": "Chưa xác nhận thanh toán lệ phí",
  "content": "Tôi đã thanh toán nhưng hệ thống vẫn báo pending_payment.",
  "evidenceLinks": [
    "https://drive.google.com/file/d/example-proof-id/view"
  ],
  "status": "Accepted",
  "processingStatus": "Processed",
  "responseContent": "Ban tổ chức đã kiểm tra sao kê ngân hàng và xác nhận thanh toán của bạn.",
  "createdAtUtc": "2026-10-27T07:00:00Z",
  "repliedAtUtc": "2026-10-27T07:30:00Z"
}
```

---

### 3. API — VĐV gửi đơn khiếu nại mới
* **Method:** `POST`
* **Endpoint:** `/api/v1/event/complaints`
* **Auth:** Bắt buộc (User / Runner đã đăng nhập).
* **Mô tả:** VĐV tạo một đơn khiếu nại mới gắn liền với một sự kiện và cự ly cụ thể, bao gồm tiêu đề, nội dung chi tiết và danh sách link bằng chứng (ví dụ link Google Drive).

#### Request Body (`application/json`):
```json
{
  "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "distanceId": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
  "title": "Sai sót kết quả thời gian chạy",
  "content": "Đồng hồ GPS của tôi ghi nhận thời gian là 45 phút nhưng trên bảng xếp hạng hiển thị sai.",
  "evidenceLinks": [
    "https://drive.google.com/file/d/strava-screenshot-id/view",
    "https://drive.google.com/file/d/bib-photo-id/view"
  ]
}
```
* **Lưu ý quy tắc:**
  * `eventId`, `distanceId`: Bắt buộc và cự ly phải thuộc đúng giải đấu đó.
  * `title`: Bắt buộc, tối đa 200 ký tự.
  * `content`: Bắt buộc, tối đa 2000 ký tự.
  * `evidenceLinks`: Bắt buộc có từ 1 đến 5 link HTTP/HTTPS hợp lệ. Form hiển thị 5 ô, ô đầu tiên bắt buộc.

#### Response Mẫu (`201 Created`):
```json
{
  "id": "7bc91e42-1234-4abc-9def-123456789abc",
  "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "eventName": "VnExpress Marathon Quy Nhon 2026",
  "distanceId": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
  "distanceName": "City 10K",
  "userId": "35bd0476-8072-47fa-a982-7d31d9c13c2b",
  "title": "Sai sót kết quả thời gian chạy",
  "content": "Đồng hồ GPS của tôi ghi nhận thời gian là 45 phút nhưng trên bảng xếp hạng hiển thị sai.",
  "evidenceLinks": [
    "https://drive.google.com/file/d/strava-screenshot-id/view",
    "https://drive.google.com/file/d/bib-photo-id/view"
  ],
  "status": "Pending",
  "processingStatus": "Unprocessed",
  "responseContent": null,
  "createdAtUtc": "2026-10-27T08:00:00Z",
  "repliedAtUtc": null
}
```

---

### 4. API — VĐV xem danh sách khiếu nại của chính mình
* **Method:** `GET`
* **Endpoint:** `/api/v1/event/complaints/mine`
* **Auth:** Bắt buộc (User / Runner đã đăng nhập).
* **Mô tả:** Giúp VĐV theo dõi lịch sử tất cả các đơn khiếu nại họ đã gửi, xem xét trạng thái xử lý hiện tại và nội dung phản hồi từ Ban Tổ Chức.

#### Query Parameters (Tùy chọn):
| Tham số | Kiểu dữ liệu | Mặc định | Mô tả |
| :--- | :--- | :--- | :--- |
| `eventId` | Guid | `null` | Lọc theo giải đấu cụ thể |
| `status` | string | `null` | Lọc theo trạng thái: `Pending`, `Accepted`, `Rejected` |
| `page` | int | `1` | Trang hiện tại |
| `pageSize` | int | `10` | Số lượng bản ghi mỗi trang |

#### Request Mẫu:
```http
GET /api/v1/event/complaints/mine?page=1&pageSize=10
Authorization: Bearer eyJhbGciOi...
```

#### Response Mẫu (`200 OK`):
```json
{
  "items": [
    {
      "id": "7bc91e42-1234-4abc-9def-123456789abc",
      "eventId": "11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "eventName": "VnExpress Marathon Quy Nhon 2026",
      "distanceId": "22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
      "distanceName": "City 10K",
      "userId": "35bd0476-8072-47fa-a982-7d31d9c13c2b",
      "title": "Sai sót kết quả thời gian chạy",
      "content": "Đồng hồ GPS của tôi ghi nhận thời gian là 45 phút nhưng trên bảng xếp hạng hiển thị sai.",
      "evidenceLinks": [
        "https://drive.google.com/file/d/strava-screenshot-id/view",
        "https://drive.google.com/file/d/bib-photo-id/view"
      ],
      "status": "Accepted",
      "processingStatus": "Processed",
      "responseContent": "Ban tổ chức đã kiểm tra lại chip timing và cập nhật kết quả chính xác cho bạn.",
      "createdAtUtc": "2026-10-27T08:00:00Z",
      "repliedAtUtc": "2026-10-27T08:15:00Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1
}
```

---

### 5. API — Chi tiết khiếu nại (Runner và Organizer dùng chung)

* **Method:** `GET`
* **Endpoint:** `/api/v1/event/complaints/detail`
* **Query bắt buộc:** `complaintId` (Guid, lấy từ trường `id` trong danh sách).
* **Auth:** Người gửi khiếu nại, chủ giải tương ứng hoặc Admin.
* **Response (`200 OK`):** Một object có cùng các trường của từng phần tử `items` trong API danh sách, bao gồm `title`, `content`, `evidenceLinks`, `status`, `responseContent`, `createdAtUtc` và `repliedAtUtc`.
* **404:** Không tồn tại hoặc người đăng nhập không được phép xem; không tiết lộ dữ liệu của tài khoản khác.

```http
GET /api/v1/event/complaints/detail?complaintId=7bc91e42-1234-4abc-9def-123456789abc
Authorization: Bearer <accessToken>
```

Danh sách `/complaints/mine` không truyền `eventId` và `status` sẽ lấy mọi khiếu nại của người đang đăng nhập, vẫn phân trang bằng `page` và `pageSize` (1–100). Danh sách Organizer luôn giới hạn trong các giải của chủ tài khoản, kể cả khi truyền `eventId` cụ thể. Cả hai quyết định Accepted/Rejected đều yêu cầu `responseContent` và không cho xử lý lại đơn đã quyết định.

---

## ⚠️ Quy tắc xử lý lỗi chung (Error Convention)

Nếu dữ liệu đầu vào không hợp lệ hoặc vi phạm quy tắc nghiệp vụ, hệ thống sẽ trả về mã lỗi HTTP **400 Bad Request** với định dạng JSON chuẩn:

```json
{
  "error": "Tiêu đề không được để trống.; Tối đa 5 link bằng chứng."
}
```

* **401 Unauthorized:** Thiếu hoặc sai token đăng nhập.
* **403 Forbidden:** Tài khoản không có quyền thực hiện thao tác (Ví dụ: VĐV cố gọi API của Organizer hoặc Organizer cố xem giải của người khác).
* **404 Not Found:** Không tìm thấy mã khiếu nại hoặc giải đấu yêu cầu.

# Race Monitoring Distance API Guide

Các API dùng cho màn hình **Live Monitoring** theo **cự ly (distance)**.
Caller hợp lệ là **Organizer** hoặc **Race-staff Active** trên giải tương ứng.

## Biến sử dụng

```text
{baseUrl}        = http://localhost:5080
{eventId}        = GUID của giải
{distanceId}     = GUID cự ly (lấy từ event detail: distances[].id)
```

Header:

```http
Authorization: Bearer {organizer-or-race-staff-jwt}
Accept: application/json
```

## 1. Thống kê số VĐV đã hoàn thành / chưa hoàn thành cự ly

```http
GET {baseUrl}/api/v1/race-monitoring/events/{eventId}/distances/{distanceId}/stats
Authorization: Bearer {organizer-or-race-staff-jwt}
Accept: application/json
```

Không cần request body.

Response `200 OK`:

```json
{
  "eventId": "11111111-1111-1111-1111-111111111111",
  "distanceId": "22222222-2222-2222-2222-222222222222",
  "totalRunners": 500,
  "passedCount": 320,
  "notPassedCount": 180
}
```

Ý nghĩa:

- `totalRunners`: tổng VĐV đủ điều kiện trong cự ly.
- `passedCount`: số VĐV đã `Finished`.
- `notPassedCount`: số VĐV chưa `Finished`.
- Luôn đảm bảo: `passedCount + notPassedCount = totalRunners`.

## 2. Danh sách VĐV theo trạng thái hoàn thành cự ly

```http
GET {baseUrl}/api/v1/race-monitoring/events/{eventId}/distances/{distanceId}/runners?status=All&search=&page=1&pageSize=20
Authorization: Bearer {organizer-or-race-staff-jwt}
Accept: application/json
```

Không cần request body.

### Query parameters

| Query | Type | Required | Description |
|---|---|---:|---|
| `status` | string | No | `Passed` / `NotPassed` / `All` (mặc định `All`) |
| `search` | string | No | Tìm theo `bibNumber` hoặc `runnerName` |
| `page` | integer | No | Trang hiện tại, mặc định `1`, tối thiểu `1` |
| `pageSize` | integer | No | Số dòng/trang, mặc định `20`, từ `1` đến `100` |

Response `200 OK`:

```json
{
  "eventId": "11111111-1111-1111-1111-111111111111",
  "distanceId": "22222222-2222-2222-2222-222222222222",
  "items": [
	{
	  "registrationId": "aaaaaaa1-1111-1111-1111-111111111111",
	  "bibNumber": "00125",
	  "runnerName": "Nguyen Van A",
	  "status": "Passed",
	  "passageTime": "2026-11-10T08:30:15Z"
	},
	{
	  "registrationId": "aaaaaaa2-2222-2222-2222-222222222222",
	  "bibNumber": "00127",
	  "runnerName": "Le Van C",
	  "status": "NotPassed",
	  "passageTime": null
	}
  ],
  "pagination": {
	"page": 1,
	"pageSize": 20,
	"totalItems": 500
  }
}
```

Quy ước dữ liệu:

- `status = Passed`: registration có trạng thái `Finished`.
- `status = NotPassed`: registration chưa ở trạng thái `Finished`.
- `passageTime`: lấy từ thời điểm `FinishedAtUtc`; null nếu chưa hoàn thành.

## 3. Luồng sử dụng với UI Live Monitoring

1. Chọn `eventId` + `distanceId`.
2. Gọi API stats để hiển thị `Total / Passed / Not Passed`.
3. Gọi API runners để hiển thị bảng danh sách chung.
4. Khi trạng thái runner thay đổi (finish), gọi lại API để refresh dữ liệu.

## 4. Nguồn dữ liệu thực tế trong hệ thống

Hai API GET ở trên đọc dữ liệu thực tế trong hệ thống từ trạng thái đăng ký theo cự ly.
Không bắt buộc gọi API demo để hiển thị.

## Lỗi thường gặp

| Status | Ý nghĩa |
|---:|---|
| `401` | Chưa đăng nhập hoặc JWT không hợp lệ |
| `403` | Không có quyền theo vai trò hoặc không thuộc giải |
| `400` | `eventId`/`distanceId`/query không hợp lệ, hoặc `status` không đúng giá trị |
| `404` | Không tìm thấy cự ly (tùy cấu hình mapping lỗi hiện tại) |

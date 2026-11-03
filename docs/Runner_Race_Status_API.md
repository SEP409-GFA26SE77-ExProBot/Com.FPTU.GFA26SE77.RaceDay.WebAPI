# Cập nhật trạng thái thi đấu DNS / DNF / DQ

## Endpoint

```http
PATCH /api/v1/event/{eventId}/registrations/{registrationId}/race-status
Authorization: Bearer <IAM access token>
Content-Type: application/json
```

```json
{
  "status": "dnf",
  "reason": "VĐV chủ động dừng tại CP2."
}
```

`status` chỉ nhận `dns`, `dnf`, `dq` (không phân biệt hoa thường, bỏ khoảng trắng hai đầu).
`reason` bắt buộc, tối đa 1000 ký tự; lưu sau khi trim.
`registrationId` là ID lượt đăng ký, không phải `userId` của runner.

Quyền: Admin, organizer sở hữu giải, hoặc Volunteer/Coordinator Active đã liên kết tài khoản
với đúng giải. Role IAM Volunteer đơn lẻ không đủ. Photographer không được cấp quyền này.
Token Wear không được dùng. Quyền hiện theo giải, không giới hạn theo station.

## Quy tắc

| Đích | Điều kiện |
| --- | --- |
| DNS | Đăng ký Confirmed, RaceStatus NotStarted, chưa có start/finish/kết quả; đã đến giờ xuất phát. |
| DNF | Đăng ký Confirmed, RaceStatus Running, chưa finish. |
| DQ | Đăng ký Confirmed/Finished, RaceStatus NotStarted/Running/Finished. |

Giờ xuất phát lấy `distance.StartAt`, dự phòng `event.StartTime`, kết hợp `event.EventDate`.
Hiện tính UTC như API finish đang có; không chuyển đổi theo múi giờ thiết bị. Thiếu giờ xuất phát
thì không đánh DNS được. Việc xác nhận runner vắng mặt là quyết định của nhân sự; không tự động đánh DNS.

Không đảo quyết định DNS/DNF/DQ và không đổi lý do qua API này. Gửi lại cùng trạng thái và
lý do đã trim trả về quyết định gốc, không ghi thêm lịch sử. Nếu request đến đồng thời, một request
có thể nhận 409; tải lại và gửi lại cùng quyết định để nhận kết quả đã lưu.

## Response 200

```json
{
  "registrationId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "eventId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "bibNumber": "1001",
  "previousStatus": "running",
  "raceStatus": "dnf",
  "reason": "VĐV chủ động dừng tại CP2.",
  "changedAtUtc": "2026-11-03T08:00:00Z",
  "changedByUserId": "cccccccc-cccc-cccc-cccc-cccccccccccc"
}
```

Thời gian và người cập nhật lấy từ server/token, không nhận từ client. Lịch sử quyết định thủ công
lưu trong `event.event_registration_race_status_changes`, cùng transaction với thay đổi trạng thái.

| HTTP | Xử lý ở frontend |
| --- | --- |
| 400 | Hiển thị `error`: dữ liệu sai, không đủ quyền trên giải, sai giải hoặc chuyển trạng thái không hợp lệ. Theo quy ước DomainException hiện tại của WebAPI. |
| 401 | Yêu cầu đăng nhập lại khi token thiếu/hết hạn/không hợp lệ. |
| 403 | Token không đạt policy HumanUser (ví dụ token Wear). |
| 409 | Có cập nhật đồng thời: tải lại dữ liệu trước khi thử lại. |

## Các API liên quan

- DTO đăng ký (danh sách/chi tiết) và danh sách check-in bổ sung `raceStatus`:
  `not_started`, `running`, `finished`, `dns`, `dnf`, `dq`.
- `status` cũ vẫn là trạng thái đăng ký. Frontend phải dùng `raceStatus` để hiển thị tình trạng thi đấu.
- Start chuyển sang Running, finish chuyển sang Finished. DNS/DNF/DQ chặn cả hai thao tác.
- Finish vẫn hỗ trợ runner chưa quét start bằng giờ xuất phát cấu hình như trước.
  Không cho bổ sung start lần đầu sau khi đã finish; quét lại start đã có vẫn trả kết quả cũ.
- DQ sau finish giữ nguyên StartedAtUtc, FinishedAtUtc, ResultSeconds và RegistrationStatus.Finished
  để đối soát, nhưng bị loại khỏi leaderboard công khai/cá nhân và không còn đủ điều kiện lấy chứng nhận.
- Danh sách check-in vẫn chỉ lấy RegistrationStatus.Confirmed như trước; không phải danh sách toàn bộ kết quả thi đấu.

## Migration

Migration `20261103041651_AddRegistrationRaceStatus` thêm RaceStatus và bảng lịch sử. Dữ liệu cũ:
Status Finished hoặc có FinishedAtUtc → Finished; còn lại có StartedAtUtc → Running; còn lại → NotStarted.
Không tạo lịch sử giả cho dữ liệu cũ. Mô hình EF dùng RaceStatus làm concurrency token để ngăn
finish/start ghi đè quyết định DNS/DNF/DQ khi đọc dữ liệu cũ.

Triển khai migration trước khi phục vụ request bằng code mới; EventDataSeeder hiện tự gọi MigrateAsync
khi host khởi động. Không chạy đồng thời instance dùng code cũ và code mới trong lúc chuyển đổi.

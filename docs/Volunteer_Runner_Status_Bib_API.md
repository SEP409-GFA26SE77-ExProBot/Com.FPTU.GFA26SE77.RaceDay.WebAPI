# API trạng thái thi đấu và mất BIB vật lý

Cập nhật: 26/10/2026. Module `Event`, .NET 10. Tài liệu dành cho backend, StaffMobile và kiểm thử Swagger/Postman.

## 1. Phạm vi và quyền

| API | Mục đích |
| --- | --- |
| `PATCH /api/v1/event/{eventId}/registrations/{registrationId}/race-status` | DNS/DNF/DQ; khôi phục quyết định nhầm |
| `PATCH /api/v1/event/{eventId}/registrations/{registrationId}/bib-status` | Báo mất và xác nhận cấp lại BIB vật lý cùng số |
| `GET /api/v1/event/{eventId}/registrations/{registrationId}/operations` | Trạng thái hiện tại, version và tối đa 100 thao tác mới nhất |

- Dùng JWT đăng nhập IAM: `Authorization: Bearer <accessToken>`. Wear/device token không được dùng.
- Volunteer/coordinator phải có `EventStaffMember.Active`, `UserId` khớp tài khoản và đúng `eventId`. IAM role/email riêng lẻ không đủ.
- Cả volunteer và coordinator được đặt DNS/DNF/DQ, báo mất và cấp lại BIB.
- Chỉ coordinator, organizer sở hữu giải hoặc admin được `restore` hoặc đổi một quyết định DNS/DNF/DQ sang quyết định khác.
- Organizer sở hữu giải và admin được dùng cả ba API. Photographer/runner/staff khác giải không được truy cập lịch sử hoặc ghi trạng thái.
- Registration để ghi thao tác phải `Confirmed` và có BIB. `registrationId` luôn thuộc `eventId` trong URL.

### Luồng thanh toán và duyệt đăng ký

`pending_payment` → xác nhận thanh toán → `pending_approval` → organizer duyệt → `confirmed` + BIB/QR.

- API duyệt và domain chỉ cho phép xác nhận từ `pending_approval`, áp dụng cả organizer và admin. Duyệt đơn chưa thanh toán trả HTTP 400 trước khi cấp BIB, lưu thay đổi, phát sự kiện xác nhận hoặc gửi vé.
- Thanh toán không tự xác nhận đăng ký. Runner vẫn phải đợi organizer duyệt.
- Migration đổi `Finished` cũ thành `Confirmed` + `RaceStatus = Finished` chỉ tách kết quả thi đấu khỏi trạng thái đăng ký của runner đã được duyệt trước đó. Không tạo đăng ký mới, không duyệt đơn đang chờ và không phát lại email/vé.

## 2. Chuẩn bị và chạy backend

Chạy lệnh từ thư mục gốc WebAPI. Cần .NET 10, cấu hình `.env` theo `.env.example` và các dịch vụ phụ thuộc của dự án.

```powershell
docker compose -f deploy/docker-compose.yml up -d
dotnet build src/Hosts/RaceDay.Api
dotnet run --project src/Hosts/RaceDay.Api --launch-profile http
```

- API: `http://localhost:5080`; Swagger: `http://localhost:5080/swagger`.
- Host hiện có hosted service chạy migration và seed khi khởi động. Lệnh trên có thể ghi DB theo cấu hình đang chọn; dùng môi trường dev/test của bạn.
- Nếu chạy API bằng Docker: rebuild image bằng `docker compose -f deploy/docker-compose.yml --profile api up -d --build`.
- Điện thoại thật: chạy host với `--urls "http://0.0.0.0:5080"`, dùng IP LAN phù hợp.

### Migration mới

`20261026035317_AddRunnerOperations` thêm `RaceStatus`, `BibStatus`, `Version` và bảng `event.runner_operations`.

- `Status = Finished` cũ → `Status = Confirmed`, `RaceStatus = Finished`.
- Các bản ghi còn lại có `StartedAtUtc` → `RaceStatus = Started`; chưa có → `NotStarted`.
- Giữ nguyên BIB, check-in, thời gian start/finish, thành tích. `BibStatus = Normal`, version khởi tạo bằng 0 cho dữ liệu cũ.
- `NotStarted` phản ánh chưa có ghi nhận start, không phải bằng chứng runner thực sự không xuất phát.
- Không chạy đồng thời backend cũ và mới trên cùng DB sau migration: code cũ chưa hiểu RaceStatus/Version.
- Down migration bị chặn nếu đã có lịch sử thao tác, để tránh âm thầm mất quyết định và audit. Nếu chưa có lịch sử, Down chuyển kết quả Finished về trạng thái cũ.

Có thể áp dụng migration riêng, không khởi động host/seeder:

```powershell
# Tự đặt connection string của DB dev/test, không commit mật khẩu.
$env:ConnectionStrings__Postgres = '<connection string DB dev/test>'
dotnet ef database update --project src/Modules/Event/RaceDay.Modules.Event.Infrastructure --context EventDbContext
```

`EventDbContextFactory` đọc biến môi trường trên, không tự đọc `.env`, và dùng bảng migration `event.__EFMigrationsHistory` giống host. Khi chỉ tạo migration/script, factory không kết nối DB hay chạy seed.

## 3. Lấy token, event và runner

1. Đăng nhập bằng `POST /api/v1/iam/auth/login` với tài khoản staff đã được liên kết.
2. Dán `accessToken` vào nút **Authorize** trong Swagger (chỉ token).
3. `GET /api/v1/event/staff/me`: lấy giải được phân công.
4. `GET /api/v1/event/check-ins?eventId={eventId}`: tìm runner, lấy `registrationId` và `version`.
5. `GET /api/v1/event/{eventId}/registrations/{registrationId}/operations`: kiểm tra thông tin mới nhất trước khi cập nhật.

Roster hiện chứa registration confirmed, bao gồm runner đã finish/DNS/DNF/DQ; đọc `raceStatus` để phân biệt. Không lấy số lượng toàn bộ roster làm số người đang chờ check-in.

## 4. Request dùng chung cho hai PATCH

```json
{
  "status": "dnf",
  "reason": "Runner xác nhận bỏ cuộc tại trạm số 2",
  "expectedVersion": 3,
  "requestId": "79d92301-8626-4dde-a47e-cf3146c49580"
}
```

| Trường | Quy tắc |
| --- | --- |
| `status` | Chuỗi; trim và không phân biệt hoa/thường |
| `reason` | Bắt buộc, không chỉ khoảng trắng, tối đa 1000 ký tự |
| `expectedVersion` | Bắt buộc, số nguyên không âm lấy từ dữ liệu mới nhất; không hardcode 3 |
| `requestId` | UUID khác rỗng; tạo một lần cho một hành động, giữ nguyên khi retry |

Người thao tác lấy từ token, timestamp UTC lấy từ server. Client không được truyền người/thời điểm giả lập. Hai API này cần online; không tự coi thao tác offline là đã thành công.

## 5. DNS, DNF, DQ và khôi phục

| `status` | Điều kiện |
| --- | --- |
| `dns` | Staff xác nhận không xuất phát; không có StartedAtUtc/FinishedAtUtc. Đã check-in kit vẫn có thể DNS |
| `dnf` | Staff xác nhận đã tham gia nhưng không hoàn thành; không có FinishedAtUtc. Cho phép thiếu quét start, không tạo start giả |
| `dq` | Bị loại; cho phép áp dụng cả sau finish. Giữ timing, loại khỏi leaderboard |
| `restore` | Chỉ coordinator/owner/admin; chỉ khi đang DNS/DNF/DQ; khôi phục Finished nếu có finish, Started nếu có start, còn lại NotStarted |

- Không tự động suy ra DNS/DNF từ lượt quét bị thiếu. Người thao tác chịu trách nhiệm xác minh và ghi lý do.
- Sau DNS/DNF/DQ, check-in/start/finish thông thường bị chặn.
- Volunteer không được đổi quyết định đã chốt; coordinator vẫn phải tuân thủ dữ liệu timing, ví dụ có finish thì không được đổi thành DNF/DNS.
- Gửi cùng trạng thái với requestId mới trả 409. Retry đúng requestId cũ trả receipt cũ.
- Start sau finish mà trước đó chưa có start bị chặn để tránh sửa mốc tính thành tích. Quét lại start/finish đã ghi vẫn giữ lần đầu.
- Refund cho đăng ký đã có trạng thái thi đấu bị từ chối trước khi xử lý payment; API mới không tự hoàn tiền.

## 6. Mất BIB vật lý

Gọi `bib-status` lần lượt với `status = lost` và sau khi thực sự giao lại BIB thì `status = reissued`. Mỗi lần có requestId mới và version mới nhất.

```text
normal → lost → reissued → lost → reissued ...
```

- `normal` chỉ nghĩa chưa có sự cố, không chứng minh đã phát kit.
- Chỉ được `reissued` sau `lost`; không được đặt trực tiếp `normal`.
- Giữ nguyên số BIB, QR, registration, check-in, start/finish, kết quả và trạng thái thi đấu.
- Có thể ghi nhận cấp lại cho runner DQ nhưng thao tác này không gỡ DQ hay cho phép thi đấu lại.
- Staff phải xác minh đúng runner trước khi giao lại; API không in/giao BIB, không trừ tồn kho Supplies.
- Tờ BIB cũ và mới cùng số/QR không phân biệt được bằng dữ liệu quét hiện tại. Báo mất không thu hồi QR hoặc tự chặn start/finish; trạng thái thi đấu vẫn quyết định quyền quét.
- Đổi sang số khác, chip RFID, phí cấp lại, báo tìm thấy BIB và in BIB nằm ngoài phạm vi API này.

## 7. Response, lịch sử và xử lý retry

PATCH trả 200 với receipt, ví dụ:

```json
{
  "id": "bf755e29-a16a-44a2-b0b6-030fe3cc9b7e",
  "requestId": "79d92301-8626-4dde-a47e-cf3146c49580",
  "registrationId": "00000000-0000-4000-8000-000000000001",
  "kind": "race_status",
  "requestedStatus": "dnf",
  "previousStatus": "started",
  "newStatus": "dnf",
  "reason": "Runner xác nhận bỏ cuộc tại trạm số 2",
  "actorUserId": "00000000-0000-4000-8000-000000000002",
  "createdAtUtc": "2026-10-26T03:00:00Z",
  "version": 4,
  "bibNumber": "TEST-1001",
  "raceStatus": "dnf",
  "bibStatus": "normal",
  "alreadyApplied": false
}
```

- History và trạng thái lưu trong cùng transaction `SaveChanges`. Version là concurrency token tại DB, tăng cả khi check-in/start/finish hoặc thay đổi registration.
- Retry cùng registration + requestId + payload chuẩn hóa + actor: trả receipt ban đầu, `alreadyApplied = true`, không ghi thêm lịch sử. Phải giữ expectedVersion ban đầu khi retry.
- Dùng lại requestId với nội dung/actor khác: 409.
- Receipt là dữ liệu **tại thời điểm thao tác**, không nhất thiết là trạng thái hiện tại. Sau retry, gọi GET operations để refresh, đặc biệt khi đã có thao tác tiếp theo.
- Hai request đồng thời cùng version: chỉ một thay đổi được lưu; request còn lại nhận 409. Với retry chưa rõ kết quả, gửi lại đúng requestId/body ban đầu. Với thao tác mới, đọc lại dữ liệu và tạo requestId mới.
- GET operations trả snapshot hiện tại và tối đa 100 lịch sử mới nhất, thứ tự version giảm dần. Lịch sử này chỉ gồm hai PATCH, không phải lịch sử mọi lần quét start/finish.

## 8. Chạy thử bằng PowerShell

Thay các placeholder bằng token/ID thực tế của dữ liệu test đã chuẩn bị. Ví dụ này báo mất rồi cấp lại; không dùng trên runner thật chỉ để thử.

```powershell
$baseUrl = 'http://localhost:5080'
$headers = @{ Authorization = 'Bearer <accessToken>' }
$eventId = '<eventId>'
$registrationId = '<registrationId>'
$runnerUrl = "$baseUrl/api/v1/event/$eventId/registrations/$registrationId"

$current = Invoke-RestMethod "$runnerUrl/operations" -Headers $headers
$requestId = [guid]::NewGuid().ToString()
$body = @{
    status = 'lost'
    reason = 'Runner báo mất BIB, đã đối chiếu vé điện tử'
    expectedVersion = $current.version
    requestId = $requestId
} | ConvertTo-Json

$receipt = Invoke-RestMethod "$runnerUrl/bib-status" -Method Patch -Headers $headers `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$receipt

# Retry giữ nguyên body: trả alreadyApplied = true.
Invoke-RestMethod "$runnerUrl/bib-status" -Method Patch -Headers $headers `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))

# Chỉ thực hiện sau khi đã thực tế giao lại BIB cùng số.
$current = Invoke-RestMethod "$runnerUrl/operations" -Headers $headers
$body = @{
    status = 'reissued'
    reason = 'Đã giao BIB cấp lại cùng số'
    expectedVersion = $current.version
    requestId = [guid]::NewGuid().ToString()
} | ConvertTo-Json
Invoke-RestMethod "$runnerUrl/bib-status" -Method Patch -Headers $headers `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

Đối với DNS/DNF/DQ: lấy lại version, đổi URL thành `race-status`, status phù hợp, lý do xác minh và requestId mới. Với `restore`, đăng nhập tài khoản coordinator/owner/admin.

## 9. HTTP và tương thích client

| HTTP | Ý nghĩa |
| --- | --- |
| 200 | Đã áp dụng hoặc replay thành công |
| 400 | Thiếu/sai trường, registration chưa confirmed/có BIB, chuyển trạng thái trái dữ liệu timing |
| 401 | JWT thiếu/hết hạn/không hợp lệ |
| 403 | Sai vai trò/phân công, wear token, volunteer cố sửa quyết định đã chốt |
| 404 | Không có event/registration trong event |
| 409 | Version cũ, requestId bị dùng khác nội dung, trạng thái đã có hoặc cạnh tranh ghi |

Lỗi nghiệp vụ trả `{ "error": "..." }`. Không tự retry 400/403 như lỗi mất mạng.

DTO registration thêm `registrationStatus`, `raceStatus`, `bibStatus`, `version`, `startedAtUtc`. Trường `status` cũ vẫn trả `finished` khi kết quả hợp lệ là Finished; DNS/DNF/DQ trả trạng thái đăng ký (`confirmed`), do đó client mới **phải đọc raceStatus**, không chỉ status. Vé/QR vẫn được trả cho registration confirmed, kể cả đã finish hoặc bị loại; QR là định danh, server kiểm tra quyền thi đấu khi quét.

Roster/check-in/start/finish thêm `raceStatus`, `bibStatus`, `version`; roster thêm thời gian start/finish và thành tích. Leaderboard cá nhân và theo cự ly chỉ tính `RaceStatus = Finished` và registration confirmed. Khi restore DQ về Finished, thứ hạng được tính lại từ timing đã giữ.

## 10. Kiểm thử

```powershell
dotnet test tests/RaceDay.Modules.Event.UnitTests
dotnet test RaceDay.slnx
dotnet build src/Hosts/RaceDay.Api
```

Các case chính: volunteer/coordinator hợp lệ, photographer/staff khác giải/unlinked/invited/revoked bị chặn; DQ sau finish và restore; DNS sau check-in; DNF thiếu start; retry đúng/sai requestId; version cũ sau quét; mất/cấp lại nhiều lần giữ nguyên BIB/timing; leaderboard bỏ DQ.

Build/unit tests không chứng minh migration đã chạy trên DB triển khai. Kiểm tra thêm trên DB test: migrate dữ liệu Finished cũ, hai client ghi cùng version, refresh roster và ứng dụng sau thay đổi.

### Kết quả xác minh ngày 26/10/2026

- API build thành công; 170 unit tests đạt (Event 68, Iam 73, Photo 21, Payment 7, Shared 1). RaceMonitoring chưa có test. Bao gồm 8 case hồi quy điều kiện duyệt: domain từ chối trạng thái không hợp lệ và organizer/admin không thể cấp BIB/gửi vé cho đơn chưa thanh toán.
- EF xác nhận model khớp migration mới.
- 21 kiểm tra PostgreSQL + HTTP đạt trên database tạm riêng: migrate Finished cũ giữ timing; cạnh tranh ghi và rollback audit; JWT/phân quyền; DNS/DQ/restore; retry; mất/cấp lại BIB; leaderboard; lỗi 400/401/403/404/409.
- Hai PATCH đồng thời cùng version: đúng một request 200, một request 409; chỉ một audit được lưu.
- Chạy endpoint thật qua EventModule trong host kiểm thử không seed; chưa chạy ứng dụng mobile/frontend E2E.
- Build vẫn có cảnh báo dependency NuGet và phiên bản EF Relational trong dependency graph hiện có; không nâng package của dự án trong thay đổi này.

### Database dự án đã migration ngày 26/10/2026

- Đã áp dụng `20261025152230_AddEventSupplies` và `20261026035317_AddRunnerOperations` trên PostgreSQL Docker local `localhost:5433`, database `postgres`, theo cấu hình dự án. Chạy EF CLI riêng, không khởi động seeder.
- Đã kiểm tra `event.__EFMigrationsHistory`, các cột `RaceStatus`/`BibStatus`/`Version` và bảng/index `event.runner_operations`.
- Trước: 4 Confirmed, 5 Finished, 1 PendingPayment. Sau: 4 Confirmed + NotStarted, 5 Confirmed + Finished, 1 PendingPayment + NotStarted. Không có PendingApproval tại thời điểm migration.
- Tổng số đăng ký vẫn là 10. Checksum tất cả trường cũ ngoài Status không đổi, bao gồm ID, BIB, giá vé, check-in, start/finish và thành tích. Migration không tạo thao tác runner giả.
- Backup trước migration: `_tmp_reports/raceday-before-runner-ops-20261026.dump` (local, ignored; không commit dữ liệu DB).
- Backend đã build lại; khởi động bằng lệnh ở mục 2 để dùng code mới. Frontend/mobile cần cập nhật theo mục 9.

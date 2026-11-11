# Staff checkpoint workspace (UserWeb)

Workspace `/staff` dành cho tài khoản RaceStaff (Coordinator/Volunteer đã hợp nhất). Bản này phục vụ **simulation**, chưa triển khai StaffMobile hay thay thế quy trình ghi nhận thi đấu thực tế.

## Chuẩn bị và sử dụng

### Dữ liệu test local đã chuẩn bị

Chạy lại khi cần bổ sung bộ dữ liệu trên PostgreSQL local `localhost:5433`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/seed-staff-workspace.ps1
```

Script chỉ thêm những bản ghi chưa có. Nó không xóa đăng ký, passage, quyết định hoặc kết quả sau khi đã commit. Không chạy `clear-and-seed` để chuẩn bị workspace này.

- Giải: **RaceDay Staff Checkpoint Test 5K**, ngày 20/12/2026, `eventId = 5aff0000-0000-4000-8000-000000000100`, cự ly `5aff0000-0000-4000-8000-000000000101`.
- 12 runner test đã **Confirmed**, có BIB `90001`–`90012`, chưa xuất phát/chưa có kết quả. Tài khoản `stafftest.runner01` đến `stafftest.runner12`.
- Organizer: `cuong.le`.
- Staff: `coord1` tại **Start** (`cp-0`, thử DNS), `coord2` tại **1.25 km** (`cp-1.25`), `coord3` tại **tiếp nước 2.5 km** (`cp-2.5`), `coord4` tại **3.75 km** (`cp-3.75`), `vol1` tại **Finish** (`cp-5`). Đây đều là RaceStaff Active, mỗi người chỉ có một phân công trong giải test. Các ID này khớp tuyến đường đã lưu ngày 11/11/2026; nếu sửa/xóa checkpoint và đổi ID lần nữa, cần gán lại staff theo checkpoint mới.
- Mật khẩu demo của các tài khoản trên: `MatKhau@123` (chỉ dùng môi trường local).

Mở `/organizer/events/5aff0000-0000-4000-8000-000000000100/race-simulation` bằng `cuong.le`, chọn cự ly 5K và tạo lượt **Ready**. Staff đăng nhập UserWeb sẽ thấy giải trong `/staff`; chọn checkpoint được giao. Runner có thể xem đăng ký ở `/my-registrations`. Lượt simulation nằm trong bộ nhớ API, nên sau khi restart cần tạo lượt Ready lại. Bộ seed không đặt lại trạng thái VĐV đã commit; khi thử commit toàn bộ 12 người thì lượt sau không còn đủ 12 runner hợp lệ.

1. Bật `Simulation__Enabled=true`, giữ cấu hình giờ xuất phát/time zone theo simulation hiện có.
2. Chạy lại API. Event startup tự chạy migration `20261111030201_AddSimulationDecisionSource`, thêm hai cột nullable vào lịch sử trạng thái: `SimulationRunId`, `CheckpointExternalId`.
3. Organizer mời tài khoản staff, gán **checkpoint trên bản đồ** của cự ly. Mã vị trí có dạng `map:{distanceId}:{checkpointId}`; API kiểm tra checkpoint thực sự thuộc giải/cự ly. Bản ghi thêm trực tiếp chưa liên kết tài khoản không đủ điều kiện đăng nhập workspace.
4. Staff đăng nhập UserWeb → `/staff` → chọn phân công → mở checkpoint. Nhiệm vụ `duty:*` chưa có màn hình checkpoint.
5. Organizer tạo/chạy lượt. Staff nhận dữ liệu realtime, tra BIB/tên, xem lượt qua mốc, bản đồ và ghi nhận tình trạng.
6. Khi lượt kết thúc, organizer xử lý hết báo cáo DQ rồi **bấm ghi kết quả**. Đã bỏ auto-commit để có thời gian duyệt báo cáo.

Nếu đang chạy API local, chỉ cần chạy lại `dotnet run --project src/Hosts/RaceDay.Api --launch-profile http`; hạ tầng Docker đang chạy không cần tạo lại. Nếu chạy API trong Docker, dùng `docker compose -f deploy/docker-compose.yml --profile api up -d --build`. Không cần `down -v`.

## Phân quyền và quy tắc

| Thao tác | Staff checkpoint | Organizer sở hữu giải |
| --- | --- | --- |
| Đọc checkpoint | Chỉ phân công Active gắn tài khoản hiện tại | Đọc lượt đầy đủ qua API organizer |
| Start/Pause/Resume/Reset/New/Commit | Không | Có |
| DNS | Chỉ Start khi lượt Ready, trước xuất phát | Khi lượt Ready |
| DNF | VĐV đang Racing tại chặng, khi Running/Paused | VĐV đang Racing sau xuất phát |
| DQ | Gửi báo cáo Pending, chưa đổi kết quả | Duyệt/từ chối hoặc ghi DQ trực tiếp |

- Chặng của checkpoint được tính từ mốc liền trước (bao gồm) tới mốc liền sau (không bao gồm). Start chỉ đến mốc kế tiếp; Finish từ mốc trước đến hết đường đua. Hai checkpoint liền kề có thể cùng hỗ trợ đoạn nối giữa chúng.
- DNS/DNF/DQ yêu cầu lý do (tối đa 1000 ký tự); báo cáo có mô tả/URL bằng chứng tùy chọn (2000 ký tự). Không cho gửi thêm quyết định khi VĐV có báo cáo Pending hoặc đã DNF/DNS/DQ.
- VĐV đã DNF/DNS/DQ dừng di chuyển, không nhận thêm passage và không được xếp hạng.
- Commit chỉ khi lượt Finished và không còn Pending. Quyết định bị khóa khi đang commit, đã ghi một phần hoặc có kết quả chưa xác định cần đối soát.
- Commit không ghi đè đăng ký đã thi đấu thực tế; chỉ cập nhật đăng ký Confirmed/NotStarted chưa có thời gian/kết quả. Trạng thái terminal và audit được ghi trong cùng transaction; retry cùng run không ghi trùng.
- Reset/New thay lượt nháp, có xác nhận bỏ dữ liệu. Không hoàn tác kết quả đã commit.

## API

Các đường dẫn dưới đây có prefix `/api/v1/simulation`, yêu cầu tài khoản người dùng đã xác thực. API tự lấy user/checkpoint từ phân công, không tin checkpoint do client gửi.

| Method | Route | Body |
| --- | --- | --- |
| GET | `/staff/events/{eventId}/checkpoint` | — |
| POST | `/staff/events/{eventId}/runs/{runId}/athletes/{registrationId}/decisions` | `{ "status": "DNF\|DNS\|DQ", "reason": "...", "evidence": "..." }` |
| POST | `/runs/{runId}/athletes/{registrationId}/decisions` | Organizer: status/reason/evidence |
| POST | `/runs/{runId}/decisions/{decisionId}/review` | Organizer: `{ "approve": true, "reason": "..." }` |
| POST | `/runs/{runId}/athletes/{registrationId}/dnf` | Legacy organizer endpoint: nay bắt buộc `{ "reason": "..." }` |

SignalR `/api/v1/simulation/hub`: client gọi **stream** `WatchCheckpoint(eventId)`. Server đọc lại phân công mỗi giây, kể cả khi chưa có lượt hoặc đang pause. Đổi phân công/Reset/New sẽ cập nhật stream; thu hồi phân công kết thúc stream. Staff không được join group `JoinRun` của organizer.

Snapshot staff chứa checkpoint, trạng thái lượt, danh sách VĐV của cự ly với split tại mốc, quyền `canDnf/canDns/canReport` và lịch sử tại checkpoint. Không trả toàn bộ split/log của lượt organizer.

## Lưu trữ và giới hạn

Lượt simulation, báo cáo Pending/Rejected, bằng chứng và lịch sử nháp hiện nằm trong bộ nhớ theo kiến trúc simulation hiện có; restart API/Reset sẽ mất chúng. Khi commit, trạng thái DNF/DNS/DQ và audit quyết định cuối (người áp dụng/duyệt, lý do, trạng thái trước/sau, thời điểm, checkpoint, run) được lưu database. Bằng chứng và toàn bộ vòng đời báo cáo chưa được lưu bền vững. Không dùng phần này làm hệ thống trọng tài chính thức.

Mỗi staff cần một phân công RaceStaff Active duy nhất trong giải; nhiều phân công trùng bị từ chối để tránh chọn nhầm checkpoint. Nếu course thay đổi/xóa mốc, organizer cần sửa phân công và tạo lượt phù hợp.

## Kiểm tra

```powershell
dotnet test tests/RaceDay.Modules.Simulation.UnitTests --no-restore
dotnet test tests/RaceDay.Modules.Event.UnitTests --no-restore
```

Bao gồm quyền organizer, DNS/DNF/DQ, duyệt/từ chối, chặn commit Pending, audit, thay lượt, phân công bị thu hồi và stream SignalR qua tài khoản thật trong test host. Kiểm tra database thực tế và giao diện trình duyệt cần môi trường chạy ứng dụng.

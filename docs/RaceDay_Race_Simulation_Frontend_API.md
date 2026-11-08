
JSON của REST và payload SignalR là **camelCase**. Tên method SignalR giữ nguyên PascalCase: `JoinRun`, `LeaveRun`, `Snapshot`, `RunReplaced`.

Lỗi domain của REST là HTTP **400**, body `{ "error": "…" }`. HTTP **409** của reset, new và commit cũng dùng `{ "error": "…" }`.

---

## 1. Quick start

### 1.1 Base URL demo trực tiếp

Gọi thẳng host API:

```text
http://localhost:5080
```

Nguồn: `src/Hosts/RaceDay.Api/Properties/launchSettings.json`, profile `http`. Cùng host mà `docs/RaceDay_API_Flows.md` và `docs/Event_Distances_Organizer_API.md` dùng cho local.

Gateway YARP lắng nghe `http://localhost:5000` và chuyển `/api/{**catch-all}` về `http://localhost:5080`. Đó là đường khác. Mục 3.4 tách riêng vì chưa được xác nhận với SignalR.

### 1.2 Feature flag

Route REST và hub chỉ được map khi process **khởi động** với cờ bật.

| Khóa cấu hình | Biến môi trường | Ý nghĩa |
| --- | --- | --- |
| `Simulation:Enabled` | `Simulation__Enabled` | `true` thì map `/api/v1/simulation` và hub. `bool.TryParse` phải thành công. Tắt lúc khởi động thì các path này không tồn tại. |
| `Simulation:StartTimeZone` | `Simulation__StartTimeZone` | Mã `TimeZoneInfo` của giờ đã lưu trên giải. Commit đọc lại mỗi lần ghi. |

`.env.example` để cả hai dòng ở dạng comment. Ví dụ trong file đó là `Simulation__Enabled=true` và `Simulation__StartTimeZone=UTC`. `UTC` chỉ đúng khi `EventDate` cùng `StartAt` / `StartTime` đã là giờ UTC. Hệ thống không tự chọn múi giờ.

Cờ tắt sau khi process đã map route: endpoint vẫn còn, nhưng commit trả HTTP 200 với `accepted: false` và `refusalReason: "Giả lập đang tắt."`.

### 1.3 Xác thực và quyền theo giải

Mọi route REST trong nhóm `/api/v1/simulation` dùng policy `HumanUser`. Hub dùng cùng policy.

`HumanUser` là JWT đăng nhập IAM, không phải token Wear (`token_use=wear` hoặc claim `device_id`).

| Tình huống | HTTP / hub |
| --- | --- |
| Không có token, token hết hạn hoặc sai chữ ký | REST **401**. Hub từ chối negotiate / start. |
| Token Wear, đã xác thực nhưng không đạt `HumanUser` | REST **403**. |
| Đã qua `HumanUser` nhưng không phải chủ giải và không phải Coordinator Active của đúng `eventId` | REST **400** `{ "error": "Bạn không có quyền điều khiển giả lập của giải này." }`. `JoinRun` ném `HubException` với cùng câu. |
| User id không có trên token dù request đã vào handler | REST **400** `{ "error": "Cần đăng nhập." }`. |

Quyền theo giải lấy từ user trong token. Client không gửi user id.

Được điều khiển giả lập:

- Chủ giải: `RaceEvents.OrganizerUserId` trùng user đang đăng nhập.
- Hoặc nhân sự của đúng giải, role `Coordinator`, status `Active`.

Role IAM `Admin` một mình không đủ. Photographer và Volunteer không đủ. Coordinator của giải khác không đủ.

Token lấy từ `POST /api/v1/iam/auth/login`. Response có `accessToken` (`LoginResult.AccessToken`). REST gửi:

```http
Authorization: Bearer <accessToken>
```

Cách UserWeb đang giữ `accessToken` phải xem trong app Next.js. Tài liệu này không giả định browser đọc được cookie httpOnly, và không thêm bước lưu token vào `localStorage`.

### 1.4 Lấy các id

| Id | Lấy từ đâu | Ghi chú |
| --- | --- | --- |
| `eventId` | `GET /api/v1/event/organizer/mine` → mỗi phần tử `id`. Hoặc `GET /api/v1/event/{eventId}` → `id`. | Cần đúng giải mà user là chủ hoặc Coordinator Active. |
| `distanceId` | `GET /api/v1/event/distances?eventId={eventId}` → `id`. Organizer: `GET /api/v1/event/organizer/{eventId}/distances` → `distances[].id`. | Public distances trả `id`, `name`, `distanceKm`, `type`. |
| `runId` | Preview `activeRunId`. Sau khi user yêu cầu tạo lượt: `runId` trong snapshot. Sau reset/new: `runId` mới trong response hoặc sự kiện `RunReplaced`. | Có thể `null` khi chưa có lượt trong process. |
| `registrationId` | `athletes[].registrationId` trong snapshot của lượt. Trước khi có lượt, danh sách đăng ký là `GET /api/v1/event/organizer/{eventId}/registrations` → `id`. | Id đăng ký, không phải `userId`. Lượt chỉ nhận VĐV còn đủ điều kiện. |
| `checkpointId` | `checkpoints[].id` trong preview hoặc snapshot. Cùng id trên course: `GET /api/v1/map/events/{eventId}/distances/{distanceId}/course` → `checkpoints[].id`. | **string**, không bắt buộc là GUID. Dùng đúng chuỗi server trả. |

VĐV được đưa vào lượt mới, theo thứ tự `registrationId` tăng dần, khi đăng ký:

- `status` confirmed
- `raceStatus` not started
- chưa có `startedAtUtc`, `finishedAtUtc`, `resultSeconds`
- có BIB khác rỗng

`eligibleCount` trên preview là số này tại thời điểm đọc. Snapshot của lượt đã tạo giữ danh sách lúc tạo hoặc lúc New Run.

### 1.5 Luồng màn hình

Mỗi cặp `(eventId, distanceId)` có tối đa một lượt trong process. Lượt nháp mất khi process tắt. Kết quả đã commit trong database vẫn còn.

1. User đã đăng nhập và có quyền trên giải.
2. Chọn cự ly. Gọi **GET preview**. Dừng ở đây nếu chỉ đang mở trang.
3. `activeRunId` khác `null`: **GET snapshot** của id đó và `JoinRun`. Tiếp tục lượt cũ, kể cả khi `phase` là `Finished`.
4. `activeRunId` là `null`: hiện course, `eligibleCount`, `courseReady`. Chưa có lượt để join.
5. Chỉ khi user bấm tạo lượt: **POST runs**. Body có thể trống. Server tạo lượt `Ready` hoặc trả lượt đang có.
6. `JoinRun(runId)` nếu chưa ở group của lượt đó.
7. Start / pause / resume / playback / tốc độ. Vị trí đổi theo snapshot server đẩy khi `phase` là `Running`.
8. `phase` là `Finished`: bảng `ranking` là kết quả nháp. `discardRequired` cho biết còn thành tích chưa commit.
9. User bấm ghi: **POST commit**. Đọc `accepted`, `refusalReason`, `commitState`, `items[].outcome`. HTTP 200 chưa có nghĩa là đã ghi.
10. Sau item `CommittedThisRun`: tải lại leaderboard đúng `eventId` và `distanceId` của lượt.

Đổi cự ly: `LeaveRun` lượt cũ, rồi GET preview của cặp mới. Không POST runs, không PUT course.

Mở trang hoặc đổi cự ly chỉ dùng API đọc. Không tự tạo lượt. Không gọi `PUT /api/v1/map/events/{eventId}/distances/{distanceId}/course` hay alias `PUT /api/v1/event/organizer/{eventId}/distances/{distanceId}/course` trong luồng này. Course đã lưu được preview đọc sẵn.

### 1.6 Request nào đọc, đổi session, ghi database

| Nhóm | Request | Session trong RAM | Database |
| --- | --- | --- | --- |
| Chỉ đọc | GET preview, GET snapshot, `JoinRun` (đọc snapshot sau khi vào group), GET leaderboard | Không đổi trạng thái lượt. GET snapshot không tăng `version`. | Preview và leaderboard chỉ đọc. |
| Đổi session | POST runs, start, pause, resume, playback, reset, new, speed, SOS, DNF, fraud chip, fraud passage | Có. Reset và new đổi `runId`. | Không ghi đăng ký, không ghi course, không ghi checkpoint scan, không tạo SOS thiết bị. |
| Có thể ghi database | POST commit | Đổi `commitState`, `version`, `committedRegistrationIds` khi batch được nhận. | Chỉ dòng đăng ký nào writer cập nhật thành công. Xem mục 4. |

`LeaveRun` chỉ rời group SignalR.

---

## 2. REST API

Prefix: `/api/v1/simulation`. Auth: mục 1.3. Path không phải GUID ở chỗ `{…:guid}` do routing trả **404** trước khi vào handler.

Body tùy chọn: bỏ body và để `Content-Length: 0`, hoặc gửi `{}`. Body rỗng mà không có `Content-Length: 0` thành **400** `{ "error": "Body không hợp lệ." }`. JSON hỏng cùng câu đó.

### 2.1 Bảng route

| Method | Path | Body | Khi gọi | Response thành công | Lỗi chính |
| --- | --- | --- | --- | --- | --- |
| GET | `/events/{eventId}/distances/{distanceId}` | Không | Mở trang, đổi cự ly, sau `RunReplaced` | **200** `SimulationPreview` | 400 quyền; 401; 403 Wear |
| POST | `/events/{eventId}/distances/{distanceId}/runs` | Không, hoặc `{ "runnerCount": 10, "seed": 42 }` | User yêu cầu tạo lượt | **200** snapshot. Đã có lượt của cùng cặp thì trả lượt đó, bỏ `runnerCount` và `seed` mới | 400 course lỗi, không có VĐV hợp lệ, `runnerCount` ≤ 0, `runnerCount` > số hợp lệ, `runnerCount` > 2000 |
| GET | `/runs/{runId}` | Không | Sau reload, sau reconnect | **200** snapshot hiện tại | 400 không thấy lượt, hoặc không có quyền |
| POST | `/runs/{runId}/start` | Không | `phase` là `Ready` | **200** snapshot `Running` | 400 `Chỉ xuất phát được từ trạng thái sẵn sàng.` |
| POST | `/runs/{runId}/pause` | Không | `phase` là `Running` | **200** snapshot `Paused` | 400 `Chỉ tạm dừng khi lượt đang chạy.` |
| POST | `/runs/{runId}/resume` | Không | `phase` là `Paused` | **200** snapshot `Running` | 400 `Chỉ tiếp tục khi lượt đang tạm dừng.` |
| POST | `/runs/{runId}/playback` | `{ "rate": 1 }` | Lượt còn tồn tại | **200** snapshot. `rate` đúng một trong `1`, `5`, `20`, `60` | 400 `Thiếu tốc độ phát.` hoặc `Tốc độ phát chỉ được 1, 5, 20 hoặc 60.` |
| POST | `/runs/{runId}/reset` | Không, hoặc `{ "confirmDiscard": false }` | User reset cùng seed và cùng danh sách VĐV | **200** snapshot **runId mới**, `generation` tăng, `version` = 1, `phase` `Ready`, `playbackRate` = 1 | 400 cần `confirmDiscard: true`; 409 đang commit |
| POST | `/runs/{runId}/new` | `{ "confirmDiscard": true, "seed": 99, "runnerCount": null }` | User lập lượt mới, đọc lại course và VĐV hợp lệ | **200** snapshot runId mới. `seed` bỏ trống thì server tự chọn. | 400 course/VĐV không hợp lệ thì **lượt cũ giữ nguyên**; 409 đang commit |
| POST | `/runs/{runId}/commit` | Không | `phase` là `Finished` | **200** `SimulationCommitResponse`. Luôn đọc các cờ ở mục 4 | 400 chưa Finished, không thấy lượt, hoặc không có quyền. Lần commit sau trên cùng lượt chờ khóa rồi trả 200. HTTP 409 trả về từ reset hoặc new khi gọi trong lúc commit đang giữ khóa |
| POST | `/runs/{runId}/athletes/{registrationId}/speed` | `{ "kmh": 12.5 }` | VĐV có trong lượt | **200** snapshot. `kmh` từ 0 đến 40 | 400 `Thiếu tốc độ.`, ngoài khoảng, hoặc `Không có VĐV này trong lượt giả lập.` |
| POST | `/runs/{runId}/athletes/{registrationId}/sos` | Không | VĐV có trong lượt | **200** snapshot, `athletes[].sos` = true | 400 không có VĐV / không thấy lượt |
| POST | `/runs/{runId}/athletes/{registrationId}/dnf` | Không | VĐV có trong lượt | **200** snapshot, `motion` = `Dnf` | Cùng các lỗi trên. Gọi lại khi đã DNF thì vẫn 200 |
| POST | `/runs/{runId}/athletes/{registrationId}/fraud/chip` | `{ "chipId": "chip-demo-2" }` | Demo gán chip trong lượt | **200** snapshot, `chipId` đã trim | 400 `Thiếu chip.` khi body thiếu hoặc chuỗi trắng |
| POST | `/runs/{runId}/athletes/{registrationId}/fraud/passage` | `{ "checkpointId": "cp-2" }` | Demo tiêm passage trong lượt | **200** snapshot kể cả khi checkpoint không tồn tại hoặc sai thứ tự. Kết quả nằm ở `log` | 400 `Thiếu checkpoint.` khi body thiếu hoặc chuỗi trắng |

Reset hoặc new khi `phase` là `Finished` và còn VĐV `validFinish` chưa nằm trong `committedRegistrationIds`: thiếu `confirmDiscard: true` thì **400**

```json
{ "error": "Lượt đã có kết quả nháp chưa ghi nhận. Gửi confirmDiscard=true để bỏ lượt này." }
```

Reset hoặc new trong lúc commit đang giữ khóa:

```json
{ "error": "Lượt giả lập đang ghi nhận kết quả. Hãy đợi xong rồi thử lại." }
```

HTTP **409**.

`runnerCount` lấy N đăng ký hợp lệ đầu tiên sau khi sort theo `registrationId`. Bỏ trống nghĩa là lấy hết, tối đa 2000. Câu 400 khi vượt số hợp lệ có dạng `Số VĐV yêu cầu (9) lớn hơn số đăng ký hợp lệ (2).`

Course chưa có: preview vẫn **200**, `courseReady: false`, `courseError: "Chưa có course cho cự ly này."`. POST runs lúc đó trả 400 với cùng nội dung `courseError`. Các lỗi course khác cũng là câu từ `SimulationCourseRules` (polyline, checkpoint start/finish, km).

### 2.2 Preview

```json
{
  "eventId": "11111111-1111-4111-8111-111111111111",
  "distanceId": "22222222-2222-4222-8222-222222222222",
  "title": "Demo",
  "distanceKm": 5,
  "eligibleCount": 2,
  "courseReady": true,
  "courseError": null,
  "polyline": [{ "lat": 13.78, "lng": 109.22 }],
  "checkpoints": [
    {
      "id": "start",
      "name": "Start",
      "km": 0,
      "lat": 13.78,
      "lng": 109.22,
      "type": "start"
    }
  ],
  "activeRunId": null
}
```

`courseError` và `activeRunId` có thể `null`. `courseReady` là `courseError == null`. `checkpoints[].type` là chuỗi lưu trong course (`start`, `finish`, hoặc loại khác).

### 2.3 Snapshot

```json
{
  "runId": "33333333-3333-4333-8333-333333333333",
  "generation": 1,
  "version": 4,
  "eventId": "11111111-1111-4111-8111-111111111111",
  "distanceId": "22222222-2222-4222-8222-222222222222",
  "phase": "Running",
  "commitState": "Idle",
  "seed": 42,
  "playbackRate": 1,
  "simulatedSeconds": 1.2,
  "distanceKm": 5,
  "title": "Demo",
  "discardRequired": false,
  "committedRegistrationIds": [],
  "polyline": [{ "lat": 13.78, "lng": 109.22 }],
  "checkpoints": [
    {
      "id": "start",
      "name": "Start",
      "km": 0,
      "lat": 13.78,
      "lng": 109.22,
      "type": "start"
    }
  ],
  "athletes": [
    {
      "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "bib": "A001",
      "fullName": "An",
      "chipId": "sim-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaa1",
      "baseKmh": 11.2,
      "speedKmh": 10.8,
      "courseKm": 0.4,
      "motion": "Racing",
      "sos": false,
      "validFinish": false,
      "resultSeconds": null,
      "finishSimSeconds": null,
      "splits": [
        {
          "checkpointId": "start",
          "name": "Start",
          "km": 0,
          "simSeconds": 0
        }
      ]
    }
  ],
  "ranking": [],
  "checkpointLeaders": [
    {
      "checkpointId": "start",
      "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "bib": "A001",
      "simSeconds": 0
    }
  ],
  "log": [
    { "sequence": 1, "simSeconds": 0, "kind": "START", "subject": "run" }
  ]
}
```

Giá trị chuỗi lấy từ `ToString()` của enum và các hằng trong session:

| Field | Giá trị |
| --- | --- |
| `phase` | `Ready`, `Running`, `Paused`, `Finished` |
| `commitState` | `Idle`, `Committing`, `Partial`, `Committed` |
| `motion` | `Racing`, `Finished`, `Dnf` |
| `log[].kind` | `START`, `PAUSE`, `RESUME`, `PLAYBACK`, `SPEED`, `SOS`, `DNF`, `CHIP`, `PASSAGE`, `FRAUD` |

`ranking` chỉ gồm VĐV `validFinish` và có cả `resultSeconds` lẫn `finishSimSeconds`. Sort: `resultSeconds`, rồi `finishSimSeconds`, rồi `registrationId`. `rank` bắt đầu từ 1. `resultSeconds` là giây làm tròn AwayFromZero. `finishSimSeconds` là giây thô, có thể `null`. `log` là tối đa 100 dòng cuối. `discardRequired` true khi đã Finished và còn về đích hợp lệ chưa nằm trong `committedRegistrationIds`.

Đồng hồ server tick 250 ms. Mỗi bước giả lập là 0,1 giây nhân `playbackRate`. Snapshot chỉ được đẩy khi có bước mới; `version` tăng 1 mỗi lần trạng thái đổi.

### 2.4 Body điều khiển

```json
{ "runnerCount": 10, "seed": 42 }
```

```json
{ "rate": 20 }
```

```json
{ "confirmDiscard": true, "seed": 99, "runnerCount": null }
```

```json
{ "kmh": 0 }
```

```json
{ "chipId": "chip-demo-2" }
```

```json
{ "checkpointId": "cp-2" }
```

`runnerCount`, `seed`, `runnerCount` trên new có thể `null`. `confirmDiscard` thiếu thì là `false`.

### 2.5 Commit response

```json
{
  "accepted": true,
  "refusalReason": null,
  "items": [
    {
      "registrationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
      "outcome": "CommittedThisRun",
      "reason": null,
      "resultSeconds": 1234
    }
  ],
  "snapshot": {
    "runId": "33333333-3333-4333-8333-333333333333",
    "generation": 1,
    "version": 12,
    "commitState": "Committed",
    "phase": "Finished"
  }
}
```

`snapshot` trong response thật là đủ field như mục 2.3. `refusalReason` và `reason` có thể `null`. `resultSeconds` có thể `null`.

### 2.6 TypeScript tối thiểu

```ts
export type SimulationPhase = "Ready" | "Running" | "Paused" | "Finished";
export type CommitState = "Idle" | "Committing" | "Partial" | "Committed";
export type AthleteMotion = "Racing" | "Finished" | "Dnf";
export type CommitOutcome =
  | "CommittedThisRun"
  | "AlreadyRecordedElsewhere"
  | "Skipped"
  | "Retryable"
  | "Unknown";

export interface SimulationPreview {
  eventId: string;
  distanceId: string;
  title: string;
  distanceKm: number;
  eligibleCount: number;
  courseReady: boolean;
  courseError: string | null;
  polyline: { lat: number; lng: number }[];
  checkpoints: {
    id: string;
    name: string;
    km: number;
    lat: number;
    lng: number;
    type: string;
  }[];
  activeRunId: string | null;
}

export interface SimulationSnapshot {
  runId: string;
  generation: number;
  version: number;
  eventId: string;
  distanceId: string;
  phase: SimulationPhase;
  commitState: CommitState;
  seed: number;
  playbackRate: number;
  simulatedSeconds: number;
  distanceKm: number;
  title: string;
  discardRequired: boolean;
  committedRegistrationIds: string[];
  polyline: { lat: number; lng: number }[];
  checkpoints: SimulationPreview["checkpoints"];
  athletes: {
    registrationId: string;
    bib: string;
    fullName: string;
    chipId: string;
    baseKmh: number;
    speedKmh: number;
    courseKm: number;
    motion: AthleteMotion;
    sos: boolean;
    validFinish: boolean;
    resultSeconds: number | null;
    finishSimSeconds: number | null;
    splits: {
      checkpointId: string;
      name: string;
      km: number;
      simSeconds: number;
    }[];
  }[];
  ranking: {
    rank: number;
    registrationId: string;
    bib: string;
    fullName: string;
    resultSeconds: number;
    finishSimSeconds: number;
  }[];
  checkpointLeaders: {
    checkpointId: string;
    registrationId: string;
    bib: string;
    simSeconds: number;
  }[];
  log: { sequence: number; simSeconds: number; kind: string; subject: string }[];
}

export interface CreateRunBody {
  runnerCount?: number | null;
  seed?: number | null;
}

export interface PlaybackBody {
  rate: number;
}

export interface RunControlBody {
  confirmDiscard?: boolean;
  seed?: number | null;
  runnerCount?: number | null;
}

export interface SpeedBody {
  kmh: number;
}

export interface ChipBody {
  chipId: string;
}

export interface PassageBody {
  checkpointId: string;
}

export interface SimulationCommitResponse {
  accepted: boolean;
  refusalReason: string | null;
  items: {
    registrationId: string;
    outcome: CommitOutcome;
    reason: string | null;
    resultSeconds: number | null;
  }[];
  snapshot: SimulationSnapshot;
}
```

### 2.7 `activeRunId`, `runId`, `generation`, `version`

| Trường | Ý nghĩa |
| --- | --- |
| `activeRunId` | `runId` của lượt trong RAM của đúng `(eventId, distanceId)`. `null` khi process chưa có lượt. Vẫn trả lượt đã Finished. |
| `runId` | Id lượt hiện tại. Reset và new tạo GUID mới và xóa id cũ. GET id cũ → 400 `Không tìm thấy lượt giả lập.` |
| `generation` | Bắt đầu ở 1. Tăng 1 khi reset hoặc new thay engine. |
| `version` | Bắt đầu ở 1. Tăng 1 mỗi lần đổi trạng thái, gồm nhịp đồng hồ và lúc commit. Reset/new đặt lại `version` = 1 trên `runId` mới. |

Tải lại trang: GET preview. Có `activeRunId` thì GET snapshot và `JoinRun` id đó. Không POST runs chỉ vì reload.

Đổi cự ly: preview của cặp mới có `activeRunId` riêng. Lượt cự ly kia không bị xóa.

Hai client POST runs cùng cặp nhận cùng `runId`, cùng `generation`, cùng `version` nếu không có thay đổi xen giữa. Client thứ hai không được seed mới.

`RunReplaced` báo id cũ đã chết. Áp snapshot mới chỉ khi `runId` khớp lượt đang theo, rồi so `generation`, rồi so `version`. Snapshot cùng `runId` mà `version` nhỏ hơn hoặc bằng bản đã vẽ thì bỏ. `JoinRun` và broadcast có thể tới lệch thứ tự; cả hai đều mang đủ ba số này.

POST runs khi lượt đã tồn tại không phát broadcast mới. Client khác vẫn cần `JoinRun` để nhận các snapshot sau.

### 2.8 `chipId`

Chip mặc định khi tạo lượt, reset và new:

```text
sim-{registrationId:N}
```

`N` là toàn bộ GUID, 32 ký tự hex, không dấu gạch. Ví dụ đăng ký `aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1` thành `sim-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaa1`. Hàm là `RaceSimulationEngine.ChipFor`. Chuỗi này chỉ nằm trong session. Không đọc chip từ đăng ký hay thiết bị.

`POST …/fraud/chip` gán đúng chuỗi client gửi, sau khi trim, vào `athletes[].chipId` của lượt. Đó là chip demo chủ đích. Hai VĐV cùng một `chipId` thì passage của chip đó không được nhận, vì chip phải thuộc đúng một VĐV. Log `FRAUD` ghi `chip không xác định duy nhất`.

---

## 3. SignalR

### 3.1 Hub trên host API

| Mục | Giá trị |
| --- | --- |
| URL | `http://localhost:5080/api/v1/simulation/hub` |
| Negotiate | `POST /api/v1/simulation/hub/negotiate?negotiateVersion=1` do client `@microsoft/signalr` gọi |
| Client → server | `JoinRun(runId)`, `LeaveRun(runId)`. `runId` là GUID. |
| Server → client | `Snapshot`, `RunReplaced` |
| Long polling | Server đặt `PollTimeout` 15 giây. Transport thực tế do negotiate chọn. |
| Group | Server tự thêm connection vào group `sim:{runId:N}`. Frontend gọi `JoinRun`, không join bằng tên group. |

Auth hub giống REST. `UseHubAccessToken` chỉ chạy khi path bắt đầu bằng `/api/v1/simulation/hub` và chưa có token ở header: khi đó lấy query `access_token`.

Test `SimulationHubTests.Join_checks_permission_and_reconnect_receives_the_current_snapshot` nối client .NET thẳng vào hub in-process, `AccessTokenProvider` đưa JWT, rồi `JoinRun` nhận `Snapshot`. Đó là test, không phải browser UserWeb.

### 3.2 Payload

`Snapshot` là nguyên `SimulationSnapshot` ở mục 2.3.

`RunReplaced`:

```json
{
  "previousRunId": "33333333-3333-4333-8333-333333333333",
  "previousGeneration": 1,
  "runId": "44444444-4444-4444-8444-444444444444",
  "generation": 2,
  "version": 1
}
```

Sự kiện này gửi tới group của `previousRunId`. Snapshot của `runId` mới gửi tới group mới. Client còn ở group cũ chỉ thấy `RunReplaced` cho đến khi `JoinRun` id mới.

`JoinRun` một id đã bị thay trong lúc đang vào group: connection bị gỡ khỏi group, `HubException` với câu

```text
Lượt giả lập đã được thay thế. Đọc lại preview để lấy activeRunId.
```

`JoinRun` một id đã không còn trước đó: `HubException` `Không tìm thấy lượt giả lập.`

### 3.3 Cách gắn vào lượt

```ts
import {
  HubConnectionBuilder,
  HubConnectionState,
} from "@microsoft/signalr";
import type { SimulationSnapshot } from "./simulation-types";

type RunReplaced = {
  previousRunId: string;
  previousGeneration: number;
  runId: string;
  generation: number;
  version: number;
};

type Applied = { runId: string; generation: number; version: number };

let applied: Applied | null = null;
let followedRunId: string | null = null;

function isNewer(next: Applied): boolean {
  if (!applied) return true;
  if (next.runId !== applied.runId) return false;
  if (next.generation !== applied.generation) return next.generation > applied.generation;
  return next.version > applied.version;
}

function applySnapshot(snapshot: SimulationSnapshot) {
  if (followedRunId && snapshot.runId !== followedRunId) return;
  const next = {
    runId: snapshot.runId,
    generation: snapshot.generation,
    version: snapshot.version,
  };
  if (applied && !isNewer(next)) return;
  applied = next;
  followedRunId = snapshot.runId;
}

export function createSimulationConnection(apiBase: string) {
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiBase}/api/v1/simulation/hub`, {
      accessTokenFactory: () => readAccessTokenAlreadyUsedByRest(),
    })
    .withAutomaticReconnect()
    .build();

  connection.on("Snapshot", (snapshot: SimulationSnapshot) => {
    applySnapshot(snapshot);
  });

  connection.on("RunReplaced", async (notice: RunReplaced) => {
    if (followedRunId && notice.previousRunId !== followedRunId) return;
    applied = null;
    followedRunId = notice.runId;
    if (connection.state === HubConnectionState.Connected) {
      await connection.invoke("LeaveRun", notice.previousRunId);
      const snapshot = await connection.invoke<SimulationSnapshot>(
        "JoinRun",
        notice.runId,
      );
      applySnapshot(snapshot);
    }
  });

  connection.onreconnected(async () => {
    if (!followedRunId) return;
    const snapshot = await connection.invoke<SimulationSnapshot>(
      "JoinRun",
      followedRunId,
    );
    applySnapshot(snapshot);
  });

  return connection;
}

async function openRun(
  connection: { invoke: (method: string, ...args: unknown[]) => Promise<unknown> },
  runId: string,
) {
  followedRunId = runId;
  const snapshot = await connection.invoke("JoinRun", runId);
  applySnapshot(snapshot as SimulationSnapshot);
}

declare function readAccessTokenAlreadyUsedByRest(): string;
```

`JoinRun` trả snapshot cho caller và cũng phát `Snapshot`. Hàm `applySnapshot` giữ bản có `version` lớn hơn.

Reconnect phải gọi lại `JoinRun`. Group gắn với connection cũ. `withAutomaticReconnect` không tự join.

Mất `RunReplaced` vì đang ngắt: `JoinRun` id cũ lỗi. GET preview lấy `activeRunId`, rồi join id đó và bỏ snapshot của id cũ.

`readAccessTokenAlreadyUsedByRest` là chỗ UserWeb đã có token cho REST. Ví dụ chỉ chỉ ra chữ ký `accessTokenFactory`. Chưa xác nhận UserWeb lấy token từ đâu. Nếu token chỉ nằm trong cookie httpOnly, JavaScript của browser không đưa được nó vào `accessTokenFactory`; khi đó cần một đường BFF hoặc proxy hub đã được team frontend kiểm tra. Đường đó chưa có trong repo này.

### 3.4 Gateway, BFF, browser

| Đường | Việc đã thấy trong source | Việc UserWeb còn kiểm tra |
| --- | --- | --- |
| Thẳng `http://localhost:5080/api/v1/simulation/hub` | Hub map cùng lúc với REST khi cờ bật. Test .NET join được bằng access token. | Browser thật, CORS origin của Next.js, và negotiate từ trang UserWeb. |
| Gateway `http://localhost:5000` | `ReverseProxy` chuyển `/api/**` tới `http://localhost:5080/`. CORS gateway: `http://localhost:3000` và `http://localhost:3001`. Rate limit policy `gateway` 200 request / phút. | WebSocket hoặc long polling của SignalR có đi qua YARP hay không. Chưa có test gateway cho hub. |
| Next.js rewrite / BFF | Không có source UserWeb trong repo. | `next.config` base URL, route handler nào gắn `Authorization`, origin có nằm trong `ALLOWS_CORS` hay không. Mặc định host gồm `http://localhost:3000`, `http://localhost:3001`, `http://localhost:51356`. |

Host API cũng bật CORS credentials cho các origin đó. Xác nhận bằng một request từ đúng origin UserWeb đang chạy.

---

## 4. Commit và leaderboard

### 4.1 Hai bảng xếp hạng

`snapshot.ranking` là hạng nháp trong RAM. Thứ tự là giây giả lập đã làm tròn. Nó đổi khi reset, new, DNF hoặc process tắt.

Leaderboard của giải là dữ liệu đăng ký đã Finished:

```http
GET /api/v1/event/{eventId}/distances/{distanceId}/leaderboard?page=1&pageSize=50
```

Query `gender` tùy chọn. Bỏ trống hoặc `All` thì không lọc giới tính. Route này nằm ở nhóm public, rate limit `public`, không gắn `RequireAuthorization` trong `EventModule`.

Một dòng vào leaderboard khi đăng ký cùng `distanceId`, `status` finished, `raceStatus` finished, và có `resultSeconds`. Sort theo `resultSeconds` tăng dần. Response không có `registrationId`. Các field là `rank`, `eventName`, `distanceName`, `bib`, `fullName`, `gender`, `startAtUtc`, `finishAtUtc`, `totalTime`, `pace`.

`totalTime` dạng `HH:mm:ss` từ `resultSeconds`. `pace` là phút/km của cự ly.

Hạng nháp và hạng leaderboard có thể lệch khi hòa giây, vì leaderboard chỉ sort `resultSeconds`.

Thành tích cá nhân của runner, sau khi đã ghi, là `GET /api/v1/event/leaderboards/mine` (JWT `PublicUser`). Đó là API khác với bảng của cự ly.

### 4.2 HTTP 200 của commit

POST commit trả **200** cả khi từ chối cả batch và khi từng VĐV bị bỏ qua. Frontend kết luận đã ghi chỉ khi:

1. HTTP là 200, và
2. `accepted === true`, và
3. `refusalReason` là `null`, và
4. từng VĐV cần ghi có `items[].outcome === "CommittedThisRun"`.

`snapshot.commitState` sau batch được nhận:

| `commitState` | Khi nào |
| --- | --- |
| `Idle` | Chưa có về đích hợp lệ nào nằm trong danh sách đã commit của lượt, hoặc không có về đích hợp lệ. |
| `Committing` | Đang gọi cổng ghi. Snapshot này có thể tới qua SignalR trước response. |
| `Partial` | Một phần về đích hợp lệ đã `CommittedThisRun`. |
| `Committed` | Mọi về đích hợp lệ của lượt đã `CommittedThisRun`. |

DNF không tính là về đích hợp lệ. `validFinish` trên snapshot đã loại `motion === "Dnf"`.

`accepted: false` thì không có item mới từ cổng ghi. `commitState` quay về trạng thái trước lần gọi. `items` chỉ còn các VĐV lượt này đã commit từ trước, outcome `CommittedThisRun`.

`refusalReason` khi cả batch bị từ chối:

| Câu | Điều kiện |
| --- | --- |
| `Giả lập đang tắt.` | `Simulation:Enabled` không còn parse thành true. |
| `Không xác định được mốc UTC. EventDate cùng StartAt/StartTime chỉ là ngày và giờ đồng hồ, không có offset. Đặt Simulation__StartTimeZone đúng nghĩa của giờ đã lưu (UTC nếu giờ đó đã là UTC). Hệ thống không tự chọn múi giờ.` | `StartTimeZone` trống. |
| `Simulation__StartTimeZone không phải mã múi giờ hợp lệ.` | Id múi giờ hệ điều hành không nhận. |
| `Không tìm thấy giải hoặc cự ly.` | Không đọc được đồng hồ của cặp id. |
| `Giải/cự ly chưa cấu hình giờ xuất phát.` | Cả `distance.startAt` và `event.startTime` đều trống. |

Giờ xuất phát chính thức là `event.EventDate` cộng `distance.StartAt`, nếu `StartAt` trống thì dùng `event.StartTime`. Giờ đó là `DateTimeKind.Unspecified`, được hiểu trong `Simulation:StartTimeZone`, rồi đổi sang UTC. `finishUtc = startUtc + resultSeconds`. Writer ghi `finishedAtUtc`, `resultSeconds`, `status = Finished`, `raceStatus = Finished`. **Không** ghi `startedAtUtc`. Vì vậy `startAtUtc` trên leaderboard của dòng này là `null`. `finishAtUtc` là mốc UTC vừa tính.

Điều kiện ghi một dòng, kiểm tra lại lúc commit: đúng `eventId` và `distanceId`, BIB trùng BIB đã đóng trong lượt, `confirmed`, `raceStatus` `NotStarted`, chưa start, chưa finish, chưa có kết quả, BIB không trắng. BIB gửi xuống là BIB lúc tạo lượt, kể cả khi đăng ký đã đổi BIB sau đó.

### 4.3 Từng `outcome`

| `outcome` | `reason` thường gặp | Việc đã xảy ra | Frontend |
| --- | --- | --- | --- |
| `CommittedThisRun` | `null` | Writer cập nhật đúng 1 dòng, hoặc lượt này đã ghi VĐV đó ở lần trước. `resultSeconds` của lần ghi mới là số đã lưu. Lần gọi sau chỉ nhắc lại VĐV đã ghi thì `resultSeconds` là giây nháp đã làm tròn. | Coi VĐV này đã xong với lượt. Tải lại leaderboard. Không gửi finish lần nữa; server cũng không đưa họ vào draft của lần commit sau. |
| `AlreadyRecordedElsewhere` | `Đăng ký đã có kết quả hoặc đã hoàn thành.` | Đã có finish từ nguồn khác. Lượt không ghi đè. Không đưa vào `committedRegistrationIds`. | Hiện là đã có kết quả. Tải leaderboard. Gọi lại commit sẽ phân loại lại cùng outcome, không tạo thêm dòng. |
| `Skipped` | `Đăng ký không còn đủ điều kiện ghi finish demo.` hoặc câu cụ thể: không thấy đăng ký, đổi event/cự ly, đổi BIB, giây không hợp lệ | Không ghi. Không đưa vào danh sách đã commit của lượt. | Hiện `reason`. Sửa dữ liệu đăng ký nếu còn hợp lệ rồi mới commit lại. Gọi lại trong khi dữ liệu vẫn sai thì vẫn `Skipped`. |
| `Retryable` | `Chưa ghi được. Có thể thử lại.` | Chưa xác nhận đã ghi. Không nằm trong tập unresolved. | POST commit lại **cùng `runId`**. Server chỉ gửi các VĐV chưa `CommittedThisRun`. |
| `Unknown` | `Không rõ đã ghi hay chưa. Hãy đọc lại đăng ký trước khi thử lại.` hoặc `Đã có kết quả nhưng không xác định được nguồn ghi. Giữ nguyên dữ liệu.` | Lệnh ghi có thể đã tới database rồi lỗi giữa chừng, hoặc lần trước là `Unknown` và nay đã thấy kết quả. Server không ghi đè khi đã có kết quả mà không biết nguồn. | Đọc đăng ký (`status`, `raceStatus`, `finishedAtUtc`, `resultSeconds`) trước khi commit lại. Có kết quả thì dừng ở dòng đó và tải leaderboard. Chưa có kết quả thì POST commit cùng `runId`; server đánh dấu id này là unresolved. |

Câu skip cụ thể từ committer:

- `Không tìm thấy đăng ký.`
- `Đăng ký đã đổi event hoặc cự ly sau khi kiểm tra. Không ghi với dữ liệu mới.`
- `Đăng ký đã đổi BIB sau khi kiểm tra. Không ghi với dữ liệu mới.`

Hủy giữa batch: VĐV chưa xử lý thành `Retryable`, hoặc `Unknown` nếu id đó đã từng `Unknown`. Phần đã `CommittedThisRun` trong cùng response được giữ.

Ném lỗi trước khi có batch (chưa Finished, mất quyền, lỗi không bắt trong committer): HTTP không phải 200. `commitState` không kẹt ở `Committing`. Lỗi domain là 400. Lỗi khác đi qua middleware thành 500. Không đọc `accepted` từ body lỗi.

Gọi lại commit cùng lượt là cùng `POST /runs/{runId}/commit`, không body, không idempotency key. Hai request chồng nhau: request sau đợi khóa, không chạy ghi song song. Khi request trước đã ghi hết, request sau không gọi writer nữa và trả các item `CommittedThisRun`.

### 4.4 Tải lại leaderboard

Dùng `eventId` và `distanceId` trên snapshot của lượt vừa commit, không lấy từ cự ly đang chọn nếu user đã đổi cự ly.

```http
GET /api/v1/event/{eventId}/distances/{distanceId}/leaderboard?page=1&pageSize=50
```

Đối chiếu BIB. Dòng mới có `finishAtUtc` và `totalTime`. `startAtUtc` null với finish do giả lập ghi, vì commit không set giờ xuất phát.

`Committed` trên snapshot vẫn là trạng thái lượt nháp. Leaderboard chỉ đổi sau khi database có dòng Finished.

### 4.5 SOS, DNF, fraud của lượt giả lập

Cả ba đường này chỉ sửa session và trả snapshot. Test `Sos_and_dnf_stay_inside_the_session` khẳng định chúng không gọi cổng commit.

| Thao tác | Trong snapshot | Database, leaderboard, SOS thiết bị, checkpoint scan |
| --- | --- | --- |
| SOS | `athletes[].sos = true`. VĐV vẫn chạy. Có thể vẫn vào `ranking` nếu về đích hợp lệ. Log `SOS`. | Không ghi. Không tạo alert SOS. |
| DNF | `motion = "Dnf"`. `validFinish` false. Ra khỏi `ranking` và khỏi draft commit. Log `DNF`. Nếu mọi VĐV còn lại đã Finished hoặc Dnf thì `phase` thành `Finished`. | Không gọi API race-status. `raceStatus` đăng ký không thành `dnf`. Không có trên leaderboard nhờ DNF giả lập. |
| Fraud chip | Đổi `chipId` trong lượt. Log `CHIP`. | Không ghi chip của đăng ký. |
| Fraud passage | Nếu đúng thứ tự checkpoint kế tiếp và chip còn duy nhất: thêm split. Checkpoint `finish` đặt `validFinish` và `finishSimSeconds` theo đồng hồ giả lập. Log `PASSAGE`. Sai id, sai thứ tự hoặc chip trùng: không nhận split, log `FRAUD`, HTTP vẫn 200. | Không ghi `CheckpointScan`. Thành tích chỉ vào leaderboard nếu sau đó commit ghi finish của VĐV đó. |

DNF giả lập khác `PATCH /api/v1/event/{eventId}/registrations/{registrationId}/race-status`. API đó mới ghi DNS/DNF/DQ chính thức. Luồng giả lập không gọi nó.

---

## 5. Checklist bàn giao

Đánh dấu theo bằng chứng đang có. Không có source UserWeb và không có log demo browser trong repo, nên không hạng mục nào dưới đây là đã nghiệm thu frontend hoặc gateway.

| Hạng mục | Đã xác nhận qua demo trực tiếp | Đối chiếu source hoặc test | Cần kiểm tra khi tích hợp UserWeb |
| --- | --- | --- | --- |
| GET preview không tạo lượt, `activeRunId` null rồi có giá trị sau POST runs | Chưa có log demo HTTP trong repo | `Preview_reports_the_active_run_without_creating_one` | Mở trang và đổi cự ly chỉ gọi GET preview. Không POST runs, không PUT course. |
| Hai client cùng `runId` nhận `Snapshot` | Chưa | Hub phát vào group `sim:{runId}`. Test một connection nhận snapshot sau start. Hai browser chưa chạy. | Hai phiên UserWeb `JoinRun` cùng id, đổi pause ở một phía, phía kia thấy `phase`. |
| Reconnect gọi lại `JoinRun` và nhận đúng `version` | Chưa trên browser | Test .NET stop/start rồi `JoinRun`, nhận snapshot mới nhất, cùng `generation` | `onreconnected` của trang UserWeb gọi `JoinRun`. Rớt mạng giữa lúc `Running`. |
| `RunReplaced` rồi bỏ snapshot cũ | Chưa trên browser | Reset/new phát `RunReplaced` vào group cũ rồi snapshot vào group mới. `Join` id cũ trả câu đã được thay thế. | UI đổi sang `runId` mới, `LeaveRun` id cũ, không vẽ thêm frame của id cũ. |
| Commit đọc `accepted` và `outcome` | Chưa có lần gọi HTTP thật trong task này | `SimulationResultCommitter`, `SimulationSessionBook.CommitAsync`, test Partial / Unknown / Skipped | Bảng nháp không đổi thành “đã ghi” khi `accepted` false hoặc outcome khác `CommittedThisRun`. |
| Gọi lại commit cùng lượt | Chưa gọi host | Test retry `Retryable` rồi lần sau không gọi writer khi đã đủ `CommittedThisRun`. Unknown được gửi lại kèm unresolved. | Nút ghi lại dùng cùng `runId`. Với `Unknown`, đọc đăng ký trước. |
| Leaderboard đúng cặp id | Chưa | `GET /api/v1/event/{eventId}/distances/{distanceId}/leaderboard` lọc Finished + `resultSeconds` | Sau `CommittedThisRun`, refetch đúng id trên snapshot. SOS/DNF/fraud không làm đổi bảng này nếu chưa commit finish. |
| Token trên browser và đường gateway / BFF | Chưa | REST Bearer. Hub đọc `access_token` khi header trống. Gateway chỉ thấy cấu hình YARP. | Xác nhận origin, chỗ lấy `accessToken` mà không đọc cookie httpOnly và không thêm `localStorage`, negotiate hub từ trang Next.js, và URL đang là `5080` hay `5000`. |
| Múi giờ commit | Chưa gọi commit trên host demo | `SimulationOfficialStartResolver`. `.env.example` ghi `UTC` khi giờ đã lưu là UTC. | `Simulation__Enabled` và `Simulation__StartTimeZone` của process mà UserWeb sẽ gọi. Một commit thử trên dữ liệu demo mà team được phép ghi, rồi so `finishAtUtc`. |

---

## 6. Nguồn đã đối chiếu

- `src/Hosts/RaceDay.Api/Program.cs` — map module khi `Simulation:Enabled`.
- `src/Hosts/RaceDay.Api/Properties/launchSettings.json` — `http://localhost:5080`.
- `src/Gateways/RaceDay.ApiGateway/appsettings.json`, `Program.cs`, `Properties/launchSettings.json` — proxy và `http://localhost:5000`.
- `.env.example` — tên biến `Simulation__Enabled`, `Simulation__StartTimeZone`.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Api/SimulationModule.cs` — route, body, 409, query `access_token`.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Api/SimulationHub.cs` — `JoinRun`, `LeaveRun`, `Snapshot`, `RunReplaced`.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Api/SimulationClockHostedService.cs` — tick 250 ms.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Application/SimulationSessionBook.cs` — preview, create, reset, new, commit, `activeRunId`.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Application/SimulationPorts.cs` — quyền, `SimulationHubJoin`.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Contracts/SimulationDtos.cs` — field JSON.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Domain/RaceSimulationEngine.cs` — phase, chip `sim-{guid:N}`, SOS, DNF, fraud.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Domain/SimulationModel.cs` — enum và rule course.
- `src/Modules/Simulation/RaceDay.Modules.Simulation.Infrastructure/SimulationFieldSource.cs` — VĐV hợp lệ và chip mặc định.
- `src/Modules/Event/RaceDay.Modules.Event.Application/Simulation/SimulationResultCommitter.cs` — múi giờ và outcome.
- `src/Modules/Event/RaceDay.Modules.Event.Contracts/Simulation/SimulationContracts.cs` — tên outcome.
- `src/Modules/Event/RaceDay.Modules.Event.Infrastructure/Simulation/SimulationDemoAdapters.cs` — cờ, đọc đăng ký, writer finish.
- `src/Modules/Event/RaceDay.Modules.Event.Infrastructure/Events/EventSosOpsAccessQuery.cs` — chủ giải hoặc Coordinator Active.
- `src/Modules/Event/RaceDay.Modules.Event.Api/EventModule.cs` — distances, đăng ký, leaderboard.
- `src/Modules/Event/RaceDay.Modules.Event.Application/Registrations/GetDistanceLeaderboardQuery.cs`.
- `src/Modules/Map/RaceDay.Modules.Map.Api/MapModule.cs` và `CourseDtos.cs` — `checkpoints[].id`.
- `src/Shared/RaceDay.Shared.Web/Auth/AuthPolicies.cs`, `DependencyInjection.cs` — `HumanUser`.
- `src/Shared/RaceDay.Shared.Web/Middleware/ExceptionHandlingMiddleware.cs` — `{ "error" }`.
- `tests/RaceDay.Modules.Simulation.UnitTests/SimulationHubTests.cs`
- `tests/RaceDay.Modules.Simulation.UnitTests/SimulationSessionBookTests.cs`
- `tests/RaceDay.Modules.Event.UnitTests/Simulation/SimulationCommitTests.cs`


-- Local fixture: staff QR check-in + out-of-zone alert test.
-- Re-run any time: it moves the race start to "now + 30 minutes" (Vietnam time),
-- so the race-day window (opens 1 hour before start) is open right away.
-- Needs the organizer demo account cuong.le (deploy/clear-and-seed.ps1); creates its own staff accounts.
-- Password of all accounts: MatKhau@123
--
--   Organizer (UserWeb, Nhân sự giải → "QR check-in nhân sự"): cuong.le
--   Race-staff (RaceDay Staff app): checkin.staff1 → Start checkpoint, checkin.staff2 → mobile along the course
--   Photographer (RaceDay Staff app): checkin.photo1 → Water station 2.5 km
--
-- Zones are in Hà Nội (Hồ Hoàn Kiếm): checkpoint = 500 m radius, mobile duty = 100 m around the course.
-- Anyone sending GPS from elsewhere (or the Android emulator default location) is "outside"
-- and gets the out-of-zone alert on their own phone only. Set the emulator location to
-- 21.0285, 105.8542 to be inside the Start zone, then move it away to trigger the alert.
\set ON_ERROR_STOP on
BEGIN;

DO $check$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM iam.users WHERE username = 'cuong.le' AND status = 1) THEN
    RAISE EXCEPTION 'Thiếu tài khoản organizer cuong.le. Chạy deploy/clear-and-seed.ps1 trước.';
  END IF;
  IF EXISTS (SELECT 1 FROM event.race_events
      WHERE "Id" = '5aff0000-0000-4000-8000-000000000200'
        AND "OrganizerUserId" <> (SELECT user_id FROM iam.users WHERE username = 'cuong.le')) THEN
    RAISE EXCEPTION 'Event test ID đã thuộc organizer khác.';
  END IF;
END $check$;

-- Staff accounts for this fixture (Race-staff 4444…, Photographer 5555…).
INSERT INTO iam.users (
  user_id, email, username, role_id, password_hash, full_name, phone,
  date_of_birth, gender, status, email_verified, created_at, updated_at,
  must_change_password, id_number
) VALUES
('5aff0000-0014-4000-8000-000000000001', 'checkin.staff1@raceday.local', 'checkin.staff1', '44444444-4444-4444-4444-444444444444',
 'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
 'Trần Staff Start', '0915000001', DATE '1996-03-10', 1, 1, true, now(), now(), false, NULL),
('5aff0000-0014-4000-8000-000000000002', 'checkin.staff2@raceday.local', 'checkin.staff2', '44444444-4444-4444-4444-444444444444',
 'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
 'Lý Staff Cơ Động', '0915000002', DATE '1997-06-21', 2, 1, true, now(), now(), false, NULL),
('5aff0000-0014-4000-8000-000000000003', 'checkin.photo1@raceday.local', 'checkin.photo1', '55555555-5555-5555-5555-555555555555',
 'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
 'Đỗ Photo Trạm Nước', '0915000003', DATE '1995-11-02', 1, 1, true, now(), now(), false, NULL)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
  user_id, description, contact_email, contact_phone,
  verification_status, verified_at, verified_by
)
SELECT user_id, 'Check-in QR test fixture', email, phone, 2, now(), 'local-seed'
FROM iam.users WHERE user_id::text LIKE '5aff0000-0014-4000-8000-%'
ON CONFLICT (user_id) DO NOTHING;

-- Race start = now + 30 minutes, in race-local (Vietnam) wall-clock time.
CREATE TEMP TABLE race_clock ON COMMIT DROP AS
SELECT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + interval '30 minutes') AS starts_local;

INSERT INTO event.race_events (
  "Id", "OrganizerUserId", "Name", "Tagline", "Location", coord_lat, coord_lng,
  "EventDate", "EndDate", "StartTime", "Status", org_name, org_email, org_phone,
  org_verified, "TotalRunnersCap", "TicketPricingJson", "MerchandiseJson",
  "RulesJson", "RewardsJson", "Description", "CreatedAtUtc", "UpdatedAtUtc",
  "RegistrationOpensAtUtc", "RegistrationClosesAtUtc"
)
SELECT '5aff0000-0000-4000-8000-000000000200', u.user_id,
  'RaceDay Test Check-in QR & Out-zone', 'Giải thử check-in nhân sự bằng QR và cảnh báo rời vùng',
  'Hồ Hoàn Kiếm, Hà Nội', 21.0285, 105.8542,
  c.starts_local::date, c.starts_local::date, date_trunc('minute', c.starts_local)::time, 'Closed',
  'RaceDay Demo', 'cuong.le@raceday.vn', '0904000004', true, 20,
  '{"currency":"VND"}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
  'Dữ liệu local để kiểm thử QR check-in nhân sự và cảnh báo rời vùng hoạt động.',
  now(), now(), now() - interval '30 days', now() - interval '1 day'
FROM iam.users u CROSS JOIN race_clock c
WHERE u.username = 'cuong.le'
ON CONFLICT ("Id") DO UPDATE SET
  "EventDate" = EXCLUDED."EventDate",
  "EndDate" = EXCLUDED."EndDate",
  "StartTime" = EXCLUDED."StartTime",
  "Status" = EXCLUDED."Status",
  "UpdatedAtUtc" = now();

INSERT INTO event.event_distances (
  "Id", "EventId", "Type", "Name", "DistanceKm", "Capacity", "RegisteredCount",
  "Price", "Currency", "Color", "RouteGeometryJson", "StartAt", "EndAt",
  "BibPrefix", "BibRangeStart", "BibRangeEnd"
)
SELECT '5aff0000-0000-4000-8000-000000000201',
  '5aff0000-0000-4000-8000-000000000200', '5KM', 'Check-in Test 5K', 5, 20, 0,
  0, 'VND', '#F97316', '{}'::jsonb,
  date_trunc('minute', c.starts_local)::time, (date_trunc('minute', c.starts_local) + interval '3 hours')::time,
  '8', 1, 20
FROM race_clock c
ON CONFLICT ("Id") DO UPDATE SET
  "StartAt" = EXCLUDED."StartAt",
  "EndAt" = EXCLUDED."EndAt";

INSERT INTO map.race_courses (
  "Id", "EventId", "DistanceId", "Title", "DistanceKm", "CourseJson",
  "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc"
)
SELECT '5aff0000-0000-4000-8000-000000000202',
  '5aff0000-0000-4000-8000-000000000200',
  '5aff0000-0000-4000-8000-000000000201', 'Check-in Test 5K', 5,
  $course${
    "title":"Check-in Test 5K", "distanceId":"5aff0000-0000-4000-8000-000000000201",
    "distanceKm":5,
    "polyline":[
      {"lat":21.0285,"lng":105.8542},{"lat":21.036,"lng":105.862},
      {"lat":21.044,"lng":105.870},{"lat":21.052,"lng":105.878},
      {"lat":21.060,"lng":105.886},{"lat":21.068,"lng":105.894}
    ],
    "checkpoints":[
      {"id":"cp-0","name":"Start - Hồ Hoàn Kiếm","km":0,"lat":21.0285,"lng":105.8542,"type":"start"},
      {"id":"cp-2.5","name":"Tiếp nước 2.5 km","km":2.5,"lat":21.044795066863387,"lng":105.8707950668634,"type":"aid"},
      {"id":"cp-5","name":"Finish - 5 km","km":5,"lat":21.061232111928124,"lng":105.88723211192811,"type":"finish"}
    ]
  }$course$::jsonb,
  user_id, now(), now()
FROM iam.users WHERE username = 'cuong.le'
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO event.event_checkpoints (
  "Id", "DistanceId", "ExternalId", "Name", "Km", "Lat", "Lng", "Type", "SortOrder"
)
SELECT ('5aff0000-0013-4000-8000-' || lpad(cp.n::text, 12, '0'))::uuid,
  '5aff0000-0000-4000-8000-000000000201', cp.external_id, cp.name, cp.km,
  cp.lat, cp.lng, cp.type, cp.n
FROM (VALUES
  (1, 'cp-0', 'Start - Hồ Hoàn Kiếm', 0.0, 21.0285, 105.8542, 'Start'),
  (2, 'cp-2.5', 'Tiếp nước 2.5 km', 2.5, 21.044795066863387, 105.8707950668634, 'Aid'),
  (3, 'cp-5', 'Finish - 5 km', 5.0, 21.061232111928124, 105.88723211192811, 'Finish')
) cp(n, external_id, name, km, lat, lng, type)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO event.event_staff_members (
  "Id", "EventId", "Role", "Status", "FullName", "Email", "Phone", "UserId",
  "StationId", "StationLabel", "StationsJson", "CreatedAtUtc", "UpdatedAtUtc"
)
SELECT ('5aff0000-0012-4000-8000-' || lpad(s.n::text, 12, '0'))::uuid,
  '5aff0000-0000-4000-8000-000000000200', s.role, 'Active',
  u.full_name, u.email, u.phone, u.user_id,
  s.station_id, s.station_label,
  jsonb_build_array(jsonb_build_object('stationId', s.station_id, 'stationLabel', s.station_label)),
  now(), now()
FROM (VALUES
  (1, 'checkin.staff1', 'RaceStaff', 'map:5aff0000-0000-4000-8000-000000000201:cp-0', 'Start - Hồ Hoàn Kiếm'),
  (2, 'checkin.staff2', 'RaceStaff', 'duty:float', 'Cơ động dọc đường chạy'),
  (3, 'checkin.photo1', 'Photographer', 'map:5aff0000-0000-4000-8000-000000000201:cp-2.5', 'Tiếp nước 2.5 km')
) s(n, username, role, station_id, station_label)
JOIN iam.users u ON u.username = s.username
ON CONFLICT ("Id") DO UPDATE SET
  "Status" = 'Active',
  "StationId" = EXCLUDED."StationId",
  "StationLabel" = EXCLUDED."StationLabel",
  "StationsJson" = EXCLUDED."StationsJson",
  "UpdatedAtUtc" = now();

COMMIT;

SELECT e."Name", e."Status", e."EventDate", e."StartTime" AS start_local_vn,
  (e."EventDate" + e."StartTime") AT TIME ZONE 'Asia/Ho_Chi_Minh' - interval '1 hour' AS checkin_and_alerts_open_utc,
  now() AS now_utc
FROM event.race_events e WHERE e."Id" = '5aff0000-0000-4000-8000-000000000200';

SELECT s."Role", s."FullName", u.username, s."StationId"
FROM event.event_staff_members s JOIN iam.users u ON u.user_id = s."UserId"
WHERE s."EventId" = '5aff0000-0000-4000-8000-000000000200' ORDER BY s."Id";

-- To retime ANY existing race instead (replace <event-id>), e.g. start in 30 minutes:
-- UPDATE event.race_events
--   SET "EventDate" = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + interval '30 minutes')::date,
--       "StartTime" = date_trunc('minute', now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + interval '30 minutes')::time,
--       "UpdatedAtUtc" = now()
--   WHERE "Id" = '<event-id>';
-- UPDATE event.event_distances
--   SET "StartAt" = date_trunc('minute', now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + interval '30 minutes')::time
--   WHERE "EventId" = '<event-id>';

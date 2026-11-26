-- Local demo fixture for UserWeb staff checkpoint simulation.
-- Additive and repeatable: existing registrations and race outcomes are never reset.
-- Run through deploy/seed-staff-workspace.ps1 (Docker Postgres hoặc localhost:5433).
\set ON_ERROR_STOP on
BEGIN;

-- clear_and_seed có coord1/coord2/vol1/vol2; fixture này cần thêm coord3/coord4.
INSERT INTO iam.users (
  user_id, email, username, role_id, password_hash, full_name, phone,
  date_of_birth, gender, status, email_verified, created_at, updated_at,
  must_change_password, id_number
) VALUES
('a1444444-0003-4000-8000-000000000003', 'coord3@raceday.vn', 'coord3', '44444444-4444-4444-4444-444444444444',
 'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
 'Lê Điều Phối Ba', '0911000003', DATE '1995-04-12', 1, 1, true,
 TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00', false, NULL),
('a1444444-0004-4000-8000-000000000004', 'coord4@raceday.vn', 'coord4', '44444444-4444-4444-4444-444444444444',
 'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
 'Phạm Điều Phối Bốn', '0911000004', DATE '1992-09-21', 2, 1, true,
 TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00', false, NULL)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
  user_id, description, contact_email, contact_phone,
  verification_status, verified_at, verified_by
) VALUES
('a1444444-0003-4000-8000-000000000003', 'Race-staff seed coord3', 'coord3@raceday.vn', '0911000003',
 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1444444-0004-4000-8000-000000000004', 'Race-staff seed coord4', 'coord4@raceday.vn', '0911000004',
 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system')
ON CONFLICT (user_id) DO NOTHING;

DO $check$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM iam.users WHERE username = 'cuong.le' AND status = 1)
     OR (SELECT count(*) FROM iam.users WHERE username IN ('coord1','coord2','coord3','coord4','vol1')
         AND role_id = '44444444-4444-4444-4444-444444444444' AND status = 1) <> 5 THEN
    RAISE EXCEPTION 'Thiếu tài khoản organizer hoặc Race-staff demo. Chạy deploy/clear-and-seed.ps1 trước.';
  END IF;
  IF EXISTS (SELECT 1 FROM event.race_events
      WHERE "Id" = '5aff0000-0000-4000-8000-000000000100'
        AND "OrganizerUserId" <> (SELECT user_id FROM iam.users WHERE username = 'cuong.le')) THEN
    RAISE EXCEPTION 'Event test ID đã thuộc organizer khác.';
  END IF;
END $check$;

INSERT INTO event.race_events (
  "Id", "OrganizerUserId", "Name", "Tagline", "Location", coord_lat, coord_lng,
  "EventDate", "EndDate", "StartTime", "Status", org_name, org_email, org_phone,
  org_verified, "TotalRunnersCap", "TicketPricingJson", "MerchandiseJson",
  "RulesJson", "RewardsJson", "Description", "CreatedAtUtc", "UpdatedAtUtc",
  "RegistrationOpensAtUtc", "RegistrationClosesAtUtc"
)
SELECT '5aff0000-0000-4000-8000-000000000100', user_id,
  'RaceDay Staff Checkpoint Test 5K', 'Giải thử luồng organizer - staff - runner',
  'Hồ Hoàn Kiếm, Hà Nội', 21.0285, 105.8542,
  DATE '2026-12-20', DATE '2026-12-20', TIME '06:00:00', 'RegistrationOpen',
  'RaceDay Demo', 'cuong.le@raceday.vn', '0904000004', true, 20,
  '{"currency":"VND"}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
  'Dữ liệu local dành riêng để kiểm thử staff checkpoint trong simulation.',
  now(), now(), TIMESTAMPTZ '2026-11-01 00:00:00+00', TIMESTAMPTZ '2026-12-19 00:00:00+00'
FROM iam.users WHERE username = 'cuong.le'
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO event.event_distances (
  "Id", "EventId", "Type", "Name", "DistanceKm", "Capacity", "RegisteredCount",
  "Price", "Currency", "Color", "RouteGeometryJson", "StartAt", "EndAt",
  "BibPrefix", "BibRangeStart", "BibRangeEnd"
) VALUES (
  '5aff0000-0000-4000-8000-000000000101',
  '5aff0000-0000-4000-8000-000000000100', '5KM', 'Staff Test 5K', 5, 20, 12,
  0, 'VND', '#F97316', '{}'::jsonb, TIME '06:00:00', TIME '08:00:00', '9', 1, 20
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO map.race_courses (
  "Id", "EventId", "DistanceId", "Title", "DistanceKm", "CourseJson",
  "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc"
)
SELECT '5aff0000-0000-4000-8000-000000000102',
  '5aff0000-0000-4000-8000-000000000100',
  '5aff0000-0000-4000-8000-000000000101', 'Staff Test 5K', 5,
  $course${
    "title":"Staff Test 5K", "distanceId":"5aff0000-0000-4000-8000-000000000101",
    "distanceKm":5,
    "polyline":[
      {"lat":21.0285,"lng":105.8542},{"lat":21.036,"lng":105.862},
      {"lat":21.044,"lng":105.870},{"lat":21.052,"lng":105.878},
      {"lat":21.060,"lng":105.886},{"lat":21.068,"lng":105.894}
    ],
    "checkpoints":[
      {"id":"cp-0","name":"Start - Hồ Hoàn Kiếm","km":0,"lat":21.0285,"lng":105.8542,"type":"start"},
      {"id":"cp-1.25","name":"Checkpoint 1.25 km","km":1.25,"lat":21.036576860882178,"lng":105.86257686088217,"type":"cp"},
      {"id":"cp-2.5","name":"Tiếp nước 2.5 km","km":2.5,"lat":21.044795066863387,"lng":105.8707950668634,"type":"aid"},
      {"id":"cp-3.75","name":"Checkpoint 3.75 km","km":3.75,"lat":21.053013483849313,"lng":105.8790134838493,"type":"cp"},
      {"id":"cp-5","name":"Finish - 5 km","km":5,"lat":21.061232111928124,"lng":105.88723211192811,"type":"finish"}
    ]
  }$course$::jsonb,
  user_id, now(), now()
FROM iam.users WHERE username = 'cuong.le'
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO event.event_checkpoints (
  "Id", "DistanceId", "ExternalId", "Name", "Km", "Lat", "Lng", "Type", "SortOrder"
)
SELECT ('5aff0000-0003-4000-8000-' || lpad(cp.n::text, 12, '0'))::uuid,
  '5aff0000-0000-4000-8000-000000000101', cp.external_id, cp.name, cp.km,
  cp.lat, cp.lng, cp.type, cp.n
FROM (VALUES
  (1, 'cp-0', 'Start - Hồ Hoàn Kiếm', 0.0, 21.0285, 105.8542, 'Start'),
  (2, 'cp-1.25', 'Checkpoint 1.25 km', 1.25, 21.036576860882178, 105.86257686088217, 'Water'),
  (3, 'cp-2.5', 'Tiếp nước 2.5 km', 2.5, 21.044795066863387, 105.8707950668634, 'Aid'),
  (4, 'cp-3.75', 'Checkpoint 3.75 km', 3.75, 21.053013483849313, 105.8790134838493, 'Water'),
  (5, 'cp-5', 'Finish - 5 km', 5.0, 21.061232111928124, 105.88723211192811, 'Finish')
) cp(n, external_id, name, km, lat, lng, type)
ON CONFLICT ("Id") DO NOTHING;

-- Rebind only the old fixture checkpoints; keep the organizer's saved map course intact.
UPDATE event.event_checkpoints AS c
SET "ExternalId" = replacement.external_id,
    "Lat" = replacement.lat,
    "Lng" = replacement.lng
FROM (VALUES
  (1, 'start', 'cp-0', 21.0285, 105.8542),
  (2, 'km1', 'cp-1.25', 21.036576860882178, 105.86257686088217),
  (3, 'aid', 'cp-2.5', 21.044795066863387, 105.8707950668634),
  (4, 'km4', 'cp-3.75', 21.053013483849313, 105.8790134838493),
  (5, 'finish', 'cp-5', 21.061232111928124, 105.88723211192811)
) AS replacement(n, old_id, external_id, lat, lng)
WHERE c."Id" = ('5aff0000-0003-4000-8000-' || lpad(replacement.n::text, 12, '0'))::uuid
  AND c."DistanceId" = '5aff0000-0000-4000-8000-000000000101'
  AND c."ExternalId" = replacement.old_id;

-- Earlier versions of this fixture used a type not present in the Event enum.
UPDATE event.event_checkpoints SET "Type" = 'Water'
WHERE "DistanceId" = '5aff0000-0000-4000-8000-000000000101'
  AND "ExternalId" IN ('km1', 'km4', 'cp-1.25', 'cp-3.75') AND "Type" = 'Checkpoint';

INSERT INTO event.event_staff_members (
  "Id", "EventId", "Role", "Status", "FullName", "Email", "Phone", "UserId",
  "StationId", "StationLabel", "CreatedAtUtc", "UpdatedAtUtc"
)
SELECT ('5aff0000-0002-4000-8000-' || lpad(s.n::text, 12, '0'))::uuid,
  '5aff0000-0000-4000-8000-000000000100', 'RaceStaff', 'Active',
  u.full_name, u.email, u.phone, u.user_id,
  'map:5aff0000-0000-4000-8000-000000000101:' || s.checkpoint_id,
  s.station_label, now(), now()
FROM (VALUES
  (1, 'coord1', 'cp-0', 'Start - DNS'),
  (2, 'coord2', 'cp-1.25', 'Checkpoint 1.25 km'),
  (3, 'coord3', 'cp-2.5', 'Tiếp nước 2.5 km'),
  (4, 'coord4', 'cp-3.75', 'Checkpoint 3.75 km'),
  (5, 'vol1', 'cp-5', 'Finish - 5 km')
) s(n, username, checkpoint_id, station_label)
JOIN iam.users u ON u.username = s.username
ON CONFLICT ("Id") DO NOTHING;

UPDATE event.event_staff_members AS s
SET "StationId" = 'map:5aff0000-0000-4000-8000-000000000101:' || replacement.new_id,
    "UpdatedAtUtc" = now()
FROM (VALUES
  (1, 'start', 'cp-0'),
  (2, 'km1', 'cp-1.25'),
  (3, 'aid', 'cp-2.5'),
  (4, 'km4', 'cp-3.75'),
  (5, 'finish', 'cp-5')
) AS replacement(n, old_id, new_id)
WHERE s."Id" = ('5aff0000-0002-4000-8000-' || lpad(replacement.n::text, 12, '0'))::uuid
  AND s."EventId" = '5aff0000-0000-4000-8000-000000000100'
  AND s."StationId" = 'map:5aff0000-0000-4000-8000-000000000101:' || replacement.old_id;

-- Test runner accounts use the same documented local seed password: MatKhau@123.
INSERT INTO iam.users (
  user_id, email, username, role_id, password_hash, full_name, phone,
  date_of_birth, gender, status, email_verified, created_at, updated_at,
  must_change_password, id_number
)
SELECT ('5aff0000-0004-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
  'stafftest.runner' || lpad(n::text, 2, '0') || '@raceday.local',
  'stafftest.runner' || lpad(n::text, 2, '0'),
  '22222222-2222-2222-2222-222222222222',
  'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
  'VĐV Test ' || lpad(n::text, 2, '0'), '090888' || lpad(n::text, 4, '0'),
  DATE '1998-01-01', 1, 1, true, now(), now(), false, NULL
FROM generate_series(1, 12) n
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
  user_id, description, contact_email, contact_phone,
  verification_status, verified_at, verified_by
)
SELECT u.user_id, 'Runner của giải staff checkpoint test', u.email, u.phone,
  2, now(), 'local-seed'
FROM iam.users u
WHERE u.user_id::text LIKE '5aff0000-0004-4000-8000-%'
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO event.event_registrations (
  "Id", "EventId", "DistanceId", "UserId", "Status", "RaceStatus",
  "FullName", "Email", "Phone", "DateOfBirth", "Gender", "IdNumber",
  "HeightCm", "WeightKg", "BloodType", "EmergencyName", "EmergencyPhone",
  "ShirtType", "ShirtSize", "ShirtColor", "MedicalCodesJson",
  "LiabilityAccepted", "LiabilityAcceptedAtUtc", "BibNumber",
  "CreatedAtUtc", "UpdatedAtUtc", "QuotedAmount", "QuotedPhaseKey", "QuotedPhaseName"
)
SELECT ('5aff0000-0005-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
  '5aff0000-0000-4000-8000-000000000100',
  '5aff0000-0000-4000-8000-000000000101', u.user_id, 'Confirmed', 'NotStarted',
  u.full_name, u.email, u.phone, DATE '1998-01-01', 'Male',
  '079198' || lpad(n::text, 6, '0'), 170, 65, 'O',
  'Người thân Test ' || lpad(n::text, 2, '0'), '090899' || lpad(n::text, 4, '0'),
  'T-Shirt', 'M', 'Cam', '[]'::jsonb, true, now(),
  '9' || lpad(n::text, 4, '0'), now(), now(), 0, 'staff-test', 'Miễn phí'
FROM generate_series(1, 12) n
JOIN iam.users u ON u.user_id = ('5aff0000-0004-4000-8000-' || lpad(n::text, 12, '0'))::uuid
ON CONFLICT ("Id") DO NOTHING;

COMMIT;

SELECT 'event' AS item, count(*) AS total FROM event.race_events
 WHERE "Id" = '5aff0000-0000-4000-8000-000000000100'
UNION ALL SELECT 'course', count(*) FROM map.race_courses
 WHERE "EventId" = '5aff0000-0000-4000-8000-000000000100'
UNION ALL SELECT 'staff', count(*) FROM event.event_staff_members
 WHERE "EventId" = '5aff0000-0000-4000-8000-000000000100' AND "Status" = 'Active'
UNION ALL SELECT 'registered_runners', count(*) FROM event.event_registrations
 WHERE "EventId" = '5aff0000-0000-4000-8000-000000000100' AND "Status" = 'Confirmed'
UNION ALL SELECT 'eligible_for_simulation', count(*) FROM event.event_registrations
 WHERE "EventId" = '5aff0000-0000-4000-8000-000000000100'
 AND "Status" = 'Confirmed' AND "RaceStatus" = 'NotStarted'
 AND "StartedAtUtc" IS NULL AND "FinishedAtUtc" IS NULL
 AND "ResultSeconds" IS NULL AND coalesce("BibNumber", '') <> '';

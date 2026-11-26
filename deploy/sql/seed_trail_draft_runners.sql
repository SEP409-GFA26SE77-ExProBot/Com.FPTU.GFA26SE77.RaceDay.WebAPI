-- Local test fixture: 12 confirmed runners with BIBs for "RaceDay Trail Draft 2027 / 5KM Draft"
-- so the organizer simulator can create a run and staff can test the Staff Logs (Nhật ký thao tác).
-- Additive and repeatable. Password for runner accounts: MatKhau@123.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO iam.users (user_id, email, username, role_id, password_hash, full_name, phone,
  date_of_birth, gender, status, email_verified, created_at, updated_at, must_change_password, id_number)
SELECT ('c0ffeeee-0004-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
  'traildraft.runner' || lpad(n::text, 2, '0') || '@raceday.local',
  'traildraft.runner' || lpad(n::text, 2, '0'),
  '22222222-2222-2222-2222-222222222222',
  'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
  'VĐV Trail ' || lpad(n::text, 2, '0'), '090777' || lpad(n::text, 4, '0'),
  DATE '1998-01-01', 1, 1, true, now(), now(), false, NULL
FROM generate_series(1, 12) n
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (user_id, description, contact_email, contact_phone, verification_status, verified_at, verified_by)
SELECT u.user_id, 'Runner Trail Draft test', u.email, u.phone, 2, now(), 'local-seed'
FROM iam.users u WHERE u.user_id::text LIKE 'c0ffeeee-0004-4000-8000-%'
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO event.event_registrations ("Id", "EventId", "DistanceId", "UserId", "Status", "RaceStatus",
  "FullName", "Email", "Phone", "DateOfBirth", "Gender", "IdNumber", "HeightCm", "WeightKg", "BloodType",
  "EmergencyName", "EmergencyPhone", "ShirtType", "ShirtSize", "ShirtColor", "MedicalCodesJson",
  "LiabilityAccepted", "LiabilityAcceptedAtUtc", "BibNumber", "CreatedAtUtc", "UpdatedAtUtc",
  "QuotedAmount", "QuotedPhaseKey", "QuotedPhaseName")
SELECT ('c0ffeeee-0005-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
  'c0ffeeee-0001-4000-8000-000000000001', 'c0ffeeee-0001-4000-8000-0000000000d1', u.user_id,
  'Confirmed', 'NotStarted', u.full_name, u.email, u.phone, DATE '1998-01-01', 'Male',
  '079297' || lpad(n::text, 6, '0'), 170, 65, 'O', 'Người thân Trail ' || lpad(n::text, 2, '0'),
  '090766' || lpad(n::text, 4, '0'), 'T-Shirt', 'M', 'Cam', '[]'::jsonb, true, now(),
  '5' || lpad((400 + n)::text, 4, '0'), now(), now(), 0, 'trail-test', 'Test'
FROM generate_series(1, 12) n
JOIN iam.users u ON u.user_id = ('c0ffeeee-0004-4000-8000-' || lpad(n::text, 12, '0'))::uuid
ON CONFLICT ("Id") DO NOTHING;
COMMIT;

SELECT count(*) AS eligible_for_simulation FROM event.event_registrations
 WHERE "DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1' AND "Status" = 'Confirmed' AND "RaceStatus" = 'NotStarted'
 AND "StartedAtUtc" IS NULL AND "FinishedAtUtc" IS NULL AND "ResultSeconds" IS NULL AND coalesce("BibNumber", '') <> '';

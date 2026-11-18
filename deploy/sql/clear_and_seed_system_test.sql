-- Destructive local reset. Run after the API has migrated PostgreSQL.
-- Keep seed_system_test_event.sql alongside this file for the relative include.
-- PowerShell: .\deploy\seed-system-test-event.ps1 -ResetDatabase
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL TIME ZONE 'UTC';

DO $reset$
DECLARE
  module_tables text;
BEGIN
  PERFORM pg_advisory_xact_lock(hashtext('raceday.system_test_seed'));
  IF to_regclass('event.race_events') IS NULL THEN
    RAISE EXCEPTION 'RaceDay schema is missing. Start the API to apply the migrations first.';
  END IF;

  SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY schemaname, tablename)
  INTO module_tables
  FROM pg_tables
  WHERE schemaname IN ('iam', 'event', 'payment', 'finance', 'map', 'photo', 'device', 'audit', 'race_monitoring')
    AND tablename <> '__EFMigrationsHistory';

  EXECUTE 'TRUNCATE TABLE ' || module_tables || ' RESTART IDENTITY';
END $reset$;

-- Start/finish, check-in, payment, and station assignment scenario.
\ir seed_system_test_event.sql

INSERT INTO iam.roles (role_id, role_name, description) VALUES
  ('11111111-1111-1111-1111-111111111111', 'Admin', 'System administrator'),
  ('77777777-7777-7777-7777-777777777777', 'Manager', 'Event approvals and package management');

INSERT INTO iam.users (
  user_id, email, username, role_id, password_hash, full_name, phone,
  date_of_birth, gender, status, email_verified, created_at, updated_at, must_change_password
)
SELECT account.id, account.email, account.username, r.role_id, organizer.password_hash,
       account.full_name, account.phone, DATE '1993-08-18', account.gender, 1, true, now(), now(), false
FROM (VALUES
  ('5eed2026-0000-4000-8000-000000000004'::uuid, 'systest.admin@raceday.test', 'systest.admin',
   'Admin', 'Hoang Thanh Lam', '0906120104', 1),
  ('5eed2026-0000-4000-8000-000000000005', 'manager@raceday.vn', 'manager',
   'Manager', 'Vu Thuy Van', '0906120105', 2)
) AS account(id, email, username, role_name, full_name, phone, gender)
JOIN iam.roles AS r ON r.role_name = account.role_name
CROSS JOIN iam.users AS organizer
WHERE organizer.username = 'systest.organizer';

INSERT INTO iam.profiles (
  user_id, description, contact_email, contact_phone, verification_status, verified_at, verified_by
)
SELECT user_id, 'RaceDay system test', email, phone, 2, now(), 'system-test-seed'
FROM iam.users WHERE username IN ('systest.admin', 'manager');

INSERT INTO event.package_duration_options (
  "Id", "Name", "Months", "Price", "Status", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc"
) VALUES
  ('5eed2026-0000-4000-8000-000000000301', '1 month', 1, 1000000, 'Active', 1, now(), now()),
  ('5eed2026-0000-4000-8000-000000000302', '3 months', 3, 2500000, 'Active', 3, now(), now()),
  ('5eed2026-0000-4000-8000-000000000303', '12 months', 12, 7500000, 'Active', 12, now(), now());

INSERT INTO event.package_capacity_options (
  "Id", "Name", "MaxParticipants", "AddOnFee", "Status", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc"
) VALUES
  ('5eed2026-0000-4000-8000-000000000311', '20 runners', 20, 0, 'Active', 20, now(), now()),
  ('5eed2026-0000-4000-8000-000000000312', '50 runners', 50, 200000, 'Active', 50, now(), now()),
  ('5eed2026-0000-4000-8000-000000000313', '100 runners', 100, 500000, 'Active', 100, now(), now());

INSERT INTO event.promo_codes (
  "Id", "Code", "PercentOff", "Status", "ExpiresAtUtc", "MaxRedemptions", "UsedCount", "CreatedAtUtc", "UpdatedAtUtc"
) VALUES (
  '5eed2026-0000-4000-8000-000000000321', 'SYSTEST25', 0.25, 'Active', now() + interval '1 year', 20, 0, now(), now()
);

-- Approval and published-results scenarios.
DO $baseline$
DECLARE
  organizer_id constant uuid := '5eed2026-0000-4000-8000-000000000001';
  pending_id constant uuid := '5eed2026-0000-4000-8000-000000000200';
  completed_id constant uuid := '5eed2026-0000-4000-8000-000000000210';
  completed_distance_id constant uuid := '5eed2026-0000-4000-8000-000000000211';
  completed_name constant text := 'Han River Results 5K';
  completed_date date := current_date - 14;
  completed_start timestamptz := (completed_date + TIME '00:30') AT TIME ZONE 'UTC';
  result_seconds constant integer := 1518;
  amount constant numeric := 120000;
  registration_id constant uuid := '5eed2026-0000-4000-8000-000000000212';
  runner_id constant uuid := '5eed2026-0000-4000-8000-000000000011';
BEGIN
  INSERT INTO event.race_events (
    "Id", "OrganizerUserId", "Name", "Location", coord_lat, coord_lng, "EventDate", "EndDate",
    "StartTime", "Status", org_name, org_email, org_phone, org_verified, "TotalRunnersCap",
    "TicketPricingJson", "MerchandiseJson", "RulesJson", "RewardsJson", "Description",
    "CreatedAtUtc", "UpdatedAtUtc", "SubmittedAtUtc", "ReviewedAtUtc",
    "RegistrationOpensAtUtc", "RegistrationClosesAtUtc"
  ) VALUES
    (pending_id, organizer_id, 'Marble Mountains Trail 10K', 'Marble Mountains, Da Nang', 16.0033, 108.2631,
     current_date + 30, current_date + 30, TIME '00:30', 'PendingApproval',
     'Bien Dong Running Club', 'systest.organizer@raceday.test', '0906120101', false, 50,
     '{}', '{}', '{}', '{}', 'Fixture for the manager approval queue.', now(), now(), now(), NULL,
     now() + interval '1 day', now() + interval '25 days'),
    (completed_id, organizer_id, completed_name, 'Han River, Da Nang', 16.0613, 108.2287,
     completed_date, completed_date, TIME '00:30', 'Completed',
     'Bien Dong Running Club', 'systest.organizer@raceday.test', '0906120101', true, 20,
     '{}', '{}', '{}', '{}', 'Fixture with a published finish time for leaderboard and certificate tests.',
     completed_start - interval '30 days', completed_start + interval '2 hours',
     completed_start - interval '30 days', completed_start - interval '29 days',
     completed_start - interval '28 days', completed_start - interval '1 day');

  INSERT INTO event.event_distances (
    "Id", "EventId", "Type", "Name", "DistanceKm", "Capacity", "RegisteredCount", "Price", "Currency",
    "Color", "RouteGeometryJson", "StartAt", "EndAt", "BibPrefix", "BibRangeStart", "BibRangeEnd",
    "ResultsPublished", "ResultsPublishedAtUtc"
  ) VALUES
    ('5eed2026-0000-4000-8000-000000000201', pending_id, '10KM', 'Marble Mountains 10K',
     10, 50, 0, 250000, 'VND', '#EA580C', '{}', TIME '00:30', TIME '03:30', '10', 1, 50, false, NULL),
    (completed_distance_id, completed_id, '5KM', 'Han River 5K',
     5, 20, 1, amount, 'VND', '#2563EB', '{}', TIME '00:30', TIME '02:00', '5', 1, 20,
     true, completed_start + interval '2 hours');

  -- Placeholder document records let the manager test approval; upload files to test downloads.
  INSERT INTO event.event_documents (
    "Id", "EventId", "DocumentKey", "Title", "FileName", "Required", "UploadedAtUtc"
  ) VALUES
    ('5eed2026-0000-4000-8000-000000000202', pending_id, 'official-letter', 'Official letter', 'test-letter.pdf', true, now()),
    ('5eed2026-0000-4000-8000-000000000203', pending_id, 'security', 'Security plan', 'test-security.pdf', true, now());

  INSERT INTO event.event_registrations (
    "Id", "EventId", "DistanceId", "UserId", "Status", "RaceStatus", "FullName", "Email", "Phone",
    "DateOfBirth", "Gender", "MedicalCodesJson", "LiabilityAccepted", "LiabilityAcceptedAtUtc",
    "BibNumber", "QuotedAmount", "CreatedAtUtc", "UpdatedAtUtc", "CheckedInAtUtc", "CheckedInByUserId",
    "StartedAtUtc", "FinishedAtUtc", "ResultSeconds"
  )
  SELECT registration_id, completed_id, completed_distance_id, u.user_id, 'Finished', 'Finished',
         u.full_name, u.email, u.phone, u.date_of_birth, 'Female', '[]', true, completed_start - interval '7 days',
         '50001', amount, completed_start - interval '7 days', completed_start + make_interval(secs => result_seconds),
         completed_start - interval '30 minutes', organizer_id, completed_start,
         completed_start + make_interval(secs => result_seconds), result_seconds
  FROM iam.users AS u WHERE u.user_id = runner_id;

  INSERT INTO payment.payments (
    "Id", "RegistrationId", "UserId", "Amount", "Currency", "PaymentMethod", "Status", "PaymentGateway",
    "TransactionReference", "OrderCode", "PaidAtUtc", "CreatedAtUtc", "UpdatedAtUtc", "Purpose", "ListAmount"
  ) VALUES (
    '5eed2026-0000-4000-8000-000000000213', registration_id, runner_id, amount, 'VND', 'PayOS',
    'Paid', 'PayOS', 'systest-han-river-ticket', 806260000210, completed_start - interval '7 days',
    completed_start - interval '7 days', completed_start - interval '7 days', 'Registration', amount
  );

  INSERT INTO finance.event_ledgers (
    "Id", "EventId", "OrganizerUserId", "EventName", "Currency", "TicketsSold", "GrossRevenue",
    "RefundedTotal", "WithdrawnTotal", "PayoutPending", "UpdatedAtUtc"
  ) VALUES (
    '5eed2026-0000-4000-8000-000000000214', completed_id, organizer_id, completed_name, 'VND',
    1, amount, 0, 0, 0, completed_start + interval '2 hours'
  );
END $baseline$;

COMMIT;

SELECT "Name", "Status", "EventDate", "StartTime" AS start_time_utc
FROM event.race_events
ORDER BY "EventDate";

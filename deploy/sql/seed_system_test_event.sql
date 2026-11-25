-- Local dev fixture for an already migrated RaceDay database.
-- Edit the defaults below, or pass -v event_start=2026-11-18T06:30:00+07:00
-- and -v duration_minutes=120 to psql. An empty start means tomorrow.
-- Run again to move the same event. Its registrations and results are kept.
\set ON_ERROR_STOP on
\if :{?event_start}
\else
  \set event_start ''
\endif
\if :{?duration_minutes}
\else
  \set duration_minutes 120
\endif

SELECT set_config('raceday.seed_event_start', :'event_start', false) AS start_value,
       set_config('raceday.seed_duration_minutes', :'duration_minutes', false) AS duration_value
\gset seed_

DO $seed$
DECLARE
  race_id constant uuid := '5eed2026-0000-4000-8000-000000000100';
  distance_id constant uuid := '5eed2026-0000-4000-8000-000000000101';
  organizer_id constant uuid := '5eed2026-0000-4000-8000-000000000001';
  package_id constant uuid := '5eed2026-0000-4000-8000-000000000110';
  package_payment_id constant uuid := '5eed2026-0000-4000-8000-000000000111';
  race_name constant text := 'Bien Dong System Test 5K';
  distance_name constant text := 'Bien Dong 5K';
  venue constant text := 'Bien Dong Park, Da Nang';
  ticket_price constant numeric := 150000;
  bib_prefix constant text := '5';
  seed_password_hash constant text :=
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==';
  start_input text := current_setting('raceday.seed_event_start');
  duration_minutes integer := current_setting('raceday.seed_duration_minutes')::integer;
  start_instant timestamptz;
  start_clock timestamp;
  finish_clock timestamp;
  cutoff_clock time;
  registration_open timestamptz;
  registration_close timestamptz;
  window_status text;
  package_expiry timestamptz;
  existing_organizer uuid;
  course_checkpoints jsonb;
BEGIN
  IF start_input = '' THEN
    start_instant := date_trunc('second', now()) + interval '24 hours';
  ELSIF start_input ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}(:[0-9]{2})?(Z|[+-][0-9]{2}:[0-9]{2})$' THEN
    start_instant := start_input::timestamptz;
  ELSE
    RAISE EXCEPTION 'Use an ISO 8601 start with an offset, e.g. 2026-11-18T06:30:00+07:00';
  END IF;
  IF duration_minutes < 1 OR duration_minutes > 1440 THEN
    RAISE EXCEPTION 'duration_minutes must be between 1 and 1440.';
  END IF;

  -- The race status and finish handlers interpret these date/time columns as UTC.
  start_clock := start_instant AT TIME ZONE 'UTC';
  finish_clock := start_clock + make_interval(mins => duration_minutes);
  -- EventDistance supports a cutoff clock within its start date.
  cutoff_clock := CASE WHEN finish_clock::date = start_clock::date THEN finish_clock::time END;
  registration_close := start_instant - interval '15 minutes';
  registration_open := least(now(), registration_close) - interval '168 hours';
  window_status := CASE WHEN now() < registration_close THEN 'RegistrationOpen' ELSE 'Closed' END;
  package_expiry := greatest(now(), start_instant) + interval '365 days';

  -- Serialize the event importer with the full reset, which uses the same lock.
  PERFORM pg_advisory_xact_lock(hashtext('raceday.system_test_seed'));
  IF to_regclass('event.race_events') IS NULL THEN
    RAISE EXCEPTION 'RaceDay schema is missing. Start the API to apply the migrations first.';
  END IF;
  SELECT e."OrganizerUserId" INTO existing_organizer
  FROM event.race_events AS e WHERE e."Id" = race_id FOR UPDATE;

  IF FOUND THEN
    IF existing_organizer <> organizer_id THEN
      RAISE EXCEPTION 'The system-test event ID is owned by another organizer.';
    END IF;

    UPDATE event.event_distances
    SET "StartAt" = start_clock::time, "EndAt" = cutoff_clock
    WHERE "Id" = distance_id AND "EventId" = race_id;
    IF NOT FOUND THEN
      RAISE EXCEPTION 'The system-test event is missing its seeded 5K distance.';
    END IF;

    UPDATE event.race_events
    SET "EventDate" = start_clock::date, "EndDate" = finish_clock::date, "StartTime" = start_clock::time,
        "RegistrationOpensAtUtc" = registration_open, "RegistrationClosesAtUtc" = registration_close,
        "Status" = CASE WHEN "Status" IN ('RegistrationOpen', 'Closed') THEN window_status ELSE "Status" END,
        "UpdatedAtUtc" = now()
    WHERE "Id" = race_id;
    RETURN;
  END IF;

  INSERT INTO iam.roles (role_id, role_name, description) VALUES
    ('22222222-2222-2222-2222-222222222222', 'User', 'Runner'),
    ('33333333-3333-3333-3333-333333333333', 'Organizer', 'Event organizer'),
    ('44444444-4444-4444-4444-444444444444', 'Race-staff', 'Race staff'),
    ('55555555-5555-5555-5555-555555555555', 'Photographer', 'Event photographer')
  ON CONFLICT (role_id) DO NOTHING;

  -- Local-only accounts. Shared dev password: MatKhau@123.
  INSERT INTO iam.users (
    user_id, email, username, role_id, password_hash, full_name, phone,
    date_of_birth, gender, status, email_verified, created_at, updated_at, must_change_password
  )
  SELECT account.id, account.username || '@raceday.test', account.username, r.role_id,
         seed_password_hash, account.full_name, account.phone,
         DATE '1997-04-12', account.gender, 1, true, now(), now(), false
  FROM (VALUES
    (organizer_id, 'systest.organizer', 'Organizer', 'Mai Quoc Bao', '0906120101', 1),
    ('5eed2026-0000-4000-8000-000000000002', 'systest.staff', 'Race-staff', 'Tran Ha Anh', '0906120102', 2),
    ('5eed2026-0000-4000-8000-000000000003', 'systest.photo', 'Photographer', 'Do Minh Duc', '0906120103', 1),
    ('5eed2026-0000-4000-8000-000000000011', 'systest.runner1', 'User', 'Nguyen My Linh', '0906120111', 2),
    ('5eed2026-0000-4000-8000-000000000012', 'systest.runner2', 'User', 'Pham Gia Huy', '0906120112', 1),
    ('5eed2026-0000-4000-8000-000000000013', 'systest.runner3', 'User', 'Le Tue Nhi', '0906120113', 2)
  ) AS account(id, username, role_name, full_name, phone, gender)
  JOIN iam.roles AS r ON r.role_name = account.role_name
  ON CONFLICT (user_id) DO NOTHING;

  INSERT INTO iam.profiles (
    user_id, description, contact_email, contact_phone, verification_status, verified_at, verified_by
  )
  SELECT user_id, 'RaceDay system test', email, phone, 2, now(), 'system-test-seed'
  FROM iam.users
  WHERE user_id::text LIKE '5eed2026-0000-4000-8000-00000000001%'
     OR user_id IN (organizer_id, '5eed2026-0000-4000-8000-000000000002', '5eed2026-0000-4000-8000-000000000003')
  ON CONFLICT (user_id) DO NOTHING;

  INSERT INTO payment.payments (
    "Id", "UserId", "Amount", "Currency", "PaymentMethod", "Status", "PaymentGateway",
    "TransactionReference", "OrderCode", "PaidAtUtc", "CreatedAtUtc", "UpdatedAtUtc",
    "Purpose", "ReferenceId", "ListAmount"
  ) VALUES (
    package_payment_id, organizer_id, 7500000, 'VND', 'PayOS', 'Paid', 'PayOS',
    'systest-event-package', 806260000100, now(), now(), now(), 'EventPackage', package_id, 7500000
  );

  INSERT INTO event.package_purchases (
    "Id", "OrganizerUserId", "OrgName", "OrgAddress", "DurationMonths", "ParticipantCap",
    "BasePrice", "CapacityFee", "Discount", "TotalAmount", "Currency", "Status", "PaymentId",
    "CreatedEventId", "PackageExpiresAtUtc", "CreatedAtUtc", "UpdatedAtUtc"
  ) VALUES (
    package_id, organizer_id, 'Bien Dong Running Club', venue, 12, 20, 7500000, 0, 0,
    7500000, 'VND', 'Paid', package_payment_id, race_id, package_expiry, now(), now()
  );

  INSERT INTO event.race_events (
    "Id", "OrganizerUserId", "Name", "Location", coord_lat, coord_lng, "EventDate", "EndDate",
    "StartTime", "Status", org_name, org_email, org_phone, org_verified, "TotalRunnersCap",
    "TicketPricingJson", "MerchandiseJson", "RulesJson", "RewardsJson", "Description",
    "CreatedAtUtc", "UpdatedAtUtc", "SubmittedAtUtc", "ReviewedAtUtc", "RegistrationOpensAtUtc",
    "RegistrationClosesAtUtc", "PackagePurchaseId", "PackageExpiresAtUtc"
  ) VALUES (
    race_id, organizer_id, race_name, venue, 16.0700, 108.2463, start_clock::date,
    finish_clock::date, start_clock::time, window_status, 'Bien Dong Running Club',
    'systest.organizer@raceday.test', '0906120101', true, 20,
    jsonb_build_object('byDistanceName', jsonb_build_object(distance_name,
      jsonb_build_object('basePrice', ticket_price, 'phases', '[]'::jsonb))),
    '{}', '{"items":["Wear your assigned bib."]}', '{}',
    'Local practice race for registration, check-in, and scheduled-start tests.',
    now(), now(), now(), now(), registration_open, registration_close, package_id, package_expiry
  );

  INSERT INTO event.event_distances (
    "Id", "EventId", "Type", "Name", "DistanceKm", "Capacity", "RegisteredCount", "Price",
    "Currency", "Color", "RouteGeometryJson", "StartAt", "EndAt", "BibPrefix", "BibRangeStart", "BibRangeEnd"
  ) VALUES (
    distance_id, race_id, '5KM', distance_name, 5, 20, 2, ticket_price, 'VND', '#F97316',
    '{}', start_clock::time, cutoff_clock, bib_prefix, 1, 20
  );

  WITH checkpoints AS (
    INSERT INTO event.event_checkpoints (
      "Id", "DistanceId", "ExternalId", "Name", "Km", "Lat", "Lng", "Type", "SortOrder"
    )
    VALUES
      ('5eed2026-0000-4000-8000-000000000121', distance_id, 'start', 'Start', 0, 16.0700, 108.2463, 'Start', 0),
      ('5eed2026-0000-4000-8000-000000000122', distance_id, 'water', 'Water Station', 2.5, 16.0475, 108.2491, 'Water', 1),
      ('5eed2026-0000-4000-8000-000000000123', distance_id, 'finish', 'Finish', 5, 16.0250, 108.2515, 'Finish', 2)
    RETURNING *
  )
  SELECT jsonb_agg(jsonb_build_object('id', "ExternalId", 'name', "Name", 'km', "Km",
    'lat', "Lat", 'lng', "Lng", 'type', CASE "Type" WHEN 'Water' THEN 'cp' ELSE lower("Type") END)
    ORDER BY "SortOrder")
  INTO course_checkpoints FROM checkpoints;

  INSERT INTO map.race_courses (
    "Id", "EventId", "DistanceId", "Title", "DistanceKm", "CourseJson",
    "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc"
  )
  SELECT '5eed2026-0000-4000-8000-000000000102', race_id, distance_id, distance_name, 5,
         jsonb_build_object('title', distance_name, 'distanceId', distance_id, 'distanceKm', 5,
           'polyline', jsonb_agg(jsonb_build_object('lat', cp->'lat', 'lng', cp->'lng') ORDER BY ord),
           'checkpoints', course_checkpoints),
         organizer_id, now(), now()
  FROM jsonb_array_elements(course_checkpoints) WITH ORDINALITY AS points(cp, ord);

  INSERT INTO event.event_staff_members (
    "Id", "EventId", "Role", "Status", "FullName", "Email", "Phone", "UserId",
    "StationId", "StationLabel", "StationsJson", "CreatedAtUtc", "UpdatedAtUtc"
  )
  SELECT assignment.id, race_id, assignment.staff_role, 'Active', u.full_name, u.email, u.phone,
         u.user_id, assignment.station_id, assignment.station_label, assignment.stations, now(), now()
  FROM (VALUES
    ('5eed2026-0000-4000-8000-000000000131'::uuid, 'systest.staff', 'RaceStaff',
     'map:' || distance_id || ':start', 'Start',
     (SELECT jsonb_agg(jsonb_build_object('stationId', 'map:' || distance_id || ':' || (cp->>'id'),
              'stationLabel', cp->>'name') ORDER BY ord)
      FROM jsonb_array_elements(course_checkpoints) WITH ORDINALITY AS points(cp, ord))),
    ('5eed2026-0000-4000-8000-000000000132', 'systest.photo', 'Photographer', NULL, NULL, '[]'::jsonb)
  ) AS assignment(id, username, staff_role, station_id, station_label, stations)
  JOIN iam.users AS u USING (username);

  INSERT INTO event.event_supplies (
    "Id", "EventId", "Name", "Quantity", "Unit", "Notes", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc"
  ) VALUES (
    '5eed2026-0000-4000-8000-000000000141', race_id, 'Water', 48, 'bottles', 'Water Station supplies', 0, now(), now()
  );

  INSERT INTO event.event_documents (
    "Id", "EventId", "DocumentKey", "Title", "FileName", "Required", "UploadedAtUtc"
  ) VALUES
    ('5eed2026-0000-4000-8000-000000000142', race_id, 'official-letter', 'Official letter', 'test-letter.pdf', true, now()),
    ('5eed2026-0000-4000-8000-000000000143', race_id, 'security', 'Security plan', 'test-security.pdf', true, now());

  WITH registrations AS (
    INSERT INTO event.event_registrations (
      "Id", "EventId", "DistanceId", "UserId", "Status", "RaceStatus", "FullName", "Email", "Phone",
      "DateOfBirth", "Gender", "MedicalCodesJson", "LiabilityAccepted", "LiabilityAcceptedAtUtc",
      "BibNumber", "QuotedAmount", "CreatedAtUtc", "UpdatedAtUtc"
    )
    SELECT r.id, race_id, distance_id, u.user_id, 'Confirmed', 'NotStarted', u.full_name,
           u.email, u.phone, u.date_of_birth, CASE u.gender WHEN 2 THEN 'Female' ELSE 'Male' END,
           '[]', true, now(), bib_prefix || lpad(r.bib::text, 4, '0'), ticket_price, now(), now()
    FROM (VALUES
      ('5eed2026-0000-4000-8000-000000000151'::uuid, 'systest.runner1', 1),
      ('5eed2026-0000-4000-8000-000000000152', 'systest.runner2', 2)
    ) AS r(id, username, bib)
    JOIN iam.users AS u USING (username)
    RETURNING "Id", "UserId", "BibNumber"
  )
  INSERT INTO payment.payments (
    "Id", "RegistrationId", "UserId", "Amount", "Currency", "PaymentMethod", "Status", "PaymentGateway",
    "TransactionReference", "OrderCode", "PaidAtUtc", "CreatedAtUtc", "UpdatedAtUtc", "Purpose", "ListAmount"
  )
  SELECT gen_random_uuid(), "Id", "UserId", ticket_price, 'VND', 'PayOS', 'Paid', 'PayOS',
         'systest-ticket-' || "BibNumber", 806260001000 + "BibNumber"::integer,
         now(), now(), now(), 'Registration', ticket_price
  FROM registrations;

  INSERT INTO finance.event_ledgers (
    "Id", "EventId", "OrganizerUserId", "EventName", "Currency", "TicketsSold", "GrossRevenue",
    "RefundedTotal", "WithdrawnTotal", "PayoutPending", "UpdatedAtUtc"
  ) VALUES (
    '5eed2026-0000-4000-8000-000000000160', race_id, organizer_id, race_name, 'VND', 2,
    2 * ticket_price, 0, 0, 0, now()
  );
END $seed$;

SELECT e."Id" AS event_id, e."Name" AS event_name, e."Status" AS event_status,
       e."EventDate" + e."StartTime" AS start_utc,
       (e."EventDate" + e."StartTime") + interval '7 hours' AS start_utc_plus_07,
       e."RegistrationClosesAtUtc" AT TIME ZONE 'UTC' AS registration_closes_utc
FROM event.race_events AS e
WHERE e."Id" = '5eed2026-0000-4000-8000-000000000100';

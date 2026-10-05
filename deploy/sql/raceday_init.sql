-- =============================================================================
-- RaceDay Postgres — schema + seed (IAM / Event / Payment)
-- Khớp EF migrations hiện tại. Idempotent (IF NOT EXISTS / ON CONFLICT DO NOTHING).
--
-- Chạy:
--   psql -h localhost -U postgres -d postgres -f deploy/sql/raceday_init.sql
-- Docker (volume trống): docker-compose mounts file này vào initdb.d
--
-- Seed password (tất cả user Active): MatKhau@123
--   admin@raceday.vn | an.nguyen@raceday.vn | binh.tran@raceday.vn
--   cuong.le@raceday.vn (Organizer) | dung.pham@raceday.vn (Pending)
--
-- Lịch giải seed (mỗi ngày 1, không trùng):
--   2026-03-15 HCMC Night | 2026-06-07 Hanoi | 2026-09-19 Nha Trang (Approved)
--   2026-11-22 Đà Nẵng | 2026-12-14 Sa Pa | 2027-02-08 Mekong (Pending) | 2027-05-01 Highlands (Draft)
--
-- Re-seed sạch Event+Payment (giữ IAM) — bỏ comment khối dưới rồi chạy lại file:
--   TRUNCATE payment.payment_transactions, payment.payments, payment.outbox_messages RESTART IDENTITY CASCADE;
--   TRUNCATE event.event_checkpoints, event.event_documents, event.event_registrations,
--            event.event_distances, event.race_events RESTART IDENTITY CASCADE;
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- -----------------------------------------------------------------------------
-- IAM
-- -----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS iam;

CREATE TABLE IF NOT EXISTS iam."__EFMigrationsHistory" (
    "MigrationId"   varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32) NOT NULL
);

CREATE TABLE IF NOT EXISTS iam.roles (
    role_id     uuid PRIMARY KEY,
    role_name   varchar(50)  NOT NULL,
    description varchar(255) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_roles_role_name"
    ON iam.roles (role_name);

CREATE TABLE IF NOT EXISTS iam.users (
    user_id         uuid PRIMARY KEY,
    email           varchar(100) NOT NULL,
    username        varchar(50)  NOT NULL,
    role_id         uuid NOT NULL REFERENCES iam.roles(role_id) ON DELETE RESTRICT,
    password_hash   varchar(255) NOT NULL,
    full_name       varchar(100) NOT NULL,
    phone           varchar(20)  NULL,
    date_of_birth   date NULL,
    gender          smallint NOT NULL,
    status          smallint NOT NULL,
    email_verified  boolean NOT NULL,
    created_at      timestamptz NOT NULL,
    updated_at      timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_email" ON iam.users (email);
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_username
    ON iam.users (username)
    WHERE username IS NOT NULL AND btrim(username) <> '';
CREATE INDEX IF NOT EXISTS "IX_users_role_id" ON iam.users (role_id);

CREATE TABLE IF NOT EXISTS iam.profiles (
    user_id             uuid PRIMARY KEY REFERENCES iam.users(user_id) ON DELETE CASCADE,
    description         varchar(255) NULL,
    contact_email       varchar(100) NULL,
    contact_phone       varchar(100) NULL,
    verification_status smallint NOT NULL DEFAULT 0,
    verified_at         timestamptz NULL,
    verified_by         varchar(100) NULL
);

CREATE TABLE IF NOT EXISTS iam.email_otps (
    id              uuid PRIMARY KEY,
    user_id         uuid NULL,
    email           varchar(100) NOT NULL,
    purpose         smallint NOT NULL,
    code_hash       varchar(128) NOT NULL,
    expires_on_utc  timestamptz NOT NULL,
    attempt_count   int NOT NULL DEFAULT 0,
    consumed_on_utc timestamptz NULL,
    created_on_utc  timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_email_otps_email_purpose_created_on_utc"
    ON iam.email_otps (email, purpose, created_on_utc);

CREATE TABLE IF NOT EXISTS iam.outbox_messages (
    id               uuid PRIMARY KEY,
    event_type       varchar(500) NOT NULL,
    payload          text NOT NULL,
    occurred_on_utc  timestamptz NOT NULL,
    created_on_utc   timestamptz NOT NULL,
    processed_on_utc timestamptz NULL
);

CREATE INDEX IF NOT EXISTS "IX_outbox_messages_created_on_utc"
    ON iam.outbox_messages (created_on_utc);
CREATE INDEX IF NOT EXISTS "IX_outbox_messages_processed_on_utc"
    ON iam.outbox_messages (processed_on_utc);

-- -----------------------------------------------------------------------------
-- EVENT
-- -----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS event;

CREATE TABLE IF NOT EXISTS event."__EFMigrationsHistory" (
    "MigrationId"   varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32) NOT NULL
);

CREATE TABLE IF NOT EXISTS event.race_events (
    "Id"                 uuid PRIMARY KEY,
    "OrganizerUserId"    uuid NOT NULL,
    "Name"               varchar(200) NOT NULL,
    "Tagline"            varchar(500) NULL,
    "BannerImage"        varchar(1000) NULL,
    "Location"           varchar(300) NOT NULL,
    coord_lat            double precision NOT NULL,
    coord_lng            double precision NOT NULL,
    "EventDate"          date NOT NULL,
    "StartTime"          time without time zone NULL,
    "Status"             varchar(40) NOT NULL,
    org_name             varchar(200) NOT NULL,
    org_legal_entity     varchar(200) NULL,
    org_email            varchar(100) NOT NULL,
    org_phone            varchar(30) NOT NULL,
    org_website          varchar(300) NULL,
    org_verified         boolean NOT NULL DEFAULT false,
    org_account_holder   varchar(200) NULL,
    org_account_number   varchar(50) NULL,
    org_bank_name        varchar(100) NULL,
    org_bank_branch      varchar(100) NULL,
    org_swift            varchar(20) NULL,
    org_transfer_note    varchar(300) NULL,
    "TotalRunnersCap"    integer NOT NULL,
    weather_temp         varchar(40) NULL,
    weather_condition    varchar(100) NULL,
    weather_humidity     varchar(40) NULL,
    permit_file_url      varchar(500) NULL,
    permit_file_name     varchar(255) NULL,
    permit_number        varchar(100) NULL,
    permit_authority     varchar(200) NULL,
    permit_issue_date    date NULL,
    permit_valid_until   date NULL,
    "TicketPricingJson"  jsonb NOT NULL DEFAULT '{}'::jsonb,
    "MerchandiseJson"    jsonb NOT NULL DEFAULT '{}'::jsonb,
    "RulesJson"          jsonb NOT NULL DEFAULT '{}'::jsonb,
    "RewardsJson"        jsonb NOT NULL DEFAULT '{}'::jsonb,
    "Description"        varchar(4000) NULL,
    "RejectionReason"    varchar(1000) NULL,
    "CreatedAtUtc"       timestamptz NOT NULL,
    "UpdatedAtUtc"       timestamptz NOT NULL,
    "SubmittedAtUtc"     timestamptz NULL,
    "ReviewedAtUtc"      timestamptz NULL
);

CREATE INDEX IF NOT EXISTS "IX_race_events_EventDate" ON event.race_events ("EventDate");
CREATE INDEX IF NOT EXISTS "IX_race_events_OrganizerUserId" ON event.race_events ("OrganizerUserId");
CREATE INDEX IF NOT EXISTS "IX_race_events_Status" ON event.race_events ("Status");

CREATE TABLE IF NOT EXISTS event.event_distances (
    "Id"                 uuid PRIMARY KEY,
    "EventId"            uuid NOT NULL REFERENCES event.race_events("Id") ON DELETE CASCADE,
    "Type"               varchar(40) NOT NULL,
    "Name"               varchar(200) NOT NULL,
    "DistanceKm"         double precision NOT NULL,
    "ElevationGainM"     double precision NOT NULL,
    "MaxElevationM"      double precision NOT NULL,
    "MinElevationM"      double precision NOT NULL,
    "Capacity"           integer NOT NULL,
    "RegisteredCount"    integer NOT NULL DEFAULT 0,
    "Price"              numeric(18,2) NOT NULL,
    "Currency"           varchar(10) NOT NULL DEFAULT 'VND',
    "Color"              varchar(20) NOT NULL DEFAULT '#F97316',
    start_name           varchar(200) NOT NULL,
    start_lat            double precision NOT NULL,
    start_lng            double precision NOT NULL,
    finish_name          varchar(200) NOT NULL,
    finish_lat           double precision NOT NULL,
    finish_lng           double precision NOT NULL,
    "RouteGeometryJson"  jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS "IX_event_distances_EventId"
    ON event.event_distances ("EventId");

CREATE TABLE IF NOT EXISTS event.event_checkpoints (
    "Id"          uuid PRIMARY KEY,
    "DistanceId"  uuid NOT NULL REFERENCES event.event_distances("Id") ON DELETE CASCADE,
    "ExternalId"  varchar(64) NOT NULL,
    "Name"        varchar(200) NOT NULL,
    "Km"          double precision NOT NULL,
    "Lat"         double precision NOT NULL,
    "Lng"         double precision NOT NULL,
    "Type"        varchar(20) NOT NULL,
    "Cutoff"      varchar(20) NULL,
    "Elevation"   integer NULL,
    "SortOrder"   integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS "IX_event_checkpoints_DistanceId"
    ON event.event_checkpoints ("DistanceId");

CREATE TABLE IF NOT EXISTS event.event_documents (
    "Id"            uuid PRIMARY KEY,
    "EventId"       uuid NOT NULL REFERENCES event.race_events("Id") ON DELETE CASCADE,
    "DocumentKey"   varchar(64) NOT NULL,
    "Title"         varchar(200) NOT NULL,
    "FileName"      varchar(255) NULL,
    "FileUrl"       varchar(500) NULL,
    "Required"      boolean NOT NULL DEFAULT false,
    "UploadedAtUtc" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_event_documents_EventId"
    ON event.event_documents ("EventId");

CREATE TABLE IF NOT EXISTS event.event_registrations (
    "Id"                    uuid PRIMARY KEY,
    "EventId"               uuid NOT NULL,
    "DistanceId"            uuid NOT NULL,
    "UserId"                uuid NOT NULL,
    "Status"                varchar(40) NOT NULL,
    "FullName"              varchar(100) NOT NULL,
    "Email"                 varchar(100) NOT NULL,
    "Phone"                 varchar(20) NOT NULL,
    "DateOfBirth"           date NULL,
    "Gender"                varchar(20) NULL,
    "IdNumber"              varchar(30) NULL,
    "HeightCm"              integer NULL,
    "WeightKg"              integer NULL,
    "BloodType"             varchar(10) NULL,
    "EmergencyName"         varchar(100) NULL,
    "EmergencyPhone"        varchar(20) NULL,
    "ShirtType"             varchar(80) NULL,
    "ShirtSize"             varchar(20) NULL,
    "ShirtColor"            varchar(80) NULL,
    "MedicalCodesJson"      jsonb NOT NULL DEFAULT '[]'::jsonb,
    "HealthCertFileName"    varchar(255) NULL,
    "HealthCertUrl"         varchar(2000) NULL,
    "LiabilityAccepted"     boolean NOT NULL DEFAULT false,
    "LiabilityAcceptedAtUtc" timestamptz NULL,
    "RejectionReason"       varchar(1000) NULL,
    "BibNumber"             varchar(30) NULL,
    "CreatedAtUtc"          timestamptz NOT NULL,
    "UpdatedAtUtc"          timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_event_registrations_EventId"
    ON event.event_registrations ("EventId");

DROP INDEX IF EXISTS event."IX_event_registrations_EventId_DistanceId_UserId";
CREATE UNIQUE INDEX "IX_event_registrations_EventId_DistanceId_UserId"
    ON event.event_registrations ("EventId", "DistanceId", "UserId")
    WHERE "Status" NOT IN ('Rejected', 'Cancelled');

-- -----------------------------------------------------------------------------
-- PAYMENT
-- -----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS payment;

CREATE TABLE IF NOT EXISTS payment."__EFMigrationsHistory" (
    "MigrationId"   varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32) NOT NULL
);

CREATE TABLE IF NOT EXISTS payment.outbox_messages (
    "Id"             uuid PRIMARY KEY,
    "EventType"      varchar(500) NOT NULL,
    "Payload"        text NOT NULL,
    "OccurredOnUtc"  timestamptz NOT NULL,
    "CreatedOnUtc"   timestamptz NOT NULL,
    "ProcessedOnUtc" timestamptz NULL
);

CREATE INDEX IF NOT EXISTS "IX_outbox_messages_CreatedOnUtc"
    ON payment.outbox_messages ("CreatedOnUtc");
CREATE INDEX IF NOT EXISTS "IX_outbox_messages_ProcessedOnUtc"
    ON payment.outbox_messages ("ProcessedOnUtc");

CREATE TABLE IF NOT EXISTS payment.payments (
    "Id"                    uuid PRIMARY KEY,
    "RegistrationId"        uuid NOT NULL,
    "UserId"                uuid NOT NULL,
    "Amount"                numeric(15,2) NOT NULL,
    "Currency"              varchar(10) NOT NULL,
    "PaymentMethod"         varchar(50) NULL,
    "Status"                varchar(40) NOT NULL,
    "PaymentGateway"        varchar(100) NULL,
    "TransactionReference"  varchar(255) NULL,
    "OrderCode"             bigint NOT NULL,
    "CheckoutUrl"           varchar(1000) NULL,
    "PaidAtUtc"             timestamptz NULL,
    "CreatedAtUtc"          timestamptz NOT NULL,
    "UpdatedAtUtc"          timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_payments_OrderCode"
    ON payment.payments ("OrderCode");
CREATE INDEX IF NOT EXISTS "IX_payments_RegistrationId"
    ON payment.payments ("RegistrationId");
CREATE INDEX IF NOT EXISTS "IX_payments_UserId"
    ON payment.payments ("UserId");

CREATE TABLE IF NOT EXISTS payment.payment_transactions (
    "Id"                    uuid PRIMARY KEY,
    "PaymentId"             uuid NOT NULL REFERENCES payment.payments("Id") ON DELETE CASCADE,
    "TransactionCode"       varchar(100) NOT NULL,
    "GatewayTransactionId"  varchar(255) NULL,
    "TransactionType"       varchar(50) NOT NULL,
    "Status"                varchar(40) NOT NULL,
    "Amount"                numeric(15,2) NOT NULL,
    "RawResponseJson"       jsonb NULL,
    "CreatedAtUtc"          timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_payment_transactions_PaymentId"
    ON payment.payment_transactions ("PaymentId");

-- -----------------------------------------------------------------------------
-- Stamp EF history (Ä‘á»ƒ `dotnet run` MigrateAsync khĂ´ng táº¡o láº¡i báº£ng)
-- -----------------------------------------------------------------------------
INSERT INTO iam."__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
    ('20260921164029_InitialIam', '10.0.12'),
    ('20260921182559_AddOutboxAndEmailOtps', '10.0.12'),
    ('20261005120000_AddUsername', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO event."__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
    ('20261005083653_InitialEvent', '10.0.12'),
    ('20261005111445_AddEventBannerImage', '10.0.12'),
    ('20261005130000_WidenRegistrationStringLimits', '10.0.12'),
    ('20261005140000_FilteredUniqueRegistrationIndex', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO payment."__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
    ('20261005104409_InitialPayment', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;


-- ========== SEED ==========

BEGIN;

-- -----------------------------------------------------------------------------
-- Roles (fixed GUIDs = IamDataSeeder)
-- -----------------------------------------------------------------------------
INSERT INTO iam.roles (role_id, role_name, description) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Admin', 'Quáº£n trá»‹ há»‡ thá»‘ng RaceDay'),
    ('22222222-2222-2222-2222-222222222222', 'User', 'NgÆ°á»i dĂ¹ng / váº­n Ä‘á»™ng viĂªn'),
    ('33333333-3333-3333-3333-333333333333', 'Organizer', 'Ban tá»• chá»©c sá»± kiá»‡n')
ON CONFLICT (role_id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- Users + profiles
-- gender: 0=Unknown 1=Male 2=Female | status: 0=Locked 1=Active 2=Pending
-- -----------------------------------------------------------------------------
-- Identity V3 hash for "MatKhau@123"
-- AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==
INSERT INTO iam.users (
    user_id, email, username, role_id, password_hash, full_name, phone,
    date_of_birth, gender, status, email_verified, created_at, updated_at
) VALUES
(
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'admin@raceday.vn', 'admin',
    '11111111-1111-1111-1111-111111111111',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Nguyá»…n VÄƒn Quáº£n Trá»‹', '0901000001', '1990-01-15', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'an.nguyen@raceday.vn', 'an.nguyen',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Nguyá»…n VÄƒn An', '0902000002', '1998-05-20', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'binh.tran@raceday.vn', 'binh.tran',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Tráº§n Thá»‹ BĂ¬nh', '0903000003', '1999-08-12', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'cuong.le@raceday.vn', 'cuong.le',
    '33333333-3333-3333-3333-333333333333',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'LĂª HoĂ ng CÆ°á»ng', '0904000004', '1995-03-08', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'dung.pham@raceday.vn', 'dung.pham',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Pháº¡m Minh DÅ©ng', '0905000005', '2000-11-25', 1, 2, false,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
    user_id, description, contact_email, contact_phone,
    verification_status, verified_at, verified_by
) VALUES
(
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'TĂ i khoáº£n quáº£n trá»‹ RaceDay', 'admin@raceday.vn', '0901000001',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'Váº­n Ä‘á»™ng viĂªn phong trĂ o TP.HCM', 'an.nguyen@raceday.vn', '0902000002',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'Runner bĂ¡n marathon HĂ  Ná»™i', 'binh.tran@raceday.vn', '0903000003',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Ban tá»• chá»©c giáº£i cháº¡y ÄĂ  Náºµng', 'cuong.le@raceday.vn', '0904000004',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'Há»“ sÆ¡ chá» xĂ¡c minh email', 'dung.pham@raceday.vn', '0905000005',
    0, NULL, NULL
)
ON CONFLICT (user_id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- Events â€” lá»‹ch KHĂ”NG trĂ¹ng ngĂ y
-- Status DB = enum name: Draft | PendingApproval | Approved | RegistrationOpen | ...
-- Organizer = cuong.le
-- -----------------------------------------------------------------------------
-- Calendar (seed):
--   2026-03-15  HCMC Night Run          RegistrationOpen
--   2026-06-07  Hanoi Lakeside Half     RegistrationOpen
--   2026-09-19  Nha Trang Ocean         Approved (chÆ°a má»Ÿ ÄK)
--   2026-11-22  Coastal ÄĂ  Náºµng         RegistrationOpen  (khá»›p EventDataSeeder)
--   2026-12-14  Sa Pa Mountain          RegistrationOpen  (khá»›p EventDataSeeder)
--   2027-02-08  Mekong Delta            PendingApproval
--   2027-05-01  Central Highlands       Draft

INSERT INTO event.race_events (
    "Id", "OrganizerUserId", "Name", "Tagline", "BannerImage", "Location",
    coord_lat, coord_lng, "EventDate", "StartTime", "Status",
    org_name, org_legal_entity, org_email, org_phone, org_website, org_verified,
    "TotalRunnersCap", weather_temp, weather_condition, weather_humidity,
    "TicketPricingJson", "MerchandiseJson", "RulesJson", "RewardsJson",
    "Description", "CreatedAtUtc", "UpdatedAtUtc", "SubmittedAtUtc", "ReviewedAtUtc"
) VALUES
-- 1) HCMC Night 2026-03-15
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Ho Chi Minh City Night Run 2026',
    'Cháº¡y Ä‘Ăªm Nguyá»…n Huá»‡',
    'https://images.unsplash.com/photo-1526676037777-05a232554f77?auto=format&fit=crop&w=1600&q=80',
    'Phá»‘ Ä‘i bá»™ Nguyá»…n Huá»‡, TP.HCM',
    10.7731, 106.7031, DATE '2026-03-15', TIME '18:00:00', 'RegistrationOpen',
    'Ban tá»• chá»©c RaceDay HCMC', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    5000, '29Â°C', 'ÄĂªm mĂ¡t', '75%',
    '{"currency":"VND"}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Giáº£i cháº¡y Ä‘Ăªm 5K/10K/21K â€” seed FE test thanh toĂ¡n.',
    TIMESTAMPTZ '2026-01-10 00:00:00+00', TIMESTAMPTZ '2026-01-10 00:00:00+00',
    TIMESTAMPTZ '2026-01-10 00:00:00+00', TIMESTAMPTZ '2026-01-11 00:00:00+00'
),
-- 2) Hanoi 2026-06-07
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Hanoi Lakeside Half 2026',
    'Half quanh Há»“ TĂ¢y',
    'https://images.unsplash.com/photo-1552674605-db6ffd4facb5?auto=format&fit=crop&w=1600&q=80',
    'Há»“ TĂ¢y, HĂ  Ná»™i',
    21.0583, 105.8229, DATE '2026-06-07', TIME '05:30:00', 'RegistrationOpen',
    'Ban tá»• chá»©c RaceDay HĂ  Ná»™i', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    2800, '24Â°C', 'MĂ¡t', '80%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Half marathon ven há»“ â€” seed.',
    TIMESTAMPTZ '2026-01-12 00:00:00+00', TIMESTAMPTZ '2026-01-12 00:00:00+00',
    TIMESTAMPTZ '2026-01-12 00:00:00+00', TIMESTAMPTZ '2026-01-13 00:00:00+00'
),
-- 3) Nha Trang 2026-09-19 (Approved, chÆ°a open)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa5',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Nha Trang Ocean Trail 2026',
    'Biá»ƒn + Ä‘á»“i',
    'https://images.unsplash.com/photo-1508609349937-5ec4ae374ebf?auto=format&fit=crop&w=1600&q=80',
    'Nha Trang, KhĂ¡nh HĂ²a',
    12.2388, 109.1967, DATE '2026-09-19', TIME '06:00:00', 'Approved',
    'Ban tá»• chá»©c RaceDay Nha Trang', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    1500, '28Â°C', 'Náº¯ng', '70%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'ÄĂ£ duyá»‡t â€” organizer cáº§n open-registration.',
    TIMESTAMPTZ '2026-01-14 00:00:00+00', TIMESTAMPTZ '2026-01-14 00:00:00+00',
    TIMESTAMPTZ '2026-01-14 00:00:00+00', TIMESTAMPTZ '2026-01-15 00:00:00+00'
),
-- 4) ÄĂ  Náºµng 2026-11-22 (EventDataSeeder)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Coastal Run ÄĂ  Náºµng 2026',
    'Cháº¡y cĂ¹ng biá»ƒn xanh â€” má»Ÿ Ä‘Äƒng kĂ½ ngay',
    'https://images.unsplash.com/photo-1452626635019-14babd4f4a0b?auto=format&fit=crop&w=1600&q=80',
    'Biá»ƒn Má»¹ KhĂª, ÄĂ  Náºµng',
    16.05995, 108.24909, DATE '2026-11-22', TIME '05:30:00', 'RegistrationOpen',
    'Ban tá»• chá»©c RaceDay ÄĂ  Náºµng', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    3200, '26Â°C', 'Náº¯ng nháº¹', '70%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Giáº£i cháº¡y duyĂªn háº£i ÄĂ  Náºµng â€” demo seed FE.',
    TIMESTAMPTZ '2026-01-16 00:00:00+00', TIMESTAMPTZ '2026-01-16 00:00:00+00',
    TIMESTAMPTZ '2026-01-16 00:00:00+00', TIMESTAMPTZ '2026-01-17 00:00:00+00'
),
-- 5) Sa Pa 2026-12-14 (EventDataSeeder)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Vietnam Mountain Marathon â€” Sa Pa (Demo)',
    'Trail nĂºi HoĂ ng LiĂªn SÆ¡n',
    'https://images.unsplash.com/photo-1476480862126-209bfaa8edc8?auto=format&fit=crop&w=1600&q=80',
    'Sa Pa, LĂ o Cai',
    22.3364, 103.8438, DATE '2026-12-14', TIME '06:00:00', 'RegistrationOpen',
    'Ban tá»• chá»©c RaceDay Sa Pa', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    1900, '14Â°C', 'SÆ°Æ¡ng mĂ¹', '85%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Demo trail Sa Pa.',
    TIMESTAMPTZ '2026-01-18 00:00:00+00', TIMESTAMPTZ '2026-01-18 00:00:00+00',
    TIMESTAMPTZ '2026-01-18 00:00:00+00', TIMESTAMPTZ '2026-01-19 00:00:00+00'
),
-- 6) Mekong 2027-02-08 Pending
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa6',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Mekong Delta Run 2027',
    'Cháº¡y Ä‘á»“ng báº±ng sĂ´ng nÆ°á»›c',
    'https://images.unsplash.com/photo-1486218119243-13883505764c?auto=format&fit=crop&w=1600&q=80',
    'Cáº§n ThÆ¡',
    10.0452, 105.7469, DATE '2027-02-08', TIME '05:45:00', 'PendingApproval',
    'Ban tá»• chá»©c RaceDay Cáº§n ThÆ¡', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', false,
    1200, NULL, NULL, NULL,
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Chá» admin duyá»‡t â€” seed admin pending list.',
    TIMESTAMPTZ '2026-01-20 00:00:00+00', TIMESTAMPTZ '2026-01-20 00:00:00+00',
    TIMESTAMPTZ '2026-01-20 00:00:00+00', NULL
),
-- 7) Highlands 2027-05-01 Draft
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa7',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Central Highlands Ultra 2027',
    'NhĂ¡p organizer',
    NULL,
    'ÄĂ  Láº¡t, LĂ¢m Äá»“ng',
    11.9404, 108.4583, DATE '2027-05-01', TIME '05:00:00', 'Draft',
    'Ban tá»• chá»©c RaceDay ÄĂ  Láº¡t', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', false,
    800, NULL, NULL, NULL,
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Báº£n nhĂ¡p â€” chÆ°a submit.',
    TIMESTAMPTZ '2026-01-22 00:00:00+00', TIMESTAMPTZ '2026-01-22 00:00:00+00',
    NULL, NULL
)
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Distances (stable IDs khá»›p EventDataSeeder cho ÄN / Sa Pa)
-- -----------------------------------------------------------------------------
INSERT INTO event.event_distances (
    "Id", "EventId", "Type", "Name", "DistanceKm",
    "ElevationGainM", "MaxElevationM", "MinElevationM",
    "Capacity", "RegisteredCount", "Price", "Currency", "Color",
    start_name, start_lat, start_lng, finish_name, finish_lat, finish_lng,
    "RouteGeometryJson"
) VALUES
-- HCMC Night
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
 '5K', '5K Night Fun', 5, 15, 10, 0, 2000, 1, 350000, 'VND', '#22C55E',
 'Start NH', 10.7731, 106.7031, 'Finish NH', 10.7731, 106.7031, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
 '10K', '10K City Lights', 10, 30, 18, 0, 1800, 0, 550000, 'VND', '#0EA5E9',
 'Start NH', 10.7731, 106.7031, 'Finish NH', 10.7760, 106.7010, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc3', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
 '21K', '21K Half Night', 21, 60, 25, 0, 1200, 0, 950000, 'VND', '#F59E0B',
 'Start NH', 10.7731, 106.7031, 'Finish NH', 10.7800, 106.7000, '{}'::jsonb),
-- Hanoi
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbd1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
 '21K', 'Lakeside Half', 21.1, 80, 20, 5, 1500, 0, 790000, 'VND', '#8B5CF6',
 'Há»“ TĂ¢y Start', 21.0583, 105.8229, 'Há»“ TĂ¢y Finish', 21.0583, 105.8229, '{}'::jsonb),
-- Nha Trang
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbe1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa5',
 '10K', 'Ocean 10K', 10, 120, 80, 0, 1000, 0, 420000, 'VND', '#06B6D4',
 'Start NT', 12.2388, 109.1967, 'Finish NT', 12.2450, 109.1900, '{}'::jsonb),
-- ÄĂ  Náºµng (EventDataSeeder IDs)
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '5K', 'Fun Run 5K', 5, 20, 15, 0, 1500, 0, 199000, 'VND', '#22c55e',
 'Start Má»¹ KhĂª', 16.05995, 108.24909, 'Finish Má»¹ KhĂª', 16.0612, 108.251, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '10K', 'Coastal 10K', 10, 40, 25, 0, 1200, 0, 349000, 'VND', '#0ea5e9',
 'Start Má»¹ KhĂª', 16.05995, 108.24909, 'Finish SÆ¡n TrĂ ', 16.08, 108.27, '{}'::jsonb),
-- Sa Pa
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb3', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
 '21K', 'Cloud Valley', 21, 980, 2150, 1450, 800, 0, 900000, 'VND', '#F59E0B',
 'Sa Pa Square', 22.3364, 103.8438, 'Sa Pa Square', 22.3364, 103.8438, '{}'::jsonb),
-- Mekong
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbf1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa6',
 '10K', 'Delta 10K', 10, 10, 5, 0, 800, 0, 299000, 'VND', '#84CC16',
 'Start CT', 10.0452, 105.7469, 'Finish CT', 10.0500, 105.7500, '{}'::jsonb),
-- Highlands draft
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbf2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa7',
 '42K', 'Ultra Highland', 42, 1800, 1600, 900, 400, 0, 1200000, 'VND', '#EF4444',
 'Start ÄĂ  Láº¡t', 11.9404, 108.4583, 'Finish ÄĂ  Láº¡t', 11.9404, 108.4583, '{}'::jsonb)
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Documents (1 plan / giáº£i public-ish)
-- -----------------------------------------------------------------------------
INSERT INTO event.event_documents (
    "Id", "EventId", "DocumentKey", "Title", "FileName", "FileUrl", "Required", "UploadedAtUtc"
) VALUES
('33333333-cccc-4ccc-8ccc-ccccccccccc1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 'plan', 'Káº¿ hoáº¡ch tá»• chá»©c', 'plan.pdf', 'https://example.com/demo/plan.pdf', true,
 TIMESTAMPTZ '2026-01-16 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
 'plan', 'Káº¿ hoáº¡ch tá»• chá»©c', 'plan.pdf', 'https://example.com/demo/sapa-plan.pdf', true,
 TIMESTAMPTZ '2026-01-18 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc3', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
 'plan', 'Káº¿ hoáº¡ch tá»• chá»©c', 'hcmc-plan.pdf', 'https://example.com/demo/hcmc-plan.pdf', true,
 TIMESTAMPTZ '2026-01-10 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc4', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa6',
 'plan', 'Káº¿ hoáº¡ch tá»• chá»©c', 'mekong-plan.pdf', 'https://example.com/demo/mekong-plan.pdf', true,
 TIMESTAMPTZ '2026-01-20 00:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Sample registrations (khĂ´ng trĂ¹ng EventId+DistanceId+UserId active)
-- An â†’ HCMC 5K Confirmed (Ä‘Ă£ tráº£ + duyá»‡t)
-- BĂ¬nh â†’ ÄĂ  Náºµng 10K PendingPayment (test checkout)
-- -----------------------------------------------------------------------------
INSERT INTO event.event_registrations (
    "Id", "EventId", "DistanceId", "UserId", "Status",
    "FullName", "Email", "Phone", "DateOfBirth", "Gender",
    "HeightCm", "WeightKg", "BloodType",
    "EmergencyName", "EmergencyPhone",
    "ShirtType", "ShirtSize", "ShirtColor",
    "MedicalCodesJson", "HealthCertFileName", "HealthCertUrl",
    "LiabilityAccepted", "LiabilityAcceptedAtUtc",
    "BibNumber", "CreatedAtUtc", "UpdatedAtUtc"
) VALUES
(
    '44444444-dddd-4ddd-8ddd-ddddddddddd1',
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
    '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc1',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'Confirmed',
    'Nguyá»…n VÄƒn An', 'an.nguyen@raceday.vn', '0902000002',
    DATE '1998-05-20', 'Male', 172, 68, 'O',
    'Nguyá»…n Thá»‹ Lan', '0902111111',
    'Tee', 'M', 'Black',
    '["none"]'::jsonb, 'health-an.pdf', 'https://example.com/demo/health-an.pdf',
    true, TIMESTAMPTZ '2026-02-01 10:00:00+00',
    '1001',
    TIMESTAMPTZ '2026-02-01 10:00:00+00', TIMESTAMPTZ '2026-02-02 08:00:00+00'
),
(
    '44444444-dddd-4ddd-8ddd-ddddddddddd2',
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
    '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2',
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'PendingPayment',
    'Tráº§n Thá»‹ BĂ¬nh', 'binh.tran@raceday.vn', '0903000003',
    DATE '1999-08-12', 'Female', 160, 52, 'A',
    'Tráº§n VÄƒn Nam', '0903222222',
    'Tee', 'S', 'White',
    '["none"]'::jsonb, NULL, NULL,
    true, TIMESTAMPTZ '2026-02-05 09:00:00+00',
    NULL,
    TIMESTAMPTZ '2026-02-05 09:00:00+00', TIMESTAMPTZ '2026-02-05 09:00:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- Sync RegisteredCount cho cá»± ly Ä‘Ă£ cĂ³ confirmed
UPDATE event.event_distances d
SET "RegisteredCount" = sub.cnt
FROM (
    SELECT "DistanceId", COUNT(*)::int AS cnt
    FROM event.event_registrations
    WHERE "Status" IN ('PendingApproval', 'Confirmed')
    GROUP BY "DistanceId"
) sub
WHERE d."Id" = sub."DistanceId";

-- -----------------------------------------------------------------------------
-- Sample payment (An â€” Paid cho registration confirmed)
-- OrderCode unique
-- -----------------------------------------------------------------------------
INSERT INTO payment.payments (
    "Id", "RegistrationId", "UserId", "Amount", "Currency",
    "PaymentMethod", "Status", "PaymentGateway", "TransactionReference",
    "OrderCode", "CheckoutUrl", "PaidAtUtc", "CreatedAtUtc", "UpdatedAtUtc"
) VALUES
(
    '55555555-eeee-4eee-8eee-eeeeeeeeeee1',
    '44444444-dddd-4ddd-8ddd-ddddddddddd1',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    350000, 'VND',
    'PayOS', 'Paid', 'PayOS', 'seed-paid-an-hcmc-5k',
    2602011001,
    NULL,
    TIMESTAMPTZ '2026-02-01 10:05:00+00',
    TIMESTAMPTZ '2026-02-01 10:00:00+00',
    TIMESTAMPTZ '2026-02-01 10:05:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO payment.payment_transactions (
    "Id", "PaymentId", "TransactionCode", "GatewayTransactionId",
    "TransactionType", "Status", "Amount", "RawResponseJson", "CreatedAtUtc"
) VALUES
(
    '66666666-ffff-4fff-8fff-fffffffffff1',
    '55555555-eeee-4eee-8eee-eeeeeeeeeee1',
    'create-2602011001', 'seed-link-1',
    'create_link', 'Succeeded', 350000,
    '{"seed":true}'::jsonb,
    TIMESTAMPTZ '2026-02-01 10:00:00+00'
),
(
    '66666666-ffff-4fff-8fff-fffffffffff2',
    '55555555-eeee-4eee-8eee-eeeeeeeeeee1',
    'pay-2602011001', 'seed-pay-1',
    'payment', 'Succeeded', 350000,
    '{"seed":true,"status":"PAID"}'::jsonb,
    TIMESTAMPTZ '2026-02-01 10:05:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

COMMIT;

-- -----------------------------------------------------------------------------
-- Verify (khĂ´ng fail transaction)
-- -----------------------------------------------------------------------------
SELECT 'roles' AS t, COUNT(*) FROM iam.roles
UNION ALL SELECT 'users', COUNT(*) FROM iam.users
UNION ALL SELECT 'events', COUNT(*) FROM event.race_events
UNION ALL SELECT 'distances', COUNT(*) FROM event.event_distances
UNION ALL SELECT 'registrations', COUNT(*) FROM event.event_registrations
UNION ALL SELECT 'payments', COUNT(*) FROM payment.payments
ORDER BY 1;

SELECT "Name", "EventDate", "Status"
FROM event.race_events
ORDER BY "EventDate";


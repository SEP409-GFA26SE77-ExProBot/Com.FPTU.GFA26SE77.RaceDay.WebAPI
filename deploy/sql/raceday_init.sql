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
--   Staff ops (4/role):
--     coord1..4@raceday.vn (Coordinator)
--     photo1..4@raceday.vn (Photographer)
--     vol1..4@raceday.vn (Volunteer)
--
-- Lịch giải seed (mỗi ngày 1, không trùng):
--   2026-03-15 HCMC Night | 2026-06-07 Hanoi | 2026-06-21 VnExpress Marathon Quy Nhon (flagship R2)
--   2026-09-19 Nha Trang (Approved) | 2026-12-14 Sa Pa
--   2027-02-08 Mekong (Pending) | 2027-05-01 Highlands (Draft)
-- Media R2 (banner / docs / health-certs) — absolute URL, không phụ thuộc Storage__PublicBaseUrl
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
-- Stamp EF history (để `dotnet run` MigrateAsync không tạo lại bảng)
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
    ('11111111-1111-1111-1111-111111111111', 'Admin', 'Quản trị hệ thống RaceDay'),
    ('22222222-2222-2222-2222-222222222222', 'User', 'Người dùng / vận động viên'),
    ('33333333-3333-3333-3333-333333333333', 'Organizer', 'Ban tổ chức sự kiện'),
    ('44444444-4444-4444-4444-444444444444', 'Coordinator', 'Điều phối viên giải'),
    ('55555555-5555-5555-5555-555555555555', 'Photographer', 'Nhiếp ảnh giải'),
    ('66666666-6666-6666-6666-666666666666', 'Volunteer', 'Tình nguyện viên giải')
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
    'Nguyễn Văn Quản Trị', '0901000001', '1990-01-15', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'an.nguyen@raceday.vn', 'an.nguyen',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Nguyễn Văn An', '0902000002', '1998-05-20', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'binh.tran@raceday.vn', 'binh.tran',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Trần Thị Bình', '0903000003', '1999-08-12', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'cuong.le@raceday.vn', 'cuong.le',
    '33333333-3333-3333-3333-333333333333',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Lê Hoàng Cường', '0904000004', '1995-03-08', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'dung.pham@raceday.vn', 'dung.pham',
    '22222222-2222-2222-2222-222222222222',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Phạm Minh Dũng', '0905000005', '2000-11-25', 1, 2, false,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
-- Coordinator ×4
(
    'a1444444-0001-4000-8000-000000000001',
    'coord1@raceday.vn', 'coord1',
    '44444444-4444-4444-4444-444444444444',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Nguyễn Điều Phối Một', '0911000001', '1994-02-10', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1444444-0002-4000-8000-000000000002',
    'coord2@raceday.vn', 'coord2',
    '44444444-4444-4444-4444-444444444444',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Trần Điều Phối Hai', '0911000002', '1993-06-18', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1444444-0003-4000-8000-000000000003',
    'coord3@raceday.vn', 'coord3',
    '44444444-4444-4444-4444-444444444444',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Lê Điều Phối Ba', '0911000003', '1996-09-05', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1444444-0004-4000-8000-000000000004',
    'coord4@raceday.vn', 'coord4',
    '44444444-4444-4444-4444-444444444444',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Phạm Điều Phối Tư', '0911000004', '1992-12-22', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
-- Photographer ×4
(
    'a1555555-0001-4000-8000-000000000001',
    'photo1@raceday.vn', 'photo1',
    '55555555-5555-5555-5555-555555555555',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Hoàng Ảnh Một', '0912000001', '1995-04-08', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1555555-0002-4000-8000-000000000002',
    'photo2@raceday.vn', 'photo2',
    '55555555-5555-5555-5555-555555555555',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Vũ Ảnh Hai', '0912000002', '1997-07-14', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1555555-0003-4000-8000-000000000003',
    'photo3@raceday.vn', 'photo3',
    '55555555-5555-5555-5555-555555555555',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Đặng Ảnh Ba', '0912000003', '1994-01-30', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1555555-0004-4000-8000-000000000004',
    'photo4@raceday.vn', 'photo4',
    '55555555-5555-5555-5555-555555555555',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Bùi Ảnh Tư', '0912000004', '1998-10-03', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
-- Volunteer ×4
(
    'a1666666-0001-4000-8000-000000000001',
    'vol1@raceday.vn', 'vol1',
    '66666666-6666-6666-6666-666666666666',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Ngô Tình Nguyện Một', '0913000001', '2001-03-12', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1666666-0002-4000-8000-000000000002',
    'vol2@raceday.vn', 'vol2',
    '66666666-6666-6666-6666-666666666666',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Đỗ Tình Nguyện Hai', '0913000002', '2000-05-21', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1666666-0003-4000-8000-000000000003',
    'vol3@raceday.vn', 'vol3',
    '66666666-6666-6666-6666-666666666666',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Mai Tình Nguyện Ba', '0913000003', '1999-08-09', 1, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
),
(
    'a1666666-0004-4000-8000-000000000004',
    'vol4@raceday.vn', 'vol4',
    '66666666-6666-6666-6666-666666666666',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Lý Tình Nguyện Tư', '0913000004', '2002-11-17', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
    user_id, description, contact_email, contact_phone,
    verification_status, verified_at, verified_by
) VALUES
(
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'Tài khoản quản trị RaceDay', 'admin@raceday.vn', '0901000001',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'Vận động viên phong trào TP.HCM', 'an.nguyen@raceday.vn', '0902000002',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'Runner bán marathon Hà Nội', 'binh.tran@raceday.vn', '0903000003',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Ban tổ chức giải chạy Đà Nẵng', 'cuong.le@raceday.vn', '0904000004',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'Hồ sơ chờ xác minh email', 'dung.pham@raceday.vn', '0905000005',
    0, NULL, NULL
),
('a1444444-0001-4000-8000-000000000001', 'Coordinator seed #1', 'coord1@raceday.vn', '0911000001', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1444444-0002-4000-8000-000000000002', 'Coordinator seed #2', 'coord2@raceday.vn', '0911000002', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1444444-0003-4000-8000-000000000003', 'Coordinator seed #3', 'coord3@raceday.vn', '0911000003', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1444444-0004-4000-8000-000000000004', 'Coordinator seed #4', 'coord4@raceday.vn', '0911000004', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1555555-0001-4000-8000-000000000001', 'Photographer seed #1', 'photo1@raceday.vn', '0912000001', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1555555-0002-4000-8000-000000000002', 'Photographer seed #2', 'photo2@raceday.vn', '0912000002', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1555555-0003-4000-8000-000000000003', 'Photographer seed #3', 'photo3@raceday.vn', '0912000003', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1555555-0004-4000-8000-000000000004', 'Photographer seed #4', 'photo4@raceday.vn', '0912000004', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1666666-0001-4000-8000-000000000001', 'Volunteer seed #1', 'vol1@raceday.vn', '0913000001', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1666666-0002-4000-8000-000000000002', 'Volunteer seed #2', 'vol2@raceday.vn', '0913000002', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1666666-0003-4000-8000-000000000003', 'Volunteer seed #3', 'vol3@raceday.vn', '0913000003', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'),
('a1666666-0004-4000-8000-000000000004', 'Volunteer seed #4', 'vol4@raceday.vn', '0913000004', 2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system')
ON CONFLICT (user_id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- Events — lịch KHÔNG trùng ngày
-- Status DB = enum name: Draft | PendingApproval | Approved | RegistrationOpen | ...
-- Organizer = cuong.le
-- -----------------------------------------------------------------------------
-- Calendar (seed):
--   2026-03-15  HCMC Night Run          RegistrationOpen
--   2026-06-07  Hanoi Lakeside Half     RegistrationOpen
--   2026-09-19  Nha Trang Ocean         Approved (chưa mở ĐK)
--   2026-06-21  VnExpress Quy Nhon      RegistrationOpen  (khớp EventDataSeeder aaa1 + R2)
--   2026-12-14  Sa Pa Mountain          RegistrationOpen  (khớp EventDataSeeder)
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
    'Chạy đêm Nguyễn Huệ',
    'https://images.unsplash.com/photo-1526676037777-05a232554f77?auto=format&fit=crop&w=1600&q=80',
    'Phố đi bộ Nguyễn Huệ, TP.HCM',
    10.7731, 106.7031, DATE '2026-03-15', TIME '18:00:00', 'RegistrationOpen',
    'Ban tổ chức RaceDay HCMC', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    5000, '29°C', 'Đêm mát', '75%',
    '{"currency":"VND"}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Giải chạy đêm 5K/10K/21K — seed FE test thanh toán.',
    TIMESTAMPTZ '2026-01-10 00:00:00+00', TIMESTAMPTZ '2026-01-10 00:00:00+00',
    TIMESTAMPTZ '2026-01-10 00:00:00+00', TIMESTAMPTZ '2026-01-11 00:00:00+00'
),
-- 2) Hanoi 2026-06-07
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Hanoi Lakeside Half 2026',
    'Half quanh Hồ Tây',
    'https://images.unsplash.com/photo-1552674605-db6ffd4facb5?auto=format&fit=crop&w=1600&q=80',
    'Hồ Tây, Hà Nội',
    21.0583, 105.8229, DATE '2026-06-07', TIME '05:30:00', 'RegistrationOpen',
    'Ban tổ chức RaceDay Hà Nội', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    2800, '24°C', 'Mát', '80%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Half marathon ven hồ — seed.',
    TIMESTAMPTZ '2026-01-12 00:00:00+00', TIMESTAMPTZ '2026-01-12 00:00:00+00',
    TIMESTAMPTZ '2026-01-12 00:00:00+00', TIMESTAMPTZ '2026-01-13 00:00:00+00'
),
-- 3) Nha Trang 2026-09-19 (Approved, chưa open)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa5',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Nha Trang Ocean Trail 2026',
    'Biển + đồi',
    'https://images.unsplash.com/photo-1508609349937-5ec4ae374ebf?auto=format&fit=crop&w=1600&q=80',
    'Nha Trang, Khánh Hòa',
    12.2388, 109.1967, DATE '2026-09-19', TIME '06:00:00', 'Approved',
    'Ban tổ chức RaceDay Nha Trang', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    1500, '28°C', 'Nắng', '70%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Đã duyệt — organizer cần open-registration.',
    TIMESTAMPTZ '2026-01-14 00:00:00+00', TIMESTAMPTZ '2026-01-14 00:00:00+00',
    TIMESTAMPTZ '2026-01-14 00:00:00+00', TIMESTAMPTZ '2026-01-15 00:00:00+00'
),
-- 4) VnExpress Marathon Quy Nhon 2026-06-21 (EventDataSeeder aaa1 + R2 media)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'VnExpress Marathon Quy Nhon 2026',
    'Chạy đêm thành phố biển — VnExpress Marathon',
    'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-banners/20261010153523-b062278b2115439abd42da13aa689368.JPG',
    'Quy Nhơn, Bình Định',
    13.782967, 109.219663, DATE '2026-06-21', TIME '04:00:00', 'RegistrationOpen',
    'Ban tổ chức VnExpress Marathon Quy Nhơn', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    12000, '27°C', 'Đêm mát', '78%',
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Giải chạy Việt: VnExpress Marathon Quy Nhơn 2026. Cung đường ven biển, check-in QR, BIB điện tử. Seed FE/Admin với banner & hồ sơ trên R2.',
    TIMESTAMPTZ '2026-01-16 00:00:00+00', TIMESTAMPTZ '2026-01-16 00:00:00+00',
    TIMESTAMPTZ '2026-01-16 00:00:00+00', TIMESTAMPTZ '2026-01-17 00:00:00+00'
),
-- 5) Sa Pa 2026-12-14 (EventDataSeeder)
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Vietnam Mountain Marathon — Sa Pa (Demo)',
    'Trail núi Hoàng Liên Sơn',
    'https://images.unsplash.com/photo-1476480862126-209bfaa8edc8?auto=format&fit=crop&w=1600&q=80',
    'Sa Pa, Lào Cai',
    22.3364, 103.8438, DATE '2026-12-14', TIME '06:00:00', 'RegistrationOpen',
    'Ban tổ chức RaceDay Sa Pa', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', true,
    1900, '14°C', 'Sương mù', '85%',
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
    'Chạy đồng bằng sông nước',
    'https://images.unsplash.com/photo-1486218119243-13883505764c?auto=format&fit=crop&w=1600&q=80',
    'Cần Thơ',
    10.0452, 105.7469, DATE '2027-02-08', TIME '05:45:00', 'PendingApproval',
    'Ban tổ chức RaceDay Cần Thơ', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', false,
    1200, NULL, NULL, NULL,
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Chờ admin duyệt — seed admin pending list.',
    TIMESTAMPTZ '2026-01-20 00:00:00+00', TIMESTAMPTZ '2026-01-20 00:00:00+00',
    TIMESTAMPTZ '2026-01-20 00:00:00+00', NULL
),
-- 7) Highlands 2027-05-01 Draft
(
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa7',
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'RaceDay Central Highlands Ultra 2027',
    'Nháp organizer',
    NULL,
    'Đà Lạt, Lâm Đồng',
    11.9404, 108.4583, DATE '2027-05-01', TIME '05:00:00', 'Draft',
    'Ban tổ chức RaceDay Đà Lạt', 'RaceDay Event Company', 'cuong.le@raceday.vn', '0904000004',
    'https://raceday.vn', false,
    800, NULL, NULL, NULL,
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    'Bản nháp — chưa submit.',
    TIMESTAMPTZ '2026-01-22 00:00:00+00', TIMESTAMPTZ '2026-01-22 00:00:00+00',
    NULL, NULL
)
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Distances (stable IDs khớp EventDataSeeder cho ĐN / Sa Pa)
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
 'Hồ Tây Start', 21.0583, 105.8229, 'Hồ Tây Finish', 21.0583, 105.8229, '{}'::jsonb),
-- Nha Trang
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbe1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa5',
 '10K', 'Ocean 10K', 10, 120, 80, 0, 1000, 0, 420000, 'VND', '#06B6D4',
 'Start NT', 12.2388, 109.1967, 'Finish NT', 12.2450, 109.1900, '{}'::jsonb),
-- Quy Nhơn (EventDataSeeder IDs — 5K/10K/21K/42K)
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '5K', 'Fun Run 5K', 5, 15, 12, 0, 4000, 0, 450000, 'VND', '#22c55e',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '10K', 'City 10K', 10, 30, 18, 0, 3500, 0, 650000, 'VND', '#0ea5e9',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.7855, 109.221, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb4', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '21K', 'Half Marathon', 21.1, 80, 35, 0, 2500, 0, 990000, 'VND', '#F59E0B',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb5', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '42K', 'Full Marathon', 42.195, 150, 40, 0, 2000, 0, 1450000, 'VND', '#EF4444',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{}'::jsonb),
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
 'Start Đà Lạt', 11.9404, 108.4583, 'Finish Đà Lạt', 11.9404, 108.4583, '{}'::jsonb)
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Checkpoints cho 3 cự ly có registration seed (HCMC 5K, Đà Nẵng 10K, Hà Nội 21K)
-- Type = tên enum CheckpointType
-- -----------------------------------------------------------------------------
INSERT INTO event.event_checkpoints (
    "Id", "DistanceId", "ExternalId", "Name", "Km", "Lat", "Lng", "Type", "Cutoff", "Elevation", "SortOrder"
) VALUES
('55555555-cccc-4ccc-8ccc-ccccccccccc1', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc1',
 'CP-HCMC-START', 'Start Nguyễn Huệ', 0, 10.7731, 106.7031, 'Start', NULL, 5, 0),
('55555555-cccc-4ccc-8ccc-ccccccccccc2', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc1',
 'CP-HCMC-AID', 'Aid 2.5K', 2.5, 10.7748, 106.7044, 'Aid', NULL, 6, 1),
('55555555-cccc-4ccc-8ccc-ccccccccccc3', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbc1',
 'CP-HCMC-FIN', 'Finish Nguyễn Huệ', 5, 10.7762, 106.7018, 'Finish', '19:30', 5, 2),
('55555555-cccc-4ccc-8ccc-ccccccccccc4', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2',
 'CP-DN-START', 'Start Mỹ Khê', 0, 16.05995, 108.24909, 'Start', NULL, 4, 0),
('55555555-cccc-4ccc-8ccc-ccccccccccc5', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2',
 'CP-DN-WATER', 'Water 5K', 5, 16.0680, 108.2580, 'Water', NULL, 8, 1),
('55555555-cccc-4ccc-8ccc-ccccccccccc6', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2',
 'CP-DN-FIN', 'Finish Sơn Trà', 10, 16.0800, 108.2700, 'Finish', '08:30', 12, 2),
('55555555-cccc-4ccc-8ccc-ccccccccccc7', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbd1',
 'CP-HN-START', 'Start Hồ Tây', 0, 21.0583, 105.8229, 'Start', NULL, 10, 0),
('55555555-cccc-4ccc-8ccc-ccccccccccc8', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbd1',
 'CP-HN-AID', 'Aid 10K', 10, 21.0680, 105.8300, 'Aid', NULL, 15, 1),
('55555555-cccc-4ccc-8ccc-ccccccccccc9', '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbd1',
 'CP-HN-FIN', 'Finish Hồ Tây', 21.1, 21.0583, 105.8229, 'Finish', '09:00', 10, 2)
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Documents (1 plan / giải public-ish)
-- -----------------------------------------------------------------------------
INSERT INTO event.event_documents (
    "Id", "EventId", "DocumentKey", "Title", "FileName", "FileUrl", "Required", "UploadedAtUtc"
) VALUES
('33333333-cccc-4ccc-8ccc-ccccccccccc1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 'plan', 'Hồ sơ / kế hoạch tổ chức',
 '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-16 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-cccccccccc10', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 'permit', 'Giấy phép / hồ sơ đính kèm',
 '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-16 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc2', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
 'plan', 'Hồ sơ / kế hoạch tổ chức',
 '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-18 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc3', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
 'plan', 'Kế hoạch tổ chức', 'hcmc-plan.pdf',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-10 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-ccccccccccc4', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa6',
 'plan', 'Kế hoạch tổ chức', 'mekong-plan.pdf',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-20 00:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

-- -----------------------------------------------------------------------------
-- Sample registrations (không trùng EventId+DistanceId+UserId active)
-- An → HCMC 5K Confirmed (đã trả + duyệt)
-- Bình → Đà Nẵng 10K PendingPayment (test checkout)
-- An → Hà Nội 21K Confirmed (registration thứ 3 cho RaceMonitoring seed)
-- Bình → Quy Nhơn 10K PendingPayment (test checkout)
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
    'Nguyễn Văn An', 'an.nguyen@raceday.vn', '0902000002',
    DATE '1998-05-20', 'Male', 172, 68, 'O',
    'Nguyễn Thị Lan', '0902111111',
    'Tee', 'M', 'Black',
    '["none"]'::jsonb,
    '20261009085141-ef89453832cd486a8e2d275de30c8a6c.png',
    'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/health-certs/20261009085141-ef89453832cd486a8e2d275de30c8a6c.png',
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
    'Trần Thị Bình', 'binh.tran@raceday.vn', '0903000003',
    DATE '1999-08-12', 'Female', 160, 52, 'A',
    'Trần Văn Nam', '0903222222',
    'Tee', 'S', 'White',
    '["none"]'::jsonb,
    '20261009085141-ef89453832cd486a8e2d275de30c8a6c.png',
    'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/health-certs/20261009085141-ef89453832cd486a8e2d275de30c8a6c.png',
    true, TIMESTAMPTZ '2026-02-05 09:00:00+00',
    NULL,
    TIMESTAMPTZ '2026-02-05 09:00:00+00', TIMESTAMPTZ '2026-02-05 09:00:00+00'
),
(
    '44444444-dddd-4ddd-8ddd-ddddddddddd3',
    '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
    '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbd1',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'Confirmed',
    'Nguyễn Văn An', 'an.nguyen@raceday.vn', '0902000002',
    DATE '1998-05-20', 'Male', 172, 68, 'O',
    'Nguyễn Thị Lan', '0902111111',
    'Tee', 'M', 'Black',
    '["none"]'::jsonb, 'health-an.pdf', 'https://example.com/demo/health-an.pdf',
    true, TIMESTAMPTZ '2026-03-01 08:00:00+00',
    '2101',
    TIMESTAMPTZ '2026-03-01 08:00:00+00', TIMESTAMPTZ '2026-03-02 08:00:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- Sync RegisteredCount cho cự ly đã có confirmed
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
-- Sample payment (An — Paid cho registration confirmed)
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

-- -----------------------------------------------------------------------------
-- Upsert flagship Viet run (volume Postgres đã có data cũ Đà Nẵng → refresh R2)
-- Chạy được cả lần init mới lẫn re-run trên DB đang sống.
-- -----------------------------------------------------------------------------
UPDATE event.race_events SET
    "Name" = 'VnExpress Marathon Quy Nhon 2026',
    "Tagline" = 'Chạy đêm thành phố biển — VnExpress Marathon',
    "BannerImage" = 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-banners/20261010153523-b062278b2115439abd42da13aa689368.JPG',
    "Location" = 'Quy Nhơn, Bình Định',
    coord_lat = 13.782967,
    coord_lng = 109.219663,
    "EventDate" = DATE '2026-06-21',
    "StartTime" = TIME '04:00:00',
    org_name = 'Ban tổ chức VnExpress Marathon Quy Nhơn',
    org_verified = true,
    org_account_holder = COALESCE(org_account_holder, 'CONG TY RACE DAY'),
    org_account_number = COALESCE(org_account_number, '0123456789'),
    org_bank_name = COALESCE(org_bank_name, 'Vietcombank'),
    org_bank_branch = COALESCE(org_bank_branch, 'Quy Nhơn'),
    org_transfer_note = COALESCE(org_transfer_note, 'Giai ngan VnExpress Marathon Quy Nhon'),
    "TotalRunnersCap" = 12000,
    weather_temp = '27°C',
    weather_condition = 'Đêm mát',
    weather_humidity = '78%',
    "Description" = 'Giải chạy Việt: VnExpress Marathon Quy Nhơn 2026. Cung đường ven biển, check-in QR, BIB điện tử. Seed FE/Admin với banner & hồ sơ trên R2.',
    "UpdatedAtUtc" = TIMESTAMPTZ '2026-10-13 00:00:00+00'
WHERE "Id" = '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1';

INSERT INTO event.event_distances (
    "Id", "EventId", "Type", "Name", "DistanceKm",
    "ElevationGainM", "MaxElevationM", "MinElevationM",
    "Capacity", "RegisteredCount", "Price", "Currency", "Color",
    start_name, start_lat, start_lng, finish_name, finish_lat, finish_lng,
    "RouteGeometryJson"
) VALUES
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb4', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '21K', 'Half Marathon', 21.1, 80, 35, 0, 2500, 0, 990000, 'VND', '#F59E0B',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{}'::jsonb),
('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb5', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 '42K', 'Full Marathon', 42.195, 150, 40, 0, 2000, 0, 1450000, 'VND', '#EF4444',
 'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{}'::jsonb)
ON CONFLICT ("Id") DO NOTHING;

UPDATE event.event_distances SET
    "Name" = CASE "Id"
        WHEN '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1' THEN 'Fun Run 5K'
        WHEN '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2' THEN 'City 10K'
        ELSE "Name" END,
    "Price" = CASE "Id"
        WHEN '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1' THEN 450000
        WHEN '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2' THEN 650000
        ELSE "Price" END,
    start_name = 'Start Quy Nhơn', start_lat = 13.782967, start_lng = 109.219663,
    finish_name = 'Finish Quy Nhơn', finish_lat = 13.782967, finish_lng = 109.219663
WHERE "EventId" = '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1'
  AND "Id" IN (
    '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1',
    '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2');

INSERT INTO event.event_documents (
    "Id", "EventId", "DocumentKey", "Title", "FileName", "FileUrl", "Required", "UploadedAtUtc"
) VALUES
('33333333-cccc-4ccc-8ccc-ccccccccccc1', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 'plan', 'Hồ sơ / kế hoạch tổ chức',
 '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-16 00:00:00+00'),
('33333333-cccc-4ccc-8ccc-cccccccccc10', '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
 'permit', 'Giấy phép / hồ sơ đính kèm',
 '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
 true, TIMESTAMPTZ '2026-01-16 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "FileName" = EXCLUDED."FileName",
    "FileUrl" = EXCLUDED."FileUrl";

UPDATE event.event_registrations SET
    "HealthCertFileName" = '20261009085141-ef89453832cd486a8e2d275de30c8a6c.png',
    "HealthCertUrl" = 'https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/health-certs/20261009085141-ef89453832cd486a8e2d275de30c8a6c.png'
WHERE "Id" IN (
    '44444444-dddd-4ddd-8ddd-ddddddddddd1',
    '44444444-dddd-4ddd-8ddd-ddddddddddd2');

COMMIT;

-- -----------------------------------------------------------------------------
-- Verify (không fail transaction)
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


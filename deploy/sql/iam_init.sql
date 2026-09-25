-- RaceDay IAM — PostgreSQL 18
-- Target database: postgres
-- Schema: iam
-- Dev password for all seed users: MatKhau@123
-- Note: password_hash values are Identity-compatible hashes generated for MatKhau@123
--       (ASP.NET Core PasswordHasher V3). If login fails after reset hasher settings, re-seed via app.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS iam;

-- ========== ROLES ==========
CREATE TABLE IF NOT EXISTS iam.roles (
    role_id     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    role_name   varchar(50)  NOT NULL,
    description varchar(255),
    CONSTRAINT uq_iam_roles_role_name UNIQUE (role_name)
);

-- ========== USERS ==========
CREATE TABLE IF NOT EXISTS iam.users (
    user_id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email           varchar(100) NOT NULL,
    role_id         uuid NOT NULL REFERENCES iam.roles(role_id),
    password_hash   varchar(255) NOT NULL,
    full_name       varchar(100) NOT NULL,
    phone           varchar(20),
    date_of_birth   date,
    gender          smallint NOT NULL DEFAULT 0,
    status          smallint NOT NULL DEFAULT 1,
    email_verified  boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_iam_users_email UNIQUE (email)
);

CREATE INDEX IF NOT EXISTS ix_iam_users_role_id ON iam.users(role_id);

-- ========== EMAIL OTPs (register verify / forgot password) ==========
CREATE TABLE IF NOT EXISTS iam.email_otps (
    id uuid PRIMARY KEY,
    user_id uuid NULL,
    email varchar(100) NOT NULL,
    purpose smallint NOT NULL,
    code_hash varchar(128) NOT NULL,
    expires_on_utc timestamptz NOT NULL,
    attempt_count int NOT NULL DEFAULT 0,
    consumed_on_utc timestamptz NULL,
    created_on_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_email_otps_email_purpose_created
    ON iam.email_otps (email, purpose, created_on_utc);

-- ========== PROFILES (1-1) ==========
CREATE TABLE IF NOT EXISTS iam.profiles (
    user_id              uuid PRIMARY KEY REFERENCES iam.users(user_id) ON DELETE CASCADE,
    description          varchar(255),
    contact_email        varchar(100),
    contact_phone        varchar(100),
    verification_status  smallint NOT NULL DEFAULT 0,
    verified_at          timestamptz,
    verified_by          varchar(100)
);

-- ========== EF migrations history (if using EF after this script) ==========
CREATE TABLE IF NOT EXISTS iam."__EFMigrationsHistory" (
    "MigrationId"    varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32)  NOT NULL
);

INSERT INTO iam."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260921164029_InitialIam', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ========== SEED ROLES ==========
INSERT INTO iam.roles (role_id, role_name, description) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Admin',     'Quản trị hệ thống RaceDay'),
    ('22222222-2222-2222-2222-222222222222', 'User',      'Người dùng / vận động viên'),
    ('33333333-3333-3333-3333-333333333333', 'Organizer', 'Ban tổ chức sự kiện')
ON CONFLICT (role_name) DO NOTHING;

-- ========== SEED USERS ==========
-- password_hash: will be overwritten by app seeder with real Identity hash if starts with PLACEHOLDER
INSERT INTO iam.users (
    user_id, email, role_id, password_hash, full_name, phone,
    date_of_birth, gender, status, email_verified, created_at, updated_at
) VALUES
(
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'admin@raceday.vn',
    '11111111-1111-1111-1111-111111111111',
    'PLACEHOLDER:MatKhau@123',
    'Nguyễn Văn Quản Trị',
    '0901000001',
    '1990-01-15', 1, 1, true, now(), now()
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'an.nguyen@raceday.vn',
    '22222222-2222-2222-2222-222222222222',
    'PLACEHOLDER:MatKhau@123',
    'Nguyễn Văn An',
    '0902000002',
    '1998-05-20', 1, 1, true, now(), now()
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'binh.tran@raceday.vn',
    '22222222-2222-2222-2222-222222222222',
    'PLACEHOLDER:MatKhau@123',
    'Trần Thị Bình',
    '0903000003',
    '1999-08-12', 2, 1, true, now(), now()
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'cuong.le@raceday.vn',
    '33333333-3333-3333-3333-333333333333',
    'PLACEHOLDER:MatKhau@123',
    'Lê Hoàng Cường',
    '0904000004',
    '1995-03-08', 1, 1, true, now(), now()
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'dung.pham@raceday.vn',
    '22222222-2222-2222-2222-222222222222',
    'PLACEHOLDER:MatKhau@123',
    'Phạm Minh Dũng',
    '0905000005',
    '2000-11-25', 1, 2, false, now(), now()
)
ON CONFLICT (email) DO NOTHING;

INSERT INTO iam.profiles (
    user_id, description, contact_email, contact_phone,
    verification_status, verified_at, verified_by
) VALUES
(
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'Tài khoản quản trị RaceDay',
    'admin@raceday.vn', '0901000001',
    2, now(), 'system'
),
(
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    'Vận động viên phong trào TP.HCM',
    'an.nguyen@raceday.vn', '0902000002',
    2, now(), 'Nguyễn Văn Quản Trị'
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'Runner bán marathon Hà Nội',
    'binh.tran@raceday.vn', '0903000003',
    2, now(), 'Nguyễn Văn Quản Trị'
),
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'Ban tổ chức giải chạy Đà Nẵng',
    'cuong.le@raceday.vn', '0904000004',
    2, now(), 'Nguyễn Văn Quản Trị'
),
(
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'Hồ sơ chờ xác minh email',
    'dung.pham@raceday.vn', '0905000005',
    0, NULL, NULL
)
ON CONFLICT (user_id) DO NOTHING;

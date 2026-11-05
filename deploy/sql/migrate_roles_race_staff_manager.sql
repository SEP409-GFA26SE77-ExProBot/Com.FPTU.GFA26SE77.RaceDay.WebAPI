-- =============================================================================
-- Migrate IAM + EventStaff roles: Coordinator/Volunteer → Race-staff; add Manager
-- Idempotent. Run on existing DBs (fresh init already has new role names).
-- =============================================================================

BEGIN;

-- 1) Rename Coordinator → Race-staff (keep GUID 4444…)
UPDATE iam.roles
SET role_name = 'Race-staff',
    description = 'Nhân sự hiện trường (gộp Coordinator + Volunteer)'
WHERE role_id = '44444444-4444-4444-4444-444444444444'
   OR role_name = 'Coordinator';

-- 2) Ensure Manager role
INSERT INTO iam.roles (role_id, role_name, description) VALUES
('77777777-7777-7777-7777-777777777777', 'Manager', 'Duyệt giải + cấu hình gói / promo')
ON CONFLICT (role_id) DO UPDATE
SET role_name = EXCLUDED.role_name,
    description = EXCLUDED.description;

-- 3) Move Volunteer users → Race-staff, then drop Volunteer role
UPDATE iam.users
SET role_id = '44444444-4444-4444-4444-444444444444',
    updated_at = NOW()
WHERE role_id = '66666666-6666-6666-6666-666666666666'
   OR role_id IN (SELECT role_id FROM iam.roles WHERE role_name = 'Volunteer');

DELETE FROM iam.roles
WHERE role_id = '66666666-6666-6666-6666-666666666666'
   OR role_name = 'Volunteer';

-- 4) Event staff enum: Volunteer (2) → RaceStaff (0). Photographer (1) unchanged.
-- Role column is string enum name (EF HasConversion<string>)
UPDATE event.event_staff_members
SET "Role" = 'RaceStaff'
WHERE "Role" IN ('Volunteer', 'Coordinator');

-- 5) Optional demo Manager (password MatKhau@123 — same hash as other seeds)
INSERT INTO iam.users (
    user_id, email, username, role_id, password_hash, full_name, phone,
    date_of_birth, gender, status, email_verified, created_at, updated_at
) VALUES (
    'f7777777-7777-4777-8777-777777777777',
    'manager@raceday.vn', 'manager',
    '77777777-7777-7777-7777-777777777777',
    'AQAAAAIAAYagAAAAEL0GU8mB2QMfid3Im8FHe01P0dcyVx1CS8x/iYFvVG+sJSNBQdSG/jVe1lH2rTZSEw==',
    'Trần Quản Lý Gói', '0907000007', '1992-04-12', 2, 1, true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO iam.profiles (
    user_id, description, contact_email, contact_phone,
    verification_status, verified_at, verified_by
) VALUES (
    'f7777777-7777-4777-8777-777777777777',
    'Manager seed — duyệt giải / catalog gói',
    'manager@raceday.vn', '0907000007',
    2, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'system'
)
ON CONFLICT (user_id) DO NOTHING;

COMMIT;

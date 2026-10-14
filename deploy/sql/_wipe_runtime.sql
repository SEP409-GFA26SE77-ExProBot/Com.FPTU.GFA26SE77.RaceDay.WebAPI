BEGIN;

TRUNCATE TABLE
  payment.payment_transactions,
  payment.payments,
  payment.outbox_messages
RESTART IDENTITY CASCADE;

TRUNCATE TABLE
  finance.refund_requests,
  finance.payout_requests,
  finance.event_ledgers,
  finance.organizer_bank_accounts
RESTART IDENTITY CASCADE;

TRUNCATE TABLE
  map.race_courses,
  map.outbox_messages
RESTART IDENTITY CASCADE;

TRUNCATE TABLE
  event.event_staff_members,
  event.event_registrations,
  event.package_purchases,
  event.event_checkpoints,
  event.event_documents,
  event.event_distances,
  event.race_events
RESTART IDENTITY CASCADE;

TRUNCATE TABLE iam.email_otps, iam.outbox_messages RESTART IDENTITY CASCADE;

DELETE FROM iam.users
WHERE email LIKE '%@staff.raceday.local'
   OR email IN (
     'coord1@raceday.vn','coord2@raceday.vn','coord3@raceday.vn','coord4@raceday.vn',
     'photo1@raceday.vn','photo2@raceday.vn','photo3@raceday.vn','photo4@raceday.vn',
     'vol1@raceday.vn','vol2@raceday.vn','vol3@raceday.vn','vol4@raceday.vn'
   );

UPDATE event.promo_codes SET "UsedCount" = 0;

COMMIT;

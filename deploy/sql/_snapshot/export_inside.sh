#!/bin/sh
set -e
EID='b8fdd6c9-c6a6-44d9-9958-a98c8094fba8'
PID='fbef5321-4a1a-46ba-8bdc-0790e7323ae2'
AID='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
OUT=/tmp/rd_snap
mkdir -p "$OUT"

psql -U postgres -d postgres -t -A -o "$OUT/event.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM event.race_events WHERE \"Id\"='$EID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/distances.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.event_distances WHERE \"EventId\"='$EID' ORDER BY \"DistanceKm\") t;"

psql -U postgres -d postgres -t -A -o "$OUT/documents.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.event_documents WHERE \"EventId\"='$EID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/supplies.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.event_supplies WHERE \"EventId\"='$EID' ORDER BY \"SortOrder\") t;"

psql -U postgres -d postgres -t -A -o "$OUT/map_courses.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM map.race_courses WHERE \"EventId\"='$EID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/pkg_duration.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.package_duration_options ORDER BY \"SortOrder\") t;"

psql -U postgres -d postgres -t -A -o "$OUT/pkg_capacity.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.package_capacity_options ORDER BY \"SortOrder\") t;"

psql -U postgres -d postgres -t -A -o "$OUT/promo.json" -c \
  "SELECT coalesce(json_agg(row_to_json(t)),'[]'::json) FROM (SELECT * FROM event.promo_codes ORDER BY \"Code\") t;"

psql -U postgres -d postgres -t -A -o "$OUT/purchase.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM event.package_purchases WHERE \"Id\"='$PID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/checkpoints.json" -c \
  "SELECT coalesce(json_agg(row_to_json(c)),'[]'::json) FROM event.event_checkpoints c JOIN event.event_distances d ON d.\"Id\"=c.\"DistanceId\" WHERE d.\"EventId\"='$EID';"

psql -U postgres -d postgres -t -A -o "$OUT/admin.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM iam.users WHERE user_id='$AID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/admin_profile.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM iam.profiles WHERE user_id='$AID') t;"

psql -U postgres -d postgres -t -A -o "$OUT/organizer.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM iam.users WHERE user_id='e1d7acca-f720-4549-8a06-3addbe58298e') t;"

psql -U postgres -d postgres -t -A -o "$OUT/organizer_profile.json" -c \
  "SELECT row_to_json(t) FROM (SELECT * FROM iam.profiles WHERE user_id='e1d7acca-f720-4549-8a06-3addbe58298e') t;"

# schema column lists for seed generator
psql -U postgres -d postgres -c "\d iam.users" > "$OUT/iam_users_d.txt"
psql -U postgres -d postgres -c "\d iam.profiles" > "$OUT/iam_profiles_d.txt"
psql -U postgres -d postgres -c "\d payment.payments" > "$OUT/payment_d.txt"
psql -U postgres -d postgres -c "\d finance.event_ledgers" > "$OUT/ledger_d.txt"

ls -la "$OUT"

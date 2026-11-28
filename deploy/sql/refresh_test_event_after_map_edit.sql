-- Run after editing a race's map in UserWeb ("Thiết lập bản đồ"). It never touches the map itself.
--   1. Lists the checkpoints the map now has (the station ids staff can be assigned to).
--   2. Lists staff assignments that point to a checkpoint the map no longer has
--      (those staff get "unconfigured" and never an out-of-zone alert).
--   3. Moves the race start to now + start_in_minutes (Vietnam wall-clock time), so
--      staff QR check-in and out-of-zone alerts (open 1 hour before start) work right away.
--
-- psql -v event_id=<race id> [-v start_in_minutes=30] [-v retime=0] -f refresh_test_event_after_map_edit.sql
-- Default event: the check-in test race from seed_staff_checkin_outzone.sql.
\set ON_ERROR_STOP on
\if :{?event_id}
\else
  \set event_id '5aff0000-0000-4000-8000-000000000200'
\endif
\if :{?start_in_minutes}
\else
  \set start_in_minutes 30
\endif
\if :{?retime}
\else
  \set retime 1
\endif

\echo '== 1. Checkpoints in the saved map (station id = map:<distance id>:<checkpoint id>)'
SELECT d."Name" AS distance,
       'map:' || c."DistanceId" || ':' || (cp->>'id') AS station_id,
       cp->>'name' AS checkpoint, cp->>'type' AS type, (cp->>'lat')::float AS lat, (cp->>'lng')::float AS lng
FROM map.race_courses c
JOIN event.event_distances d ON d."Id" = c."DistanceId"
CROSS JOIN LATERAL jsonb_array_elements(c."CourseJson"->'checkpoints') WITH ORDINALITY AS points(cp, ord)
WHERE c."EventId" = :'event_id'
ORDER BY d."Name", ord;

\echo '== 2. Active staff stations that are not on the map any more (empty = all good)'
WITH stations AS (
  SELECT s."Id", s."FullName", s."Role", st->>'stationId' AS station_id
  FROM event.event_staff_members s
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_array_length(s."StationsJson") > 0 THEN s."StationsJson"
         WHEN s."StationId" IS NOT NULL THEN jsonb_build_array(jsonb_build_object('stationId', s."StationId"))
         ELSE '[]'::jsonb END) AS st
  WHERE s."EventId" = :'event_id' AND s."Status" = 'Active'
), on_map AS (
  SELECT 'map:' || c."DistanceId" || ':' || (cp->>'id') AS station_id
  FROM map.race_courses c, jsonb_array_elements(c."CourseJson"->'checkpoints') AS cp
  WHERE c."EventId" = :'event_id'
)
SELECT "FullName", "Role", station_id AS missing_station
FROM stations
WHERE station_id LIKE 'map:%' AND station_id NOT IN (SELECT station_id FROM on_map);

\if :retime
\echo '== 3. Race start moved to now +' :start_in_minutes 'minutes (Vietnam time)'
BEGIN;
UPDATE event.race_events
SET "EventDate" = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + make_interval(mins => :start_in_minutes))::date,
    "EndDate" = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + make_interval(mins => :start_in_minutes))::date,
    "StartTime" = date_trunc('minute', now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + make_interval(mins => :start_in_minutes))::time,
    "UpdatedAtUtc" = now()
WHERE "Id" = :'event_id';
UPDATE event.event_distances
SET "StartAt" = date_trunc('minute', now() AT TIME ZONE 'Asia/Ho_Chi_Minh' + make_interval(mins => :start_in_minutes))::time
WHERE "EventId" = :'event_id';
COMMIT;
SELECT "Name", "Status", "EventDate", "StartTime" AS start_vn FROM event.race_events WHERE "Id" = :'event_id';
\endif

-- To move a staff member to a checkpoint from list 1 (copy its station_id):
-- UPDATE event.event_staff_members
--   SET "StationId" = '<station_id>', "StationLabel" = '<checkpoint>',
--       "StationsJson" = jsonb_build_array(jsonb_build_object('stationId', '<station_id>', 'stationLabel', '<checkpoint>')),
--       "UpdatedAtUtc" = now()
--   WHERE "EventId" = '<event id>' AND "Email" = '<staff email>';
-- A mobile staff member uses 'duty:float' (100 m along the whole course) instead of a checkpoint.

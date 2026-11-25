-- Local test fixture: copy the saved map-course checkpoints of "RaceDay Trail Draft 2027 / 5KM Draft"
-- into event.event_checkpoints so checkpoint scans (and the Race-staff "Timeline trực tiếp") work.
-- Additive and repeatable; existing checkpoint rows are kept.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO event.event_checkpoints ("Id", "DistanceId", "ExternalId", "Name", "Km", "Lat", "Lng", "Type", "SortOrder")
SELECT ('c0ffeeee-0003-4000-8000-' || lpad(cp.ord::text, 12, '0'))::uuid,
  'c0ffeeee-0001-4000-8000-0000000000d1',
  cp.j->>'id', cp.j->>'name', (cp.j->>'km')::numeric, (cp.j->>'lat')::double precision, (cp.j->>'lng')::double precision,
  CASE lower(cp.j->>'type') WHEN 'start' THEN 'Start' WHEN 'finish' THEN 'Finish' WHEN 'aid' THEN 'Aid' ELSE 'Water' END,
  cp.ord
FROM map.race_courses c
CROSS JOIN LATERAL jsonb_array_elements(c."CourseJson"->'checkpoints') WITH ORDINALITY AS cp(j, ord)
WHERE c."DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1'
  AND NOT EXISTS (SELECT 1 FROM event.event_checkpoints e
                  WHERE e."DistanceId" = c."DistanceId" AND e."ExternalId" = cp.j->>'id')
ON CONFLICT ("Id") DO NOTHING;
COMMIT;
SELECT "Id", "ExternalId", "Name", "Km", "Type", "SortOrder" FROM event.event_checkpoints
 WHERE "DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1' ORDER BY "SortOrder";

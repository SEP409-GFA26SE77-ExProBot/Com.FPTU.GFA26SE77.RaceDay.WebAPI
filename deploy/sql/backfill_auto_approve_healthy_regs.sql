-- Backfill: PendingApproval không bệnh lý đặc biệt → Confirmed + cấp BIB tiếp theo theo dải cự ly.
-- «Không bệnh lý» = MedicalCodesJson rỗng / [] / chỉ chứa "none" (case-insensitive).
BEGIN;

WITH eligible AS (
  SELECT er."Id", er."EventId", er."DistanceId"
  FROM event.event_registrations er
  WHERE er."Status" = 'PendingApproval'
    AND (
      er."MedicalCodesJson" IS NULL
      OR btrim(er."MedicalCodesJson"::text) IN ('', '[]', 'null')
      OR (
        jsonb_typeof(er."MedicalCodesJson"::jsonb) = 'array'
        AND NOT EXISTS (
          SELECT 1
          FROM jsonb_array_elements_text(er."MedicalCodesJson"::jsonb) AS c(code)
          WHERE lower(btrim(c.code)) <> '' AND lower(btrim(c.code)) <> 'none'
        )
      )
    )
),
numbered AS (
  SELECT
    e."Id",
    e."EventId",
    e."DistanceId",
    d."BibPrefix",
    d."BibRangeStart",
    d."BibRangeEnd",
    row_number() OVER (PARTITION BY e."EventId", e."DistanceId" ORDER BY e."Id") AS rn
  FROM eligible e
  JOIN event.event_distances d ON d."Id" = e."DistanceId"
  WHERE d."BibRangeStart" IS NOT NULL AND d."BibRangeEnd" IS NOT NULL
),
used AS (
  SELECT
    r."EventId",
    r."DistanceId",
    CASE
      WHEN coalesce(d."BibPrefix", '') = '' THEN NULLIF(regexp_replace(r."BibNumber", '[^0-9]', '', 'g'), '')::int
      WHEN r."BibNumber" ILIKE d."BibPrefix" || '%'
        THEN NULLIF(regexp_replace(substr(r."BibNumber", length(d."BibPrefix") + 1), '[^0-9]', '', 'g'), '')::int
      ELSE NULL
    END AS seq
  FROM event.event_registrations r
  JOIN event.event_distances d ON d."Id" = r."DistanceId"
  WHERE r."BibNumber" IS NOT NULL
    AND r."Status" IN ('Confirmed', 'Finished')
),
max_used AS (
  SELECT "EventId", "DistanceId", coalesce(max(seq), 0) AS max_seq
  FROM used
  GROUP BY 1, 2
),
assigned AS (
  SELECT
    n."Id",
    CASE
      WHEN coalesce(n."BibPrefix", '') = '' THEN lpad((coalesce(m.max_seq, n."BibRangeStart" - 1) + n.rn)::text, 4, '0')
      ELSE n."BibPrefix" || lpad((coalesce(m.max_seq, n."BibRangeStart" - 1) + n.rn)::text, 4, '0')
    END AS bib,
    (coalesce(m.max_seq, n."BibRangeStart" - 1) + n.rn) AS seq,
    n."BibRangeStart",
    n."BibRangeEnd"
  FROM numbered n
  LEFT JOIN max_used m ON m."EventId" = n."EventId" AND m."DistanceId" = n."DistanceId"
)
UPDATE event.event_registrations er
SET
  "Status" = 'Confirmed',
  "BibNumber" = a.bib,
  "RejectionReason" = NULL,
  "UpdatedAtUtc" = now()
FROM assigned a
WHERE er."Id" = a."Id"
  AND a.seq BETWEEN a."BibRangeStart" AND a."BibRangeEnd";

COMMIT;

SELECT "Status", count(*) FROM event.event_registrations GROUP BY 1 ORDER BY 1;

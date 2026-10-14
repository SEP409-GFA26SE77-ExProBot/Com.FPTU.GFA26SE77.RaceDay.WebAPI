UPDATE event.promo_codes
SET "Code" = 'RACEDAY99',
    "PercentOff" = 0.9900,
    "UpdatedAtUtc" = NOW()
WHERE "Code" = 'RACEDAY100';

-- Nếu đã có RACEDAY99 từ lần seed trước thì bỏ bản 100 (không trùng).
DELETE FROM event.promo_codes a
USING event.promo_codes b
WHERE a."Code" = 'RACEDAY100'
  AND b."Code" = 'RACEDAY99'
  AND a."Id" <> b."Id";

SELECT "Code", "PercentOff", "Status"
FROM event.promo_codes
ORDER BY "PercentOff";

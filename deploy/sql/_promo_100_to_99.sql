UPDATE event.promo_codes
SET "PercentOff" = 0.9900,
    "UpdatedAtUtc" = NOW()
WHERE "Code" = 'RACEDAY100';

SELECT "Code", "PercentOff", "Status", "UpdatedAtUtc"
FROM event.promo_codes
ORDER BY "PercentOff";

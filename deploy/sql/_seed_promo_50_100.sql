INSERT INTO event.promo_codes ("Id", "Code", "PercentOff", "Status", "ExpiresAtUtc", "MaxRedemptions", "UsedCount", "CreatedAtUtc", "UpdatedAtUtc")
SELECT gen_random_uuid(), v.code, v.pct, 'Active', NOW() + INTERVAL '2 years', NULL, 0, NOW(), NOW()
FROM (VALUES
  ('RACEDAY50', 0.5000::numeric),
  ('RACEDAY100', 1.0000::numeric)
) AS v(code, pct)
WHERE NOT EXISTS (SELECT 1 FROM event.promo_codes p WHERE p."Code" = v.code);

SELECT "Code", "PercentOff", "Status", "ExpiresAtUtc", "UsedCount"
FROM event.promo_codes
ORDER BY "PercentOff";

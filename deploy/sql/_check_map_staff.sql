SELECT COUNT(*) AS events FROM event.race_events;
SELECT id::text, name, status::text, "CreatedAtUtc" FROM event.race_events ORDER BY "CreatedAtUtc" DESC;

SELECT COUNT(*) AS courses FROM map.race_courses;
SELECT "Id"::text, "EventId"::text, "DistanceId"::text, "Title", "DistanceKm", "UpdatedAtUtc"
FROM map.race_courses ORDER BY "UpdatedAtUtc" DESC;

SELECT COUNT(*) AS staff FROM event.event_staff_members;
SELECT "EventId"::text, "Role", "FullName", "Email", COALESCE("StationId",'') AS station_id,
       COALESCE("StationLabel",'') AS station_label, "CreatedAtUtc"
FROM event.event_staff_members ORDER BY "CreatedAtUtc" DESC;

SELECT COUNT(*) AS distances FROM event.event_distances;
SELECT "Id"::text, "EventId"::text, "Name", "DistanceKm" FROM event.event_distances;

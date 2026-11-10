-- Publish gate for public leaderboard (EventDistance.ResultsPublished)
ALTER TABLE event.event_distances
  ADD COLUMN IF NOT EXISTS "ResultsPublished" boolean NOT NULL DEFAULT false;

ALTER TABLE event.event_distances
  ADD COLUMN IF NOT EXISTS "ResultsPublishedAtUtc" timestamp with time zone NULL;

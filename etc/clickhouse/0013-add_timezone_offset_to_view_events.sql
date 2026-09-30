ALTER TABLE view_events
ADD COLUMN IF NOT EXISTS timezone_offset Nullable(Int16);
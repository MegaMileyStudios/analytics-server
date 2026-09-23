ALTER TABLE events
ADD COLUMN IF NOT EXISTS persistent_user_id Nullable(String);

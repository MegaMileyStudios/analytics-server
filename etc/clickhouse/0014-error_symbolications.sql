CREATE TABLE IF NOT EXISTS error_symbolications
(
    `app_id` String,
    `error_id` String,
    `symbolicated_at` DateTime64(3),
    `status` LowCardinality(String),
    `resolved_frames` UInt16,
    `total_frames` UInt16,
    `missing_keys` Array(String),
    `result` String,
    `ttl` DateTime
)
ENGINE = ReplacingMergeTree(symbolicated_at)
ORDER BY (app_id, error_id)
TTL ttl;

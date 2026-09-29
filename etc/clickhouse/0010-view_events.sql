CREATE TABLE IF NOT EXISTS view_events
(
    `app_id` String,
    `timestamp` DateTime64(3),
    `session_id` String,
    `persistent_user_id` Nullable(String),
    `view_type` LowCardinality(String),
    `view_name` String,
    `os_name` LowCardinality(String),
    `os_version` String,
    `device_model` String,
    `locale` LowCardinality(String),
    `app_version` String,
    `app_build_number` String,
    `engine_name` LowCardinality(String),
    `engine_version` String,
    `sdk_version` String,
    `country_code` LowCardinality(String),
    `region_name` LowCardinality(String),
    `ttl` DateTime,
    INDEX idx_session_id session_id TYPE bloom_filter GRANULARITY 4
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (app_id, timestamp)
TTL ttl;

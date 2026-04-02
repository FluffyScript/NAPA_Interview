-- pg_stat_statements must be listed in shared_preload_libraries before this runs.
-- Both docker-compose.yml and docker-compose.db-only.yml pass:
--   command: postgres -c shared_preload_libraries=pg_stat_statements
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

-- Sample schema for local development and testing
CREATE TABLE IF NOT EXISTS orders (
    id          BIGSERIAL PRIMARY KEY,
    customer_id BIGINT NOT NULL,
    status      TEXT   NOT NULL DEFAULT 'pending',
    total       NUMERIC(12, 2),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_orders_customer ON orders(customer_id);
CREATE INDEX IF NOT EXISTS idx_orders_status   ON orders(status);

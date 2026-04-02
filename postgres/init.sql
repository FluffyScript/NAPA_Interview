-- Enable pg_stat_statements extension for slow-query tracking
-- This must run as a superuser (the default postgres init user qualifies).
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

-- Example schema for local development / testing
CREATE TABLE IF NOT EXISTS orders (
    id          BIGSERIAL PRIMARY KEY,
    customer_id BIGINT NOT NULL,
    status      TEXT   NOT NULL DEFAULT 'pending',
    total       NUMERIC(12, 2),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_orders_customer ON orders(customer_id);
CREATE INDEX IF NOT EXISTS idx_orders_status   ON orders(status);

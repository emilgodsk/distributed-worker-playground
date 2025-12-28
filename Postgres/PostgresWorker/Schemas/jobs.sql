CREATE TABLE jobs
(
    id           UUID PRIMARY KEY,
    payload      JSONB       NOT NULL,

    status       TEXT        NOT NULL, -- pending | leased | completed | failed
    leased_by    TEXT,
    lease_until  TIMESTAMPTZ,

    heartbeat_at TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_jobs_pending ON jobs (status);
CREATE INDEX idx_jobs_lease_until ON jobs (lease_until);

INSERT INTO jobs (
    id,
    payload,
    status,
    leased_by,
    leased_at,
    lease_until,
    heartbeat_at,
    created_at,
    updated_at
)
SELECT
    gen_random_uuid(),
    jsonb_build_object(
            'type', 'sync',
            'entityId', gen_random_uuid(),
            'priority', (random() * 10)::int,
            'data', md5(random()::text)
    ),
    'pending',
    NULL,
    NULL,
    NULL,
    NULL,
    now() - (random() * interval '1 hour'),
    now()
FROM generate_series(1, 100);

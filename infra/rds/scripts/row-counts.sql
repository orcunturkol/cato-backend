-- Prints "<table>: <row count>" for every table in the public schema.
-- Schema-agnostic on purpose: run identically against the source (local
-- Postgres container) and target (RDS) databases, then diff the two outputs.
DO $$
DECLARE
    r RECORD;
    cnt BIGINT;
BEGIN
    FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename LOOP
        EXECUTE format('SELECT count(*) FROM %I', r.tablename) INTO cnt;
        RAISE NOTICE '%: %', r.tablename, cnt;
    END LOOP;
END $$;

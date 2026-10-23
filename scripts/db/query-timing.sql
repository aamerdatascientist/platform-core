-- Run by query-timing.ps1. Read-only: reports how long Postgres itself spends on the queries
-- the app makes, as opposed to the network time between the API and the database.
SET default_transaction_read_only = on;
\pset footer off

\echo
\echo '== Postgres statement statistics (pg_stat_statements), if the extension is installed'
SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_stat_statements') AS has_pg_stat_statements \gset
\if :has_pg_stat_statements
SELECT SUM(calls) AS total_calls,
       ROUND((SUM(total_exec_time) / NULLIF(SUM(calls), 0))::numeric, 3) AS avg_ms_per_query
FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database());
SELECT calls, ROUND(mean_exec_time::numeric, 3) AS avg_ms, ROUND(max_exec_time::numeric, 1) AS max_ms,
       LEFT(regexp_replace(query, '\s+', ' ', 'g'), 110) AS query
FROM pg_stat_statements
WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
ORDER BY total_exec_time DESC LIMIT 15;
\else
\echo 'Not installed - measuring the sign-in and form-list queries directly instead.'
\endif

\echo
\echo '== Server-side execution time of the queries behind sign-in and the forms list'
\echo '   (Execution Time = time inside Postgres; no network included)'
EXPLAIN (ANALYZE, BUFFERS OFF, COSTS OFF, SUMMARY ON)
SELECT u.*, t."Name" FROM "Users" AS u
LEFT JOIN LATERAL (
    SELECT r."Name", r."Id" FROM "Roles" AS r
    WHERE NOT r."IsDeleted" AND EXISTS (SELECT 1 FROM "UserRoles" AS ur WHERE ur."UserId" = u."Id" AND ur."RoleId" = r."Id")
) AS t ON TRUE
WHERE NOT u."IsDeleted" AND u."Email" = (SELECT "Email" FROM "Users" ORDER BY "CreatedAtUtc" LIMIT 1);

EXPLAIN (ANALYZE, BUFFERS OFF, COSTS OFF, SUMMARY ON)
SELECT f.* FROM "FormDefinitions" AS f
LEFT JOIN "FormDefinitionRoles" AS fr ON fr."FormDefinitionId" = f."Id"
LEFT JOIN "FormDefinitionUsers" AS fu ON fu."FormDefinitionId" = f."Id"
WHERE NOT f."IsDeleted" ORDER BY f."ModuleName", f."Name";

\echo
\echo '== Round trip from THIS computer to the database (network + Postgres), 5 tries'
\timing on
SELECT 1 AS ping;
SELECT 1 AS ping;
SELECT 1 AS ping;
SELECT 1 AS ping;
SELECT 1 AS ping;
\timing off

\echo
\echo '== Tables read by full scan most often (large seq_scan counts on big tables = missing index)'
SELECT relname AS table_name, n_live_tup AS rows, seq_scan, idx_scan
FROM pg_stat_user_tables ORDER BY seq_scan DESC LIMIT 10;

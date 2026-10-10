-- Removes the dummy data loaded by seed-dummy-data.ps1: the five projects whose code starts
-- with DUMMY- and every row in the other six forms that points at one of them. Nothing else
-- is touched - the rows that existed before the seed (DEMO-01, the PRJ-... projects and
-- their rows) do not match and stay exactly as they are.
--
-- Run against the Railway Postgres database (psql, or Railway's own query tab).
-- It runs inside one transaction: check the counts it prints, and if any looks wrong,
-- replace COMMIT with ROLLBACK before running.
--
-- Afterwards delete "Claude outputs\dummy-seed-ledger.tsv" on the machine that ran the
-- seed, or the loader will refuse to run again (it will say the ledger is out of date).

BEGIN;

CREATE TEMP TABLE dummy_projects ON COMMIT DROP AS
SELECT "Id" FROM "Data_Projects" WHERE "project_code" LIKE 'DUMMY-%';

SELECT 'projects to remove' AS what, COUNT(*) FROM dummy_projects;          -- expect 5

WITH d AS (DELETE FROM "Data_DailyProgressReport" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'daily progress reports removed' AS what, COUNT(*) FROM d;           -- expect 375

WITH d AS (DELETE FROM "Data_StockOutflow" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'stock outflow rows removed' AS what, COUNT(*) FROM d;               -- expect 149

WITH d AS (DELETE FROM "Data_StockInflow" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'stock inflow rows removed' AS what, COUNT(*) FROM d;                -- expect 97

WITH d AS (DELETE FROM "Data_ProjectZones" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'zones removed' AS what, COUNT(*) FROM d;                            -- expect 26

WITH d AS (DELETE FROM "Data_ProjectFootings" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'footings removed' AS what, COUNT(*) FROM d;                         -- expect 40

WITH d AS (DELETE FROM "Data_ProjectFloors" WHERE "project" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'floors removed' AS what, COUNT(*) FROM d;                           -- expect 15

WITH d AS (DELETE FROM "Data_Projects" WHERE "Id" IN (SELECT "Id" FROM dummy_projects) RETURNING 1)
SELECT 'projects removed' AS what, COUNT(*) FROM d;                         -- expect 5

COMMIT;

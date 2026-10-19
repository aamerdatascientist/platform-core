-- Clears every submitted record from the seven live forms so the system starts empty for
-- real use. Agreed with the client on 2026-10-19 after review of the dummy data.
--
-- REMOVED: every row in the seven forms below (dummy projects, the older test projects such
-- as DEMO-01 and PRJ-..., their zones/footings/floors, all daily reports, all stock in/out),
-- plus anything attached to those rows: workflow progress and file-attachment records.
--
-- KEPT: the forms themselves (fields, options, conditions, access settings), users, roles,
-- workflow definitions, and the Report_* views. Forms deleted earlier are not touched.
--
-- THIS CANNOT BE UNDONE. Take a backup first (see the run instructions).
--
-- SAFE BY DEFAULT: the script ends in ROLLBACK, so running it as-is only shows the counts
-- and changes nothing. To really delete, change the last line from ROLLBACK; to COMMIT;
-- and run it again.

BEGIN;

-- Fail loudly, before deleting anything, if a form or table is missing or renamed.
DO $$
DECLARE missing text;
BEGIN
    SELECT string_agg(code, ', ') INTO missing
    FROM (VALUES ('projects'), ('project-zones'), ('project-footings'), ('project-floors'),
                 ('daily-progress-report'), ('stock-inflow'), ('stock-outflow')) AS f(code)
    WHERE NOT EXISTS (SELECT 1 FROM "FormDefinitions" d
                      WHERE d."Code" = f.code AND d."IsDeleted" = false AND d."TableName" IS NOT NULL);
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Live form(s) not found: %. Nothing was deleted.', missing;
    END IF;
END $$;

CREATE TEMP TABLE live_forms ON COMMIT DROP AS
SELECT "Id", "Code", "TableName" FROM "FormDefinitions"
WHERE "IsDeleted" = false
  AND "Code" IN ('projects', 'project-zones', 'project-footings', 'project-floors',
                 'daily-progress-report', 'stock-inflow', 'stock-outflow');

-- Attachment files live in Azure Blob Storage, not here. Deleting their records leaves the
-- files behind; this lists them so they can be removed from the 'attachments' container.
SELECT 'attachment file to remove from blob storage' AS what, "BlobName"
FROM "FileMetadataEntries" WHERE "FormDefinitionId" IN (SELECT "Id" FROM live_forms);

WITH d AS (DELETE FROM "FileMetadataEntries"
           WHERE "FormDefinitionId" IN (SELECT "Id" FROM live_forms) RETURNING 1)
SELECT 'attachment records removed' AS what, COUNT(*) FROM d;

WITH d AS (DELETE FROM "WorkflowInstanceHistoryEntries"
           WHERE "WorkflowInstanceId" IN (SELECT "Id" FROM "WorkflowInstances"
                                          WHERE "FormDefinitionId" IN (SELECT "Id" FROM live_forms))
           RETURNING 1)
SELECT 'workflow history entries removed' AS what, COUNT(*) FROM d;

WITH d AS (DELETE FROM "WorkflowInstances"
           WHERE "FormDefinitionId" IN (SELECT "Id" FROM live_forms) RETURNING 1)
SELECT 'workflow instances removed' AS what, COUNT(*) FROM d;

-- Child forms before "projects", since they point at it. Soft-deleted rows go too.
WITH d AS (DELETE FROM "Data_DailyProgressReport" RETURNING 1) SELECT 'daily-progress-report rows removed' AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_StockOutflow"        RETURNING 1) SELECT 'stock-outflow rows removed'         AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_StockInflow"         RETURNING 1) SELECT 'stock-inflow rows removed'          AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_ProjectZones"        RETURNING 1) SELECT 'project-zones rows removed'         AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_ProjectFootings"     RETURNING 1) SELECT 'project-footings rows removed'      AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_ProjectFloors"       RETURNING 1) SELECT 'project-floors rows removed'        AS what, COUNT(*) FROM d;
WITH d AS (DELETE FROM "Data_Projects"            RETURNING 1) SELECT 'projects rows removed'              AS what, COUNT(*) FROM d;

-- Every table should now be empty.
SELECT 'rows left (all should be 0)' AS what,
       (SELECT COUNT(*) FROM "Data_Projects")            AS projects,
       (SELECT COUNT(*) FROM "Data_ProjectZones")        AS zones,
       (SELECT COUNT(*) FROM "Data_ProjectFootings")     AS footings,
       (SELECT COUNT(*) FROM "Data_ProjectFloors")       AS floors,
       (SELECT COUNT(*) FROM "Data_DailyProgressReport") AS daily_reports,
       (SELECT COUNT(*) FROM "Data_StockInflow")         AS stock_in,
       (SELECT COUNT(*) FROM "Data_StockOutflow")        AS stock_out;

ROLLBACK;   -- change to COMMIT; to really delete

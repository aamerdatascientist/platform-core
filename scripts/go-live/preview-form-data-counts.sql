-- READ-ONLY. One query, one result row: how many records each live form holds right now,
-- plus the workflow and attachment records tied to them. Run it before
-- clear-all-form-data.sql to see what will be removed, and after it to confirm all zeros.
-- Changes nothing.
SELECT
    (SELECT COUNT(*) FROM "Data_Projects")            AS projects,
    (SELECT COUNT(*) FROM "Data_ProjectZones")        AS zones,
    (SELECT COUNT(*) FROM "Data_ProjectFootings")     AS footings,
    (SELECT COUNT(*) FROM "Data_ProjectFloors")       AS floors,
    (SELECT COUNT(*) FROM "Data_DailyProgressReport") AS daily_reports,
    (SELECT COUNT(*) FROM "Data_StockInflow")         AS stock_in,
    (SELECT COUNT(*) FROM "Data_StockOutflow")        AS stock_out,
    (SELECT COUNT(*) FROM "WorkflowInstances" w
       JOIN "FormDefinitions" f ON f."Id" = w."FormDefinitionId"
      WHERE f."IsDeleted" = false
        AND f."Code" IN ('projects','project-zones','project-footings','project-floors',
                         'daily-progress-report','stock-inflow','stock-outflow'))  AS workflows,
    (SELECT COUNT(*) FROM "FileMetadataEntries" m
       JOIN "FormDefinitions" f ON f."Id" = m."FormDefinitionId"
      WHERE f."IsDeleted" = false
        AND f."Code" IN ('projects','project-zones','project-footings','project-floors',
                         'daily-progress-report','stock-inflow','stock-outflow'))  AS attachments;

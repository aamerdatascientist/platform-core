-- READ-ONLY. Lists live form columns that are still NOT NULL although their field is no
-- longer unconditionally required (archived, made optional, or made conditional). Every
-- submission that leaves such a field empty fails with a database error. Fields changed
-- this way from now on are fixed automatically; this finds the ones changed before.
--
-- The "fix" column holds the statement that repairs each one. Run those one at a time in
-- Railway's query editor. Making a column nullable changes no data.
SELECT f."Code"      AS form,
       fd."Code"     AS field,
       CASE WHEN NOT fd."IsActive" THEN 'archived'
            WHEN NOT fd."IsRequired" THEN 'optional'
            ELSE 'conditional' END AS why,
       format('ALTER TABLE %I ALTER COLUMN %I DROP NOT NULL;', c.table_name, c.column_name) AS fix
FROM "FieldDefinitions" fd
JOIN "FormVersions" fv    ON fv."Id" = fd."FormVersionId" AND fv."IsDeleted" = false
JOIN "FormDefinitions" f  ON f."Id" = fv."FormDefinitionId" AND f."IsDeleted" = false
JOIN information_schema.columns c
     ON c.table_schema = current_schema() AND c.table_name = f."TableName" AND c.column_name = fd."Code"
WHERE c.is_nullable = 'NO'
  AND fd."IsDeleted" = false
  AND (fd."IsActive" = false OR fd."IsRequired" = false OR fd."VisibleWhenFieldCode" IS NOT NULL)
ORDER BY 1, 2;

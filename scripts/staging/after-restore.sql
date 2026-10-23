-- Run by restore-staging-db.ps1 against the STAGING database after a production dump is
-- restored into it. Drops the sign-in sessions copied from production, then reports what
-- the copy holds.
DELETE FROM "RefreshTokens";

SELECT (SELECT COUNT(*) FROM "Users")                                      AS users,
       (SELECT COUNT(*) FROM "FormDefinitions" WHERE "IsDeleted" = false) AS forms,
       (SELECT COUNT(*) FROM "__EFMigrationsHistory")                      AS migrations;

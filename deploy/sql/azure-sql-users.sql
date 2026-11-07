-- Azure SQL: least-privilege database users for the App Service managed identities (no passwords).
-- Run once per environment, connected to the application database as the Microsoft Entra admin
-- (for example with Azure Data Studio, the Azure portal query editor or `sqlcmd -G`).
--
-- Replace the names in square brackets with the names of the managed identities:
--   app-starter-identity-<env>, app-starter-api-<env>, app-starter-web-<env>  - runtime identities of the App Services
--   id-starter-migrator-<env>                                                 - identity used by the migration job/pipeline
-- Connection strings then use: Authentication=Active Directory Managed Identity; (User Id=<client id> for user-assigned identities)

IF SCHEMA_ID(N'identity') IS NULL EXEC(N'CREATE SCHEMA [identity]');
IF SCHEMA_ID(N'app') IS NULL EXEC(N'CREATE SCHEMA [app]');
IF SCHEMA_ID(N'web') IS NULL EXEC(N'CREATE SCHEMA [web]');
GO

CREATE USER [id-starter-migrator-prod] FROM EXTERNAL PROVIDER;
CREATE USER [app-starter-identity-prod] FROM EXTERNAL PROVIDER WITH DEFAULT_SCHEMA = [identity];
CREATE USER [app-starter-api-prod] FROM EXTERNAL PROVIDER WITH DEFAULT_SCHEMA = [app];
CREATE USER [app-starter-web-prod] FROM EXTERNAL PROVIDER WITH DEFAULT_SCHEMA = [web];
GO

ALTER ROLE db_ddladmin ADD MEMBER [id-starter-migrator-prod];
ALTER ROLE db_datareader ADD MEMBER [id-starter-migrator-prod];
ALTER ROLE db_datawriter ADD MEMBER [id-starter-migrator-prod];

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[identity] TO [app-starter-identity-prod];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[app] TO [app-starter-api-prod];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[web] TO [app-starter-web-prod];
GO

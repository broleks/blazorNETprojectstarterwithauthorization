-- Creates the database, one schema per application and least-privilege SQL logins.
-- Used by the local Docker setup (run by the "db-setup" service in compose.yaml with sqlcmd).
-- For Azure SQL use azure-sql-users.sql instead (Microsoft Entra managed identities, no passwords).
--
-- Who can do what:
--   starter_migrator  - changes the schema (EF Core migrations). Used only by the migration step.
--   starter_identity  - reads/writes the [identity] schema (accounts, OpenIddict, Data Protection keys).
--   starter_api       - reads/writes the [app] schema (business data).
--   starter_web       - reads/writes the [web] schema (server-side sessions, Data Protection keys).
-- The applications cannot change the schema, and none of them can read another application's schema.
--
-- Variables (sqlcmd -v): DatabaseName, MigratorPassword, IdentityPassword, ApiPassword, WebPassword
:on error exit
SET NOCOUNT ON;

IF DB_ID(N'$(DatabaseName)') IS NULL
    EXEC(N'CREATE DATABASE [$(DatabaseName)]');
GO

-- Server logins (passwords come from .env; re-running the script updates them).
IF SUSER_ID(N'starter_migrator') IS NULL CREATE LOGIN [starter_migrator] WITH PASSWORD = N'$(MigratorPassword)', DEFAULT_DATABASE = [$(DatabaseName)];
ELSE ALTER LOGIN [starter_migrator] WITH PASSWORD = N'$(MigratorPassword)';
IF SUSER_ID(N'starter_identity') IS NULL CREATE LOGIN [starter_identity] WITH PASSWORD = N'$(IdentityPassword)', DEFAULT_DATABASE = [$(DatabaseName)];
ELSE ALTER LOGIN [starter_identity] WITH PASSWORD = N'$(IdentityPassword)';
IF SUSER_ID(N'starter_api') IS NULL CREATE LOGIN [starter_api] WITH PASSWORD = N'$(ApiPassword)', DEFAULT_DATABASE = [$(DatabaseName)];
ELSE ALTER LOGIN [starter_api] WITH PASSWORD = N'$(ApiPassword)';
IF SUSER_ID(N'starter_web') IS NULL CREATE LOGIN [starter_web] WITH PASSWORD = N'$(WebPassword)', DEFAULT_DATABASE = [$(DatabaseName)];
ELSE ALTER LOGIN [starter_web] WITH PASSWORD = N'$(WebPassword)';
GO

USE [$(DatabaseName)];
GO

IF SCHEMA_ID(N'identity') IS NULL EXEC(N'CREATE SCHEMA [identity]');
IF SCHEMA_ID(N'app') IS NULL EXEC(N'CREATE SCHEMA [app]');
IF SCHEMA_ID(N'web') IS NULL EXEC(N'CREATE SCHEMA [web]');
GO

IF USER_ID(N'starter_migrator') IS NULL CREATE USER [starter_migrator] FOR LOGIN [starter_migrator];
IF USER_ID(N'starter_identity') IS NULL CREATE USER [starter_identity] FOR LOGIN [starter_identity] WITH DEFAULT_SCHEMA = [identity];
IF USER_ID(N'starter_api') IS NULL CREATE USER [starter_api] FOR LOGIN [starter_api] WITH DEFAULT_SCHEMA = [app];
IF USER_ID(N'starter_web') IS NULL CREATE USER [starter_web] FOR LOGIN [starter_web] WITH DEFAULT_SCHEMA = [web];
GO

-- Migrations: DDL plus data access (EF Core reads and writes the migrations history table).
ALTER ROLE db_ddladmin ADD MEMBER [starter_migrator];
ALTER ROLE db_datareader ADD MEMBER [starter_migrator];
ALTER ROLE db_datawriter ADD MEMBER [starter_migrator];

-- Runtime: data access in the application's own schema only. Grants on a schema also cover tables created later.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[identity] TO [starter_identity];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[app] TO [starter_api];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[web] TO [starter_web];
GO

PRINT 'Database $(DatabaseName) and application users are ready.';
GO

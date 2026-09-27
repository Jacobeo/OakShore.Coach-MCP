IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppIdentityName)')
    EXEC(N'CREATE USER [$(AppIdentityName)] FROM EXTERNAL PROVIDER');
IF IS_ROLEMEMBER(N'db_datareader', N'$(AppIdentityName)') <> 1
    ALTER ROLE db_datareader ADD MEMBER [$(AppIdentityName)];

IF IS_ROLEMEMBER(N'db_datawriter', N'$(AppIdentityName)') <> 1
    ALTER ROLE db_datawriter ADD MEMBER [$(AppIdentityName)];

-- The app applies its idempotent schema migration on startup.
IF IS_ROLEMEMBER(N'db_ddladmin', N'$(AppIdentityName)') <> 1
    ALTER ROLE db_ddladmin ADD MEMBER [$(AppIdentityName)];

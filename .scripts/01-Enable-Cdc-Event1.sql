-- Event1 is captured with Change Data Capture (CDC) instead of SQL Service Broker, so it has no SqlChangeFeed
-- trigger or queues. This enables CDC on it and creates the relay's checkpoint and parked-change tables, using the
-- production scripts in .scripts/cdc. Runs before the seed scripts so that CDC captures every seeded change.
USE [FakeDb]
GO

-- Enabling CDC on the first table creates SQL Server Agent jobs, which fails with error 14258 while the Agent is still
-- starting. sys.dm_server_services reports the Agent as running before that, so wait for msdb to say it has started.
DECLARE @deadline datetime2 = DATEADD(MINUTE, 3, SYSUTCDATETIME());
DECLARE @starting int = NULL;
WHILE 1 = 1
BEGIN
	BEGIN TRY
		EXEC @starting = msdb.dbo.sp_is_sqlagent_starting;
	END TRY
	BEGIN CATCH
		SET @starting = NULL;
	END CATCH

	IF @starting = 0 AND EXISTS (SELECT 1 FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server Agent%' AND status_desc = N'Running')
		BREAK;
	IF SYSUTCDATETIME() > @deadline
		THROW 50000, N'SQL Server Agent did not start within 3 minutes. CDC needs it: set MSSQL_AGENT_ENABLED=true.', 1;
	WAITFOR DELAY '00:00:01';
END
GO

-- Both scripts are written for dbo.Event1 and the default relay schema 'Chatter'
:r /scripts/cdc/enable-cdc.sql
:r /scripts/cdc/create-relay-tables.sql

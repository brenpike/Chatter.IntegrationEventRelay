-- Creates the tables Chatter.IntegrationEventRelay.Cdc keeps in the source database, in the relay's schema
-- (setting 'IntegrationEventRelay:Schema', default 'Chatter'). Safe to run again.
-- Not run by the demo's SQL container: it runs only the scripts directly in .scripts.

DECLARE @schema sysname = N'Chatter';

IF SCHEMA_ID(@schema) IS NULL
	EXEC (N'CREATE SCHEMA ' + QUOTENAME(@schema));

-- The last LSN fully published per source and capture instance. Delete a row (or set LastLsn) to replay or skip changes.
IF OBJECT_ID(QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints]', N'U') IS NULL
	EXEC (N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints] (
		SourceName nvarchar(128) NOT NULL,
		CaptureInstance sysname NOT NULL,
		LastLsn binary(10) NOT NULL,
		UpdatedAt datetime2 NOT NULL,
		CONSTRAINT [PK_Chatter_RelayCheckpoints] PRIMARY KEY (SourceName, CaptureInstance)
	);');
GO

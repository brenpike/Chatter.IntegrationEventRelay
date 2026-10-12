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

-- Changes parked by the ParkRow error policy, and later changes to the same rows parked behind them.
-- To republish a row's parked changes once the cause is fixed, request a replay; the relay republishes them in order
-- and deletes them, or records the error and clears the request if they fail again:
--   UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME()
--   WHERE SourceName = N'<source>' AND CaptureInstance = N'<capture instance>' AND RowKey = N'<row key, e.g. [42]>';
IF OBJECT_ID(QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges]', N'U') IS NULL
	EXEC (N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges] (
		Id bigint IDENTITY NOT NULL,
		SourceName nvarchar(128) NOT NULL,
		CaptureInstance sysname NOT NULL,
		RowKey nvarchar(400) NOT NULL,
		Lsn binary(10) NOT NULL,
		SeqVal binary(10) NOT NULL,
		Operation nvarchar(16) NOT NULL,
		Position nvarchar(400) NOT NULL,
		CommittedAt datetimeoffset NULL,
		Payload nvarchar(max) NOT NULL,
		Error nvarchar(max) NOT NULL,
		Attempts int NOT NULL,
		ParkedAt datetime2 NOT NULL,
		ReplayRequestedAt datetime2 NULL,
		CONSTRAINT [PK_Chatter_RelayParkedChanges] PRIMARY KEY (Id),
		CONSTRAINT [UQ_Chatter_RelayParkedChanges_Position] UNIQUE (SourceName, CaptureInstance, Position)
	);
	CREATE INDEX [IX_Chatter_RelayParkedChanges_RowKey] ON ' + QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges] (SourceName, CaptureInstance, RowKey, Lsn, SeqVal);');
GO

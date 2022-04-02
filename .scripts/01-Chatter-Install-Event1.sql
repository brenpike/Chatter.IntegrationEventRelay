USE [FakeDb]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

                DECLARE @ExplicitCols bit = 1
                            -- Service Broker configuration statement.
                            
                IF EXISTS (SELECT * FROM sys.databases 
                                    WHERE name = 'FakeDb' AND is_broker_enabled = 0) 
                BEGIN
                    ALTER DATABASE [FakeDb] SET ENABLE_BROKER; 

                    ALTER AUTHORIZATION ON DATABASE::[FakeDb] TO [sa]
                END

                IF NOT EXISTS (SELECT * FROM sys.service_message_types WHERE name = '//Chatter/BrokeredMessage')
                    CREATE MESSAGE TYPE [//Chatter/BrokeredMessage] VALIDATION = NONE;

                IF NOT EXISTS (SELECT * FROM sys.service_contracts WHERE name = '//Chatter')
                    CREATE CONTRACT [//Chatter] ([//Chatter/BrokeredMessage] SENT BY ANY, [DEFAULT] SENT BY ANY);

                IF NOT EXISTS (SELECT * FROM sys.service_queues WHERE name = 'Chatter_Queue_Event1ChangedEvent')
	                CREATE QUEUE dbo.[Chatter_Queue_Event1ChangedEvent] WITH POISON_MESSAGE_HANDLING (STATUS = OFF)

                IF NOT EXISTS(SELECT * FROM sys.services WHERE name = 'Chatter_Service_Event1ChangedEvent')
	                CREATE SERVICE [Chatter_Service_Event1ChangedEvent] ON QUEUE dbo.[Chatter_Queue_Event1ChangedEvent] ([//Chatter])

                IF NOT EXISTS (SELECT * FROM sys.service_queues WHERE name = 'Chatter_DeadLetterQueue_Event1ChangedEvent')
	                CREATE QUEUE dbo.[Chatter_DeadLetterQueue_Event1ChangedEvent] WITH POISON_MESSAGE_HANDLING (STATUS = OFF)

                IF NOT EXISTS(SELECT * FROM sys.services WHERE name = 'Chatter_DeadLetterService_Event1ChangedEvent')
	                CREATE SERVICE [Chatter_DeadLetterService_Event1ChangedEvent] ON QUEUE dbo.[Chatter_DeadLetterQueue_Event1ChangedEvent] ([//Chatter]) 
            

                            IF OBJECT_ID ('dbo.Chatter_ChangeFeedTrigger_Event1ChangedEvent', 'TR') IS NOT NULL
                                RETURN;

                            -- Build column collection for target table:
                            DECLARE @tbl_Columns TABLE (COLUMN_NAME sysname NOT NULL, INCLUDE_OUTPUT bit NOT NULL, PK_ORDINAL int NULL);
                            INSERT INTO @tbl_Columns (COLUMN_NAME, INCLUDE_OUTPUT, PK_ORDINAL)
                            SELECT cols.COLUMN_NAME,
	                            CASE WHEN cols.DATA_TYPE IN ('text','ntext','image','geometry','geography') THEN 0 ELSE 1 END [INCLUDE_OUTPUT],
	                            colkeys.ORDINAL_POSITION [PK_ORDINAL]
                             FROM INFORMATION_SCHEMA.TABLES tab
                             INNER JOIN INFORMATION_SCHEMA.COLUMNS cols ON cols.TABLE_CATALOG = tab.TABLE_CATALOG
	                            AND cols.TABLE_SCHEMA = tab.TABLE_SCHEMA
	                            AND cols.TABLE_NAME = tab.TABLE_NAME
                             LEFT JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tabcon ON tabcon.TABLE_CATALOG = tab.TABLE_CATALOG
	                            AND tabcon.TABLE_SCHEMA = tab.TABLE_SCHEMA
	                            AND tabcon.TABLE_NAME = tab.TABLE_NAME
	                            AND tabcon.CONSTRAINT_TYPE = 'PRIMARY KEY'
                             LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE colkeys ON colkeys.TABLE_CATALOG = cols.TABLE_CATALOG
	                            AND colkeys.TABLE_SCHEMA = cols.TABLE_SCHEMA
	                            AND colkeys.TABLE_NAME = cols.TABLE_NAME
	                            AND colkeys.COLUMN_NAME = cols.COLUMN_NAME
	                            AND colkeys.CONSTRAINT_NAME = tabcon.CONSTRAINT_NAME
                             WHERE tab.TABLE_CATALOG = 'FakeDb'
	                            AND tab.TABLE_SCHEMA = 'dbo'
	                            AND tab.TABLE_NAME = 'Event1';

                            -- Construct column and join column strings:
                            DECLARE @ColumnList nvarchar(max) = '';
                            SELECT @ColumnList = @ColumnList + ',%PFX%.[' + COLUMN_NAME + ']' FROM @tbl_Columns;
                            DECLARE @JoinColumns nvarchar(max) = '';
                            SELECT @JoinColumns = @JoinColumns + ' AND del.[' + COLUMN_NAME + '] = ins.[' + COLUMN_NAME + ']'
                             FROM @tbl_Columns
                             WHERE PK_ORDINAL IS NOT NULL
                             ORDER BY PK_ORDINAL;

                            -- Construct statement for trigger to actually build message content:
                            DECLARE @TriggerMessageStatement nvarchar(max) = '
                            SET @Message = (
                            SELECT
	                            JSON_QUERY(NULLIF(JSON_QUERY((SELECT ' + CASE @ExplicitCols WHEN 1 THEN REPLACE(SUBSTRING(@ColumnList, 2, LEN(@ColumnList)), '%PFX%.', 'ins.') ELSE 'ins.*' END + ' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)), ''{}'')) [Inserted],
	                            JSON_QUERY(NULLIF(JSON_QUERY((SELECT ' + CASE @ExplicitCols WHEN 1 THEN REPLACE(SUBSTRING(@ColumnList, 2, LEN(@ColumnList)), '%PFX%.', 'del.') ELSE 'del.*' END + ' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)), ''{}'')) [Deleted]
                            FROM INSERTED ins
                            FULL OUTER JOIN DELETED del ON ' + SUBSTRING(@JoinColumns, 6, LEN(@JoinColumns)) + '
                            FOR JSON AUTO
                            );
                            SET @message = (SELECT JSON_QUERY(@message) [Changes] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);';

                            -- Change Feed Trigger configuration statement.
                            DECLARE @triggerStatement NVARCHAR(MAX) = REPLACE(CONVERT(nvarchar(max), N'
                CREATE TRIGGER dbo.[Chatter_ChangeFeedTrigger_Event1ChangedEvent]
                ON dbo.[Event1]
                WITH EXECUTE AS OWNER
                AFTER INSERT, UPDATE, DELETE 
                AS

                SET NOCOUNT ON;

                IF EXISTS (SELECT * FROM sys.services WHERE name = ''Chatter_Service_Event1ChangedEvent'')
                BEGIN
                    DECLARE @message NVARCHAR(MAX);
                    %set_message_statement%
                    IF @message IS NOT NULL
					BEGIN

                	    DECLARE @ConvHandle UNIQUEIDENTIFIER;
                	    BEGIN DIALOG @ConvHandle 
                            FROM SERVICE [Chatter_Service_Event1ChangedEvent] TO SERVICE ''Chatter_Service_Event1ChangedEvent'' ON CONTRACT [//Chatter] WITH ENCRYPTION=OFF; 

                        SEND ON CONVERSATION @ConvHandle MESSAGE TYPE [DEFAULT] (COMPRESS(@message));
                    END
                END
            '), '%set_message_statement%', @TriggerMessageStatement);

                            EXEC sp_executesql @triggerStatement
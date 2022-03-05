USE [FakeDb]
GO
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

							PRINT N'CREATING TRIGGER.';

							-- Change Feed Trigger configuration statement.
							DECLARE @triggerStatement NVARCHAR(MAX)
							DECLARE @select NVARCHAR(MAX)
							DECLARE @sqlInserted NVARCHAR(MAX)
							DECLARE @sqlDeleted NVARCHAR(MAX)
							
							SET @triggerStatement = N'
				CREATE TRIGGER dbo.[Chatter_ChangeFeedTrigger_Event1ChangedEvent]
				ON dbo.[Event1]
				WITH EXECUTE AS OWNER
				AFTER INSERT, UPDATE, DELETE 
				AS

				SET NOCOUNT ON;

				IF EXISTS (SELECT * FROM sys.services WHERE name = ''Chatter_Service_Event1ChangedEvent'')
				BEGIN
					DECLARE @message NVARCHAR(MAX)
					SET @message = N''''

					DECLARE @InsertedJSON NVARCHAR(MAX) 
					DECLARE @DeletedJSON NVARCHAR(MAX) 
					
					%inserted_select_statement%
					
					%deleted_select_statement% 
					
					IF (COALESCE(@DeletedJSON, N'''') = N'''') SET @message = @InsertedJSON
					ELSE
						IF (COALESCE(@InsertedJSON, N'''') = N'''') SET @message = @DeletedJSON
					ELSE
						SET @message = CONCAT(SUBSTRING(@InsertedJSON,1,LEN(@InsertedJSON) - 1), N'','', SUBSTRING(@DeletedJSON,2,LEN(@DeletedJSON)-1))

					IF @message IS NOT NULL
					BEGIN
						SET @message = compress(@message)                    

						DECLARE @ConvHandle UNIQUEIDENTIFIER

						BEGIN DIALOG @ConvHandle 
							FROM SERVICE [Chatter_Service_Event1ChangedEvent] TO SERVICE ''Chatter_Service_Event1ChangedEvent'' ON CONTRACT [//Chatter] WITH ENCRYPTION=OFF; 

						SEND ON CONVERSATION @ConvHandle MESSAGE TYPE [DEFAULT] (@message);
					END
				END
			'
							
							SET @select = STUFF((SELECT ',' + '[' + COLUMN_NAME + ']'
							   FROM INFORMATION_SCHEMA.COLUMNS
							   WHERE DATA_TYPE NOT IN  ('text','ntext','image','geometry','geography') AND TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Event1' AND TABLE_CATALOG = 'FakeDb'
							   FOR XML PATH ('')
							   ), 1, 1, '')

							SET @sqlInserted =
								N'SET @InsertedJSON = (SELECT ' + @select + N'
																		 FROM INSERTED
																		 FOR JSON AUTO, ROOT(''Inserted''))'

							SET @sqlDeleted =
								N'SET @DeletedJSON = (SELECT ' + @select + N'
																		 FROM DELETED
																		 FOR JSON AUTO, ROOT(''Deleted''))'

							SET @triggerStatement = REPLACE(@triggerStatement
													 , '%inserted_select_statement%', @sqlInserted)

							SET @triggerStatement = REPLACE(@triggerStatement
													 , '%deleted_select_statement%', @sqlDeleted)

							EXEC sp_executesql @triggerStatement

							PRINT N'TRIGGER CREATED:';
							PRINT @triggerStatement
							PRINT N'END TRIGGER DISPLAY';

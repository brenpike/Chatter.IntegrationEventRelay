USE [FakeDb]
GO

-- Small delay after seeding before triggering events
--WAITFOR DELAY '00:00:05'

-- Soft Delete all 25 records in [dbo].[Event1]
UPDATE [dbo].[Event1] SET [DeletedBy] = 1, [DeletedAt] = GETDATE() WHERE [DeletedBy] IS NULL
-- Restore all 25 records in [dbo].[Event1]
UPDATE [dbo].[Event1] SET [DeletedBy] = NULL, [DeletedAt] = NULL WHERE [DeletedBy] IS NOT NULL

-- Create 25 [dbo].[Event2] records
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'GOTMEONQANCFSYECWQ','TVTPUPQHCLVIUYUIJQ',75.5)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'VLALDOIKTQIBYLWKRZ','CKQDFNYATTOTQLICVE',80.79)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'UFGKCBQTFGCAGSDOTX','NLAWYXIGVPXVKUDXVE',15.62)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'CGJVAHWJOXVZYSIITJ','PIAJJJMPBYISGOIKMT',41.36)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'UVHOOILCIQTFKSGQYU','VKKRXXFZRZVSMULKQF',44.94)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'SSPRGAPUGCXXOOKPZI','JRIMJLPHVAATRUSQCB',15.26)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'CQCDCEOJKPKPPZESZH','BOMLQTMVRYKUZKMVNA',20.96)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'GSCMFUYLWQNDUQZGFP','YSIMGHMHWQDNDLNADM',15.98)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'UEVBEESSSXNKKMJEUV','DRKCRJKTZHBHSQTLPM',42.74)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'BAFLMJTZEPJTPSRYGN','AGWEOPBQZDBHLHNXXS',47.38)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'BCMDTEXORGXUKNCRLC','AYSHLFOTEVHOUJLFZS',58.1)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'ESDCHJZPIQATXAJWSE','PUXYDYFXPXGZKCNRBY',22.18)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'EVBDDHAFWVNDTKKCXA','EBRFOINRCPAONYBHYA',49.24)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'IZPRVJLFRQJWLMKTHQ','AEWSLSDLOAGUNVSPKL',2.97)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'ICPVHYKGMFVGNQFLNE','OULFJRVCQGKFGKDDXS',9.47)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'TNHCTNSNNMMJDSAPBF','UVKZKWBXXSGSPGURDX',94.92)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'RCDQTGWFDNAETKECHB','SLSJRESXRCPHSSWIUU',1.64)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'DHVBJURLRVYSJUWSYD','XHWIWJSOBXHQTRHWQD',27.6)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'TGSZFCMGYAZCFGHWLU','MJOVQGDJFCQKQEJTFP',75.93)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'DXUXSQESJAHECFILYK','LDUFQLFUAICJSMPLHE',26.42)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'FVNVACTEXYVAOLTAPN','BAFAGMMPUXFKYRNGVM',86.54)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'QGUCXYLBITYHHXNCAH','MLIDRTFJNNEUZZQEGN',29.04)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'LCQGHGDTKAHSMWUQDB','OUYGLUGNXCGEFMYLUO',94.21)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'HFZELHRMXUMFIQWLWV','XHVXYITEUDBJODGTLT',20.94)
INSERT INTO [dbo].[Event2] ([Id] ,[StringData] ,[MoreStringData] ,[DecimalData]) VALUES (NEWID(), 'SNWSJHWAYQOUVQBPQJ','VMDFAQZWHUYGLTNEKH',84.97)

-- Hard Delete all 25 records in [dbo].[Event1]
DELETE [dbo].[Event1]
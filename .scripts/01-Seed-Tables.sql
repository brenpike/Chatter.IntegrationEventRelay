USE [FakeDb]
GO

INSERT INTO [dbo].[Event1] ([Id] ,[StringData] ,[BoolData] ,[IntData] ,[DeletedBy] ,[DeletedAt])
VALUES ('873752D2-BE32-4DBF-8FAB-E4E1F40774CB', 'some string data', 0, 975, null, null)

INSERT INTO [dbo].[Event1] ([Id] ,[StringData] ,[BoolData] ,[IntData] ,[DeletedBy] ,[DeletedAt])
VALUES ('13EB3ED7-5563-44D7-8001-F0BB7572A5B2', 'restore me', 1, 948975, 6458, '20211210')

INSERT INTO [dbo].[Event1] ([Id] ,[StringData] ,[BoolData] ,[IntData] ,[DeletedBy] ,[DeletedAt])
VALUES ('DFA77BD6-373D-4B95-A39C-C815A2A0D7D1', 'soft delete me', 0, 354736, null, null)

INSERT INTO [dbo].[Event1] ([Id] ,[StringData] ,[BoolData] ,[IntData] ,[DeletedBy] ,[DeletedAt])
VALUES ('A460DBE8-C7D6-4EE1-A1EA-1F8B041503E1', 'hard delete me', 0, 354736, null, null)

INSERT INTO [dbo].[Event2] ([Id], [StringData], [MoreStringData], [DecimalData])
VALUES ('54285F62-D5F3-4CF3-9537-6E3D9E050EC0', 'some string data', 'more string data', 3434.3)

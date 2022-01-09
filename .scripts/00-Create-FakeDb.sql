CREATE DATABASE FakeDb
GO

USE [FakeDb]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Event1](
	[Id] [uniqueidentifier] NOT NULL,
	[StringData] [varchar](50) NULL,
	[BoolData] [bit] NULL,
	[IntData] [int] NULL,
	[DeletedBy] [int] NULL,
	[DeletedAt] [datetime] NULL
) ON [PRIMARY]
GO

USE [FakeDb]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Event2](
	[Id] [uniqueidentifier] NOT NULL,
	[StringData] [varchar](50) NULL,
	[MoreStringData] [varchar](50) NULL,
	[DecimalData] [decimal](18, 2) NULL
) ON [PRIMARY]
GO



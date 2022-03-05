using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.SqlChangeFeed;
using System;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;

public class EventMappingConfigurationItemCreator : MockCreator<EventMappingConfigurationItem>
{
    public EventMappingConfigurationItemCreator(IMockContext newContext, EventMappingConfigurationItem creation = null)
        : base(newContext, creation)
    {
        var config = new EventMappingConfigurationItem()
        {
            DatabaseName = "database",
            ConnectionString = "connStr",
            InfrastructureMessageName = "msgName",
            IntegrationEventTypeName = "This.Is.Definitely.Not.A.Real.IntegrationEvent.TypeName",
            SourceChangeType = ChangeTypes.Insert,
            SourceEventTypeName = "This.Is.Definitely.Not.A.Real.SourceEvent.TypeName",
            MessagingInfrastructureType = "infra",
            TableName = "table"
        };

        Mock = config;
    }

    public EventMappingConfigurationItemCreator WithConnectionString(string connStr)
    {
        Mock.ConnectionString = connStr;
        return this;
    }

    public EventMappingConfigurationItemCreator WithNullConnectionString()
    {
        Mock.ConnectionString = null;
        return this;
    }

    public EventMappingConfigurationItemCreator WithDatabaseName(string dbName)
    {
        Mock.DatabaseName = dbName ?? string.Empty;
        return this;
    }

    public EventMappingConfigurationItemCreator WithNullDatabaseName()
    {
        Mock.DatabaseName = null;
        return this;
    }

    public EventMappingConfigurationItemCreator WithSourceEventType(Type type)
    {
        Mock.SourceEventType = type;
        Mock.SourceEventTypeName = type.FullName;
        return this;
    }

    public EventMappingConfigurationItemCreator WithIntegrationEventType(Type type)
    {
        Mock.IntegrationEventType = type;
        Mock.IntegrationEventTypeName = type.FullName;
        return this;
    }


    public EventMappingConfigurationItemCreator WithTable(string table)
    {
        Mock.TableName = table;
        return this;
    }

    public EventMappingConfigurationItemCreator WithChangeType(ChangeTypes changeType)
    {
        Mock.SourceChangeType = changeType;
        return this;
    }
}

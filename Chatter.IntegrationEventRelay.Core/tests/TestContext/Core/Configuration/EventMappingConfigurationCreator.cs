using Chatter.IntegrationEventRelay.Core.Configuration;
using System.Collections.Generic;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;

public class EventMappingConfigurationCreator : MockCreator<EventMappingConfiguration>
{
    public EventMappingConfigurationCreator(IMockContext newContext, EventMappingConfiguration creation = null)
        : base(newContext, creation)
    {
        Mock = new EventMappingConfiguration() { ConnectionString = "ConnStr", Mappings = new List<EventMappingConfigurationItem>() };
    }

    public EventMappingConfigurationCreator WithMappings(params EventMappingConfigurationItem[] mappings)
    {
        Mock.Mappings = mappings;
        return this;
    }

    public EventMappingConfigurationCreator WithNullMappings()
    {
        Mock.Mappings = null;
        return this;
    }

    public EventMappingConfigurationCreator WithConnectionString(string connStr)
    {
        Mock.ConnectionString = connStr;
        return this;
    }

    public EventMappingConfigurationCreator WithNullConnectionString()
    {
        Mock.ConnectionString = null;
        return this;
    }
}

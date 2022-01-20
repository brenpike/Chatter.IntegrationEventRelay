namespace Chatter.IntegrationEventRelay.Core.Configuration;

public class EventMappingConfiguration
{
    public string? ConnectionString { get; set; }
    public IEnumerable<EventMappingConfigurationItem>? Mappings { get; set; }
}

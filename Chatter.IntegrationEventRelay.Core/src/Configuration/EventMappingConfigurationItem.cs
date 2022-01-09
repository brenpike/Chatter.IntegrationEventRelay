using Chatter.SqlTableWatcher;
using System.ComponentModel.DataAnnotations;

namespace Chatter.IntegrationEventRelay.Core.Configuration;

public class EventMappingConfigurationItem
{
    /// <summary>
    /// The name of the type that changes to <see cref="TableName"/> will be mapped to. Will be used to get the type <see cref="SourceEventType"/>.
    /// </summary>
    /// <remarks>
    /// For successful mapping from <see cref="SourceEventTypeName"/> to <see cref="SourceEventType"/>, using <see cref="Type.AssemblyQualifiedName"/> or <see cref="Type.FullName"/> is prefered.
    /// If just the name of the type is used,an attempt will be made to match the with the correct <see cref="Type"/>, but will fail exactly one match is not found.
    /// </remarks>
    [Required]
    public string SourceEventTypeName { get; set; }
    /// <summary>
    /// The name of the type that changes to <see cref="TableName"/> will be relayed to messaging infrastructure as an integration event. Will be used to get the type <see cref="IntegrationEventType"/>.
    /// </summary>
    /// <remarks>
    /// For successful mapping from <see cref="IntegrationEventTypeName"/> to <see cref="IntegrationEventType"/>, using <see cref="Type.AssemblyQualifiedName"/> or <see cref="Type.FullName"/> is prefered.
    /// If just the name of the type is used,an attempt will be made to match the with the correct <see cref="Type"/>, but will fail exactly one match is not found.
    /// </remarks>
    [Required]
    public string IntegrationEventTypeName { get; set; }

    /// <summary>
    /// The type that changes to <see cref="TableName"/> will be mapped to.
    /// </summary>
    public Type? SourceEventType { get; set; } = null;

    /// <summary>
    /// The type that changes to <see cref="TableName"/> will be relayed to messaging infrastructure as an integration event.
    /// </summary>
    public Type? IntegrationEventType { get; set; } = null;

    /// <summary>
    /// Optional. The connection string of the sql server to watch for changes that are to be relayed to messaging infrastructure as integration events. If not set, <see cref="EventMappingConfiguration.ConnectionString"/> will be used.
    /// </summary>
    public string ConnectionString { get; set; }
    /// <summary>
    /// The database of which contains the table to watch for changes to be relayed to messaging infrastructure as integration events.
    /// </summary>
    public string DatabaseName { get; set; }
    /// <summary>
    /// The table to watch for changes to be relayed to messaging infrastructure as integration events.
    /// </summary>
    [Required]
    public string TableName { get; set; }
    /// <summary>
    /// The type of change to watch <see cref="TableName"/> for, so that <see cref="SourceEventType"/> and be relayed as an integration event of type <see cref="IntegrationEventType"/>
    /// </summary>
    [Required]
    public ChangeTypes SourceChangeType { get; set; }

    /// <summary>
    /// Optional. The messaging infrastructure where the <see cref="IntegrationEventType"/> should be relayed upon a change to <see cref="SourceEventType"/>. If left empty, the first registered messaging infrastructure will be used.
    /// </summary>
    /// <remarks>
    /// The messaging infrastructure type should match the defined constant defined by the relevant messaging infrastructure.
    /// For example, "Chatter.Infrastructure.AzureServiceBus" or "Chatter.Infrastructure.SqlServiceBroker" for Azure Serive Bus and Sql Service Broker, respectively.
    /// </remarks>
    public string MessagingInfrastructureType { get; set; } = string.Empty;

    /// <summary>
    /// Optional. The name of the topic/event on the messaging infrastructure to publish the integration event to.
    /// If supplied, will override the value specified by the <see cref="MessageBrokers.BrokeredMessageAttribute"/> on the <see cref="IntegrationEventType"/>.
    /// </summary>
    public string InfrastructureMessageName { get; set; }
}

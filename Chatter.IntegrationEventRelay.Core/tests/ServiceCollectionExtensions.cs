using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chatter.IntegrationEventRelay.Core.Tests;

public static class ServiceCollectionExtensions
{
    public static List<ServiceDescriptor> GetServiceDescriptorsByImplementationType(this IServiceCollection serviceCollection, Type implementationType)
        => serviceCollection.Where(sd => sd.ImplementationType == implementationType).ToList();

    public static List<ServiceDescriptor> GetServiceDescriptorsByServiceType(this IServiceCollection serviceCollection, Type serviceType)
        => serviceCollection.Where(sd => sd.ServiceType == serviceType).ToList();
}

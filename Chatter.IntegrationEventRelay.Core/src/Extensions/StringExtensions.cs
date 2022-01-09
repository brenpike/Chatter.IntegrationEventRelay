using System.Reflection;

namespace Chatter.IntegrationEventRelay.Core.Extensions;

public static class StringExtensions
{
    public static Type GetTypeFromString(this string typeName, IEnumerable<Assembly> assemblies = null)
    {
        if (assemblies == null)
            assemblies = new[] { Assembly.GetExecutingAssembly() };

        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var types = assemblies?.SelectMany(a => a.GetTypes()).Where(t => t.FullName?.ToLowerInvariant().Contains(typeName.ToLowerInvariant()) ?? false);
        if (!types.Any())
            return null;
        else if (types.Count() == 1)
            return types.First();
        else
            throw new ArgumentException($"The type name provided was not specific enough to find an exact type match. Consider using the {nameof(Type.FullName)}.", nameof(typeName));
    }
}

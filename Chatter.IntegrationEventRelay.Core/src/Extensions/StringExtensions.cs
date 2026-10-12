using System.Reflection;

namespace Chatter.IntegrationEventRelay.Core.Extensions;

public static class StringExtensions
{
	/// <summary>
	/// Finds the type named <paramref name="typeName"/> in <paramref name="assemblies"/>.
	/// </summary>
	/// <remarks>
	/// Matches, in order: the assembly-qualified name, the full name, then the simple name. Full and simple names are
	/// compared ignoring case, as before. A simple name must match exactly one type.
	/// </remarks>
	/// <returns>The type, or null if none matches.</returns>
	/// <exception cref="ArgumentException">The simple name matches more than one type.</exception>
	public static Type? GetTypeFromString(this string typeName, IEnumerable<Assembly>? assemblies = null)
	{
		if (string.IsNullOrWhiteSpace(typeName))
			return null;

		var types = (assemblies ?? new[] { Assembly.GetExecutingAssembly() })
			.Distinct()
			.SelectMany(GetLoadableTypes)
			.ToList();

		var byQualifiedName = types.FirstOrDefault(t => string.Equals(t.AssemblyQualifiedName, typeName, StringComparison.Ordinal));
		if (byQualifiedName is not null)
			return byQualifiedName;

		var byFullName = types.Where(t => string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
		if (byFullName.Count == 1)
			return byFullName[0];
		if (byFullName.Count > 1)
			throw new ArgumentException($"The type name '{typeName}' matches more than one type. Use the {nameof(Type.AssemblyQualifiedName)}.", nameof(typeName));

		var byName = types.Where(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
		return byName.Count switch
		{
			0 => null,
			1 => byName[0],
			_ => throw new ArgumentException($"The type name '{typeName}' matches more than one type ({string.Join(", ", byName.Select(t => t.FullName))}). Use the {nameof(Type.FullName)}.", nameof(typeName))
		};
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			return e.Types.Where(t => t is not null)!;
		}
	}
}

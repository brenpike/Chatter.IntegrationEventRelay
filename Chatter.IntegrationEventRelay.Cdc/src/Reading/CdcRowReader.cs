using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;

namespace Chatter.IntegrationEventRelay.Cdc.Reading;

/// <summary>
/// Turns CDC change rows into <see cref="RowChange{TSource}"/>s: captured columns set the <typeparamref name="TSource"/>
/// properties of the same name (ignoring case), and an update's before and after images become one update.
/// </summary>
internal sealed class CdcRowReader<TSource> where TSource : class, new()
{
	private readonly IReadOnlyDictionary<string, PropertyInfo> _properties;
	private readonly ILogger _logger;
	private readonly HashSet<string> _defaultedProperties = new(StringComparer.OrdinalIgnoreCase);

	public CdcRowReader(ILogger logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_properties = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
			.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The settable properties of <typeparamref name="TSource"/> that no captured column sets.
	/// </summary>
	public IReadOnlyList<string> GetUnmappedProperties(IEnumerable<string> capturedColumns)
	{
		var columns = new HashSet<string>(capturedColumns, StringComparer.OrdinalIgnoreCase);
		return _properties.Keys.Where(p => !columns.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
	}

	/// <param name="rows">Rows in commit order, as <see cref="ICdcQueries.GetChangesAsync"/> returns them.</param>
	/// <param name="serverUtcOffset">The offset of the server's local time, which commit times are in.</param>
	public IReadOnlyList<RowChange<TSource>> Read(IReadOnlyList<CdcChangeRow> rows, CdcCapture capture, TimeSpan serverUtcOffset)
	{
		var changes = new List<RowChange<TSource>>(rows.Count);
		var positions = new Dictionary<string, int>(StringComparer.Ordinal);

		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			switch (row.Operation)
			{
				case CdcChangeRow.Delete:
					changes.Add(Create(RowOperation.Delete, null, row));
					break;
				case CdcChangeRow.Insert:
					changes.Add(Create(RowOperation.Insert, null, row));
					break;
				case CdcChangeRow.UpdateBefore:
					var after = i + 1 < rows.Count ? rows[i + 1] : null;
					if (after is null || after.Operation != CdcChangeRow.UpdateAfter || after.StartLsn != row.StartLsn || after.SeqVal != row.SeqVal)
						throw new InvalidOperationException($"Capture instance '{capture.CaptureInstance}' returned an update before image at LSN {row.StartLsn}, sequence {row.SeqVal} without its after image.");
					changes.Add(Create(RowOperation.Update, Materialize(row), after));
					i++;
					break;
				case CdcChangeRow.UpdateAfter:
					throw new InvalidOperationException($"Capture instance '{capture.CaptureInstance}' returned an update after image at LSN {row.StartLsn}, sequence {row.SeqVal} without its before image. Changes must be read with the 'all update old' row filter.");
				default:
					throw new InvalidOperationException($"Capture instance '{capture.CaptureInstance}' returned unknown operation {row.Operation} at LSN {row.StartLsn}.");
			}
		}

		return changes;

		RowChange<TSource> Create(RowOperation operation, TSource? oldValue, CdcChangeRow row)
			=> new(capture.SourceName, operation, oldValue, Materialize(row), capture.Table, GetPosition(row), GetCommittedAt(row));

		// Positions identify a change within the source, so the relay's message ids are the same when a batch is replayed.
		// Commit LSN, sequence value and operation are unique per change; should two rows ever share them, the
		// occurrence number keeps their ids apart.
		ChangePosition GetPosition(CdcChangeRow row)
		{
			var value = $"{capture.CaptureInstance}:{row.StartLsn}:{row.SeqVal}:{row.Operation}";
			var occurrence = positions.TryGetValue(value, out var seen) ? seen + 1 : 0;
			positions[value] = occurrence;
			return new ChangePosition(occurrence == 0 ? value : $"{value}#{occurrence}", new CdcChangeOrder(row.StartLsn, row.SeqVal, row.Operation));
		}

		DateTimeOffset? GetCommittedAt(CdcChangeRow row)
			=> row.CommitTime is { } time ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), serverUtcOffset) : null;
	}

	private TSource Materialize(CdcChangeRow row)
	{
		var value = new TSource();
		foreach (var (column, columnValue) in row.Columns)
		{
			if (!_properties.TryGetValue(column, out var property))
				continue;

			object? converted;
			try
			{
				converted = Convert(columnValue, property.PropertyType, out var defaulted);
				if (defaulted && _defaultedProperties.Add(property.Name))
					_logger.LogWarning("Column '{ColumnName}' is null but property '{SourceEventTypeName}.{PropertyName}' isn't nullable; using its default value. Logged once.", column, typeof(TSource).Name, property.Name);
			}
			catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
			{
				throw new InvalidOperationException($"Can't set property '{typeof(TSource).Name}.{property.Name}' of type '{property.PropertyType}' from column '{column}' value of type '{columnValue?.GetType()}'.", e);
			}

			property.SetValue(value, converted);
		}

		return value;
	}

	internal static object? Convert(object? value, Type targetType, out bool defaulted)
	{
		defaulted = false;
		var underlying = Nullable.GetUnderlyingType(targetType);

		if (value is null)
		{
			if (!targetType.IsValueType || underlying is not null)
				return null;
			defaulted = true;
			return Activator.CreateInstance(targetType);
		}

		var type = underlying ?? targetType;
		if (type.IsInstanceOfType(value))
			return value;
		if (type.IsEnum)
			return value is string name ? Enum.Parse(type, name, ignoreCase: true) : Enum.ToObject(type, value);
		if (type == typeof(Guid) && value is string guid)
			return Guid.Parse(guid);
		if (type == typeof(DateTimeOffset) && value is DateTime dateTime)
			throw new InvalidCastException("A datetime column has no offset; map it to a DateTime property.");

		return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
	}
}

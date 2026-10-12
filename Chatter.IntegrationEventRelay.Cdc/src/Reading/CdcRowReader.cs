using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
		=> Read(rows, capture, serverUtcOffset, Array.Empty<string>()).Select(c => c.Change).ToList();

	/// <summary>
	/// Reads changes with the key of the row each one changed, made of <paramref name="keyColumns"/>' values.
	/// </summary>
	public IReadOnlyList<CdcRowChange<TSource>> Read(IReadOnlyList<CdcChangeRow> rows, CdcCapture capture, TimeSpan serverUtcOffset, IReadOnlyList<string> keyColumns)
	{
		var changes = new List<CdcRowChange<TSource>>(rows.Count);
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

		CdcRowChange<TSource> Create(RowOperation operation, TSource? oldValue, CdcChangeRow row)
			=> new(new(capture.SourceName, operation, oldValue, Materialize(row), capture.Table, GetPosition(row), GetCommittedAt(row)), GetRowKey(row, keyColumns), row.StartLsn, row.SeqVal);

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

	/// <summary>
	/// The changed row's key as a JSON array of its key column values, or null without key columns.
	/// </summary>
	/// <remarks>Keys longer than <see cref="MaxRowKeyLength"/> are replaced by their SHA-256 hash.</remarks>
	internal static string? GetRowKey(CdcChangeRow row, IReadOnlyList<string> keyColumns)
	{
		if (keyColumns.Count == 0)
			return null;

		var key = JsonSerializer.Serialize(keyColumns.Select(c => row.Columns.TryGetValue(c, out var value) ? value : null));
		return key.Length <= MaxRowKeyLength ? key : $"sha256:{System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";
	}

	internal const int MaxRowKeyLength = 400;

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

/// <summary>
/// A change read from CDC, with the key of the row it changed (null when not needed) and where CDC recorded it.
/// </summary>
internal sealed record CdcRowChange<TSource>(RowChange<TSource> Change, string? RowKey, CdcLsn Lsn, CdcLsn SeqVal) where TSource : class;

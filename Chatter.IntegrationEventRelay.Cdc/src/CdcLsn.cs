using System.Buffers.Binary;
using System.Globalization;

namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// A SQL Server log sequence number as CDC exposes it: a <c>binary(10)</c> value, ordered as a big-endian unsigned number.
/// </summary>
public readonly struct CdcLsn : IComparable<CdcLsn>, IComparable, IEquatable<CdcLsn>
{
	public const int Length = 10;

	// The first 8 bytes and the last 2, big-endian, so the pair compares like the binary value
	private readonly ulong _high;
	private readonly ushort _low;

	private CdcLsn(ulong high, ushort low)
	{
		_high = high;
		_low = low;
	}

	/// <summary>
	/// The all-zero value, which CDC functions return when there is no LSN (for example an unknown capture instance).
	/// </summary>
	public static CdcLsn Zero => default;

	public bool IsZero => _high == 0 && _low == 0;

	public static CdcLsn FromBytes(ReadOnlySpan<byte> value)
	{
		if (value.Length != Length)
			throw new ArgumentException($"A CDC LSN is {Length} bytes; got {value.Length}.", nameof(value));

		return new CdcLsn(BinaryPrimitives.ReadUInt64BigEndian(value), BinaryPrimitives.ReadUInt16BigEndian(value[8..]));
	}

	/// <summary>
	/// Parses the hex form, with or without a <c>0x</c> prefix, as written by <see cref="ToString"/> or SQL Server.
	/// </summary>
	public static CdcLsn Parse(string value)
	{
		_ = value ?? throw new ArgumentNullException(nameof(value));
		var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
		if (hex.Length != Length * 2)
			throw new FormatException($"A CDC LSN is {Length * 2} hex digits; got '{value}'.");

		return FromBytes(Convert.FromHexString(hex));
	}

	public byte[] ToArray()
	{
		var bytes = new byte[Length];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
		BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), _low);
		return bytes;
	}

	/// <summary>
	/// The next LSN, as <c>sys.fn_cdc_increment_lsn</c> returns: this value plus one, carrying into the high bytes.
	/// </summary>
	public CdcLsn Increment()
	{
		if (_low != ushort.MaxValue)
			return new CdcLsn(_high, (ushort)(_low + 1));
		if (_high == ulong.MaxValue)
			throw new OverflowException("The CDC LSN is already the largest possible value.");
		return new CdcLsn(_high + 1, 0);
	}

	public int CompareTo(CdcLsn other)
	{
		var high = _high.CompareTo(other._high);
		return high != 0 ? high : _low.CompareTo(other._low);
	}

	public int CompareTo(object? obj) => obj switch
	{
		null => 1,
		CdcLsn other => CompareTo(other),
		_ => throw new ArgumentException($"Object must be of type {nameof(CdcLsn)}.", nameof(obj))
	};

	public bool Equals(CdcLsn other) => _high == other._high && _low == other._low;
	public override bool Equals(object? obj) => obj is CdcLsn other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(_high, _low);

	/// <summary>
	/// The value as <c>0x</c> followed by 20 upper-case hex digits, as SQL Server displays it.
	/// </summary>
	public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"0x{_high:X16}{_low:X4}");

	public static bool operator ==(CdcLsn left, CdcLsn right) => left.Equals(right);
	public static bool operator !=(CdcLsn left, CdcLsn right) => !left.Equals(right);
	public static bool operator <(CdcLsn left, CdcLsn right) => left.CompareTo(right) < 0;
	public static bool operator >(CdcLsn left, CdcLsn right) => left.CompareTo(right) > 0;
	public static bool operator <=(CdcLsn left, CdcLsn right) => left.CompareTo(right) <= 0;
	public static bool operator >=(CdcLsn left, CdcLsn right) => left.CompareTo(right) >= 0;
}

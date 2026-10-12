using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// A lease held as a session-owned exclusive application lock (<c>sp_getapplock</c>) on a dedicated, unpooled
/// connection: SQL Server releases it when the connection closes or breaks, so a crashed relay can't keep it.
/// </summary>
internal sealed class SqlAppLockLease : ICdcLease
{
	private const int MaxResourceLength = 255;

	private readonly string _connectionString;
	private readonly string _resource;
	private SqlConnection? _connection;

	public SqlAppLockLease(string connectionString, string resource)
	{
		_connectionString = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
		_resource = ToResourceName(resource);
	}

	public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
	{
		const string sql = """
			DECLARE @result int;
			EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0, @DbPrincipal = 'public';
			SELECT @result;
			""";

		await ReleaseConnectionAsync().ConfigureAwait(false);
		var connection = new SqlConnection(_connectionString);
		try
		{
			await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using var command = SqlCdcQueries.CreateCommand(connection, sql, ResourceParameter());
			// 0: granted, 1: granted after waiting; negative: not granted
			if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= 0)
			{
				_connection = connection;
				return true;
			}
		}
		catch
		{
			await connection.DisposeAsync().ConfigureAwait(false);
			throw;
		}

		await connection.DisposeAsync().ConfigureAwait(false);
		return false;
	}

	public async Task<bool> IsHeldAsync(CancellationToken cancellationToken)
	{
		if (_connection is null || _connection.State != System.Data.ConnectionState.Open)
			return false;

		try
		{
			await using var command = SqlCdcQueries.CreateCommand(_connection, "SELECT APPLOCK_MODE('public', @resource, 'Session');", ResourceParameter());
			return string.Equals(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string, "Exclusive", StringComparison.OrdinalIgnoreCase);
		}
		catch (SqlException)
		{
			return false;
		}
	}

	public ValueTask DisposeAsync() => new(ReleaseConnectionAsync());

	// Closing the unpooled connection ends the session, which releases the lock
	private async Task ReleaseConnectionAsync()
	{
		var connection = _connection;
		_connection = null;
		if (connection is not null)
			await connection.DisposeAsync().ConfigureAwait(false);
	}

	private SqlParameter ResourceParameter() => new("@resource", System.Data.SqlDbType.NVarChar, MaxResourceLength) { Value = _resource };

	/// <summary>
	/// Application lock names are at most 255 characters; longer ones are shortened with a hash so they stay unique.
	/// </summary>
	internal static string ToResourceName(string resource)
	{
		if (string.IsNullOrWhiteSpace(resource))
			throw new ArgumentNullException(nameof(resource));
		if (resource.Length <= MaxResourceLength)
			return resource;

		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
		return $"{resource[..(MaxResourceLength - hash.Length - 1)]}#{hash}";
	}
}

internal sealed class SqlAppLockLeaseFactory : ICdcLeaseFactory
{
	private readonly string _connectionString;

	public SqlAppLockLeaseFactory(string connectionString)
		=> _connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;

	public ICdcLease Create(string resource) => new SqlAppLockLease(_connectionString, resource);
}

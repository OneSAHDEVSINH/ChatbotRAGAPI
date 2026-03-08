using ChatbotRAGAPI.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ChatbotRAGAPI.Services.HealthChecks;

public sealed class PostgresDependencyHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly RagOptions _options;

    public PostgresDependencyHealthCheck(IOptions<RagOptions> options, NpgsqlDataSource? dataSource = null)
    {
        _dataSource = dataSource;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Postgres.IsConfigured)
        {
            return HealthCheckResult.Healthy("PostgreSQL is not configured.");
        }

        if (_dataSource is null)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL data source is unavailable.");
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL health check failed.", exception);
        }
    }
}

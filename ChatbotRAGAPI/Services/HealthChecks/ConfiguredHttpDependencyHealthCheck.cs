using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChatbotRAGAPI.Services.HealthChecks;

public sealed class ConfiguredHttpDependencyHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _dependencyName;
    private readonly string? _healthUrl;

    public ConfiguredHttpDependencyHealthCheck(HttpClient httpClient, string dependencyName, string? healthUrl)
    {
        _httpClient = httpClient;
        _dependencyName = dependencyName;
        _healthUrl = healthUrl;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_healthUrl))
        {
            return HealthCheckResult.Healthy($"{_dependencyName} is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _healthUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return (int)response.StatusCode < 500
                ? HealthCheckResult.Healthy($"{_dependencyName} is reachable.")
                : HealthCheckResult.Degraded($"{_dependencyName} responded with status {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy($"{_dependencyName} is unreachable.", exception);
        }
    }
}

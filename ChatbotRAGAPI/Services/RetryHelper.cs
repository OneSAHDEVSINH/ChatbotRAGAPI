using ChatbotRAGAPI.Options;

namespace ChatbotRAGAPI.Services;

public static class RetryHelper
{
    public static async Task ExecuteAsync(Func<CancellationToken, Task> operation, ResilienceOptions options, CancellationToken cancellationToken)
    {
        await ExecuteAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, options, cancellationToken);
    }

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, ResilienceOptions options, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < options.MaxRetries)
            {
                lastException = null;
            }
            catch (Exception exception) when (attempt < options.MaxRetries)
            {
                lastException = exception;
            }
            catch (Exception exception)
            {
                throw exception;
            }

            var delay = ComputeDelay(options, attempt + 1);
            await Task.Delay(delay, cancellationToken);
        }

        throw lastException ?? new InvalidOperationException("Retry operation failed without an explicit exception.");
    }

    private static TimeSpan ComputeDelay(ResilienceOptions options, int retryNumber)
    {
        var delayMs = options.InitialDelayMs * Math.Pow(options.BackoffMultiplier, retryNumber - 1);
        delayMs = Math.Min(delayMs, options.MaxDelayMs);

        if (options.UseJitter)
        {
            delayMs *= 0.8 + (Random.Shared.NextDouble() * 0.4);
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }
}

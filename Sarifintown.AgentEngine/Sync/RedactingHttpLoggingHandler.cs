using System.Text;

namespace Sarifintown.AgentEngine.Sync;

internal sealed record SyncHttpLoggingOptions(string WorkspaceRoot);

internal sealed class RedactingHttpLoggingHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim LogWriteLock = new(1, 1);
    private readonly string _logFilePath;

    public RedactingHttpLoggingHandler(SyncHttpLoggingOptions loggingOptions)
    {
        ArgumentNullException.ThrowIfNull(loggingOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(loggingOptions.WorkspaceRoot);

        _logFilePath = Path.Combine(Path.GetFullPath(loggingOptions.WorkspaceRoot), ".sarif", "sarif_sync_http.log");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = DateTimeOffset.UtcNow;
        string requestBody;
        try
        {
            requestBody = await ReadContentAsync(request.Content, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            requestBody = "<failed to read request body>";
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            var responseBody = await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);

            await AppendLogAsync(
                BuildLogBlock(request, requestBody, response, responseBody, startedAt, completedAt),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort logging; never disrupt the actual HTTP operation
        }

        return response;
    }

    private static string BuildLogBlock(
        HttpRequestMessage request,
        string requestBody,
        HttpResponseMessage response,
        string responseBody,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{startedAt:O}] Request");
        builder.AppendLine($"Method: {request.Method}");
        builder.AppendLine($"Uri: {request.RequestUri}");

        foreach (var header in request.Headers)
        {
            var value = string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
                ? "[REDACTED]"
                : string.Join(", ", header.Value);
            builder.AppendLine($"{header.Key}: {value}");
        }

        foreach (var header in request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
        {
            builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        builder.AppendLine("RequestBody:");
        builder.AppendLine(string.IsNullOrWhiteSpace(requestBody) ? "<empty>" : requestBody);
        builder.AppendLine();

        builder.AppendLine($"[{completedAt:O}] Response");
        builder.AppendLine($"StatusCode: {(int)response.StatusCode} {response.StatusCode}");

        foreach (var header in response.Headers)
        {
            builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        foreach (var header in response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
        {
            builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        builder.AppendLine("ResponseBody:");
        builder.AppendLine(string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody);
        builder.AppendLine(new string('-', 80));

        return builder.ToString();
    }

    private static async Task<string> ReadContentAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content == null)
        {
            return string.Empty;
        }

        return await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendLogAsync(string block, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await LogWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_logFilePath, block, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LogWriteLock.Release();
        }
    }
}

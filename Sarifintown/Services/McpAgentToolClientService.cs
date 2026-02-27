using System.Text.Json;

namespace Sarifintown.Services
{
    public sealed class McpAgentToolClientService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly McpUiBridgeService _bridge;

        public McpAgentToolClientService(McpUiBridgeService bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// Requests the current workspace SARIF file list from AgentEngine through the host bridge.
        /// </summary>
        public async Task<IReadOnlyList<McpSarifFileDescriptor>> ListWorkspaceSarifFilesAsync(CancellationToken cancellationToken = default)
        {
            var envelope = await _bridge.RequestAsync(
                "ui.request.tool",
                new
                {
                    tool = "ListWorkspaceSarifFiles",
                    arguments = new { }
                },
                cancellationToken: cancellationToken);

            return ParseToolJsonPayload<List<McpSarifFileDescriptor>>(envelope.Payload) ?? new List<McpSarifFileDescriptor>();
        }

        private static T? ParseToolJsonPayload<T>(JsonElement payload)
        {
            if (!payload.TryGetProperty("toolResult", out var resultElement))
            {
                return default;
            }

            if (resultElement.ValueKind == JsonValueKind.String)
            {
                var json = resultElement.GetString();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }

            return resultElement.Deserialize<T>(JsonOptions);
        }
    }

    public sealed record McpSarifFileDescriptor(string Name, string Path);
}

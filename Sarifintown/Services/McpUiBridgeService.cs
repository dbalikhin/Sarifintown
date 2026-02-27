using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Sarifintown.Services
{
    public sealed class McpUiBridgeService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private DotNetObjectReference<McpUiBridgeService>? _dotNetReference;
        private bool _isInitialized;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<McpEnvelope>> _pendingRequests = new(StringComparer.Ordinal);

        public event Func<McpEnvelope, Task>? HostMessageReceived;

        public McpUiBridgeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync(string channel = "sarifintown.mcp.v1", string targetOrigin = "*")
        {
            if (_isInitialized)
            {
                return;
            }

            _dotNetReference = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("mcpUiBridge.start", _dotNetReference, new { channel, targetOrigin });
            _isInitialized = true;
        }

        public ValueTask SendAsync(string type, object? payload = null, string? requestId = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);

            if (!_isInitialized)
            {
                throw new InvalidOperationException("MCP UI bridge has not been initialized.");
            }

            return _jsRuntime.InvokeVoidAsync("mcpUiBridge.send", type, payload, requestId);
        }

        public async Task<McpEnvelope> RequestAsync(
            string type,
            object? payload = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<McpEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pendingRequests.TryAdd(requestId, tcs))
            {
                throw new InvalidOperationException("Failed to register MCP request.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

            await SendAsync(type, payload, requestId);

            try
            {
                return await tcs.Task;
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        [JSInvokable]
        public async Task ReceiveHostMessage(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return;
            }

            var envelope = JsonSerializer.Deserialize<McpEnvelope>(rawMessage);
            if (envelope == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(envelope.RequestId)
                && _pendingRequests.TryGetValue(envelope.RequestId, out var pendingRequest))
            {
                pendingRequest.TrySetResult(envelope);
            }

            var callback = HostMessageReceived;
            if (callback != null)
            {
                await callback(envelope);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isInitialized)
            {
                await _jsRuntime.InvokeVoidAsync("mcpUiBridge.stop");
            }

            _dotNetReference?.Dispose();

            foreach (var pendingRequest in _pendingRequests.Values)
            {
                pendingRequest.TrySetCanceled();
            }

            _pendingRequests.Clear();
        }
    }

    public sealed class McpEnvelope
    {
        public string Channel { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string? RequestId { get; set; }

        public JsonElement Payload { get; set; }
    }
}

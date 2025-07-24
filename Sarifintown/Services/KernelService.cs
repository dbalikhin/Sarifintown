using Microsoft.JSInterop;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;

namespace Sarifintown.Services
{
    // It's good practice to have this in its own file, e.g., Services/KernelService.cs
    public class KernelService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly IHttpClientFactory _httpClientFactory;
        private const string LocalStorageKey = "AiChatSettings";

        public AiSettings Settings { get; private set; } = new();
        public Kernel? Kernel { get; private set; }
        public IChatCompletionService? ChatCompletionService { get; set; }

        public bool IsInitialized { get; private set; }

        // Event to notify components of state changes
        public event Action? OnChange;

        public KernelService(IJSRuntime jsRuntime, IHttpClientFactory httpClientFactory)
        {
            _jsRuntime = jsRuntime;
            _httpClientFactory = httpClientFactory;
        }

        // This method is called once at application startup
        public async Task InitializeAsync()
        {
            try
            {
                var settingsJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", LocalStorageKey);
                if (string.IsNullOrEmpty(settingsJson))
                {
                    // use default settings if nothing is stored
                    Settings = new AiSettings
                    {
                        ModelId = "deepseek-r1-distill-qwen-14b",
                        Endpoint = "http://localhost:5333/v1",
                        ApiKey = "NOTREQUIREDFORLOCALMODEL",
                    };
                }
                else
                {
                    Settings = JsonSerializer.Deserialize<AiSettings>(settingsJson) ?? new AiSettings();
                }
            }
            catch (JSException) { /* Local storage might not be available during pre-rendering */ }

            await BuildKernelAsync();
        }

        private async Task BuildKernelAsync()
        {
            // Reset state before attempting to build
            IsInitialized = false;
            Kernel = null;
            ChatCompletionService = null;

            if (string.IsNullOrWhiteSpace(Settings.ModelId) || string.IsNullOrWhiteSpace(Settings.Endpoint))
            {
                NotifyStateChanged();
                return;
            }

            try
            {
                var builder = Kernel.CreateBuilder();
                builder.Plugins.AddFromType<TimePlugin>();

                // Create a Blazor-safe HttpClient using the factory
                var httpClient = _httpClientFactory.CreateClient();
                // Set the base address for the HttpClient to the custom endpoint
                httpClient.BaseAddress = new Uri(Settings.Endpoint);

                // Use AddOpenAIChatCompletion and provide the configured HttpClient
                builder.AddOpenAIChatCompletion(
                    modelId: Settings.ModelId,
                    apiKey: Settings.ApiKey,
                    httpClient: httpClient
                );

                // Build the kernel with the specified configuration.
                Kernel = builder.Build();

                ChatCompletionService = Kernel.GetRequiredService<IChatCompletionService>();

                // If we get here, initialization was successful.
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                var m = ex.Message;
                // Catch any exceptions during build or service resolution.
                // This ensures the application remains in a stable, non-initialized state.
                IsInitialized = false;
                Kernel = null;
                ChatCompletionService = null;
            }
            NotifyStateChanged();
        }

        public async Task SaveSettingsAndReloadAsync(AiSettings newSettings)
        {
            Settings = newSettings;
            var settingsJson = JsonSerializer.Serialize(Settings);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", LocalStorageKey, settingsJson);
            await BuildKernelAsync();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }

    // Data class for AI settings
    public class AiSettings
    {
        public string ModelId { get; set; } = "deepseek-r1-distill-qwen-14b";
        public string Endpoint { get; set; } = "http://localhost:5333/v1"; 
        public string? ApiKey { get; set; } = "NOTREQUIREDFORLOCALMODEL";
    }

    // Plugin class remains the same
    public class TimePlugin
    {
        [KernelFunction, Description("Gets the current date and time.")]
        public string GetCurrentTime() => DateTime.Now.ToString("R");

        [KernelFunction, Description("Gets the current date.")]
        public string GetCurrentDate() => DateTime.Now.ToLongDateString();
    }
}
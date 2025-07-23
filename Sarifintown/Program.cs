using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MudBlazor;
using MudBlazor.Services;
using Sarifintown.Services;

namespace Sarifintown
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

            // Ensure IHttpClientFactory is registered
            builder.Services.AddHttpClient();

            builder.Services.AddScoped<JSInteropService>();

            builder.Services.AddSingleton<KernelService>();
            builder.Services.AddSingleton<IChatCompletionService>(
                sp => sp.GetRequiredService<IChatCompletionService>()
            );

            builder.Services.AddMudServices(config =>
            {
                config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;

                config.SnackbarConfiguration.PreventDuplicates = false;
                config.SnackbarConfiguration.NewestOnTop = false;
                config.SnackbarConfiguration.ShowCloseIcon = true;
                config.SnackbarConfiguration.VisibleStateDuration = 5000;
                config.SnackbarConfiguration.HideTransitionDuration = 500;
                config.SnackbarConfiguration.ShowTransitionDuration = 500;
                config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
            });

            builder.Services.AddSingleton<SarifFileService>();
            builder.Services.AddSingleton<LocalFilesService>();
            builder.Services.AddSingleton<SettingsService>();

            var host = builder.Build();

            // Initialize the service after the host is built but before it runs
            var kernelService = host.Services.GetRequiredService<KernelService>();

            await kernelService.InitializeAsync();

            await host.RunAsync();
        }
    }
}

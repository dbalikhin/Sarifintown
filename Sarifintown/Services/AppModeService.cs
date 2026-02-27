using Microsoft.AspNetCore.Components;

namespace Sarifintown.Services
{
    public enum AppMode
    {
        Standalone,
        McpHosted
    }

    public sealed class AppModeService
    {
        private readonly NavigationManager _navigationManager;

        public AppModeService(NavigationManager navigationManager)
        {
            _navigationManager = navigationManager;
        }

        /// <summary>
        /// Returns the currently active UI mode inferred from the browser location.
        /// </summary>
        public AppMode CurrentMode => IsMcpHostedRoute(_navigationManager.Uri) ? AppMode.McpHosted : AppMode.Standalone;

        public bool IsMcpHosted => CurrentMode == AppMode.McpHosted;

        /// <summary>
        /// Determines whether the supplied absolute URI belongs to an MCP-hosted route.
        /// </summary>
        public static bool IsMcpHostedRoute(string absoluteUri)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUri);

            var uri = new Uri(absoluteUri, UriKind.Absolute);
            return uri.AbsolutePath.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
        }
    }
}

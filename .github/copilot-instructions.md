# Copilot Instructions

## Project Guidelines
- User prefers fewer dependencies.
- Always use explicit, fully qualified version numbers for NuGet packages in .csproj files and avoid wildcards or floating versions.
- User prefers standard NUnit assertions over FluentAssertions because FluentAssertions is not free.
- When documenting MCP setup for this project, do not require DOTNET_ENVIRONMENT in configuration unless explicitly needed by project behavior.
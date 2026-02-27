using NUnit.Framework;

namespace Sarifintown.AgentEngine.Tests;

[TestFixture]
public class WorkspaceSarifDiscoveryTests
{
    private static readonly string[] WorkspaceEnvironmentVariables =
    [
        "PROJECT_ROOT",
        "WORKSPACE_FOLDER",
        "WORKSPACE_ROOT",
        "MCP_WORKSPACE_ROOT",
        "PWD"
    ];

    [Test]
    public void Discover_WhenSarifFolderExists_ReturnsSarifFilesOnly()
    {
        // Arrange
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var sarifDirectory = Path.Combine(workspaceRoot, ".sarif");
        Directory.CreateDirectory(sarifDirectory);

        var expectedSarif = Path.Combine(sarifDirectory, "result.sarif");
        var ignoredFile = Path.Combine(sarifDirectory, "notes.txt");
        File.WriteAllText(expectedSarif, "{}");
        File.WriteAllText(ignoredFile, "text");

        try
        {
            // Act
            var result = WorkspaceSarifDiscovery.Discover(workspaceRoot);

            // Assert
            Assert.That(result.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
            Assert.That(result.SarifFiles.Count, Is.EqualTo(1));
            Assert.That(result.SarifFiles[0], Is.EqualTo(expectedSarif));
        }
        finally
        {
            Directory.Delete(workspaceRoot, true);
        }
    }

    [Test]
    public void Discover_WhenSarifFolderMissing_ReturnsEmptyList()
    {
        // Arrange
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            // Act
            var result = WorkspaceSarifDiscovery.Discover(workspaceRoot);

            // Assert
            Assert.That(result.SarifFiles, Is.Empty);
        }
        finally
        {
            Directory.Delete(workspaceRoot, true);
        }
    }

    [Test]
    public void Discover_WhenProjectRootIsPlaceholder_UsesNextValidWorkspaceEnvironmentVariable()
    {
        // Arrange
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var sarifDirectory = Path.Combine(workspaceRoot, ".sarif");
        Directory.CreateDirectory(sarifDirectory);

        var filePath = Path.Combine(sarifDirectory, "scan.sarif");
        File.WriteAllText(filePath, "{}");

        var originalValues = CaptureEnvironmentVariables();
        Environment.SetEnvironmentVariable("PROJECT_ROOT", "{workspaceFolder}");
        Environment.SetEnvironmentVariable("WORKSPACE_FOLDER", workspaceRoot);

        try
        {
            // Act
            var result = WorkspaceSarifDiscovery.Discover();

            // Assert
            Assert.That(result.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
            Assert.That(result.SarifFiles.Count, Is.EqualTo(1));
        }
        finally
        {
            RestoreEnvironmentVariables(originalValues);
            Directory.Delete(workspaceRoot, true);
        }
    }

    [Test]
    public void Discover_WhenExplicitWorkspaceIsPlaceholder_UsesWorkspaceEnvironmentVariable()
    {
        // Arrange
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var sarifDirectory = Path.Combine(workspaceRoot, ".sarif");
        Directory.CreateDirectory(sarifDirectory);

        var filePath = Path.Combine(sarifDirectory, "scan.sarif");
        File.WriteAllText(filePath, "{}");

        var originalValues = CaptureEnvironmentVariables();
        Environment.SetEnvironmentVariable("PROJECT_ROOT", workspaceRoot);

        try
        {
            // Act
            var result = WorkspaceSarifDiscovery.Discover("${workspaceFolder}");

            // Assert
            Assert.That(result.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
            Assert.That(result.SarifFiles.Count, Is.EqualTo(1));
        }
        finally
        {
            RestoreEnvironmentVariables(originalValues);
            Directory.Delete(workspaceRoot, true);
        }
    }

    private static Dictionary<string, string?> CaptureEnvironmentVariables()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in WorkspaceEnvironmentVariables)
        {
            snapshot[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }

        return snapshot;
    }

    private static void RestoreEnvironmentVariables(IReadOnlyDictionary<string, string?> snapshot)
    {
        foreach (var item in snapshot)
        {
            Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }
}

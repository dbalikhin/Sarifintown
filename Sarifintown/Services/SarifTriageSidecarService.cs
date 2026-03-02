using Microsoft.JSInterop;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Text.Json;

namespace Sarifintown.Services
{
    public sealed class SarifTriageSidecarService
    {
        private const string SidecarRelativePath = ".sarif/suppressions.sidecar.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly IJSRuntime _jsRuntime;
        private readonly Dictionary<int, SarifTriageSidecar> _sidecarsByDirectoryId = new();

        public SarifTriageSidecarService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        /// <summary>
        /// Loads the sidecar suppression file for the selected directory into memory.
        /// </summary>
        public async Task PrimeDirectoryAsync(int directoryId, CancellationToken cancellationToken = default)
        {
            if (directoryId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(directoryId));
            }

            await _jsRuntime.InvokeVoidAsync("scriptLoader.ensure", cancellationToken, "/js/fileReader.js");
            var sidecarJson = await _jsRuntime.InvokeAsync<string>("fileSystemHelpers.readTextFile", cancellationToken, directoryId, SidecarRelativePath);

            if (string.IsNullOrWhiteSpace(sidecarJson))
            {
                _sidecarsByDirectoryId[directoryId] = new SarifTriageSidecar();
                return;
            }

            var parsed = JsonSerializer.Deserialize<SarifTriageSidecar>(sidecarJson, JsonOptions);
            _sidecarsByDirectoryId[directoryId] = parsed ?? new SarifTriageSidecar();
        }

        /// <summary>
        /// Applies loaded sidecar suppressions to SARIF results by stable identity matching.
        /// </summary>
        public void ApplySuppressions(SarifLog sarifLog, int directoryId)
        {
            ArgumentNullException.ThrowIfNull(sarifLog);

            if (!_sidecarsByDirectoryId.TryGetValue(directoryId, out var sidecar) || sidecar.Suppressions.Count == 0)
            {
                return;
            }

            var byIdentity = sidecar.Suppressions
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Identity) && entry.Suppression != null)
                .GroupBy(entry => entry.Identity, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            foreach (var result in (sarifLog.Runs ?? new List<Run>()).SelectMany(run => run.Results ?? Enumerable.Empty<Result>()))
            {
                var identity = SarifTriageIdentityHelper.BuildIdentity(result);
                if (!byIdentity.TryGetValue(identity, out var entries))
                {
                    continue;
                }

                result.Suppressions ??= new List<Suppression>();
                foreach (var entry in entries)
                {
                    if (!result.Suppressions.Any(existing =>
                        string.Equals(existing.Kind, entry.Suppression.Kind, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.Status, entry.Suppression.Status, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.Justification, entry.Suppression.Justification, StringComparison.Ordinal)))
                    {
                        result.Suppressions.Add(entry.Suppression);
                    }
                }
            }
        }

        /// <summary>
        /// Adds or updates suppression metadata in the sidecar file without changing the original SARIF file.
        /// </summary>
        public async Task UpsertSuppressionAsync(
            int directoryId,
            Result result,
            string justification,
            string status = "accepted",
            string kind = "external",
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (string.IsNullOrWhiteSpace(justification))
            {
                throw new ArgumentException("Suppression justification is required.", nameof(justification));
            }

            if (!_sidecarsByDirectoryId.ContainsKey(directoryId))
            {
                await PrimeDirectoryAsync(directoryId, cancellationToken);
            }

            var sidecar = _sidecarsByDirectoryId[directoryId];
            var identity = SarifTriageIdentityHelper.BuildIdentity(result);

            var firstLocation = result.Locations?.FirstOrDefault()?.PhysicalLocation;
            var existingIndex = sidecar.Suppressions.FindIndex(entry => string.Equals(entry.Identity, identity, StringComparison.Ordinal));

            var suppression = new Suppression
            {
                Kind = kind,
                Status = status,
                Justification = justification,
                Location = new Suppression.SuppressionLocation
                {
                    Uri = result.FilenamePath,
                    Region = firstLocation?.Region
                }
            };

            var updatedEntry = new SarifTriageSuppressionEntry
            {
                Identity = identity,
                RuleId = result.RuleId ?? string.Empty,
                Path = result.FilenamePath ?? firstLocation?.ArtifactLocation?.Uri ?? string.Empty,
                StartLine = firstLocation?.Region?.StartLine,
                UpdatedUtc = DateTime.UtcNow,
                Suppression = suppression
            };

            if (existingIndex >= 0)
            {
                sidecar.Suppressions[existingIndex] = updatedEntry;
            }
            else
            {
                sidecar.Suppressions.Add(updatedEntry);
            }

            result.Suppressions ??= new List<Suppression>();
            if (!result.Suppressions.Any(existing =>
                string.Equals(existing.Kind, suppression.Kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Status, suppression.Status, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Justification, suppression.Justification, StringComparison.Ordinal)))
            {
                result.Suppressions.Add(suppression);
            }

            var serialized = JsonSerializer.Serialize(sidecar, JsonOptions);
            await _jsRuntime.InvokeVoidAsync("fileSystemHelpers.writeTextFile", cancellationToken, directoryId, SidecarRelativePath, serialized);
        }
    }
}

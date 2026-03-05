using Sarifintown.Models;
using System.Security.Cryptography;
using System.Text;

namespace Sarifintown.Helpers
{
    public static class SarifTriageIdentityHelper
    {
        /// <summary>
        /// Builds a stable identity for a SARIF result by preferring SARIF fingerprints and falling back to location and message attributes.
        /// </summary>
        public static string BuildIdentity(Result result, string toolName = "")
        {
            ArgumentNullException.ThrowIfNull(result);
            var normalizedToolName = NormalizeToolName(toolName);

            var fingerprintSource = BuildFingerprintSource(result, normalizedToolName);
            if (string.IsNullOrWhiteSpace(fingerprintSource))
            {
                var location = result.Locations?.FirstOrDefault()?.PhysicalLocation;
                var fallbackPath = result.FilenamePath
                    ?? location?.ArtifactLocation?.Uri
                    ?? string.Empty;

                var region = location?.Region;
                fingerprintSource = string.Join('|',
                    normalizedToolName,
                    result.RuleId ?? string.Empty,
                    FileHelper.NormalizePath(fallbackPath),
                    region?.StartLine ?? 0,
                    region?.StartColumn ?? 0,
                    region?.EndLine ?? 0,
                    region?.EndColumn ?? 0,
                    result.Message?.Text ?? string.Empty);
            }

            return ComputeSha256Hex(fingerprintSource);
        }

        private static string BuildFingerprintSource(Result result, string normalizedToolName)
        {
            if (result.PartialFingerprints != null && result.PartialFingerprints.Count > 0)
            {
                return normalizedToolName + "|partial|" + string.Join('|', result.PartialFingerprints
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            }

            if (result.Fingerprints != null && result.Fingerprints.Count > 0)
            {
                return normalizedToolName + "|full|" + string.Join('|', result.Fingerprints
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            }

            return string.Empty;
        }

        private static string NormalizeToolName(string toolName)
        {
            return string.IsNullOrWhiteSpace(toolName)
                ? "unknown-tool"
                : toolName.Trim().ToLowerInvariant();
        }

        private static string ComputeSha256Hex(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

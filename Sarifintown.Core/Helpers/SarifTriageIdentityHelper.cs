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
        public static string BuildIdentity(Result result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var fingerprintSource = BuildFingerprintSource(result);
            if (string.IsNullOrWhiteSpace(fingerprintSource))
            {
                var location = result.Locations?.FirstOrDefault()?.PhysicalLocation;
                var fallbackPath = result.FilenamePath
                    ?? location?.ArtifactLocation?.Uri
                    ?? string.Empty;

                var region = location?.Region;
                fingerprintSource = string.Join('|',
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

        private static string BuildFingerprintSource(Result result)
        {
            if (result.PartialFingerprints != null && result.PartialFingerprints.Count > 0)
            {
                return "partial|" + string.Join('|', result.PartialFingerprints
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            }

            if (result.Fingerprints != null && result.Fingerprints.Count > 0)
            {
                return "full|" + string.Join('|', result.Fingerprints
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            }

            return string.Empty;
        }

        private static string ComputeSha256Hex(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

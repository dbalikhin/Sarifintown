using Sarifintown.Helpers;
using Sarifintown.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sarifintown.Services
{
    public class CodeSnippetService
    {
        private readonly Sarifintown.Core.IFileReader _fileReader;
        private readonly LocalFilesService _localFilesService;
        private readonly SettingsService _settingsService;
        private readonly Dictionary<string, string> _fileContentCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExtractedCodeSnippet> _snippetCache = new(StringComparer.Ordinal);

        public CodeSnippetService(
            Sarifintown.Core.IFileReader fileReader,
            LocalFilesService localFilesService,
            SettingsService settingsService)
        {
            _fileReader = fileReader;
            _localFilesService = localFilesService;
            _settingsService = settingsService;
        }

        /// <summary>
        /// Loads code snippets for all results in a run using batched UI updates and internal file/snippet caching.
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> AddCodeSnippetsToRunAsync(
            Run run,
            Action? onSnippetAdded = null,
            int batchSize = 25,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);

            if (!_localFilesService.AllDirectories.Any())
            {
                return (false, "No Source Code Folder Selected");
            }

            try
            {
                var processedInBatch = 0;

                foreach (var result in run.Results ?? new List<Result>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (result.Locations?.Any() != true)
                    {
                        continue;
                    }

                    if (result.IsSnippetLoaded)
                    {
                        continue;
                    }

                    var (success, errorMessage) = await EnsureCodeSnippetAsync(run, result, cancellationToken);
                    if (!success)
                    {
                        return (false, errorMessage);
                    }

                    processedInBatch++;
                    if (processedInBatch >= Math.Max(1, batchSize))
                    {
                        onSnippetAdded?.Invoke();
                        processedInBatch = 0;
                    }
                }

                onSnippetAdded?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return (false, "Code snippet loading was canceled.");
            }
            catch (InvalidOperationException ex)
            {
                return (false, ex.Message);
            }
            catch (IOException ex)
            {
                return (false, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return (false, ex.Message);
            }

            return (true, null);
        }

        /// <summary>
        /// Ensures a single result snippet is loaded and cached, enabling lazy and deep-link loading.
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> EnsureCodeSnippetAsync(
            Run run,
            Result result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(result);

            if (result.Locations?.Any() != true)
            {
                return (false, "Finding does not contain a physical location.");
            }

            if (result.IsSnippetLoaded)
            {
                return (true, null);
            }

            var location = result.Locations[0];
            var region = location?.PhysicalLocation?.Region;
            if (region == null)
            {
                return (false, "Finding location does not include region information.");
            }

            var fallbackPath = location?.PhysicalLocation?.ArtifactLocation?.Uri;
            var normalizedPath = FileHelper.NormalizePath(string.IsNullOrWhiteSpace(result.FilenamePath) ? fallbackPath : result.FilenamePath);

            string error;
            var (adjustedPath, matchedFolder) = FileHelper.AdjustPathToGrantedFolder(normalizedPath, _localFilesService.AllDirectories, out error);
            if (adjustedPath == null || matchedFolder == null)
            {
                return (false, error);
            }

            if (run.JSDirectoryId == 0)
            {
                run.JSDirectoryId = matchedFolder.Id;
            }

            result.FilenamePath = adjustedPath;

            if (string.IsNullOrWhiteSpace(result.ResultIdentity))
            {
                result.ResultIdentity = SarifTriageIdentityHelper.BuildIdentity(result);
            }

            var snippetCacheKey = string.IsNullOrWhiteSpace(result.ResultIdentity)
                ? $"{result.RuleId}|{result.FilenamePath}|{region.StartLine}:{region.StartColumn}-{region.EndLine}:{region.EndColumn}"
                : result.ResultIdentity;

            if (_snippetCache.TryGetValue(snippetCacheKey, out var cachedSnippet))
            {
                location.PhysicalLocation.ExtractedCodeSnippet = cachedSnippet;
                result.IsSnippetLoaded = true;
                return (true, null);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_fileContentCache.TryGetValue(adjustedPath, out var content))
            {
                content = await _fileReader.ReadFileAsync(adjustedPath);
                if (string.IsNullOrEmpty(content))
                {
                    return (false, $"Unable to read {result.FilenamePath}");
                }

                _fileContentCache[adjustedPath] = content;
            }

            var snippet = SnippetHelper.ExtractCodeSnippet(
                content,
                region.StartLine,
                region.StartColumn,
                region.EndLine,
                region.EndColumn,
                _settingsService.SurroundingLines);
            location.PhysicalLocation.ExtractedCodeSnippet = snippet;
            result.IsSnippetLoaded = snippet != null;

            if (snippet != null)
            {
                _snippetCache[snippetCacheKey] = snippet;
            }

            return (true, null);
        }

        /// <summary>
        /// Clears in-memory file and snippet caches.
        /// </summary>
        public void ClearCaches()
        {
            _fileContentCache.Clear();
            _snippetCache.Clear();
        }
    }
}
using Sarifintown.Helpers;
using Sarifintown.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sarifintown.Services
{
    public class CodeSnippetService
    {
        private readonly Sarifintown.Core.IFileReader _fileReader;
        private readonly LocalFilesService _localFilesService;

        public CodeSnippetService(Sarifintown.Core.IFileReader fileReader, LocalFilesService localFilesService)
        {
            _fileReader = fileReader;
            _localFilesService = localFilesService;
        }

        public async Task<(bool Success, string ErrorMessage)> AddCodeSnippetsToRunAsync(Run run, Action onSnippetAdded = null)
        {
            if (!_localFilesService.AllDirectories.Any())
            {
                return (false, "No Source Code Folder Selected");
            }

            try
            {
                foreach (var result in run.Results ?? new List<Result>())
                {
                    if (result.Locations?.Any() != true)
                    {
                        continue;
                    }

                    var fallbackPath = result.Locations[0]?.PhysicalLocation?.ArtifactLocation?.Uri;
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

                    var content = await _fileReader.ReadFileAsync(adjustedPath);
                    if (string.IsNullOrEmpty(content))
                    {
                        return (false, $"Unable to read {result.FilenamePath}");
                    }

                    var region = result.Locations[0].PhysicalLocation.Region;
                    var snippet = SnippetHelper.ExtractCodeSnippet(content, region.StartLine, region.StartColumn, region.EndLine, region.EndColumn);
                    result.Locations[0].PhysicalLocation.ExtractedCodeSnippet = snippet;

                    onSnippetAdded?.Invoke();
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            return (true, null);
        }
    }
}
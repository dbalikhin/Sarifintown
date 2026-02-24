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
        private readonly Sarifintown.Core.IFileReader _jsInteropService;
        private readonly LocalFilesService _localFilesService;

        public CodeSnippetService(Sarifintown.Core.IFileReader jsInteropService, LocalFilesService localFilesService)
        {
            _jsInteropService = jsInteropService;
            _localFilesService = localFilesService;
        }

        public async Task<(bool Success, string ErrorMessage)> AddCodeSnippetsToRunAsync(Run run, Action onSnippetAdded = null)
        {
            if (run.JSDirectoryId == 0 && !_localFilesService.AllDirectories.Any())
            {
                return (false, "No Source Code Folder Selected");
            }

            if (run.JSDirectoryId == 0)
            {
                try
                {
                    foreach (var result in run.Results)
                    {
                        if (result.Locations.Any())
                        {
                            var normalizedPath = FileHelper.NormalizePath(result.FilenamePath);

                            string error;
                            string content;
                            var (adjustedPath, matchedFolder) = FileHelper.AdjustPathToGrantedFolder(normalizedPath, _localFilesService.AllDirectories, out error);
                            if (adjustedPath == null || matchedFolder == null)
                            {
                                return (false, error);
                            }
                            else
                            {
                                // Assign the matched folder's Id to the run
                                run.JSDirectoryId = matchedFolder.Id;

                                // save adjusted path for furure references
                                result.FilenamePath = adjustedPath;

                                content = await _jsInteropService.ReadFileContentAsync(run.JSDirectoryId, adjustedPath);
                                if (string.IsNullOrEmpty(content))
                                {
                                    return (false, $"Unable to read {result.FilenamePath}");
                                }
                            }

                            var region = result.Locations[0].PhysicalLocation.Region;
                            var res = SnippetHelper.ExtractCodeSnippet(content, region.StartLine, region.StartColumn, region.EndLine, region.EndColumn);

                            result.Locations[0].PhysicalLocation.ExtractedCodeSnippet = res;

                            // Notify UI of changes after each snippet is added
                            onSnippetAdded?.Invoke();
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }

            return (true, null);
        }
    }
}
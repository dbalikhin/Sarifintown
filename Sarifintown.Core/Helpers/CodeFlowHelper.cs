using Sarifintown.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sarifintown.Helpers
{
    public static class CodeFlowHelper
    {
        public static List<CodeFlowData> PrepareCodeResults(Result result, int jsDirectoryId, IEnumerable<DirectoryPicker> allDirectories)
        {
            var codeFlowDataList = new List<CodeFlowData>();
            var fileRegionTracker = new Dictionary<string, List<CodeFlowData>>();

            if (jsDirectoryId == 0)
            {
                // 0 = User didn't select and grant access to the folder
                return codeFlowDataList;
            }

            if (result.CodeFlows != null && result.CodeFlows.Count > 0 && result.CodeFlows[0].ThreadFlows.Count > 0)
            {
                // Assume we will have a single set of locations
                foreach (var threadFlowLocation in result.CodeFlows[0].ThreadFlows[0].Locations)
                {
                    var location = threadFlowLocation.Location;
                    if (location?.PhysicalLocation != null)
                    {
                        var region = location.PhysicalLocation.Region;
                        if (region == null) 
                            continue;

                        var normalizedPathForPhysicalLocation = result.ParentRun != null
                            ? FileHelper.ResolveArtifactPath(location.PhysicalLocation.ArtifactLocation, result.ParentRun)
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(normalizedPathForPhysicalLocation))
                        {
                            normalizedPathForPhysicalLocation = FileHelper.NormalizePath(location.PhysicalLocation.ArtifactLocation.Uri);
                        }

                        string error;
                        var (adjustedPath, matchedFolder) = FileHelper.AdjustPathToGrantedFolder(normalizedPathForPhysicalLocation, allDirectories, out error);

                        if (error != null)
                        {
                            // handle error logic
                        }
                        string filePath = adjustedPath;

                        if (!fileRegionTracker.ContainsKey(filePath))
                        {
                            fileRegionTracker[filePath] = new List<CodeFlowData>();
                        }

                        var existingData = fileRegionTracker[filePath]
                            .FirstOrDefault(data => data.Region.StartLine == region.StartLine && data.Region.EndLine == region.EndLine);

                        if (existingData != null)
                        {
                            ExpandRegionColumns(existingData.Region, region);
                        }
                        else
                        {
                            var id = threadFlowLocation.Location.Id;

                            var newCodeFlowData = new CodeFlowData
                            {
                                Id = id,
                                Filename = filePath,
                                FilenameExt = Path.GetExtension(filePath).TrimStart('.'),
                                Region = new Region
                                {
                                    StartLine = region.StartLine,
                                    EndLine = region.EndLine,
                                    StartColumn = region.StartColumn,
                                    EndColumn = region.EndColumn
                                }
                            };

                            codeFlowDataList.Add(newCodeFlowData);

                            fileRegionTracker[filePath].Add(newCodeFlowData);
                        }
                    }
                }
            }

            return codeFlowDataList;
        }

        // method to expand the column range of an existing region
        private static void ExpandRegionColumns(Region existingRegion, Region newRegion)
        {
            existingRegion.StartColumn = Math.Min(existingRegion.StartColumn, newRegion.StartColumn);
            existingRegion.EndColumn = Math.Max(existingRegion.EndColumn, newRegion.EndColumn);
        }
    }
}

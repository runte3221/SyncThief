using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using SyncThief.Models;

namespace SyncThief.Services;

public class ExportProgressInfo
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
}

public class ExportResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int ExportedFileCount { get; set; }
    public long ExportedTotalSize { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class PmpPacker
{
    private readonly IPluginLog _log;

    public PmpPacker(IPluginLog log)
    {
        _log = log;
    }

    public async Task<ExportResult> PackToPmpAsync(
        string modName,
        string authorName,
        IEnumerable<ParsedResource> resources,
        string outputDirectory,
        IProgress<ExportProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ExportResult();

        try
        {
            if (string.IsNullOrWhiteSpace(modName))
            {
                modName = "SyncThief_Export";
            }

            // Sanitize filename
            var safeModName = string.Join("_", modName.Split(Path.GetInvalidFileNameChars()));
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var pmpPath = Path.Combine(outputDirectory, $"{safeModName}.pmp");
            // If file already exists, create with timestamp
            if (File.Exists(pmpPath))
            {
                pmpPath = Path.Combine(outputDirectory, $"{safeModName}_{DateTime.Now:yyyyMMdd_HHmmss}.pmp");
            }

            var distinctResources = new Dictionary<string, ParsedResource>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in resources)
            {
                if (!distinctResources.ContainsKey(r.GamePath) && File.Exists(r.DiskPath))
                {
                    distinctResources[r.GamePath] = r;
                }
            }

            if (distinctResources.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No valid files selected or found on disk.";
                return result;
            }

            var totalFiles = distinctResources.Count;
            int currentIndex = 0;
            long totalBytes = 0;

            await Task.Run(() =>
            {
                // Create temporary zip archive
                var tempPath = Path.GetTempFileName();
                try
                {
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, true))
                    {
                        // 1. Create meta.json
                        var meta = new
                        {
                            FileVersion = 3,
                            Name = modName,
                            Author = string.IsNullOrWhiteSpace(authorName) ? "SyncThief" : $"SyncThief ({authorName})",
                            Description = "",
                            Version = "1.0.0",
                            Website = (string?)null,
                            ModTags = Array.Empty<string>()
                        };
                        var metaEntry = archive.CreateEntry("meta.json", CompressionLevel.Fastest);
                        using (var metaStream = metaEntry.Open())
                        {
                            JsonSerializer.Serialize(metaStream, meta, new JsonSerializerOptions { WriteIndented = true });
                        }

                        // 2. Prepare default_mod.json
                        var filesDict = new Dictionary<string, string>();
                        foreach (var kvp in distinctResources)
                        {
                            filesDict[kvp.Key] = kvp.Key;
                        }

                        var defaultMod = new
                        {
                            Files = filesDict,
                            FileSwaps = new Dictionary<string, string>(),
                            Manipulations = Array.Empty<object>()
                        };

                        var defaultModEntry = archive.CreateEntry("default_mod.json", CompressionLevel.Fastest);
                        using (var modStream = defaultModEntry.Open())
                        {
                            JsonSerializer.Serialize(modStream, defaultMod, new JsonSerializerOptions { WriteIndented = true });
                        }

                        // 3. Add actual files
                        byte[] buffer = new byte[81920];
                        foreach (var kvp in distinctResources)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var gamePath = kvp.Key;
                            var diskPath = kvp.Value.DiskPath;

                            currentIndex++;
                            progress?.Report(new ExportProgressInfo
                            {
                                Current = currentIndex,
                                Total = totalFiles,
                                CurrentFile = Path.GetFileName(gamePath)
                            });

                            var entry = archive.CreateEntry(gamePath, CompressionLevel.Optimal);
                            using (var sourceStream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var targetStream = entry.Open())
                            {
                                int bytesRead;
                                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    targetStream.Write(buffer, 0, bytesRead);
                                    totalBytes += bytesRead;
                                }
                            }
                        }
                    }

                    // Move to destination
                    if (File.Exists(pmpPath))
                    {
                        File.Delete(pmpPath);
                    }
                    File.Move(tempPath, pmpPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }, cancellationToken);

            result.Success = true;
            result.OutputPath = pmpPath;
            result.ExportedFileCount = totalFiles;
            result.ExportedTotalSize = totalBytes;
            _log.Information($"Successfully exported .pmp to: {pmpPath} ({totalFiles} files, {totalBytes} bytes)");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Export was canceled by user.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to pack .pmp");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}

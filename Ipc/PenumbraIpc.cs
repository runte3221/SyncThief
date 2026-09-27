using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api;
using Penumbra.Api.IpcSubscribers;

namespace SyncThief.Ipc;

public class PenumbraIpc
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;

    private readonly ApiVersion _apiVersion;
    private readonly GetGameObjectResourceTrees _getResourceTrees;
    private readonly GetPlayerResourceTrees _getPlayerResourceTrees;
    private readonly GetModDirectory _getModDirectory;

    public PenumbraIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _log = log;

        _apiVersion = new ApiVersion(pluginInterface);
        _getResourceTrees = new GetGameObjectResourceTrees(pluginInterface);
        _getPlayerResourceTrees = new GetPlayerResourceTrees(pluginInterface);
        _getModDirectory = new GetModDirectory(pluginInterface);
    }

    public bool IsAvailable()
    {
        try
        {
            var version = _apiVersion.Invoke();
            return version.Breaking >= 5;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Penumbra IPC is not available.");
            return false;
        }
    }

    public string GetModDirectoryPath()
    {
        try
        {
            return _getModDirectory.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get Penumbra ModDirectory.");
            return string.Empty;
        }
    }

    public IEnumerable<ResourceTreeDto>? GetTreesForObject(ushort objectIndex)
    {
        try
        {
            var trees = _getResourceTrees.Invoke(true, [objectIndex]);
            return trees?.Where(t => t != null)!;
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Failed to get resource trees for object index {objectIndex}.");
            return null;
        }
    }

    public IEnumerable<ResourceTreeDto>? GetTreesForPlayer()
    {
        try
        {
            var dict = _getPlayerResourceTrees.Invoke(true);
            return dict?.Values;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get resource trees for player.");
            return null;
        }
    }
}

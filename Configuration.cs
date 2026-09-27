using System;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace SyncThief;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string ExportDirectory { get; set; } = string.Empty;
    public bool AutoOpenExportFolder { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            ExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(ExportDirectory) || !Directory.Exists(ExportDirectory))
            {
                ExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }
    }

    public void Save()
    {
        _pluginInterface?.SavePluginConfig(this);
    }
}

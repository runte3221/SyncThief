using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SyncThief.Ipc;
using SyncThief.Services;
using SyncThief.UI;

namespace SyncThief;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "SyncThief";
    private const string CommandName = "/syncthief";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem { get; init; } = new("SyncThief");

    public PenumbraIpc PenumbraIpc { get; init; }
    public ResourceScanner Scanner { get; init; }
    public PmpPacker Packer { get; init; }
    public MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        PenumbraIpc = new PenumbraIpc(PluginInterface, Log);
        Scanner = new ResourceScanner(ObjectTable, TargetManager, PenumbraIpc, Log);
        Packer = new PmpPacker(Log);
        MainWindow = new MainWindow(Configuration, Scanner, Packer, Log);

        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open SyncThief window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void ToggleMainUI()
    {
        MainWindow.IsOpen = !MainWindow.IsOpen;
    }
}

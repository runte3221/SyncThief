using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using SyncThief.Models;
using SyncThief.Services;

namespace SyncThief.UI;

public class MainWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly ResourceScanner _scanner;
    private readonly PmpPacker _packer;
    private readonly IPluginLog _log;

    private List<PlayerSyncData> _players = [];
    private int _selectedPlayerIndex = -1;
    private PlayerSyncData? _selectedPlayer => _selectedPlayerIndex >= 0 && _selectedPlayerIndex < _players.Count ? _players[_selectedPlayerIndex] : null;

    private string _modNameInput = string.Empty;
    private string _exportDirInput = string.Empty;

    // Export progress state
    private bool _isExporting;
    private float _exportProgress;
    private string _exportCurrentFileName = string.Empty;
    private CancellationTokenSource? _exportCts;
    private ExportResult? _lastExportResult;

    public MainWindow(
        Configuration config,
        ResourceScanner scanner,
        PmpPacker packer,
        IPluginLog log) : base("SyncThief###SyncThiefMainWindow", ImGuiWindowFlags.None)
    {
        _config = config;
        _scanner = scanner;
        _packer = packer;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 450),
            MaximumSize = new Vector2(1200, 1000)
        };

        _exportDirInput = _config.ExportDirectory;
    }

    public void Dispose()
    {
        _exportCts?.Cancel();
        _exportCts?.Dispose();
    }

    public override void OnOpen()
    {
        RefreshPlayers();
    }

    public void RefreshPlayers()
    {
        _players = _scanner.ScanPlayers();
        if (_players.Count > 0)
        {
            // Default to target player if available
            _selectedPlayerIndex = 0;
            _scanner.PopulatePlayerResources(_players[_selectedPlayerIndex]);
            UpdateDefaultModName();
        }
        else
        {
            _selectedPlayerIndex = -1;
        }
    }

    private void UpdateDefaultModName()
    {
        if (_selectedPlayer != null)
        {
            var cleanName = string.Join("_", _selectedPlayer.Name.Split(Path.GetInvalidFileNameChars()));
            _modNameInput = $"SyncThief_{cleanName}";
        }
        else
        {
            _modNameInput = "SyncThief_Export";
        }
    }

    public override void Draw()
    {
        DrawPlayerSelection();
        ImGui.Separator();

        if (_selectedPlayer == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "周囲に他プレイヤーが見つかりませんでした。「プレイヤー再スキャン」を押してください。");
            return;
        }

        DrawSlotList();
        ImGui.Separator();

        DrawExportSettings();
    }

    private void DrawPlayerSelection()
    {
        ImGui.BeginGroup();
        if (ImGui.Button("プレイヤー再スキャン##RefreshBtn"))
        {
            RefreshPlayers();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);

        var playerNames = _players.Select(p => $"{(p.IsTarget ? "[Target] " : "")}{p.Name} @ {p.World} ({p.TotalCustomFileCount} files)").ToArray();
        if (playerNames.Length > 0 && ImGui.Combo("対象プレイヤー##PlayerCombo", ref _selectedPlayerIndex, playerNames, playerNames.Length))
        {
            if (_selectedPlayer != null)
            {
                _scanner.PopulatePlayerResources(_selectedPlayer);
                UpdateDefaultModName();
            }
        }

        if (_selectedPlayer != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 1f, 1f),
                $"選択中: {_selectedPlayer.Name} ({_selectedPlayer.World}) | 同期MODファイル: {_selectedPlayer.TotalCustomFileCount} 個 ({FormatSize(_selectedPlayer.TotalCustomSize)})");
        }
        ImGui.EndGroup();
    }

    private void DrawSlotList()
    {
        if (_selectedPlayer == null) return;

        ImGui.TextUnformatted("エクスポートする部位を選択してください:");

        ImGui.SameLine();
        if (ImGui.SmallButton("すべて選択##SelectAll"))
        {
            foreach (var g in _selectedPlayer.SlotGroups) g.IsSelected = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("すべて解除##DeselectAll"))
        {
            foreach (var g in _selectedPlayer.SlotGroups) g.IsSelected = false;
        }

        if (_selectedPlayer.SlotGroups.Count == 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "このキャラクターには現在同期されているMODファイルが見つかりませんでした。");
            return;
        }

        var listHeight = 220 * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginChild("SlotGroupChild##Slots", new Vector2(-1, listHeight), true))
        {
            for (int i = 0; i < _selectedPlayer.SlotGroups.Count; i++)
            {
                var group = _selectedPlayer.SlotGroups[i];
                var isSelected = group.IsSelected;

                if (ImGui.Checkbox($"##slot_{i}", ref isSelected))
                {
                    group.IsSelected = isSelected;
                }

                ImGui.SameLine();
                var treeOpen = ImGui.TreeNodeEx($"##tree_{i}", ImGuiTreeNodeFlags.SpanAvailWidth,
                    $"{group.DisplayName} ({group.FileCount} ファイル - {FormatSize(group.TotalSize)})");

                if (treeOpen)
                {
                    ImGui.Indent();
                    foreach (var res in group.Resources)
                    {
                        ImGui.TextUnformatted($"{Path.GetFileName(res.GamePath)} ({FormatSize(res.FileSize)})");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"ゲームパス: {res.GamePath}\n実体パス: {res.DiskPath}");
                        }
                    }
                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawExportSettings()
    {
        ImGui.TextUnformatted("エクスポート設定:");

        ImGui.SetNextItemWidth(350 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("Mod 名##ModNameInput", ref _modNameInput, 100);

        ImGui.SetNextItemWidth(350 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("出力先フォルダ##ExportDirInput", ref _exportDirInput, 260);
        ImGui.SameLine();
        if (ImGui.Button("参照...##BrowseDirBtn"))
        {
            OpenFolderDialog();
        }

        var autoOpen = _config.AutoOpenExportFolder;
        if (ImGui.Checkbox("エクスポート完了後にフォルダを開く##AutoOpen", ref autoOpen))
        {
            _config.AutoOpenExportFolder = autoOpen;
            _config.Save();
        }

        // Calculate selected count & size
        int selectedCount = 0;
        long selectedSize = 0;
        if (_selectedPlayer != null)
        {
            foreach (var g in _selectedPlayer.SlotGroups.Where(g => g.IsSelected))
            {
                selectedCount += g.FileCount;
                selectedSize += g.TotalSize;
            }
        }

        ImGui.Spacing();

        if (_isExporting)
        {
            ImGui.ProgressBar(_exportProgress, new Vector2(-1, 24 * ImGuiHelpers.GlobalScale),
                $"出力中... ({Math.Round(_exportProgress * 100)}%) - {_exportCurrentFileName}");

            if (ImGui.Button("キャンセル##CancelExportBtn"))
            {
                _exportCts?.Cancel();
            }
        }
        else
        {
            ImGui.BeginDisabled(selectedCount == 0 || string.IsNullOrWhiteSpace(_exportDirInput));
            var buttonText = selectedCount > 0
                ? $"選択した部位を .pmp に出力 ({selectedCount} ファイル - {FormatSize(selectedSize)})"
                : "部位を選択してください";

            if (ImGui.Button(buttonText, new Vector2(-1, 32 * ImGuiHelpers.GlobalScale)))
            {
                StartExport();
            }
            ImGui.EndDisabled();
        }

        // Result message
        if (_lastExportResult != null)
        {
            ImGui.Spacing();
            if (_lastExportResult.Success)
            {
                ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f),
                    $"出力完了: {Path.GetFileName(_lastExportResult.OutputPath)} ({_lastExportResult.ExportedFileCount} ファイル, {FormatSize(_lastExportResult.ExportedTotalSize)})");

                if (ImGui.Button("保存先フォルダを開く##OpenExportFolder"))
                {
                    OpenFileInExplorer(_lastExportResult.OutputPath);
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f),
                    $"出力失敗: {_lastExportResult.ErrorMessage}");
            }
        }
    }

    private void StartExport()
    {
        if (_selectedPlayer == null || _isExporting) return;

        var selectedResources = _selectedPlayer.SlotGroups
            .Where(g => g.IsSelected)
            .SelectMany(g => g.Resources)
            .ToList();

        if (selectedResources.Count == 0) return;

        _config.ExportDirectory = _exportDirInput;
        _config.Save();

        _isExporting = true;
        _exportProgress = 0f;
        _exportCurrentFileName = string.Empty;
        _lastExportResult = null;
        _exportCts = new CancellationTokenSource();

        var modName = _modNameInput;
        var playerName = _selectedPlayer.Name;
        var exportDir = _exportDirInput;
        var autoOpen = _config.AutoOpenExportFolder;

        var progress = new Progress<ExportProgressInfo>(p =>
        {
            _exportProgress = p.Total > 0 ? (float)p.Current / p.Total : 0f;
            _exportCurrentFileName = p.CurrentFile;
        });

        Task.Run(async () =>
        {
            var result = await _packer.PackToPmpAsync(
                modName,
                playerName,
                selectedResources,
                exportDir,
                progress,
                _exportCts.Token);

            _lastExportResult = result;
            _isExporting = false;

            if (result.Success && autoOpen)
            {
                OpenFileInExplorer(result.OutputPath);
            }
        });
    }

    private void OpenFolderDialog()
    {
        var thread = new Thread(() =>
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "SyncThief - .pmp 出力先フォルダを選択";
            if (Directory.Exists(_exportDirInput))
            {
                dialog.SelectedPath = _exportDirInput;
            }

            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _exportDirInput = dialog.SelectedPath;
                _config.ExportDirectory = _exportDirInput;
                _config.Save();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void OpenFileInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to open explorer.");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024f * 1024f):F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024f:F2} KB";
        return $"{bytes} B";
    }
}

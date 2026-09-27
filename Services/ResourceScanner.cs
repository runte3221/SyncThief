using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Penumbra.Api;
using SyncThief.Ipc;
using SyncThief.Models;

namespace SyncThief.Services;

public class ResourceScanner
{
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly PenumbraIpc _penumbraIpc;
    private readonly IPluginLog _log;

    public ResourceScanner(
        IObjectTable objectTable,
        ITargetManager targetManager,
        PenumbraIpc penumbraIpc,
        IPluginLog log)
    {
        _objectTable = objectTable;
        _targetManager = targetManager;
        _penumbraIpc = penumbraIpc;
        _log = log;
    }

    public List<PlayerSyncData> ScanPlayers()
    {
        var result = new List<PlayerSyncData>();
        var target = _targetManager.Target as IPlayerCharacter;

        // Collect all PlayerCharacters in the ObjectTable
        foreach (var obj in _objectTable)
        {
            if (obj is IPlayerCharacter pc)
            {
                var isTarget = target != null && pc.EntityId == target.EntityId;
                var data = new PlayerSyncData
                {
                    Name = pc.Name.TextValue,
                    World = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty,
                    ObjectIndex = pc.ObjectIndex,
                    EntityId = pc.EntityId,
                    IsTarget = isTarget,
                };

                // Place target at the beginning
                if (isTarget)
                {
                    result.Insert(0, data);
                }
                else
                {
                    result.Add(data);
                }
            }
        }

        return result;
    }

    public void PopulatePlayerResources(PlayerSyncData player)
    {
        player.SlotGroups.Clear();

        if (!_penumbraIpc.IsAvailable())
        {
            _log.Warning("Penumbra IPC is not available.");
            return;
        }

        var trees = _penumbraIpc.GetTreesForObject((ushort)player.ObjectIndex);
        if (trees == null || trees.Length == 0)
        {
            _log.Debug($"No resource trees returned for {player.Name} (index: {player.ObjectIndex})");
            return;
        }

        // Initialize default slot groups
        var groups = new Dictionary<SlotCategory, SlotGroup>
        {
            { SlotCategory.Head, new SlotGroup { Category = SlotCategory.Head, DisplayName = "頭防具 (Head)" } },
            { SlotCategory.Body, new SlotGroup { Category = SlotCategory.Body, DisplayName = "胴防具 (Body/Top)" } },
            { SlotCategory.Hands, new SlotGroup { Category = SlotCategory.Hands, DisplayName = "手防具 (Hands)" } },
            { SlotCategory.Legs, new SlotGroup { Category = SlotCategory.Legs, DisplayName = "脚防具 (Legs)" } },
            { SlotCategory.Feet, new SlotGroup { Category = SlotCategory.Feet, DisplayName = "足防具 (Feet)" } },
            { SlotCategory.Ears, new SlotGroup { Category = SlotCategory.Ears, DisplayName = "耳アクセ (Ears)" } },
            { SlotCategory.Neck, new SlotGroup { Category = SlotCategory.Neck, DisplayName = "首アクセ (Neck)" } },
            { SlotCategory.Wrists, new SlotGroup { Category = SlotCategory.Wrists, DisplayName = "腕アクセ (Wrists)" } },
            { SlotCategory.RightRing, new SlotGroup { Category = SlotCategory.RightRing, DisplayName = "右指アクセ (Right Ring)" } },
            { SlotCategory.LeftRing, new SlotGroup { Category = SlotCategory.LeftRing, DisplayName = "左指アクセ (Left Ring)" } },
            { SlotCategory.Hair, new SlotGroup { Category = SlotCategory.Hair, DisplayName = "髪型 (Hair)" } },
            { SlotCategory.Face, new SlotGroup { Category = SlotCategory.Face, DisplayName = "顔 / メイク (Face)" } },
            { SlotCategory.BodyCustom, new SlotGroup { Category = SlotCategory.BodyCustom, DisplayName = "体型 / 肌 (Body/Skin)" } },
            { SlotCategory.Tail, new SlotGroup { Category = SlotCategory.Tail, DisplayName = "尻尾 (Tail)" } },
            { SlotCategory.Weapon, new SlotGroup { Category = SlotCategory.Weapon, DisplayName = "武器 (Weapon)" } },
            { SlotCategory.Other, new SlotGroup { Category = SlotCategory.Other, DisplayName = "その他 (Other)" } },
        };

        var visitedActualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedGamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tree in trees)
        {
            if (tree.Nodes == null) continue;

            foreach (var rootNode in tree.Nodes)
            {
                var inheritedCategory = DetermineCategoryFromNode(rootNode);
                TraverseNode(rootNode, inheritedCategory, groups, visitedActualPaths, visitedGamePaths);
            }
        }

        // Only add non-empty groups
        foreach (var kvp in groups)
        {
            if (kvp.Value.FileCount > 0)
            {
                player.SlotGroups.Add(kvp.Value);
            }
        }
    }

    private void TraverseNode(
        ResourceNodeDto node,
        SlotCategory currentCategory,
        Dictionary<SlotCategory, SlotGroup> groups,
        HashSet<string> visitedActualPaths,
        HashSet<string> visitedGamePaths)
    {
        var category = DetermineCategoryFromNode(node, currentCategory);

        if (!string.IsNullOrEmpty(node.ActualPath) && !string.IsNullOrEmpty(node.GamePath))
        {
            // Check if actual path is a valid existing disk file
            if (File.Exists(node.ActualPath))
            {
                var ext = Path.GetExtension(node.ActualPath).ToLowerInvariant();
                // Skip game .dat archive files if any
                if (ext != ".dat")
                {
                    // Avoid duplicate files in the same game path
                    if (!visitedGamePaths.Contains(node.GamePath))
                    {
                        visitedGamePaths.Add(node.GamePath);
                        visitedActualPaths.Add(node.ActualPath);

                        long size = 0;
                        try
                        {
                            var fi = new FileInfo(node.ActualPath);
                            size = fi.Length;
                        }
                        catch { }

                        var parsed = new ParsedResource
                        {
                            GamePath = node.GamePath,
                            DiskPath = node.ActualPath,
                            ResourceType = node.Type.ToString(),
                            FileSize = size,
                        };

                        groups[category].Resources.Add(parsed);
                    }
                }
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                TraverseNode(child, category, groups, visitedActualPaths, visitedGamePaths);
            }
        }
    }

    private static SlotCategory DetermineCategoryFromNode(ResourceNodeDto node, SlotCategory fallback = SlotCategory.Other)
    {
        var name = node.Name ?? string.Empty;
        var gamePath = (node.GamePath ?? string.Empty).ToLowerInvariant();

        // Check node name first
        if (name.Contains("Head", StringComparison.OrdinalIgnoreCase) || name.Contains("頭", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Head;
        if (name.Contains("Body", StringComparison.OrdinalIgnoreCase) || name.Contains("胴", StringComparison.OrdinalIgnoreCase) || name.Contains("Top", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Body;
        if (name.Contains("Hands", StringComparison.OrdinalIgnoreCase) || name.Contains("手", StringComparison.OrdinalIgnoreCase) || name.Contains("Gloves", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Hands;
        if (name.Contains("Legs", StringComparison.OrdinalIgnoreCase) || name.Contains("脚", StringComparison.OrdinalIgnoreCase) || name.Contains("Pants", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Legs;
        if (name.Contains("Feet", StringComparison.OrdinalIgnoreCase) || name.Contains("足", StringComparison.OrdinalIgnoreCase) || name.Contains("Shoes", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Feet;
        if (name.Contains("Ear", StringComparison.OrdinalIgnoreCase) || name.Contains("耳", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Ears;
        if (name.Contains("Neck", StringComparison.OrdinalIgnoreCase) || name.Contains("首", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Neck;
        if (name.Contains("Wrist", StringComparison.OrdinalIgnoreCase) || name.Contains("腕", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Wrists;
        if (name.Contains("Right Ring", StringComparison.OrdinalIgnoreCase) || name.Contains("右指", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.RightRing;
        if (name.Contains("Left Ring", StringComparison.OrdinalIgnoreCase) || name.Contains("左指", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.LeftRing;
        if (name.Contains("Hair", StringComparison.OrdinalIgnoreCase) || name.Contains("髪", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Hair;
        if (name.Contains("Face", StringComparison.OrdinalIgnoreCase) || name.Contains("顔", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Face;
        if (name.Contains("Tail", StringComparison.OrdinalIgnoreCase) || name.Contains("尻尾", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Tail;
        if (name.Contains("Weapon", StringComparison.OrdinalIgnoreCase) || name.Contains("武器", StringComparison.OrdinalIgnoreCase) || name.Contains("Main Hand", StringComparison.OrdinalIgnoreCase) || name.Contains("Off Hand", StringComparison.OrdinalIgnoreCase))
            return SlotCategory.Weapon;

        // Check game path pattern
        if (gamePath.Contains("chara/equipment/e"))
        {
            if (gamePath.Contains("_met_")) return SlotCategory.Head;
            if (gamePath.Contains("_top_")) return SlotCategory.Body;
            if (gamePath.Contains("_glv_")) return SlotCategory.Hands;
            if (gamePath.Contains("_dwn_")) return SlotCategory.Legs;
            if (gamePath.Contains("_sho_")) return SlotCategory.Feet;
            return SlotCategory.Body;
        }

        if (gamePath.Contains("chara/accessory/a"))
        {
            if (gamePath.Contains("_ear_")) return SlotCategory.Ears;
            if (gamePath.Contains("_nek_")) return SlotCategory.Neck;
            if (gamePath.Contains("_wrs_")) return SlotCategory.Wrists;
            if (gamePath.Contains("_rir_")) return SlotCategory.RightRing;
            if (gamePath.Contains("_ril_")) return SlotCategory.LeftRing;
            return SlotCategory.Ears;
        }

        if (gamePath.Contains("chara/human/c") && gamePath.Contains("/obj/hair/"))
            return SlotCategory.Hair;
        if (gamePath.Contains("chara/human/c") && gamePath.Contains("/obj/face/"))
            return SlotCategory.Face;
        if (gamePath.Contains("chara/human/c") && gamePath.Contains("/obj/body/"))
            return SlotCategory.BodyCustom;
        if (gamePath.Contains("chara/human/c") && gamePath.Contains("/obj/tail/"))
            return SlotCategory.Tail;
        if (gamePath.Contains("chara/weapon/w"))
            return SlotCategory.Weapon;

        return fallback;
    }
}

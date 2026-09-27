using System;
using System.Collections.Generic;

namespace SyncThief.Models;

public enum SlotCategory
{
    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    RightRing,
    LeftRing,
    Hair,
    Face,
    BodyCustom,
    Tail,
    Weapon,
    Other
}

public class ParsedResource
{
    public string GamePath { get; set; } = string.Empty;
    public string DiskPath { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public class SlotGroup
{
    public SlotCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public List<ParsedResource> Resources { get; } = [];

    public int FileCount => Resources.Count;
    public long TotalSize
    {
        get
        {
            long size = 0;
            foreach (var r in Resources)
            {
                size += r.FileSize;
            }
            return size;
        }
    }
}

public class PlayerSyncData
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public int ObjectIndex { get; set; }
    public ulong EntityId { get; set; }
    public bool IsTarget { get; set; }

    public List<SlotGroup> SlotGroups { get; } = [];

    public int TotalCustomFileCount
    {
        get
        {
            int count = 0;
            foreach (var g in SlotGroups)
            {
                count += g.FileCount;
            }
            return count;
        }
    }

    public long TotalCustomSize
    {
        get
        {
            long size = 0;
            foreach (var g in SlotGroups)
            {
                size += g.TotalSize;
            }
            return size;
        }
    }
}

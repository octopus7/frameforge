using System;
using System.Collections.Generic;

namespace FrameForge;

public sealed class FrameProjectDocument
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "FrameForge";
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public string AssetsRoot { get; set; } = string.Empty;
    public FrameProjectSettings Settings { get; set; } = new();
    public List<FrameProjectFrameEntry> Frames { get; set; } = [];
}

public sealed class FrameProjectSettings
{
    public bool IsLoopEnabled { get; set; }
    public int SelectedFrameIndex { get; set; } = -1;
}

public sealed class FrameProjectFrameEntry
{
    public string Name { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
}

public sealed class FrameProjectLoadResult
{
    public FrameProjectLoadResult(IReadOnlyList<AnimationFrame> frames, bool isLoopEnabled, int selectedFrameIndex, int missingFrameCount)
    {
        Frames = frames;
        IsLoopEnabled = isLoopEnabled;
        SelectedFrameIndex = selectedFrameIndex;
        MissingFrameCount = missingFrameCount;
    }

    public IReadOnlyList<AnimationFrame> Frames { get; }
    public bool IsLoopEnabled { get; }
    public int SelectedFrameIndex { get; }
    public int MissingFrameCount { get; }
}

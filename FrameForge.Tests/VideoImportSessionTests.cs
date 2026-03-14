using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FrameForge.Tests;

public sealed class VideoImportSessionTests
{
    [Fact]
    public void ControlToggle_OnActiveFrame_FallsBackToNearestSelectedFrame()
    {
        var session = CreateSession(frameCount: 4);
        session.SetActiveFrameIndex(1);

        session.ApplySelectionInteraction(1, ModifierKeys.Control, [0, 2]);

        Assert.Equal(0, session.ActiveFrameIndex);
    }

    [Fact]
    public void ShiftSelection_KeepsAnchorAndMovesActiveFrame()
    {
        var session = CreateSession(frameCount: 5);
        session.ApplySelectionInteraction(1, ModifierKeys.None, [1]);

        session.ApplySelectionInteraction(4, ModifierKeys.Shift, [1, 2, 3, 4]);

        Assert.Equal(1, session.SelectionAnchorIndex);
        Assert.Equal(4, session.ActiveFrameIndex);
    }

    [Fact]
    public void RemoveFrames_ReindexesNamesAndChoosesNextActiveFrame()
    {
        var session = CreateSession(frameCount: 4);
        session.SetActiveFrameIndex(1);

        var removed = session.RemoveFrames([1, 2]);

        Assert.True(removed);
        Assert.Equal(2, session.Frames.Count);
        Assert.Equal(1, session.ActiveFrameIndex);
        Assert.Equal(["프레임 1", "프레임 2"], session.Frames.Select(frame => frame.DisplayName).ToArray());
        Assert.Equal(["sample_0001", "sample_0002"], session.Frames.Select(frame => frame.OutputName).ToArray());
    }

    [Fact]
    public void SyncActiveFrame_UsesPreferredIndexForKeyboardNavigation()
    {
        var session = CreateSession(frameCount: 4);
        session.SetActiveFrameIndex(0);

        session.SyncActiveFrame([2], preferredIndex: 2);

        Assert.Equal(2, session.ActiveFrameIndex);
    }

    [Fact]
    public void CreateResult_UsesInternalOutputNames()
    {
        var session = CreateSession(frameCount: 2);

        var result = session.CreateResult();

        Assert.Equal(["sample_0001", "sample_0002"], result.Frames.Select(frame => frame.Name).ToArray());
    }

    [Fact]
    public void GetInvertedSelection_ReturnsUnselectedFrameIndices()
    {
        var session = CreateSession(frameCount: 5);

        var invertedSelection = session.GetInvertedSelection([1, 3]);

        Assert.Equal([0, 2, 4], invertedSelection);
    }

    [Fact]
    public void RemoveFrames_UsingUncheckedFrameIndices_RemovesOnlyUncheckedFrames()
    {
        var session = CreateSession(frameCount: 4);
        session.SetActiveFrameIndex(2);
        session.Frames[1].IsChecked = false;
        session.Frames[3].IsChecked = false;

        var removed = session.RemoveFrames(session.GetUncheckedFrameIndices());

        Assert.True(removed);
        Assert.Equal(2, session.Frames.Count);
        Assert.Equal(["프레임 1", "프레임 2"], session.Frames.Select(frame => frame.DisplayName).ToArray());
        Assert.Equal(["sample_0001", "sample_0002"], session.Frames.Select(frame => frame.OutputName).ToArray());
        Assert.All(session.Frames, frame => Assert.True(frame.IsChecked));
    }

    [Fact]
    public void SetCheckedState_AppliesTargetStateToAllSelectedFrames()
    {
        var session = CreateSession(frameCount: 5);
        session.Frames[1].IsChecked = false;

        session.SetCheckedState([1, 2, 3], true);

        Assert.True(session.Frames[1].IsChecked);
        Assert.True(session.Frames[2].IsChecked);
        Assert.True(session.Frames[3].IsChecked);
        Assert.True(session.Frames[0].IsChecked);
        Assert.True(session.Frames[4].IsChecked);
    }

    [Fact]
    public void CropSelection_RequiresMinimumSize()
    {
        var session = CreateSession(frameCount: 2);

        session.SetCropSelection(new Rect(0, 0, 1, 1));
        Assert.False(session.HasCropSelection);

        session.SetCropSelection(new Rect(0, 0, 6, 4));
        Assert.True(session.HasCropSelection);
    }

    [Fact]
    public void ApplyCrop_CropsAllFramesAndClearsSelection()
    {
        var session = CreateSession(frameCount: 3, width: 6, height: 5);
        session.SetCropSelection(new Rect(1, 1, 3, 2));

        var applied = session.ApplyCrop(new Int32Rect(1, 1, 3, 2));

        Assert.True(applied);
        Assert.False(session.HasCropSelection);
        Assert.All(session.Frames, frame =>
        {
            Assert.Equal(3, frame.Image.PixelWidth);
            Assert.Equal(2, frame.Image.PixelHeight);
        });
    }

    private static VideoImportSession CreateSession(int frameCount, int width = 8, int height = 6)
    {
        var session = new VideoImportSession();
        session.BeginLoading(@"C:\temp\sample.mp4");
        session.CompleteLoading(
            Enumerable.Range(0, frameCount)
                .Select(index => new VideoCapturedFrame(index, TimeSpan.FromMilliseconds(index * 100), CreateBitmap(width, height)))
                .ToArray());
        return session;
    }

    private static BitmapSource CreateBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Colors.White.B;
            pixels[i + 1] = Colors.White.G;
            pixels[i + 2] = Colors.White.R;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}

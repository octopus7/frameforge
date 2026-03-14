using System;
using System.IO;
using System.Windows;

namespace FrameForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        VideoDecoderRuntime.Configure();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (TryGetStartupProjectPath(e.Args, out var startupProjectPath))
        {
            mainWindow.OpenProjectFromPath(startupProjectPath, confirmUnsavedChanges: false);
        }
    }

    private static bool TryGetStartupProjectPath(string[] args, out string projectPath)
    {
        projectPath = string.Empty;

        if (args.Length == 0)
        {
            return false;
        }

        var candidatePath = args[0];
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(candidatePath), ".ffproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(candidatePath))
        {
            return false;
        }

        projectPath = candidatePath;
        return true;
    }
}

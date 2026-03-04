using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FrameForge;

public static class FileAssociationService
{
    private const string ProjectExtension = ".ffproj";
    private const string ProjectProgId = "FrameForge.ProjectFile";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static void RegisterProjectFileAssociation()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
        }

        using (var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProjectExtension}", writable: true))
        {
            extensionKey?.SetValue(string.Empty, ProjectProgId, RegistryValueKind.String);
        }

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProjectProgId}", writable: true))
        {
            progIdKey?.SetValue(string.Empty, "FrameForge Project File", RegistryValueKind.String);
        }

        using (var defaultIconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProjectProgId}\DefaultIcon", writable: true))
        {
            defaultIconKey?.SetValue(string.Empty, $"\"{executablePath}\",0", RegistryValueKind.String);
        }

        using (var openCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProjectProgId}\shell\open\command", writable: true))
        {
            openCommandKey?.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"", RegistryValueKind.String);
        }

        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

namespace TensaZangetsu.Services.AdbFastboot;

/// <summary>
/// Configuration options for external Android Platform Tools (ADB and Fastboot).
/// Supports resolution from user paths, bundled assets, standard installation directories, and system PATH.
/// </summary>
public sealed class AdbFastbootOptions
{
    public string? AdbPath { get; set; }
    public string? FastbootPath { get; set; }
    public string? CustomToolsDirectory { get; set; }
    public int DefaultTimeoutSeconds { get; set; } = 15;

    public string ResolveAdbExecutable() => ResolveTool("adb.exe", AdbPath);

    public string ResolveFastbootExecutable() => ResolveTool("fastboot.exe", FastbootPath);

    public bool IsAdbAvailable() => File.Exists(ResolveAdbExecutable());

    public bool IsFastbootAvailable() => File.Exists(ResolveFastbootExecutable());

    private string ResolveTool(string executableName, string? configuredPath)
    {
        // 1. Explicit configured path — authoritative, no fallback.
        // If the user explicitly set a path, return it regardless of whether it exists;
        // the caller (backend) will verify and fail-closed accordingly.
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        // 2. Check CustomToolsDirectory
        if (!string.IsNullOrWhiteSpace(CustomToolsDirectory) && Directory.Exists(CustomToolsDirectory))
        {
            string candidate = Path.Combine(CustomToolsDirectory, executableName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        // 3. Check bundled application assets
        string[] relativeAssetFolders =
        [
            Path.Combine("Assets", "Tools", "PlatformTools"),
            Path.Combine("Assets", "Tools", "ADB"),
            Path.Combine("ExternalAssets", "Tools", "ADB"),
            Path.Combine("ExternalAssets", "Tools", "PlatformTools"),
            Path.Combine("Assets", "ADB")
        ];

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var folder in relativeAssetFolders)
        {
            string direct = Path.Combine(baseDir, folder, executableName);
            if (File.Exists(direct)) return Path.GetFullPath(direct);
        }

        // Look upwards from base directory
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            foreach (var folder in relativeAssetFolders)
            {
                string candidate = Path.Combine(current.FullName, folder, executableName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);

                string appCandidate = Path.Combine(current.FullName, "src", "TensaZangetsu.App", folder, executableName);
                if (File.Exists(appCandidate)) return Path.GetFullPath(appCandidate);
            }
            current = current.Parent;
        }

        // 4. Check standard C:\platform-tools\
        string standardPath = Path.Combine(@"C:\platform-tools", executableName);
        if (File.Exists(standardPath)) return standardPath;

        // 5. Check LocalAppData Android SDK platform-tools
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string sdkPath = Path.Combine(localAppData, @"Android\Sdk\platform-tools", executableName);
            if (File.Exists(sdkPath)) return sdkPath;
        }

        // 6. Return executable name for PATH resolution or default
        return executableName;
    }
}

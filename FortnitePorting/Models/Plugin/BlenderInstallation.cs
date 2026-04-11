using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortnitePorting.Shared.Extensions;
using FortnitePorting.ViewModels;
using Newtonsoft.Json;
using Tomlyn;

namespace FortnitePorting.Models.Plugin;

public partial class BlenderInstallation(string blenderExecutablePath) : ObservableObject
{
    [ObservableProperty] private string _blenderPath = blenderExecutablePath;
    
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ExtensionVersionString))] 
    [property: JsonIgnore]
    private Version? _extensionVersion = null;

    [JsonIgnore]
    public string ExtensionVersionString => ExtensionVersion is null ? string.Empty : $"v{ExtensionVersion.ToString()}";
    
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusBrush))]
    [property: JsonIgnore]
    private EPluginStatusType _status = EPluginStatusType.Newest;

    [JsonIgnore]
    public SolidColorBrush StatusBrush => Status switch
    {
        EPluginStatusType.Newest => SolidColorBrush.Parse("#17854F"),
        EPluginStatusType.UpdateAvailable => SolidColorBrush.Parse("#E0A100"),
        EPluginStatusType.Failed => SolidColorBrush.Parse("#A61717"),
        EPluginStatusType.Modifying => SolidColorBrush.Parse("#6F6F75"),
    };
    
    [JsonIgnore]
    public Version BlenderVersion => GetVersion(BlenderPath);

    private string StartupPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Blender Foundation",
            "Blender",
            BlenderVersion.ToString(2),
            "scripts",
            "startup")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "blender",
            BlenderVersion.ToString(2),
            "scripts",
            "startup");

    private string ManifestPath => Path.Combine(StartupPath,
        "fortnite_porting",
        "blender_manifest.toml");
    
    public static readonly DirectoryInfo PluginWorkingDirectory = new(Path.Combine(App.PluginsFolder.FullName, "Blender"));
    public static readonly Version MinimumVersion = new(5, 0);

    public static Version GetVersion(string blenderPath)
    {
        if (!File.Exists(blenderPath))
            throw new FileNotFoundException("The selected Blender executable does not exist.", blenderPath);

        if (TryGetVersionFromProcess(blenderPath, out var processVersion))
            return processVersion;

        if (TryParseVersion(FileVersionInfo.GetVersionInfo(blenderPath).ProductVersion, out var productVersion))
            return productVersion;

        if (TryParseVersion(FileVersionInfo.GetVersionInfo(blenderPath).FileVersion, out var fileVersion))
            return fileVersion;

        throw new InvalidOperationException($"Failed to determine Blender version from executable: {blenderPath}");
    }

    private static bool TryGetVersionFromProcess(string blenderPath, out Version version)
    {
        version = null!;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = blenderPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return false;

            var stdout = process.StandardOutput.ReadLine();
            process.WaitForExit(3000);

            return TryParseVersion(stdout, out version);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseVersion(string? input, out Version version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var match = Regex.Match(input, @"\d+\.\d+(?:\.\d+)?");
        if (!match.Success) return false;

        return Version.TryParse(match.Value, out version);
    }

    public bool SyncExtensionVersion()
    {
        if (!File.Exists(ManifestPath))
        {
            Info.Message("Blender Extension", $"Plugin manifest does not exist at path {ManifestPath}, installation may have gone wrong.\nPlease remove the installation from Fortnite Porting and try again.");
            Status = EPluginStatusType.Failed;
            return false;
        }
        
        var manifestContents = File.ReadAllText(ManifestPath);
        var manifestToml = Toml.ToModel(manifestContents);
        ExtensionVersion = new Version((string) manifestToml["version"]);

        var fpExtensionVersion = new Version(ExtensionVersion.Major, ExtensionVersion.Minor, ExtensionVersion.Build);
        Status = fpExtensionVersion.Equals(Globals.Version.ToVersion())
            ? EPluginStatusType.Newest
            : EPluginStatusType.UpdateAvailable;
        
        return true;
    }
    
    public void Install(bool verbose = true)
    {
        Status = EPluginStatusType.Modifying;
        
        Directory.CreateDirectory(StartupPath);
        
        MiscExtensions.Copy(Path.Combine(PluginWorkingDirectory.FullName, "fortnite_porting"), Path.Combine(StartupPath, "fortnite_porting"));

        var didSyncProperly = SyncExtensionVersion();
        if (verbose)
        {
            if (!didSyncProperly)
            {
                Info.Message("Plugin Installation Failed", 
                    "Failed to install the plugin, please install it manually by dragging and dropping the Fortnite Porting plugin in Blender.", 
                    useButton: true, buttonTitle: "Open Plugins Folder", buttonCommand: () => App.Launch(App.PluginsFolder.FullName));

                Status = EPluginStatusType.Failed;
            }
        }

        Status = EPluginStatusType.Newest;
    }

    public void Uninstall()
    {
        Status = EPluginStatusType.Modifying;

        var installPath = Path.Combine(StartupPath, "fortnite_porting");
        if (Directory.Exists(installPath))
            Directory.Delete(installPath, true);
    }

    public async Task Launch()
    {
        App.Launch(BlenderPath);
    }
}

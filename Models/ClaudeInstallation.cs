namespace ClaudePlusPlus.Models;

internal sealed record ClaudeInstallation(
    string PackageFamilyName,
    string PackageFullName,
    string InstallLocation,
    string Version)
{
    public string AppUserModelId => $"{PackageFamilyName}!Claude";

    public string ExecutablePath => Path.Combine(InstallLocation, "app", "Claude.exe");

    public string AsarPath => Path.Combine(InstallLocation, "app", "resources", "app.asar");

    public string UserDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Claude-3p");
}

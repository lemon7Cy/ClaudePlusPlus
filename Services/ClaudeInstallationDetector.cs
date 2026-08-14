using System.Diagnostics;
using System.Text.Json;
using ClaudePlusPlus.Models;

namespace ClaudePlusPlus.Services;

internal static class ClaudeInstallationDetector
{
    public static async Task<ClaudeInstallation> DetectAsync(CancellationToken cancellationToken = default)
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var command = "Get-AppxPackage -Name Claude | " +
                      "Sort-Object Version -Descending | " +
                      "Select-Object -First 1 Name,PackageFamilyName,PackageFullName,InstallLocation," +
                      "@{n='Version';e={$_.Version.ToString()}} | ConvertTo-Json -Compress";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = windowsPowerShell,
                Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException(
                "未检测到 Microsoft Store 版 Claude。" +
                (string.IsNullOrWhiteSpace(standardError) ? string.Empty : $" {standardError.Trim()}"));
        }

        using var document = JsonDocument.Parse(standardOutput);
        var root = document.RootElement;
        var installation = new ClaudeInstallation(
            root.GetProperty("PackageFamilyName").GetString() ?? string.Empty,
            root.GetProperty("PackageFullName").GetString() ?? string.Empty,
            root.GetProperty("InstallLocation").GetString() ?? string.Empty,
            root.GetProperty("Version").GetString() ?? string.Empty);

        if (!File.Exists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("Claude.exe 不存在。", installation.ExecutablePath);
        }

        return installation;
    }
}

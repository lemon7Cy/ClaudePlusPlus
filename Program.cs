using System.Text.Json;
using ClaudePlusPlus.Services;

namespace ClaudePlusPlus;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--cdp-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            RunCdpSmokeTestAsync().GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static async Task RunCdpSmokeTestAsync()
    {
        var installation = await ClaudeInstallationDetector.DetectAsync();
        var progress = new Progress<string>(message => Console.Error.WriteLine(message));
        var runtime = await ClaudeCdpRuntimeService.PrepareAsync(installation, progress);
        var target = await ClaudeCdpLaunchService.LaunchAsync(runtime, progress: progress);
        Console.WriteLine(JsonSerializer.Serialize(target, new JsonSerializerOptions { WriteIndented = true }));
    }
}

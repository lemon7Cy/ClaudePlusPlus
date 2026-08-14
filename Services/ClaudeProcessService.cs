using System.Diagnostics;

namespace ClaudePlusPlus.Services;

internal static class ClaudeProcessService
{
    public static IReadOnlyList<Process> FindOwnedProcesses(string executablePath)
    {
        var expectedPath = Path.GetFullPath(executablePath);
        var results = new List<Process>();

        foreach (var process in Process.GetProcessesByName("Claude"))
        {
            try
            {
                var actualPath = process.MainModule?.FileName;
                if (actualPath is not null &&
                    string.Equals(Path.GetFullPath(actualPath), expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(process);
                    continue;
                }
            }
            catch
            {
                // A process that cannot be positively identified is never touched.
            }

            process.Dispose();
        }

        return results;
    }

    public static async Task<int> StopOwnedProcessesAsync(string executablePath)
    {
        var processes = FindOwnedProcesses(executablePath);
        var stopped = 0;

        foreach (var process in processes.OrderByDescending(process => process.Id))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    stopped++;
                }
                catch (InvalidOperationException)
                {
                    // The process already exited.
                }
            }
        }

        return stopped;
    }
}

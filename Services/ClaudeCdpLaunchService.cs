using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudePlusPlus.Models;

namespace ClaudePlusPlus.Services;

internal static class ClaudeCdpLaunchService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public static async Task<ClaudeCdpTarget> LaunchAsync(
        ClaudeCdpRuntime runtime,
        int preferredPort = 9344,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (var port = preferredPort; port < preferredPort + 32; port++)
        {
            if (await TryGetRuntimeTargetAsync(runtime, port, cancellationToken) is { } existingTarget)
            {
                progress?.Report($"复用已运行的 Claude CDP：127.0.0.1:{port}");
                return existingTarget with { ReusedExistingProcess = true };
            }
        }

        var selectedPort = Enumerable.Range(preferredPort, 32)
            .FirstOrDefault(IsPortAvailable);
        if (selectedPort == 0)
        {
            throw new InvalidOperationException($"{preferredPort}-{preferredPort + 31} 没有可用的本机调试端口。");
        }

        Directory.CreateDirectory(runtime.UserDataPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            WorkingDirectory = runtime.RuntimeDirectory,
            UseShellExecute = false,
            Arguments =
                $"--remote-debugging-address=127.0.0.1 " +
                $"--remote-debugging-port={selectedPort} " +
                $"--remote-allow-origins=http://127.0.0.1:{selectedPort}"
        };
        startInfo.Environment["CLAUDE_USER_DATA_DIR"] = runtime.UserDataPath;

        progress?.Report($"正在启动 Claude CDP（127.0.0.1:{selectedPort}）…");
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动 Claude CDP 副本。");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Claude CDP 在端口就绪前退出（ExitCode={process.ExitCode}）。");
            }

            if (await TryGetRuntimeTargetAsync(runtime, selectedPort, cancellationToken) is { } target)
            {
                progress?.Report($"Claude CDP 已就绪，target={target.Id}");
                return target with { ProcessId = process.Id };
            }

            await Task.Delay(350, cancellationToken);
        }

        throw new TimeoutException($"Claude CDP 未在 30 秒内监听端口 {selectedPort}。");
    }

    public static void OpenInspector(ClaudeCdpTarget target)
    {
        Process.Start(new ProcessStartInfo(target.InspectorUrl)
        {
            UseShellExecute = true
        });
    }

    private static async Task<ClaudeCdpTarget?> TryGetRuntimeTargetAsync(
        ClaudeCdpRuntime runtime,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await HttpClient.GetStreamAsync(
                $"http://127.0.0.1:{port}/json/list",
                cancellationToken);
            var targets = await JsonSerializer.DeserializeAsync<List<CdpTargetPayload>>(
                stream,
                cancellationToken: cancellationToken);
            if (targets is null)
            {
                return null;
            }

            var runtimeUriPrefix = new Uri(runtime.RuntimeDirectory.TrimEnd(Path.DirectorySeparatorChar) +
                                           Path.DirectorySeparatorChar).AbsoluteUri;
            var target = targets.FirstOrDefault(candidate =>
                             candidate.Type == "page" &&
                             candidate.Url.StartsWith(runtimeUriPrefix, StringComparison.OrdinalIgnoreCase) &&
                             candidate.Url.Contains("main_window/index.html", StringComparison.OrdinalIgnoreCase))
                         ?? targets.FirstOrDefault(candidate =>
                             candidate.Type == "page" &&
                             candidate.Url.StartsWith(runtimeUriPrefix, StringComparison.OrdinalIgnoreCase));
            if (target is null || string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
            {
                return null;
            }

            var websocketUri = new Uri(target.WebSocketDebuggerUrl);
            var inspectorUrl =
                $"http://127.0.0.1:{port}/devtools/inspector.html?ws=127.0.0.1:{port}{websocketUri.AbsolutePath}";
            return new ClaudeCdpTarget(
                port,
                null,
                target.Id,
                target.Title,
                target.Url,
                target.WebSocketDebuggerUrl,
                inspectorUrl,
                false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed record CdpTargetPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);
}

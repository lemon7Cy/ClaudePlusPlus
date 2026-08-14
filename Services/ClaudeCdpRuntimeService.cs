using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudePlusPlus.Models;

namespace ClaudePlusPlus.Services;

internal static class ClaudeCdpRuntimeService
{
    private const string SupportedProductVersion = "1.30096.1";
    private const string SupportedAsarSha256 =
        "122359D81D2FBAAA5E88B5E5BFFE9CB78DD8FAF1F5E38B125DBDA5E9E28AB91B";

    private static readonly byte[] OriginalGuard =
        Encoding.UTF8.GetBytes("yae(process.argv)&&!f9()&&process.exit(1)");

    private static readonly byte[] PatchedGuard =
        Encoding.UTF8.GetBytes("yae(process.argv)&&!1   &&process.exit(1)");

    private static readonly byte[] FuseSentinel =
        Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX");

    public static string RuntimeBasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudePlusPlus",
        "runtime");

    public static bool Supports(string productVersion) =>
        string.Equals(productVersion, SupportedProductVersion, StringComparison.Ordinal);

    public static async Task<ClaudeCdpRuntime> PrepareAsync(
        ClaudeInstallation installation,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inspection = AsarInspector.Inspect(installation.AsarPath);
        if (!Supports(inspection.ProductVersion))
        {
            throw new NotSupportedException(
                $"Claude {inspection.ProductVersion} 尚无经过验证的 CDP 补丁；当前只支持 {SupportedProductVersion}。");
        }

        progress?.Report("正在校验官方 app.asar…");
        var sourceAsarHash = await ComputeSha256Async(installation.AsarPath, cancellationToken);
        if (!sourceAsarHash.Equals(SupportedAsarSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Claude app.asar 与已验证版本的哈希不一致，已拒绝盲目补丁。" + Environment.NewLine +
                $"期望：{SupportedAsarSha256}" + Environment.NewLine +
                $"实际：{sourceAsarHash}");
        }

        var safeVersion = string.Concat(inspection.ProductVersion.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
        var sourceAppDirectory = Path.GetDirectoryName(installation.ExecutablePath)
                                 ?? throw new InvalidOperationException("无法确定 Claude app 目录。");
        var runtimeDirectory = Path.Combine(RuntimeBasePath, safeVersion);
        var runtime = BuildRuntime(inspection.ProductVersion, runtimeDirectory, sourceAsarHash, string.Empty);

        if (await TryValidateExistingRuntimeAsync(runtime, cancellationToken) is { } validatedRuntime)
        {
            progress?.Report($"复用已校验的 Claude CDP 副本：{validatedRuntime.RuntimeDirectory}");
            return validatedRuntime;
        }

        if (Directory.Exists(runtimeDirectory))
        {
            runtimeDirectory = Path.Combine(RuntimeBasePath, $"{safeVersion}-{sourceAsarHash[..12].ToLowerInvariant()}");
            runtime = BuildRuntime(inspection.ProductVersion, runtimeDirectory, sourceAsarHash, string.Empty);
            if (await TryValidateExistingRuntimeAsync(runtime, cancellationToken) is { } alternateRuntime)
            {
                progress?.Report($"复用已校验的 Claude CDP 副本：{alternateRuntime.RuntimeDirectory}");
                return alternateRuntime;
            }

            if (Directory.Exists(runtimeDirectory))
            {
                throw new InvalidDataException($"现有 CDP 副本校验失败：{runtimeDirectory}");
            }
        }

        Directory.CreateDirectory(RuntimeBasePath);
        var stagingDirectory = runtimeDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        AssertChildPath(RuntimeBasePath, stagingDirectory);

        try
        {
            progress?.Report("正在复制 Claude 到可写副本（约 630 MB）…");
            await Task.Run(
                () => CopyDirectory(sourceAppDirectory, stagingDirectory, cancellationToken),
                cancellationToken);

            var stagingAsar = Path.Combine(stagingDirectory, "resources", "app.asar");
            var stagingExecutable = Path.Combine(stagingDirectory, "Claude.exe");

            progress?.Report("正在应用固定长度 CDP 启动保护补丁…");
            await Task.Run(() => PatchAsar(stagingAsar), cancellationToken);

            progress?.Report("正在为副本关闭 Electron 内嵌 ASAR 完整性 fuse…");
            await Task.Run(() => DisableEmbeddedAsarIntegrityFuse(stagingExecutable), cancellationToken);

            var patchedAsarHash = await ComputeSha256Async(stagingAsar, cancellationToken);
            var manifest = new RuntimeManifest(
                1,
                inspection.ProductVersion,
                Path.GetFullPath(sourceAppDirectory),
                sourceAsarHash,
                patchedAsarHash,
                "disable-cdp-auth-exit-guard-v1",
                "disable-embedded-asar-integrity-validation-v1",
                DateTimeOffset.UtcNow);

            var manifestPath = Path.Combine(stagingDirectory, "claudeplusplus-runtime.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);

            Directory.Move(stagingDirectory, runtimeDirectory);
            progress?.Report("Claude CDP 副本准备完成。");
            return BuildRuntime(inspection.ProductVersion, runtimeDirectory, sourceAsarHash, patchedAsarHash);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                AssertChildPath(RuntimeBasePath, stagingDirectory);
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static ClaudeCdpRuntime BuildRuntime(
        string version,
        string runtimeDirectory,
        string sourceAsarHash,
        string patchedAsarHash) => new(
        version,
        runtimeDirectory,
        Path.Combine(runtimeDirectory, "Claude.exe"),
        Path.Combine(runtimeDirectory, "resources", "app.asar"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudePlusPlus",
            "profiles",
            version),
        sourceAsarHash,
        patchedAsarHash);

    private static async Task<ClaudeCdpRuntime?> TryValidateExistingRuntimeAsync(
        ClaudeCdpRuntime runtime,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(runtime.RuntimeDirectory, "claudeplusplus-runtime.json");
        if (!File.Exists(manifestPath) || !File.Exists(runtime.ExecutablePath) || !File.Exists(runtime.AsarPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var root = document.RootElement;
            var sourceHash = root.GetProperty("sourceAsarSha256").GetString();
            var patchedHash = root.GetProperty("patchedAsarSha256").GetString();
            if (!string.Equals(sourceHash, runtime.SourceAsarSha256, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(patchedHash))
            {
                return null;
            }

            var actualPatchedHash = await ComputeSha256Async(runtime.AsarPath, cancellationToken);
            if (!actualPatchedHash.Equals(patchedHash, StringComparison.OrdinalIgnoreCase) ||
                !HasExactPatch(runtime.AsarPath) ||
                !IsEmbeddedAsarIntegrityFuseDisabled(runtime.ExecutablePath))
            {
                return null;
            }

            return runtime with { PatchedAsarSha256 = actualPatchedHash };
        }
        catch (Exception error) when (error is IOException or
                                      UnauthorizedAccessException or
                                      JsonException or
                                      InvalidOperationException or
                                      ArgumentException)
        {
            return null;
        }
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static void PatchAsar(string asarPath)
    {
        if (OriginalGuard.Length != PatchedGuard.Length)
        {
            throw new InvalidOperationException("CDP 补丁长度不一致。");
        }

        var bytes = File.ReadAllBytes(asarPath);
        var originalOffsets = FindPatternOffsets(bytes, OriginalGuard);
        var patchedOffsets = FindPatternOffsets(bytes, PatchedGuard);
        if (originalOffsets.Count != 1 || patchedOffsets.Count != 0)
        {
            throw new InvalidDataException(
                $"CDP 补丁签名不匹配：original={originalOffsets.Count}, patched={patchedOffsets.Count}");
        }

        PatchedGuard.CopyTo(bytes, originalOffsets[0]);
        File.WriteAllBytes(asarPath, bytes);

        if (!HasExactPatch(asarPath))
        {
            throw new InvalidDataException("写入后的 CDP 补丁校验失败。");
        }
    }

    private static bool HasExactPatch(string asarPath)
    {
        var bytes = File.ReadAllBytes(asarPath);
        return FindPatternOffsets(bytes, OriginalGuard).Count == 0 &&
               FindPatternOffsets(bytes, PatchedGuard).Count == 1;
    }

    private static void DisableEmbeddedAsarIntegrityFuse(string executablePath)
    {
        var bytes = File.ReadAllBytes(executablePath);
        var sentinelOffsets = FindPatternOffsets(bytes, FuseSentinel);
        if (sentinelOffsets.Count != 1)
        {
            throw new InvalidDataException($"Electron fuse sentinel 数量异常：{sentinelOffsets.Count}");
        }

        var sentinelEnd = sentinelOffsets[0] + FuseSentinel.Length;
        if (bytes.Length <= sentinelEnd + 2)
        {
            throw new InvalidDataException("Electron fuse wire 已截断。");
        }

        var fuseVersion = bytes[sentinelEnd];
        var fuseCount = bytes[sentinelEnd + 1];
        const int embeddedAsarIntegrityFuseIndex = 4;
        if (fuseVersion != 1 || fuseCount <= embeddedAsarIntegrityFuseIndex)
        {
            throw new InvalidDataException($"不支持的 Electron fuse wire：v{fuseVersion}, count={fuseCount}");
        }

        var fuseOffset = sentinelEnd + 2 + embeddedAsarIntegrityFuseIndex;
        if (bytes[fuseOffset] != (byte)'1')
        {
            throw new InvalidDataException(
                $"EnableEmbeddedAsarIntegrityValidation fuse 不是启用状态：{bytes[fuseOffset]}");
        }

        bytes[fuseOffset] = (byte)'0';
        File.WriteAllBytes(executablePath, bytes);
        if (!IsEmbeddedAsarIntegrityFuseDisabled(executablePath))
        {
            throw new InvalidDataException("Electron ASAR 完整性 fuse 写入后校验失败。");
        }
    }

    private static bool IsEmbeddedAsarIntegrityFuseDisabled(string executablePath)
    {
        var bytes = File.ReadAllBytes(executablePath);
        var sentinelOffsets = FindPatternOffsets(bytes, FuseSentinel);
        if (sentinelOffsets.Count != 1)
        {
            return false;
        }

        var sentinelEnd = sentinelOffsets[0] + FuseSentinel.Length;
        const int embeddedAsarIntegrityFuseIndex = 4;
        return bytes.Length > sentinelEnd + 2 + embeddedAsarIntegrityFuseIndex &&
               bytes[sentinelEnd] == 1 &&
               bytes[sentinelEnd + 1] > embeddedAsarIntegrityFuseIndex &&
               bytes[sentinelEnd + 2 + embeddedAsarIntegrityFuseIndex] == (byte)'0';
    }

    private static List<int> FindPatternOffsets(byte[] bytes, byte[] pattern)
    {
        var offsets = new List<int>();
        for (var index = 0; index <= bytes.Length - pattern.Length; index++)
        {
            if (bytes[index] != pattern[0])
            {
                continue;
            }

            var matches = true;
            for (var patternIndex = 1; patternIndex < pattern.Length; patternIndex++)
            {
                if (bytes[index + patternIndex] == pattern[patternIndex])
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                offsets.Add(index);
                index += pattern.Length - 1;
            }
        }

        return offsets;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void AssertChildPath(string parent, string child)
    {
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childPath = Path.GetFullPath(child);
        if (!childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝在运行目录以外操作：{childPath}");
        }
    }

    private sealed record RuntimeManifest(
        int SchemaVersion,
        string ClaudeVersion,
        string SourceAppDirectory,
        string SourceAsarSha256,
        string PatchedAsarSha256,
        string AsarPatch,
        string ExecutablePatch,
        DateTimeOffset CreatedAtUtc);
}

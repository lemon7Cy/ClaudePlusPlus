using System.Text;
using System.Text.Json;
using ClaudePlusPlus.Models;

namespace ClaudePlusPlus.Services;

internal static class AsarInspector
{
    public static AsarInspection Inspect(string asarPath)
    {
        using var stream = File.OpenRead(asarPath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        stream.Position = 4;
        var headerSize = reader.ReadUInt32();
        stream.Position = 12;
        var jsonSize = reader.ReadUInt32();
        var headerJson = Encoding.UTF8.GetString(reader.ReadBytes(checked((int)jsonSize)));
        using var header = JsonDocument.Parse(headerJson);
        var contentBase = 8L + headerSize;

        var packageJson = ReadEntry(reader, header.RootElement, contentBase, "package.json");
        using var package = JsonDocument.Parse(packageJson);
        var version = package.RootElement.TryGetProperty("version", out var versionElement)
            ? versionElement.GetString() ?? "未知"
            : "未知";

        var mainScript = ReadEntry(reader, header.RootElement, contentBase, ".vite", "build", "index.pre.js");
        return new AsarInspection(
            version,
            mainScript.Contains("remote-debugging-port", StringComparison.OrdinalIgnoreCase) &&
            mainScript.Contains("process.exit(1)", StringComparison.Ordinal),
            mainScript.Contains("CLAUDE_CDP_AUTH", StringComparison.Ordinal) &&
            mainScript.Contains("invalid signature", StringComparison.Ordinal),
            mainScript.Contains("CLAUDE_USER_DATA_DIR", StringComparison.Ordinal));
    }

    private static string ReadEntry(
        BinaryReader reader,
        JsonElement root,
        long contentBase,
        params string[] pathParts)
    {
        var node = root;
        foreach (var part in pathParts)
        {
            node = node.GetProperty("files").GetProperty(part);
        }

        var size = node.GetProperty("size").GetInt32();
        var offsetText = node.GetProperty("offset").GetString() ?? "0";
        var offset = long.Parse(offsetText, System.Globalization.CultureInfo.InvariantCulture);
        reader.BaseStream.Position = contentBase + offset;
        return Encoding.UTF8.GetString(reader.ReadBytes(size));
    }
}

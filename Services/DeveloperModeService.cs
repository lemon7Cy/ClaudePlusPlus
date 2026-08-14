using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudePlusPlus.Services;

internal static class DeveloperModeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetConfigPath(string userDataPath) =>
        Path.Combine(userDataPath, "developer_settings.json");

    public static bool IsEnabled(string userDataPath)
    {
        var path = GetConfigPath(userDataPath);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return root?["allowDevTools"]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(string userDataPath, bool enabled)
    {
        Directory.CreateDirectory(userDataPath);
        var path = GetConfigPath(userDataPath);
        JsonObject root;

        if (File.Exists(path))
        {
            var backupPath = path + ".claudeplusplus.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(path, backupPath);
            }

            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidDataException("developer_settings.json 的根节点不是 JSON 对象。");
        }
        else
        {
            root = new JsonObject();
        }

        root["allowDevTools"] = enabled;
        var temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

using ShadowVPNApi.Models;
using Tomlyn;
using Tomlyn.Model;

namespace ShadowVPNApi.Utils;

public static class TomlHelper
{
    private const string ConfigDir = "/etc/openvpn/clients";
    private const string ConfigFile = "server_config.toml";

    public static async Task EnsureServerConfigAsync()
    {
        Directory.CreateDirectory(ConfigDir);
        var path = Path.Combine(ConfigDir, ConfigFile);
        if (File.Exists(path)) return;

        var table = new TomlTable
        {
            ["auth"] = new TomlTable { ["admin_password"] = "changeme" }
        };
        await File.WriteAllTextAsync(path, Toml.FromModel(table));
    }

    public static ServerConfig? LoadConfig()
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(ConfigDir, ConfigFile));
            var model = Toml.ToModel(text);
            var auth  = model?["auth"] as TomlTable;
            var pwd   = auth?["admin_password"] as string;
            return new ServerConfig { AdminPassword = pwd ?? "" };
        }
        catch
        {
            // Повреждённый файл
            return null;
        }
    }
}
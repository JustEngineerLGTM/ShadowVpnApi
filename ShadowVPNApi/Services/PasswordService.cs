using System.Security.Cryptography;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace ShadowVPNApi.Services;

public static class PasswordService
{
    /// <summary>
    /// Сравнивает строку с паролем из файла server_config.toml
    /// </summary>
    public static async Task<bool> CheckPasswordAsync(string receivedHash)
    {
        const string configPath = "/etc/openvpn/clients/server_config.toml";

        if (!File.Exists(configPath))
            return false;

        var content = await File.ReadAllTextAsync(configPath);
        var model = Toml.ToModel(content);
        if (model["auth"] is not TomlTable auth || auth["admin_password"] is not string password)
            return false;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();

        return receivedHash.Equals(expected, StringComparison.InvariantCultureIgnoreCase);
    }
}
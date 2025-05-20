using ShadowVPNApi.Utils;

namespace ShadowVPNApi.Services;

public class ConfigService
{
    public async Task<IResult> GetVpnConfigAsync(string raw)
    {
        // проверка формата
        var idx = raw.LastIndexOf('=');
        if (idx <= 0 || idx == raw.Length - 1)
            return Results.BadRequest("Неверный формат запроса");

        var username = raw[..idx];
        var hash = raw[(idx + 1)..];

        // валидация пароля
        var cfg = TomlHelper.LoadConfig();
        if (cfg is null || !ValidateHash(hash, cfg.AdminPassword))
            return Results.Unauthorized();

        var path = Path.Combine("/etc/openvpn/clients", $"{username}.ovpn");
        if (!File.Exists(path))
            return Results.NotFound($"Конфигурация для {username} не найдена.");

        var content = await File.ReadAllTextAsync(path);
        return Results.Ok(content);
    }

    private bool ValidateHash(string received, string adminPassword)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var calc = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(adminPassword)))
            .ToLowerInvariant();
        return string.Equals(received, calc, StringComparison.OrdinalIgnoreCase);
    }
}
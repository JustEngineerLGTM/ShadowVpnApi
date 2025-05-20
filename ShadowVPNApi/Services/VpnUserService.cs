using ShadowVPNApi.OpenVPN;
using ShadowVPNApi.Utils;

namespace ShadowVPNApi.Services;

public class VpnUserService
{
    public async Task<IResult> CreateVpnUserAsync(string raw)
    {
        // raw: "user=hash"
        var idx = raw.LastIndexOf('=');
        if (idx <= 0 || idx == raw.Length - 1)
            return Results.BadRequest("Неверный формат запроса");

        var username = raw[..idx];
        var hash = raw[(idx + 1)..];

        // проверяем админ‑хеш
        var cfg = TomlHelper.LoadConfig();
        if (cfg is null || !ValidateHash(hash, cfg.AdminPassword))
            return Results.Unauthorized();

        // создаём сертификаты и .ovpn
        var path = await OpenVpnSetup.CreateClientConfigAsync(username);
        return path is null
            ? Results.BadRequest("Ошибка генерации")
            : Results.Ok(path);
    }

    private bool ValidateHash(string received, string adminPassword)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var calc = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(adminPassword)))
            .ToLowerInvariant();
        return string.Equals(received, calc, StringComparison.OrdinalIgnoreCase);
    }
}
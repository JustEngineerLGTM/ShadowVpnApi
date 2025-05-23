using ShadowVPNApi.Services;

namespace ShadowVPNApi.Endpoints;

public static class VpnEndpoints
{
    public static void MapVpnEndpoints(this WebApplication app)
    {
        app.MapPost("/createvpnuser", async (string raw) =>
        {
            var parts = raw.Split('=', 2);
            if (parts.Length != 2) return Results.BadRequest("Неверный формат");

            var user = parts[0];
            var hash = parts[1];

            if (!await PasswordService.CheckPasswordAsync(hash))
                return Results.Unauthorized();

            var path = await VpnService.CreateVpnUserAsync(user);
            return path != null
                ? Results.Ok(path)
                : Results.BadRequest("Ошибка создания пользователя");
        });

        app.MapGet("/getvpnconfig", async (string raw) =>
        {
            var parts = raw.Split('=', 2);
            if (parts.Length != 2) return Results.BadRequest("Неверный формат");

            var user = parts[0];
            var hash = parts[1];

            if (!await PasswordService.CheckPasswordAsync(hash))
                return Results.Unauthorized();

            var cfg = await VpnService.GetVpnConfigAsync(user);
            return cfg != null
                ? Results.Ok(cfg)
                : Results.NotFound("Не найдена конфигурация");
        });
    }
}
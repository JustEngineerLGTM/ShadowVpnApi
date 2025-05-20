using Microsoft.OpenApi.Models;
using ShadowVPNApi.Services;
using ShadowVPNApi.Utils;

var builder = WebApplication.CreateBuilder(args);

// Конфиг для сервера
await TomlHelper.EnsureServerConfigAsync();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v2", new OpenApiInfo { Title = "ShadowVPN API", Version = "v2" });
});

// Сервисы
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<VpnUserService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v2/swagger.json", "ShadowVPN API v2"));
}

app.UseHttpsRedirection();

// Маршруты
app.MapPost("/createvpnuser", async (string raw, VpnUserService svc) =>
        await svc.CreateVpnUserAsync(raw))
    .WithName("CreateVpnUser");

app.MapGet("/getvpnconfig", async (string raw, ConfigService svc) =>
        await svc.GetVpnConfigAsync(raw))
    .WithName("GetVpnConfig");

app.Run();
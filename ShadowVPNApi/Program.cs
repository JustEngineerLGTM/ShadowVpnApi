using ShadowVPNApi.Endpoints;
using ShadowVPNApi.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Создаем файлы для OpenVPN
await ServerSetup.EnsureOpenVpnServerConfigAsync();
await ServerSetup.EnsureServerConfigAsync();
builder.WebHost.UseUrls("http://*:5000");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v2", new OpenApiInfo { Title = "ShadowVPN API", Version = "v2" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v2/swagger.json", "ShadowVPN API v2");
        options.RoutePrefix = string.Empty;
    });
}
app.UseHttpsRedirection();
// Маппинг маршрутов для VPN‑эндпоинтов
app.MapVpnEndpoints();
app.Run();

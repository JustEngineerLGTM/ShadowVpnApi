using ShadowVPNApi.Endpoints;
using ShadowVPNApi.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Гарантируем, что OpenVPN настроен и созданы все необходимые файлы
await ServerSetup.EnsureOpenVpnServerConfigAsync();
await ServerSetup.EnsureServerConfigAsync();

// Добавляем поддержку OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v2", new OpenApiInfo { Title = "ShadowVPN API", Version = "v2" });
});

var app = builder.Build();

// В режиме разработки включаем Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v2/swagger.json", "ShadowVPN API v2");
        options.RoutePrefix = string.Empty;
    });
}

// Перенаправляем HTTP → HTTPS
app.UseHttpsRedirection();

// Маппинг маршрутов для VPN‑эндпоинтов
VpnEndpoints.Map(app);

// Запускаем приложение
app.Run();
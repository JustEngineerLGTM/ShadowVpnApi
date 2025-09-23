using Microsoft.AspNetCore.Routing.Constraints;
using ShadowVPNApi.Endpoints;
using ShadowVPNApi.Services;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

// Создаем файлы для OpenVPN
await ServerSetup.EnsureOpenVpnServerConfigAsync();
await ServerSetup.EnsureServerConfigAsync();

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
builder.Services.AddHealthChecks();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "ShadowVPN API", Version = "v2"
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "ShadowVPN API v2");
    options.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();
app.MapVpnEndpoints();
app.MapHealthChecks("/health");
app.Run();

[JsonSerializable(typeof(String))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}   
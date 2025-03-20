using Microsoft.OpenApi.Models;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShadowVPN API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShadowVPN API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();

app.MapPost("/createvpnuser", async (string username) =>
{
    var result = await CreateVpnUserAsync(username);
    return result != null ? Results.Ok(result) : Results.BadRequest("Error creating VPN user.");
})
.WithName("CreateVpnUser");

app.MapGet("/getvpnconfig", async (string username) =>
{
    var config = await GetVpnConfigAsync(username);
    return config != null ? Results.Ok(config) : Results.NotFound($"Config for user {username} not found.");
})
.WithName("GetVpnConfig");

async Task<string?> CreateVpnUserAsync(string username)
{
    return await Task.Run(() =>
    {
        try
        {
            string scriptPath = "/root/openvpn-ca/create_vpn_user.sh";
            string outputPath = "/etc/openvpn/clients";

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            string command = $"{scriptPath} {username}";
            Console.WriteLine($"Executing script: {command}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                }
            };

            process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("[OUT] " + e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("[ERR] " + e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(30000))
            {
                process.Kill();
                Console.WriteLine("ERROR: Process timeout.");
                return null;
            }

            if (process.ExitCode != 0)
            {
                Console.WriteLine("ERROR: Certificate creation failed.");
                return null;
            }

            string certPath = Path.Combine(outputPath, $"{username}.ovpn");
            return certPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            return null;
        }
    });
}


async Task<string?> GetVpnConfigAsync(string username)
{
    try
    {
        string configPath = Path.Combine("/etc/openvpn/clients", $"{username}.ovpn");
        if (File.Exists(configPath))
        {
            return await File.ReadAllTextAsync(configPath);
        }
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
}

app.Run();

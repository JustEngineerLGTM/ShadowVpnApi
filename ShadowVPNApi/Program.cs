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
    try
    {
        string easyRsaPath = "/root/openvpn-ca";
        string outputPath = "/etc/openvpn/clients";

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        string command = $"echo \"\" | EASYRSA_BATCH=1 {easyRsaPath}/easyrsa build-client-full {username} nopass";
        Console.WriteLine($"Executing command: {command}");

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
        string configContent = $"client\n" +
                               $"dev tun\n" +
                               $"proto udp\n" +
                               $"remote 109.120.132.39 1194\n" +
                               $"resolv-retry infinite\n" +
                               $"nobind\n" +
                               $"persist-key\n" +
                               $"persist-tun\n\n" +
                               $"<ca>\n" +
                               $"{await File.ReadAllTextAsync("/etc/openvpn/ca.crt")}\n" +
                               $"</ca>\n\n" +
                               $"<cert>\n" +
                               $"{await File.ReadAllTextAsync(Path.Combine(easyRsaPath, "pki", "issued", $"{username}.crt"))}\n" +
                               $"</cert>\n\n" +
                               $"<key>\n" +
                               $"{await File.ReadAllTextAsync(Path.Combine(easyRsaPath, "pki", "private", $"{username}.key"))}\n" +
                               $"</key>\n\n" +
                               $"<tls-auth>\n" +
                               $"{await File.ReadAllTextAsync("/etc/openvpn/ta.key")}\n" +
                               $"</tls-auth>\n" +
                               $"cipher AES-256-CBC\n" +
                               $"auth SHA256\n" +
                               $"verb 3";

        await File.WriteAllTextAsync(certPath, configContent);
        return certPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
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
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Эндпоинт для создания пользователя OpenVPN
app.MapPost("/createvpnuser", async (string username) =>
{
    var result = await CreateVpnUserAsync(username);
    return result != null ? Results.Ok(result) : Results.BadRequest("Error creating VPN user.");
});

// Логика создания пользователя
async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {
        string easyRsaPath = "/etc/openvpn/easy-rsa"; // Путь к EasyRSA
        string outputPath = "/etc/openvpn/clients";   // Папка для сертификатов

        // Проверяем наличие EasyRSA и создаём сертификат
        string command = $"./easyrsa gen-req {username} nopass";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"cd {easyRsaPath} && {command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"Error generating user: {error}");
            return null;
        }

        // Если сертификат создан, собираем путь к файлам
        string certPath = Path.Combine(outputPath, $"{username}.ovpn");
        string configContent = $"client\n" +
                               $"dev tun\n" +
                               $"proto udp\n" +
                               $"remote YOUR_SERVER_IP 1194\n" +
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

        // Записываем конфиг в файл
        await File.WriteAllTextAsync(certPath, configContent);
        return certPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
}

app.Run();

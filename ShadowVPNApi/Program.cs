using Microsoft.OpenApi.Models;
using System.Diagnostics;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Добавьте Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Настройка Swagger (опционально)
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShadowVPN API", Version = "v1" });
});

var app = builder.Build();

// Использование Swagger в режиме разработки
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); // Подключаем Swagger
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShadowVPN API v1"); // Указываем путь до Swagger UI
        options.RoutePrefix = string.Empty; // Делает Swagger UI доступным по корневому URL
    });
}

app.UseHttpsRedirection();

// API для создания нового VPN пользователя
app.MapPost("/createvpnuser", async (string username) =>
{
    var result = await CreateVpnUserAsync(username);
    return result != null ? Results.Ok(result) : Results.BadRequest("Error creating VPN user.");
})
.WithName("CreateVpnUser");

// API для получения конфигурации по имени пользователя
app.MapGet("/getvpnconfig", async (string username) =>
{
    var config = await GetVpnConfigAsync(username);
    return config != null ? Results.Ok(config) : Results.NotFound($"Config for user {username} not found.");
})
.WithName("GetVpnConfig");


// Логика создания нового пользователя и сертификатов
async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {
        string easyRsaPath = "/root/openvpn-ca/easyrsa"; 
        string outputPath = "/etc/openvpn/clients";

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        // Генерация запроса на сертификат
        string command = $"./easyrsa --batch gen-req {username} nopass";
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

        process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("ERROR: " + e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.WriteLine("Ошибка создания запроса на сертификат.");
            return null;
        }

        // Подписание сертификата
        string signCommand = $"echo yes | ./easyrsa sign-req client {username}";
        var signProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"cd {easyRsaPath} && {signCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        signProcess.Start();
        signProcess.BeginOutputReadLine();
        signProcess.BeginErrorReadLine();
        signProcess.WaitForExit();

        if (signProcess.ExitCode != 0)
        {
            Console.WriteLine("Ошибка подписания сертификата.");
            return null;
        }

        // Генерация конфигурации
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

        await File.WriteAllTextAsync(certPath, configContent);
        return certPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
}


// Логика для получения конфигурации по пользователю
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

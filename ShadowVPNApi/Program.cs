using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Добавьте Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShadowVPN API", Version = "v1" });
});

var app = builder.Build();

// Использование Swagger в режиме разработки
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
}).WithName("CreateVpnUser");

app.MapGet("/getvpnconfig", async (string username) =>
{
    var config = await GetVpnConfigAsync(username);
    return config != null ? Results.Ok(config) : Results.NotFound($"Config for user {username} not found.");
}).WithName("GetVpnConfig");

async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {
        string outputPath = "/etc/openvpn/clients";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        using (RSA rsa = RSA.Create(2048))
        {
            var certRequest = new CertificateRequest($"CN={username}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            DateTime notBefore = DateTime.UtcNow;
            DateTime notAfter = notBefore.AddYears(1);

            string caCertPath = "/etc/openvpn/ca.crt";
            string caKeyPath = "/etc/openvpn/ca.key";

            // Загрузка сертификата CA через X509CertificateLoader
            X509Certificate2 caCert = LoadCertificate(caCertPath);
            RSA caKey = LoadPrivateKey(caKeyPath);

            // Создание и подписание клиентского сертификата
            X509Certificate2 clientCert = certRequest.Create(caCert, notBefore, notAfter, Guid.NewGuid().ToByteArray());
            var signedClientCert = clientCert.CopyWithPrivateKey(caKey);

            // Сохраняем сертификат и ключ клиента
            string certPath = Path.Combine(outputPath, $"{username}.crt");
            string keyPath = Path.Combine(outputPath, $"{username}.key");
            File.WriteAllText(certPath, ExportCertificateToPem(signedClientCert));
            File.WriteAllText(keyPath, ExportPrivateKeyToPem(rsa));

            // Генерация конфигурации OpenVPN
            string configContent = $"client\n" +
                                   $"dev tun\n" +
                                   $"proto udp\n" +
                                   $"remote 109.120.132.39 1194\n" +
                                   $"resolv-retry infinite\n" +
                                   $"nobind\n" +
                                   $"persist-key\n" +
                                   $"persist-tun\n\n" +
                                   $"<ca>\n{await File.ReadAllTextAsync(caCertPath)}\n</ca>\n\n" +
                                   $"<cert>\n{await File.ReadAllTextAsync(certPath)}\n</cert>\n\n" +
                                   $"<key>\n{await File.ReadAllTextAsync(keyPath)}\n</key>\n\n" +
                                   $"<tls-auth>\n{await File.ReadAllTextAsync("/etc/openvpn/ta.key")}\n</tls-auth>\n" +
                                   $"cipher AES-256-CBC\n" +
                                   $"auth SHA256\n" +
                                   $"verb 3";

            string configPath = Path.Combine(outputPath, $"{username}.ovpn");
            await File.WriteAllTextAsync(configPath, configContent);
            return configPath;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
}

static X509Certificate2 LoadCertificate(string certPath, string? password = null)
{
    // Считываем сертификат
    byte[] certBytes = File.ReadAllBytes(certPath);
    
    // Определяем тип содержимого сертификата (X.509 или PKCS12)
    var certContentType = GetCertContentType(certBytes);

    switch (certContentType)
    {
        case CertContentType.X509:
            return new X509Certificate2(certBytes); // Для X.509
        case CertContentType.Pkcs12:
            // Для PKCS12 необходимо указать пароль и флаги
            return new X509Certificate2(certBytes, password, X509KeyStorageFlags.DefaultKeySet);
        default:
            throw new InvalidOperationException("Unknown certificate type.");
    }
}

static CertContentType GetCertContentType(byte[] certBytes)
{
    // Проверка на X.509
    if (certBytes.Length > 4 && certBytes[0] == 0x30)
    {
        return CertContentType.X509;
    }
    
    // Проверка на PKCS12
    if (certBytes.Length > 4 && certBytes[0] == 0x30 && certBytes[1] == 0x82)
    {
        return CertContentType.Pkcs12;
    }
    
    // Если не найдено, выбрасываем исключение
    throw new InvalidOperationException("Unknown certificate content.");
}

static string ExportCertificateToPem(X509Certificate2 certificate)
{
    var builder = new StringBuilder();
    builder.AppendLine("-----BEGIN CERTIFICATE-----");
    builder.AppendLine(Convert.ToBase64String(certificate.Export(X509ContentType.Cert), Base64FormattingOptions.InsertLineBreaks));
    builder.AppendLine("-----END CERTIFICATE-----");
    return builder.ToString();
}

static string ExportPrivateKeyToPem(RSA rsa)
{
    var builder = new StringBuilder();
    builder.AppendLine("-----BEGIN PRIVATE KEY-----");
    builder.AppendLine(Convert.ToBase64String(rsa.ExportRSAPrivateKey(), Base64FormattingOptions.InsertLineBreaks));
    builder.AppendLine("-----END PRIVATE KEY-----");
    return builder.ToString();
}

static RSA LoadPrivateKey(string keyPath)
{
    return RSA.Create();
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

enum CertContentType
{
    X509,
    Pkcs12
}
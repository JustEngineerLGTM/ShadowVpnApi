using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShadowVPN API", Version = "v1" });
});

var app = builder.Build();

// Use Swagger in development mode
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

        // Загрузите корневой сертификат и ключ CA
        string caCertPath = "/root/openvpn-ca/pki/ca.crt";
        string caKeyPath = "/root/openvpn-ca/pki/private/ca.key"; // путь к закрытому ключу CA
        X509Certificate2 caCert = new X509Certificate2(caCertPath);
        // Чтение и обработка ключа из PEM-формата
        string keyText = File.ReadAllText(caKeyPath);
        string pemContent = keyText
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "");

        RSA caPrivateKey = RSA.Create();
        caPrivateKey.ImportPkcs8PrivateKey(Convert.FromBase64String(pemContent), out _);
        using X509Certificate2 caCertWithPrivateKey = X509Certificate2.CreateFromPem(
            File.ReadAllText(caCertPath),
            File.ReadAllText(caKeyPath));
        
        using (RSA rsa = RSA.Create(2048))
        {
            var certRequest = new CertificateRequest($"CN={username}", rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            DateTime notBefore = DateTime.UtcNow;
            DateTime notAfter = notBefore.AddYears(1);

            X509Certificate2 signedClientCert =
                certRequest.Create(caCertWithPrivateKey, notBefore, notAfter, new byte[] { 1, 2, 3, 4 });
            signedClientCert = new X509Certificate2(signedClientCert.Export(X509ContentType.Cert));

            // Save certificate and key
            string certPath = Path.Combine(outputPath, $"{username}.crt");
            string keyPath = Path.Combine(outputPath, $"{username}.key");
            File.WriteAllText(certPath, ExportCertificateToPem(signedClientCert));
            File.WriteAllText(keyPath, ExportPrivateKeyToPem(rsa));

            // Generate OpenVPN config
            string configContent = $"client\n" +
                                   $"dev tun\n" +
                                   $"proto udp\n" +
                                   $"remote 109.120.132.39 1194\n" +
                                   $"resolv-retry infinite\n" +
                                   $"nobind\n" +
                                   $"persist-key\n" +
                                   $"persist-tun\n\n" +
                                   $"<ca>\n{await File.ReadAllTextAsync("/root/openvpn-ca/pki/ca.crt")}\n</ca>\n\n" +
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

static string ExportCertificateToPem(X509Certificate2 certificate)
{
    var builder = new StringBuilder();
    builder.AppendLine("-----BEGIN CERTIFICATE-----");
    builder.AppendLine(Convert.ToBase64String(certificate.Export(X509ContentType.Cert),
        Base64FormattingOptions.InsertLineBreaks));
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
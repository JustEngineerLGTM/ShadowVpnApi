using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Tomlyn;
using Tomlyn.Model;

var builder = WebApplication.CreateBuilder(args);


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

app.MapPost("/createvpnuser", async (string username) =>
{
    var result = await CreateVpnUserAsync(username);
    return result != null ? Results.Ok(result) : Results.BadRequest("Error creating VPN user.");
}).WithName("CreateVpnUser");

app.MapGet("/getvpnconfig", async (string raw) =>
{
    var lastEqual = raw.LastIndexOf('=');
    if (lastEqual <= 0 || lastEqual == raw.Length - 1)
        return Results.BadRequest("Неверный формат запроса");

    string username = raw[..lastEqual];
    string hash = raw[(lastEqual + 1)..];
    if (!await CheckPasswordAsync(hash))
        return Results.Unauthorized();

    var config = await GetVpnConfigAsync(username);
    return config != null ? Results.Ok(config) : Results.NotFound($"Конфигурация для пользователя {username} не найдена.");
}).WithName("GetVpnConfig");

async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {   
        string vpnconfigPath = "/client/vpnconfig";
        string outputPath = "/etc/openvpn/clients";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        if (File.Exists(vpnconfigPath) )
        {
            
        }
        
        string caCertPath = "/root/openvpn-ca/pki/ca.crt";
        string caKeyPath = "/root/openvpn-ca/pki/private/ca.key"; 
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

        using RSA rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest($"CN={username}", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        DateTime notBefore = DateTime.UtcNow;
        DateTime notAfter = notBefore.AddYears(1);

        X509Certificate2 signedClientCert =
            certRequest.Create(caCertWithPrivateKey, notBefore, notAfter, new byte[] { 1, 2, 3, 4 });
        signedClientCert = new X509Certificate2(signedClientCert.Export(X509ContentType.Cert));
        
        string certPath = Path.Combine(outputPath, $"{username}.crt");
        string keyPath = Path.Combine(outputPath, $"{username}.key");
        File.WriteAllText(certPath, ExportCertificateToPem(signedClientCert));
        File.WriteAllText(keyPath, ExportPrivateKeyToPem(rsa));
        
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
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        return null;
    }
}

async Task<bool> CheckPasswordAsync(string receivedHash)
{
    string configPath = "/etc/openvpn/clients/server_config.toml";

    if (!File.Exists(configPath))
        return false;

    var content = await File.ReadAllTextAsync(configPath);
    var model = Toml.ToModel(content) as TomlTable;

    if (model?["auth"] is TomlTable auth && auth["admin_password"] is string password)
    {
        using var sha256 = SHA256.Create();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hashedBytes = sha256.ComputeHash(passwordBytes);
        var calculatedHash = Convert.ToHexString(hashedBytes).ToLowerInvariant();

        return receivedHash.ToLowerInvariant() == calculatedHash;
    }

    return false;
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
    builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
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
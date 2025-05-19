using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using ShadowVPNApi;
using Tomlyn;
using Tomlyn.Model;

var builder = WebApplication.CreateBuilder(args);

await EnsureOpenVpnServerConfigAsync();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v2", new OpenApiInfo { Title = "ShadowVPN API", Version = "v2" });
});

var app = builder.Build();
await EnsureServerConfigAsync();
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

app.MapPost("/createvpnuser", async (string raw) =>
{
    // raw — строка вида "someuser=abcdef123456..."
    var lastEq = raw.LastIndexOf('=');
    if (lastEq <= 0 || lastEq == raw.Length - 1)
        return Results.BadRequest("Неверный формат запроса");

    var username = raw[..lastEq];
    var hash = raw[(lastEq + 1)..];

    // проверяем хеш
    if (!await CheckPasswordAsync(hash))
        return Results.Unauthorized();

    // создаём пользователя
    var result = await CreateVpnUserAsync(username);
    return result != null
        ? Results.Ok(result)
        : Results.BadRequest("Error creating VPN user.");
}).WithName("CreateVpnUser");


app.MapGet("/getvpnconfig", async (string raw) =>
{
    var lastEqual = raw.LastIndexOf('=');
    if (lastEqual <= 0 || lastEqual == raw.Length - 1)
        return Results.BadRequest("Неверный формат запроса");

    var username = raw[..lastEqual];
    var hash = raw[(lastEqual + 1)..];
    if (!await CheckPasswordAsync(hash))
        return Results.Unauthorized();

    var config = await GetVpnConfigAsync(username);
    return config != null
        ? Results.Ok(config)
        : Results.NotFound($"Конфигурация для пользователя {username} не найдена.");
}).WithName("GetVpnConfig");

async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {
        const string outputPath = "/etc/openvpn/clients";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        const string caCertPath = "/root/openvpn-ca/pki/ca.crt";
        const string caKeyPath = "/root/openvpn-ca/pki/private/ca.key";
        var keyText = File.ReadAllText(caKeyPath);
        var pemContent = keyText
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "");

        var caPrivateKey = RSA.Create();
        caPrivateKey.ImportPkcs8PrivateKey(Convert.FromBase64String(pemContent), out _);
        using var caCertWithPrivateKey = X509Certificate2.CreateFromPem(
            File.ReadAllText(caCertPath),
            File.ReadAllText(caKeyPath));

        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest($"CN={username}", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var notBefore = DateTime.UtcNow;
        var notAfter = notBefore.AddYears(1);

        X509Certificate2 signedClientCert =
            certRequest.Create(caCertWithPrivateKey, notBefore, notAfter, new byte[] { 1, 2, 3, 4 });
#pragma warning disable SYSLIB0057
        signedClientCert = new X509Certificate2(signedClientCert.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057

        var certPath = Path.Combine(outputPath, $"{username}.crt");
        var keyPath = Path.Combine(outputPath, $"{username}.key");
        File.WriteAllText(certPath, ExportCertificateToPem(signedClientCert));
        File.WriteAllText(keyPath, ExportPrivateKeyToPem(rsa));

        var configContent = $"client\n" +
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
    const string configPath = "/etc/openvpn/clients/server_config.toml";

    if (!File.Exists(configPath))
        return false;

    var content = await File.ReadAllTextAsync(configPath);
    var model = Toml.ToModel(content);
    if (model["auth"] is not TomlTable auth || auth["admin_password"] is not string password) return false;
    using var sha256 = SHA256.Create();
    var passwordBytes = Encoding.UTF8.GetBytes(password);
    var hashedBytes = sha256.ComputeHash(passwordBytes);
    var calculatedHash = Convert.ToHexString(hashedBytes).ToLowerInvariant();
    return receivedHash.Equals(calculatedHash, StringComparison.InvariantCultureIgnoreCase);
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

async Task EnsureServerConfigAsync()
{
    const string configDir = "/etc/openvpn/clients";
    var configPath = Path.Combine(configDir, "server_config.toml");
    if (File.Exists(configPath))
        return;

    Directory.CreateDirectory(configDir);

    var table = new TomlTable
    {
        ["auth"] = new TomlTable
        {
            ["admin_password"] = "changeme"
        }
    };
    var toml = Toml.FromModel(table);
    await File.WriteAllTextAsync(configPath, toml);
}

async Task EnsureOpenVpnServerConfigAsync()
{
    const string configPath = "/etc/openvpn/server.conf";
    if (!File.Exists(configPath))
    {
        const string template = """
                                port 1194
                                topology subnet
                                proto udp
                                user nobody
                                dev tun
                                ca /etc/openvpn/ca.crt
                                cert /etc/openvpn/server.crt
                                key /etc/openvpn/server.key
                                dh /etc/openvpn/dh.pem
                                server 10.8.0.0 255.255.255.0
                                push "redirect-gateway def1 bypass-dhcp"
                                push "dhcp-option DNS 8.8.8.8"
                                keepalive 10 120
                                auth SHA256
                                tls-auth /etc/openvpn/ta.key 1
                                tls-server
                                key-direction 0
                                persist-key
                                persist-tun
                                data-ciphers AES-256-GCM:AES-128-GCM:CHACHA20-POLY1305
                                cipher AES-256-CBC
                                status /var/log/openvpn-status.log
                                verb 3
                                """;
        await File.WriteAllTextAsync(configPath, template);
    }

    Directory.CreateDirectory("/etc/openvpn");
    // Генерация ключей и сертификатов
    if (!File.Exists("/etc/openvpn/ca.crt") || !File.Exists("/etc/openvpn/ca.key"))
    {
        using var rsa = RSA.Create(4096);
        var req = new CertificateRequest("CN=ShadowVPN-CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));
        File.WriteAllText("/etc/openvpn/ca.crt", ExportCertificateToPem(cert));
        File.WriteAllText("/etc/openvpn/ca.key", ExportPrivateKeyToPem(rsa));
    }

    if (!File.Exists("/etc/openvpn/server.crt") || !File.Exists("/etc/openvpn/server.key"))
    {
#pragma warning disable SYSLIB0057
        var caCert = new X509Certificate2("/etc/openvpn/ca.crt");
#pragma warning restore SYSLIB0057
        var caKeyPem = await File.ReadAllTextAsync("/etc/openvpn/ca.key");
        var base64 = caKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "").Replace("\r", "");

        var caKey = RSA.Create();
        caKey.ImportPkcs8PrivateKey(Convert.FromBase64String(base64), out _);

        using RSA rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serverCert = req.Create(caCert, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(5),
            new byte[] { 1, 2, 3 });

        File.WriteAllText("/etc/openvpn/server.crt", ExportCertificateToPem(serverCert));
        File.WriteAllText("/etc/openvpn/server.key", ExportPrivateKeyToPem(rsa));
    }

    const string dhPath = "/etc/openvpn/dh.pem";
    if (!File.Exists(dhPath))
    {
        DhParamGenerator.GenerateDhParamsPem(2048, dhPath);
    }

    if (!File.Exists("/etc/openvpn/ta.key"))
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[2048 / 8];
        rng.GetBytes(key);
        File.WriteAllText("/etc/openvpn/ta.key", Convert.ToBase64String(key));
    }
}

app.Run();
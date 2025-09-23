using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ShadowVPNApi.Services;

public static class VpnService
{
    // Получаем публичный ip текущей машины
    static async Task<string> GetPublicIpAsync()
    {
        using var httpClient = new HttpClient();
        return await httpClient.GetStringAsync("https://api.ipify.org");
    }

    // Генерирует конфиг для клиента
    public static async Task<string?> CreateVpnUserAsync(string username)
    {
        try
        {
            const string clientDir = "/etc/openvpn/clients";
            Directory.CreateDirectory(clientDir);

            var caCrt = Path.Combine("/etc/openvpn", "ca.crt");
            var caKey = Path.Combine("/etc/openvpn", "ca.key");

            // создаём сертификат клиента
            using var caWithKey = X509Certificate2.CreateFromPem(
                await File.ReadAllTextAsync(caCrt),
                await File.ReadAllTextAsync(caKey));

            using var rsaClient = RSA.Create(2048);
            var req = new CertificateRequest($"CN={username}", rsaClient, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var notBefore = DateTime.UtcNow;
            var notAfter = notBefore.AddYears(1);
            var clientCert = req.Create(caWithKey, notBefore, notAfter, new byte[]
            {
                1, 2, 3
            });
#pragma warning disable SYSLIB0057
            clientCert = new X509Certificate2(clientCert.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057

            var crtPath = Path.Combine(clientDir, $"{username}.crt");
            var keyPath = Path.Combine(clientDir, $"{username}.key");
            await File.WriteAllTextAsync(crtPath, CertificateService.ExportCertificateToPem(clientCert));
            await File.WriteAllTextAsync(keyPath, CertificateService.ExportPrivateKeyToPem(rsaClient));
            var ip = await GetPublicIpAsync();
            // собираем .ovpn
            var config = new StringBuilder();
            config.AppendLine("client");
            config.AppendLine("dev tun");
            config.AppendLine("proto udp");
            config.AppendLine($"remote {ip.Trim()} 1194");
            config.AppendLine("resolv-retry infinite");
            config.AppendLine("nobind");
            config.AppendLine("persist-key");
            config.AppendLine("persist-tun");
            config.AppendLine("dhcp-option DNS 1.1.1.1");
            config.AppendLine();
            config.AppendLine("<ca>");
            config.AppendLine(await File.ReadAllTextAsync(caCrt));
            config.AppendLine("</ca>");
            config.AppendLine();
            config.AppendLine("<cert>");
            config.AppendLine(await File.ReadAllTextAsync(crtPath));
            config.AppendLine("</cert>");
            config.AppendLine();
            config.AppendLine("<key>");
            config.AppendLine(await File.ReadAllTextAsync(keyPath));
            config.AppendLine("</key>");
            config.AppendLine();
            config.AppendLine("<tls-auth>");
            config.AppendLine(await File.ReadAllTextAsync("/etc/openvpn/ta.key"));
            config.AppendLine("</tls-auth>");
            config.AppendLine("cipher AES-256-CBC");
            config.AppendLine("auth SHA256");
            config.AppendLine("verb 3");

            var ovpnPath = Path.Combine(clientDir, $"{username}.ovpn");
            await File.WriteAllTextAsync(ovpnPath, config.ToString());

            return ovpnPath;
        }
        catch
        {
            return null;
        }
    }

    // Получаем конфиг для юзера
    public static async Task<string?> GetVpnConfigAsync(string username)
    {
        var path = Path.Combine("/etc/openvpn/clients", $"{username}.ovpn");
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : null;
    }
}
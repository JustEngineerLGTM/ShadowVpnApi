using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ShadowVPNApi.OpenVPN
{
    public static class OpenVpnSetup
    {
        private const string ClientsDir = "/etc/openvpn/clients";
        private const string CaCertPath = "/root/openvpn-ca/pki/ca.crt";
        private const string CaKeyPath = "/root/openvpn-ca/pki/private/ca.key";
        private const string TaKeyPath = "/etc/openvpn/ta.key";

        /// <summary>
        /// Генерирует сертификат и ключ для пользователя, и создаёт .ovpn конфиг.
        /// </summary>
        public static async Task<string?> CreateClientConfigAsync(string username)
        {
            Directory.CreateDirectory(ClientsDir);

            var certPath = Path.Combine(ClientsDir, $"{username}.crt");
            var keyPath = Path.Combine(ClientsDir, $"{username}.key");
            var ovpnPath = Path.Combine(ClientsDir, $"{username}.ovpn");

            try
            {
                // Загрузка CA
                var caCertPem = await File.ReadAllTextAsync(CaCertPath);
                var caKeyPem = await File.ReadAllTextAsync(CaKeyPath);

                // Импорт CA ключа
                var caKeyBase64 = caKeyPem
                    .Replace("-----BEGIN PRIVATE KEY-----", "")
                    .Replace("-----END PRIVATE KEY-----", "")
                    .Replace("\r", "").Replace("\n", "");
                using var caKey = RSA.Create();
                caKey.ImportPkcs8PrivateKey(Convert.FromBase64String(caKeyBase64), out _);

                // Создание запроса на сертификацию клиента
                using var rsa = RSA.Create(2048);
                var req = new CertificateRequest(
                    $"CN={username}",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                var notBefore = DateTimeOffset.UtcNow;
                var notAfter = notBefore.AddYears(1);

                //Генерация подписанного сертификата
                var caCert = X509Certificate2.CreateFromPem(caCertPem, caKeyPem);
                var clientCert = req.Create(
                    caCert,
                    notBefore,
                    notAfter,
                    new byte[] { 1, 2, 3, 4 });

#pragma warning disable SYSLIB0057
                clientCert = new X509Certificate2(clientCert.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057

                // Сохранение PEM файлов
                await File.WriteAllTextAsync(certPath, ExportToPem("CERTIFICATE", clientCert.RawData));
                await File.WriteAllTextAsync(keyPath, ExportToPem("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()));

                // Формирование .ovpn конфига
                var tlsAuthPem = File.Exists(TaKeyPath)
                    ? await File.ReadAllTextAsync(TaKeyPath)
                    : "";

                var template = new StringBuilder();
                template.AppendLine("client");
                template.AppendLine("dev tun");
                template.AppendLine("proto udp");
                template.AppendLine("remote 109.120.132.39 1194");
                template.AppendLine("resolv-retry infinite");
                template.AppendLine("nobind");
                template.AppendLine("persist-key");
                template.AppendLine("persist-tun");
                template.AppendLine();
                template.AppendLine("<ca>");
                template.AppendLine(caCertPem.Trim());
                template.AppendLine("</ca>");
                template.AppendLine();
                template.AppendLine("<cert>");
                template.AppendLine(ExportToPem("CERTIFICATE", clientCert.RawData)
                    .Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "").Trim());
                template.AppendLine("</cert>");
                template.AppendLine();
                template.AppendLine("<key>");
                template.AppendLine(ExportToPem("PRIVATE KEY", rsa.ExportPkcs8PrivateKey())
                    .Replace("-----BEGIN PRIVATE KEY-----", "").Replace("-----END PRIVATE KEY-----", "").Trim());
                template.AppendLine("</key>");
                template.AppendLine();
                if (!string.IsNullOrEmpty(tlsAuthPem))
                {
                    template.AppendLine("<tls-auth>");
                    template.AppendLine(tlsAuthPem.Trim());
                    template.AppendLine("</tls-auth>");
                }

                template.AppendLine("remote-cert-tls server");
                template.AppendLine("cipher AES-256-CBC");
                template.AppendLine("auth SHA256");
                template.AppendLine("verb 3");

                await File.WriteAllTextAsync(ovpnPath, template.ToString());
                return ovpnPath;
            }
            catch
            {
                return null;
            }
        }

        private static string ExportToPem(string label, byte[] rawData)
        {
            var b64 = Convert.ToBase64String(rawData, Base64FormattingOptions.InsertLineBreaks);
            return $"-----BEGIN {label}-----\n{b64}\n-----END {label}-----";
        }
    }
}
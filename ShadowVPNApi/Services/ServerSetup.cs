using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;

namespace ShadowVPNApi.Services;

public static class ServerSetup
{
    private const string EtcDir = "/etc/openvpn";
    private const string ConfPath = "/etc/openvpn/server/server.conf";

    public static async Task EnsureOpenVpnServerConfigAsync()
    {
        Directory.CreateDirectory(EtcDir);

        await EnsureServerConfAsync();
        await EnsureCaCertificateAsync();
        await EnsureServerCertificateAsync();
        await EnsureDhParamsAsync();
        await EnsureTlsAuthKeyAsync();
    }

    private static async Task EnsureServerConfAsync()
    {
        if (File.Exists(ConfPath)) return;

        const string template =
            """
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
            push "dhcp-option DNS 8.8.4.4"
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
        await File.WriteAllTextAsync(ConfPath, template);
    }

    private static async Task EnsureCaCertificateAsync()
    {
        var caCrt = Path.Combine(EtcDir, "ca.crt");
        var caKey = Path.Combine(EtcDir, "ca.key");

        if (File.Exists(caCrt) && File.Exists(caKey)) return;

        using var rsaCa = RSA.Create(4096);
        var reqCa = new CertificateRequest("CN=ShadowVPN-CA", rsaCa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        reqCa.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        reqCa.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(reqCa.PublicKey, false));
        reqCa.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DataEncipherment, false));

        var certCa = reqCa.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));

        await File.WriteAllTextAsync(caCrt, CertificateService.ExportCertificateToPem(certCa));
        await File.WriteAllTextAsync(caKey, CertificateService.ExportPrivateKeyToPem(rsaCa));
    }

    private static async Task EnsureServerCertificateAsync()
    {
        var srvCrt = Path.Combine(EtcDir, "server.crt");
        var srvKey = Path.Combine(EtcDir, "server.key");

        if (File.Exists(srvCrt) && File.Exists(srvKey)) return;

        var caCrt = Path.Combine(EtcDir, "ca.crt");
        var caKey = Path.Combine(EtcDir, "ca.key");

        using var caCertificate = new X509Certificate2(caCrt);
        var caPemKey = await File.ReadAllTextAsync(caKey);
        var base64 = caPemKey
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "");

        using var rsaCaKey = RSA.Create();
        rsaCaKey.ImportPkcs8PrivateKey(Convert.FromBase64String(base64), out _);

        using var rsaSrv = RSA.Create(2048);
        var reqSrv = new CertificateRequest("CN=server", rsaSrv, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);


        reqSrv.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        reqSrv.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        reqSrv.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            },
            true));
        reqSrv.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(reqSrv.PublicKey, false));

        var certSrv = reqSrv.Create(caCertificate.CopyWithPrivateKey(rsaCaKey),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(5), new byte[]
            {
                1, 2, 3
            });

        await File.WriteAllTextAsync(srvCrt, CertificateService.ExportCertificateToPem(certSrv));
        await File.WriteAllTextAsync(srvKey, CertificateService.ExportPrivateKeyToPem(rsaSrv));
    }

    private static async Task EnsureDhParamsAsync()
    {
        var dhPath = Path.Combine(EtcDir, "dh.pem");
        if (File.Exists(dhPath)) return;

        const string dhPem =
            """
            -----BEGIN DH PARAMETERS-----
            MIIBCAKCAQEAvYf9dHftuT2T1xJ4nsyUjW6n9R7lx9KHzBy8Z8BPlXdw6twLoQej
            7iFqWmBJPpUe3MvCkMmjgmNDi0sEO6QZrmZz+xvVb6dkPGViD5L5qEslj3V4MaI8
            rhVmTq13ZzvD+ZFcA4K51Tx7AnApJSWgDFLBz/c0UX1SxiObO6TSqH8JSYo61cSE
            tXZMH8PBJhk4EHO9/SmppDE1ekmlvYJbDWnKIgQHnF6UCIkt7sbQCD1m1Hk+S2J4
            Kmvhnsmxv8FJqsn5uRQQ0yfOuzAAgxusK8wKPywW3v7hKLV5cNB4nfbXnAozHMP0
            b0v8Xw/h7byy4SCDPVvVWrHDxxlKYdAzJQIBAg==
            -----END DH PARAMETERS-----
            """;

        await File.WriteAllTextAsync(dhPath, dhPem);
    }


    private static async Task EnsureTlsAuthKeyAsync()
    {
        var taKey = Path.Combine(EtcDir, "ta.key");
        if (File.Exists(taKey)) return;

        using var rng = RandomNumberGenerator.Create();
        var key = new byte[256];
        rng.GetBytes(key);

        var sb = new StringBuilder();
        sb.AppendLine("#");
        sb.AppendLine("# 2048 bit OpenVPN static key");
        sb.AppendLine("#");
        sb.AppendLine("-----BEGIN OpenVPN Static key V1-----");

        for (int i = 0; i < key.Length; i += 16)
        {
            sb.AppendLine(BitConverter.ToString(key, i, 16).Replace("-", "").ToLower());
        }

        sb.AppendLine("-----END OpenVPN Static key V1-----");

        await File.WriteAllTextAsync(taKey, sb.ToString());
    }

    public static async Task EnsureServerConfigAsync()
    {
        const string clientsDir = "/etc/openvpn/clients";
        Directory.CreateDirectory(clientsDir);

        var cfg = Path.Combine(clientsDir, "server_config.toml");

        if (!File.Exists(cfg))
        {
            var table = new TomlTable
            {
                ["auth"] = new TomlTable
                {
                    ["admin_password"] = "changeme"
                }
            };
            var toml = Toml.FromModel(table);
            await File.WriteAllTextAsync(cfg, toml);
        }
    }
}
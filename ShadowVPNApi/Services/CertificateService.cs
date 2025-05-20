namespace ShadowVPNApi.Services;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

public static class CertificateService
{
    public static string ExportCertificateToPem(X509Certificate2 certificate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN CERTIFICATE-----");
        builder.AppendLine(Convert.ToBase64String(
            certificate.Export(X509ContentType.Cert),
            Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END CERTIFICATE-----");
        return builder.ToString();
    }

    public static string ExportPrivateKeyToPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(
            rsa.ExportPkcs8PrivateKey(),
            Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }
}
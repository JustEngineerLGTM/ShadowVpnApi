// Не забудьте в csproj подключить пакет:
// <PackageReference Include="BouncyCastle.NetCore" Version="1.9.0" />

using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace ShadowVPNApi;

static class DhParamGenerator
{
    /// <summary>
    /// Генерирует DH-параметры в PEM-формате (PKCS#3).
    /// </summary>
    public static void GenerateDhParamsPem(int keySizeBits, string outputPath)
    {
        // Генерация p и g
        var generator = new DHParametersGenerator();
        generator.Init(keySizeBits, 20, new SecureRandom());

        DHParameters dhParams = generator.GenerateParameters();
        // OpenSSL dhparam по умолчанию берёт g = 2
        var realParams = new DHParameters(dhParams.P, BigInteger.Two);

        // Формируем ASN.1-последовательность {p, g}
        var seq = new Asn1EncodableVector();
        seq.Add(new DerInteger(realParams.P));
        seq.Add(new DerInteger(realParams.G));
        byte[] derEncoded = new DerSequence(seq).GetDerEncoded();

        // Кодируем в Base64
        string b64 = Convert.ToBase64String(derEncoded);

        // Записываем в PEM
        using var writer = new StreamWriter(outputPath, false, Encoding.ASCII);
        writer.WriteLine("-----BEGIN DH PARAMETERS-----");
        for (var i = 0; i < b64.Length; i += 64)
            writer.WriteLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
        writer.WriteLine("-----END DH PARAMETERS-----");
    }
}
using Microsoft.OpenApi.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShadowVPN API", Version = "v1" });
});

var app = builder.Build();

// Always enable Swagger in this case since authentication isn't implemented
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShadowVPN API v1");
    options.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();

app.MapPost("/createvpnuser", async (string username) =>
{
    // Validate username to prevent command injection
    if (string.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9_-]+$"))
    {
        return Results.BadRequest("Invalid username. Use only letters, numbers, underscore and hyphen.");
    }

    var result = await CreateVpnUserAsync(username);
    return result != null ? Results.Ok(new { ConfigPath = result }) : Results.BadRequest("Error creating VPN user.");
})
.WithName("CreateVpnUser")
.WithOpenApi();

app.MapGet("/getvpnconfig", async (string username) =>
{
    // Validate username to prevent path traversal
    if (string.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9_-]+$"))
    {
        return Results.BadRequest("Invalid username. Use only letters, numbers, underscore and hyphen.");
    }

    var config = await GetVpnConfigAsync(username);
    return config != null ? Results.Ok(config) : Results.NotFound($"Config for user {username} not found.");
})
.WithName("GetVpnConfig")
.WithOpenApi();

async Task<string?> CreateVpnUserAsync(string username)
{
    try
    {
        string easyRsaPath = "/root/openvpn-ca";
        string outputPath = "/etc/openvpn/clients";

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        // Check if vars file exists and source it first
        bool varsFileExists = File.Exists(Path.Combine(easyRsaPath, "vars"));
        
        // Improved command that properly sets batch mode and specifies Common Name
        string command;
        if (varsFileExists)
        {
            // Source vars first, then run easyrsa with proper environment variables
            command = $"cd {easyRsaPath} && source ./vars && export EASYRSA_BATCH=1 EASYRSA_REQ_CN={username} && ./easyrsa --batch build-client-full {username} nopass";
        }
        else
        {
            // Direct command with environment variables
            command = $"cd {easyRsaPath} && export EASYRSA_BATCH=1 EASYRSA_REQ_CN={username} && ./easyrsa --batch build-client-full {username} nopass";
        }

        Console.WriteLine($"Executing command: {command}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) => 
        { 
            if (!string.IsNullOrEmpty(e.Data))
            {
                output.AppendLine(e.Data);
                Console.WriteLine("[OUT] " + e.Data);
            }
        };
        process.ErrorDataReceived += (sender, e) => 
        { 
            if (!string.IsNullOrEmpty(e.Data))
            {
                error.AppendLine(e.Data);
                Console.WriteLine("[ERR] " + e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Give more time for certificate generation
        if (!process.WaitForExit(60000))  // Increase timeout to 60 seconds
        {
            process.Kill(true);  // Kill process tree
            Console.WriteLine("ERROR: Process timeout.");
            return null;
        }

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"ERROR: Certificate creation failed with exit code {process.ExitCode}");
            Console.WriteLine($"Error output: {error}");
            return null;
        }

        // Check if required files exist before reading
        string caCertPath = "/etc/openvpn/ca.crt";
        string userCertPath = Path.Combine(easyRsaPath, "pki", "issued", $"{username}.crt");
        string userKeyPath = Path.Combine(easyRsaPath, "pki", "private", $"{username}.key");
        string tlsAuthPath = "/etc/openvpn/ta.key";

        if (!File.Exists(caCertPath))
        {
            Console.WriteLine($"ERROR: CA certificate not found at {caCertPath}");
            return null;
        }
        
        if (!File.Exists(userCertPath))
        {
            Console.WriteLine($"ERROR: User certificate not found at {userCertPath}");
            return null;
        }
        
        if (!File.Exists(userKeyPath))
        {
            Console.WriteLine($"ERROR: User key not found at {userKeyPath}");
            return null;
        }
        
        if (!File.Exists(tlsAuthPath))
        {
            Console.WriteLine($"ERROR: TLS auth key not found at {tlsAuthPath}");
            return null;
        }

        string certPath = Path.Combine(outputPath, $"{username}.ovpn");
        
        // Read certificate files
        string caCert = await File.ReadAllTextAsync(caCertPath);
        string userCert = await File.ReadAllTextAsync(userCertPath);
        string userKey = await File.ReadAllTextAsync(userKeyPath);
        string tlsAuth = await File.ReadAllTextAsync(tlsAuthPath);
        
        // Extract certificate from full cert file (remove header/footer if needed)
        string certContent = ExtractCertificateContent(userCert, "CERTIFICATE");
        string keyContent = ExtractCertificateContent(userKey, "PRIVATE KEY");

        string configContent = $"client\n" +
                               $"dev tun\n" +
                               $"proto udp\n" +
                               $"remote 109.120.132.39 1194\n" +
                               $"resolv-retry infinite\n" +
                               $"nobind\n" +
                               $"persist-key\n" +
                               $"persist-tun\n\n" +
                               $"<ca>\n" +
                               $"{caCert}\n" +
                               $"</ca>\n\n" +
                               $"<cert>\n" +
                               $"{certContent}\n" +
                               $"</cert>\n\n" +
                               $"<key>\n" +
                               $"{keyContent}\n" +
                               $"</key>\n\n" +
                               $"<tls-auth>\n" +
                               $"{tlsAuth}\n" +
                               $"</tls-auth>\n" +
                               $"key-direction 1\n" +  // Added key-direction
                               $"cipher AES-256-CBC\n" +
                               $"auth SHA256\n" +
                               $"verb 3";

        await File.WriteAllTextAsync(certPath, configContent);
        Console.WriteLine($"Successfully created VPN config at {certPath}");
        return certPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return null;
    }
}

// Helper method to extract certificate content
string ExtractCertificateContent(string fullCertText, string certType)
{
    // If the content already looks like a properly formatted certificate, return it as is
    if (fullCertText.Contains($"-----BEGIN {certType}-----") && 
        fullCertText.Contains($"-----END {certType}-----"))
    {
        return fullCertText;
    }
    
    // Otherwise try to extract just the certificate part
    var match = Regex.Match(fullCertText, $"-----BEGIN.*?{certType}-----.*?-----END.*?{certType}-----", 
                           RegexOptions.Singleline);
    return match.Success ? match.Value : fullCertText;
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
        
        Console.WriteLine($"Config file not found: {configPath}");
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception in GetVpnConfigAsync: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return null;
    }
}

app.Run();
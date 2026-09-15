using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Expenses.Integration.Tests.Harness;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from api-surface: "The HTTP interface can be served over HTTPS".
/// </summary>
/// <remarks>
/// HTTPS is Kestrel's own configuration and the host adds no code for it (D6), so these passed on
/// their first run. They are here to fail the day someone adds HTTPS redirection or an HTTPS-only
/// binding, which would break every HTTP caller — e2e, the desktop client, the healthcheck. With
/// <c>app.UseHttpsRedirection()</c> added, the two that go over plain HTTP were seen failing; the
/// unreadable-certificate test differs from the passing HTTPS ones only in the path it points at.
/// Unlike the rest of the suite they run on real Kestrel, because a TLS handshake is the thing
/// under test and the in-memory server has none.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class HttpsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string PhoneOrigin = "https://phone.test:5173";

    private readonly string _certificates = Directory.CreateTempSubdirectory("expenses-api-https-tests-").FullName;

    private readonly List<ExpensesApi> _hosts = [];

    private string _thumbprint = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        _thumbprint = certificate.Thumbprint;
        await File.WriteAllTextAsync(Path.Combine(_certificates, "lan.pem"), certificate.ExportCertificatePem());
        await File.WriteAllTextAsync(Path.Combine(_certificates, "lan-key.pem"), key.ExportPkcs8PrivateKeyPem());
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        Directory.Delete(_certificates, recursive: true);
    }

    [Fact]
    public async Task A_configured_certificate_serves_HTTPS()
    {
        var (http, https) = Addresses(Start(withCertificate: true));
        string? presented = null;
        using var client = Client((_, certificate, _, _) =>
        {
            presented = certificate?.Thumbprint;
            return true;
        });

        string overHttps = await client.GetStringAsync($"{https}/categories");
        string overHttp = await client.GetStringAsync($"{http}/categories");

        Assert.Equal(_thumbprint, presented);
        Assert.Equal(overHttp, overHttps);
    }

    [Fact]
    public async Task HTTP_is_not_redirected()
    {
        var (http, _) = Addresses(Start(withCertificate: true));
        using var client = Client((_, _, _, _) => true);

        var response = await client.GetAsync($"{http}/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Uri.UriSchemeHttp, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task Cross_origin_headers_over_HTTPS()
    {
        var (_, https) = Addresses(Start(withCertificate: true));
        using var client = Client((_, _, _, _) => true);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{https}/categories");
        request.Headers.Add("Origin", PhoneOrigin);

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed));
        Assert.Equal(PhoneOrigin, Assert.Single(allowed));
    }

    [Fact]
    public void No_certificate_means_HTTP_only()
    {
        var addresses = ServerAddresses(Start(withCertificate: false));

        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.StartsWith("http://", address, StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreadable_certificate_stops_the_host()
    {
        var host = Host(
            ("urls", "http://127.0.0.1:0;https://127.0.0.1:0"),
            ("Kestrel:Certificates:Default:Path", Path.Combine(_certificates, "missing.pem")),
            ("Kestrel:Certificates:Default:KeyPath", Path.Combine(_certificates, "missing-key.pem")));

        var error = Record.Exception(() =>
        {
            host.UseKestrel();
            host.StartServer();
        });

        Assert.NotNull(error);
    }

    private static HttpClient Client(Func<HttpRequestMessage, X509Certificate2?, X509Chain?, System.Net.Security.SslPolicyErrors, bool> validate)
        => new(new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = validate });

    private static string[] ServerAddresses(ExpensesApi host)
        => [.. host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses];

    private static (string Http, string Https) Addresses(ExpensesApi host)
    {
        var addresses = ServerAddresses(host);
        return (
            addresses.Single(address => address.StartsWith("http://", StringComparison.Ordinal)),
            addresses.Single(address => address.StartsWith("https://", StringComparison.Ordinal)));
    }

    private ExpensesApi Start(bool withCertificate)
    {
        var host = withCertificate
            ? Host(
                ("urls", "http://127.0.0.1:0;https://127.0.0.1:0"),
                ("Kestrel:Certificates:Default:Path", Path.Combine(_certificates, "lan.pem")),
                ("Kestrel:Certificates:Default:KeyPath", Path.Combine(_certificates, "lan-key.pem")))
            : Host(("urls", "http://127.0.0.1:0"));

        host.UseKestrel();
        host.StartServer();
        return host;
    }

    private ExpensesApi Host(params (string Key, string Value)[] settings)
    {
        var host = new ExpensesApi(postgres.ConnectionString, [.. settings, ("Cors:AllowedOrigins:0", PhoneOrigin)])
        {
            Environment = "Testing",
        };

        _hosts.Add(host);
        return host;
    }
}

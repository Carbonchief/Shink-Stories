using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class SupabaseResponseCompressionTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(10000)]
    [DataRow("gzip")]
    [DataRow("deflate")]
    [DataRow("br")]
    [DataRow("identity")]
    public async Task SupabaseClient_NegotiatesCompressionAndReadsOriginalJson(string encoding)
    {
        const string json = "[{\"title\":\"Schink se stories\",\"is_completed\":true}]";
        var cancellationToken = testContext.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RespondAsync();
        var services = new ServiceCollection();
        services.AddHttpClient("supabase-test").WithSupabaseResponseCompression();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("supabase-test");

        using var response = await client.GetAsync($"http://127.0.0.1:{port}/rest/v1/stories", cancellationToken);
        var actual = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestHeaders = await server;

        Assert.AreEqual(json, actual);
        Assert.IsEmpty(response.Content.Headers.ContentEncoding);
        Assert.Contains("gzip", requestHeaders);
        Assert.Contains("deflate", requestHeaders);
        Assert.Contains("br", requestHeaders);

        async Task<string> RespondAsync()
        {
            using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var network = connection.GetStream();
            using var reader = new StreamReader(network, Encoding.ASCII, leaveOpen: true);
            var headers = new StringBuilder();
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            {
                headers.AppendLine(line);
            }

            using var buffer = new MemoryStream();
            var bytes = Encoding.UTF8.GetBytes(json);
            if (encoding == "identity")
            {
                buffer.Write(bytes);
            }
            else
            {
                using Stream compressor = encoding switch
                {
                    "gzip" => new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                    "deflate" => new DeflateStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                    _ => new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true)
                };
                compressor.Write(bytes);
            }

            var encodingHeader = encoding == "identity" ? "" : $"Content-Encoding: {encoding}\r\n";
            await network.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {buffer.Length}\r\n{encodingHeader}Connection: close\r\n\r\n"), cancellationToken);
            await network.WriteAsync(buffer.ToArray(), cancellationToken);
            return headers.ToString();
        }
    }
}

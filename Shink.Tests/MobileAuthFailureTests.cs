using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileAuthFailureTests
{
    private const string ClientId = "019a1234567840008000123456789001";
    private const string ServerId = "019a1234567840008000123456789002";

    [TestMethod]
    public void Create_Empty400_ShowsRetryInstructionsAndRetainsDiagnosticDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);

        var failure = MobileAuthFailure.Create(response, "", ClientId);

        Assert.AreEqual("Kon nie nou inteken nie. Probeer asseblief weer. As dit aanhou, kontak ons vir hulp.", failure.Message);
        Assert.AreEqual(400, failure.StatusCode);
        Assert.AreEqual(ClientId, failure.RequestId);
        Assert.AreEqual("empty_response", failure.Reason);
        Assert.AreEqual("empty", failure.ResponseBodyKind);
    }

    [TestMethod]
    [DataRow("<html>proxy error with private-token</html>", "html")]
    [DataRow("upstream error with private-token", "other")]
    [DataRow("{\"message\":", "other")]
    [DataRow("[\"private-token\"]", "json")]
    [DataRow("{\"message\":42}", "json")]
    public void Create_UnexpectedBody_UsesSafeFallback(string body, string expectedKind)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var failure = MobileAuthFailure.Create(response, body, ClientId);

        Assert.StartsWith("Kon nie nou inteken nie.", failure.Message);
        Assert.AreEqual(expectedKind, failure.ResponseBodyKind);
        Assert.DoesNotContain("private-token", failure.Message);
    }

    [TestMethod]
    public void Create_KnownCredentialFailure_PreservesServerMessageAndCorrelation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Headers.Add(MobileAuthFailure.RequestIdHeader, ServerId);
        response.Headers.Add("X-Schink-Auth-Reason", "invalid_credentials");
        const string message = "Ongeldige e-pos of wagwoord. Probeer asseblief weer.";

        var failure = MobileAuthFailure.Create(response, JsonSerializer.Serialize(new { message }), ClientId);

        Assert.AreEqual(message, failure.Message);
        Assert.AreEqual(ServerId, failure.RequestId);
        Assert.AreEqual("invalid_credentials", failure.Reason);
    }

    [TestMethod]
    [DataRow(429, "rate_limited", "Te veel aanmeldpogings.")]
    [DataRow(503, "server_error", "Ons kan jou nie nou inteken nie.")]
    public void Create_ServiceFailure_ExplainsAppropriateNextStep(int status, string reason, string prefix)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        var failure = MobileAuthFailure.Create(response, "", ClientId);

        Assert.AreEqual(reason, failure.Reason);
        Assert.StartsWith(prefix, failure.Message);
    }

    [TestMethod]
    public void AddDiagnostics_UntrustedResponse_ExcludesMessagesBodiesAndArbitraryHeaderValues()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Headers.TryAddWithoutValidation(MobileAuthFailure.RequestIdHeader, "private-token");
        response.Headers.TryAddWithoutValidation("X-Schink-Auth-Reason", "password=private-token");
        var failure = MobileAuthFailure.Create(response, """{"message":"private-token"}""", ClientId);
        var properties = new Dictionary<string, object> { ["status_code"] = 400 };

        MobileAuthFailure.AddDiagnostics(properties, failure, ClientId);

        Assert.AreEqual(400, properties["status_code"]);
        Assert.AreEqual(ClientId, properties["request_id"]);
        Assert.AreEqual("auth_rejected", properties["failure_reason"]);
        Assert.AreEqual("json", properties["response_body_kind"]);
        Assert.HasCount(4, properties);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(properties));
    }

    [TestMethod]
    [DataRow(0, "no_response")]
    [DataRow(200, "response_processing_failed")]
    public void AddDiagnostics_TransportOrParsingFailure_DistinguishesWhetherResponseArrived(int status, string reason)
    {
        var properties = new Dictionary<string, object> { ["status_code"] = status };

        MobileAuthFailure.AddDiagnostics(properties, new InvalidOperationException("private-token"), ClientId);

        Assert.AreEqual(reason, properties["failure_reason"]);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(properties));
    }

    [TestMethod]
    public void SignInRequest_WiresCorrelationAndFailureHandlingWithoutRetryingPosts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shink.Mobile", "Services", "MobileApiClient.cs")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "Shink.Mobile", "Services", "MobileApiClient.cs"));

        Assert.Contains("request.Headers.Add(MobileAuthFailure.RequestIdHeader, authRequestId)", source);
        Assert.Contains("EnsureSuccessAsync(response, cancellationToken, authRequestId)", source);
        Assert.Contains("MobileAuthFailure.AddDiagnostics(properties, exception, authRequestId)", source);
        Assert.DoesNotContain("SendPostWithTransientRetry", source);
    }
}

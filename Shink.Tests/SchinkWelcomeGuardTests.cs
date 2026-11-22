using System.Net;
using System.Text;
using System.Text.Json;
using Schink.WelcomeGuard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class SchinkWelcomeGuardTests
{
    [TestMethod]
    [DataRow("full,full", true)]
    [DataRow("full,full,missing", true)]
    [DataRow("full,missing,full", false)]
    [DataRow("full,other,full", false)]
    [DataRow("full,delivered", false)]
    [DataRow("full,other-recipient", false)]
    [DataRow("full,missing", false)]
    public async Task AppliesOnlyToAdjacentConfirmedSends(string statuses, bool expected)
    {
        using var provider = new Provider(statuses.Split(','));
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://example.test") };
        var guard = new WelcomeGuard(http);
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(temp.FullName, "cursor");
            Assert.AreEqual(expected ? 1 : 0, await guard.CheckAsync(path));
            Assert.AreEqual(0, await guard.CheckAsync(path));
            Assert.AreEqual(expected ? 1 : 0, provider.Cancellations);
            Assert.IsTrue(File.Exists(path));
        }
        finally { temp.Delete(true); }
    }

    [TestMethod]
    public void SeparateRunsForTheSameRecipientCannotShareAStreak()
    {
        var a = new WelcomeGuard.Send("a", "reader@example.test");
        var b = a with { Id = "b" };
        var c = a with { Id = "c" };
        var d = a with { Id = "d" };
        HashSet<WelcomeGuard.Send> full = [b, c];
        Assert.IsFalse(WelcomeGuard.HasPair(new("run-1", DateTimeOffset.UtcNow, [a, b]), full));
        Assert.IsFalse(WelcomeGuard.HasPair(new("run-2", DateTimeOffset.UtcNow, [c, d]), full));
        Assert.IsFalse(WelcomeGuard.HasPair(new("duplicate", DateTimeOffset.UtcNow, [b, b]), full));
    }

    [TestMethod]
    public async Task FailurePreservesCheckpointAndNextRunRetries()
    {
        using var provider = new Provider(["full", "full"]) { FailReads = true };
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://example.test") };
        var guard = new WelcomeGuard(http);
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(temp.FullName, "cursor");
            await File.WriteAllTextAsync(path, """{"Cursor":"old","ReconciledAt":"2026-11-01T00:00:00Z"}""");
            var previous = await File.ReadAllTextAsync(path);
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => guard.CheckAsync(path));
            Assert.AreEqual(previous, await File.ReadAllTextAsync(path));
            provider.FailReads = false;
            Assert.AreEqual(1, await guard.CheckAsync(path));
        }
        finally { temp.Delete(true); }
    }

    private sealed class Provider(string[] outcomes) : HttpMessageHandler
    {
        internal bool FailReads { get; set; }
        internal int Cancellations { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var ids = Enumerable.Range(0, outcomes.Length);
            object result;
            if (path == WelcomeGuard.Events)
            {
                // Arrival order differs from send order. The first email has a duplicate webhook, too.
                var data = ids.Prepend(0).Where(i => outcomes[i] != "missing").Select((i, j) => new
                {
                    id = $"{i}-{j}", type = "email.bounced", created_at = "2026-11-10T12:00:00Z"
                });
                result = new { has_more = false, data };
            }
            else if (path == WelcomeGuard.Runs)
            {
                var running = request.RequestUri.Query.Contains("status=running") && Cancellations == 0;
                result = new { has_more = false, data = running ? new[] { new { id = "run" } } : [] };
            }
            else if (path == WelcomeGuard.Runs + "/run")
            {
                result = new { id = "run", status = Cancellations == 0 ? "running" : "cancelled",
                    started_at = "2026-11-01 10:00:00.123456+00", steps = ids.Reverse().Select(i => new
                    {
                        type = "send_email", status = "completed", completed_at = $"2026-11-0{i+1}T10:00:00Z",
                        output = new { email_id = $"mail{i}", to = "reader@example.test" }
                    }) };
            }
            else if (path.StartsWith(WelcomeGuard.Events + "/"))
            {
                if (FailReads) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                var i = int.Parse(path.Split('/')[^1].Split('-')[0]);
                result = new { payload = new { created_at = "2026-11-10T10:00:00Z", data = new
                {
                    email_id = $"mail{i}", to = new[] { outcomes[i] == "other-recipient" ? "else@example.test" : "reader@example.test" },
                    bounce = new { subType = outcomes[i] == "other" ? "General" : "MailboxFull" }
                } } };
            }
            else if (path.StartsWith("/emails/mail"))
                result = new { last_event = outcomes[int.Parse(path["/emails/mail".Length..])] == "delivered" ? "delivered" : "bounced" };
            else if (path == WelcomeGuard.Runs + "/run/cancel" && request.Method == HttpMethod.Post)
            {
                Cancellations++;
                result = new { id = "run" };
            }
            else throw new AssertFailedException("Unexpected request " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") });
        }
    }
}

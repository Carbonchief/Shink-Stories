using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Schink.WelcomeGuard;

internal sealed class WelcomeGuard(HttpClient http)
{
    internal const string Runs = "/automations/019ff20f-1705-73dd-93fa-89d30970f380/runs";
    internal const string Events = "/webhooks/d8debf46-2b84-4625-b643-6d1488d36e5d/events";
    private static readonly string[] Active = ["running", "quota_exceeded"];

    private sealed record Checkpoint(string Cursor, DateTimeOffset ReconciledAt);
    internal sealed record Send(string? Id, string? Recipient);
    internal sealed record Run(string Id, DateTimeOffset Started, Send[] Sends);

    public async Task<int> CheckAsync(string checkpointPath)
    {
        var state = File.Exists(checkpointPath)
            ? JsonSerializer.Deserialize<Checkpoint>(await File.ReadAllTextAsync(checkpointPath))
            : null;
        var due = state == null || DateTimeOffset.UtcNow - state.ReconciledAt >= TimeSpan.FromHours(1);
        string? newest = null;
        await foreach (var item in Items(Events))
        {
            var id = Required(item, "id");
            newest ??= id;
            if (id == state?.Cursor) break;
            if (due || (string?)item["type"] == "email.bounced")
            {
                due = true;
                break;
            }
        }

        var matched = 0;
        if (due)
        {
            var candidates = new List<Run>();
            foreach (var status in Active)
            {
                await foreach (var item in Items(Runs, status))
                {
                    var run = await Request(Runs + "/" + Required(item, "id"));
                    if (Active.Contains((string?)run["status"]))
                        candidates.Add(ParseRun(run));
                }
            }
            matched = await ReconcileAsync(candidates, apply: true);
        }
        if (newest != null && (due || newest != state?.Cursor))
        {
            var next = new Checkpoint(newest, due ? DateTimeOffset.UtcNow : state!.ReconciledAt);
            Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath)!);
            await File.WriteAllTextAsync(checkpointPath + ".tmp", JsonSerializer.Serialize(next));
            File.Move(checkpointPath + ".tmp", checkpointPath, overwrite: true);
        }
        Console.WriteLine($"Checked webhook history. Reconciled={due}, matched={matched}.");
        return matched;
    }

    internal static Run ParseRun(JsonNode run)
    {
        var sends = run["steps"]!.AsArray().OfType<JsonObject>()
            .Where(s => (string?)s["type"] == "send_email" && (string?)s["status"] == "completed")
            .OrderBy(s => Time(s, "completed_at"))
            .Select(s => new Send((string?)s["output"]?["email_id"], Recipient(s["output"]?["to"]))).ToArray();
        return new Run(Required(run, "id"), Time(run, "started_at"), sends);
    }

    internal static bool HasPair(Run run, HashSet<Send> full)
    {
        Send? previous = null;
        foreach (var send in run.Sends)
        {
            if (send.Id == null || string.IsNullOrEmpty(send.Recipient) || !full.Contains(send))
            {
                previous = null;
                continue;
            }
            if (previous != null && previous.Id != send.Id && previous.Recipient == send.Recipient)
                return true;
            previous = send;
        }
        return false;
    }

    internal async Task<int> ReconcileAsync(IEnumerable<Run> runs, bool apply)
    {
        var candidates = runs.Where(r => r.Sends.Length >= 2).ToList();
        if (candidates.Count == 0) return 0;
        var earliest = candidates.Min(r => r.Started);
        var wanted = candidates.SelectMany(r => r.Sends).ToHashSet();
        var outcomes = new Dictionary<Send, (DateTimeOffset Time, bool Full)>();
        await foreach (var item in Items(Events))
        {
            if (Time(item, "created_at") < earliest) break;
            if ((string?)item["type"] != "email.bounced") continue;
            var detail = await Request(Events + "/" + Required(item, "id"));
            var payload = detail["payload"]!;
            var data = payload["data"]!;
            var key = new Send((string?)data["email_id"], Recipient(data["to"]));
            if (!wanted.Contains(key)) continue;
            var time = Time(payload, "created_at");
            var full = (string?)data["bounce"]?["subType"] == "MailboxFull";
            if (!outcomes.TryGetValue(key, out var old) || time > old.Time || (time == old.Time && !full))
                outcomes[key] = (time, full);
        }
        var reported = outcomes.Where(kv => kv.Value.Full).Select(kv => kv.Key).ToHashSet();
        if (!candidates.Any(r => HasPair(r, reported))) return 0;
        var verified = new HashSet<Send>();
        foreach (var send in reported)
        {
            // A later delivery can supersede an earlier bounce report.
            var email = await Request("/emails/" + send.Id);
            if ((string?)email["last_event"] == "bounced") verified.Add(send);
        }
        var matched = 0;
        foreach (var run in candidates.Where(r => HasPair(r, verified)))
        {
            if (apply)
            {
                var path = Runs + "/" + run.Id;
                try
                {
                    await Request(path + "/cancel", HttpMethod.Post);
                }
                catch (HttpRequestException)
                {
                    var current = await Request(path);
                    if (Active.Contains((string?)current["status"])) throw;
                    continue;
                }
                if ((string?)(await Request(path))["status"] != "cancelled")
                    throw new InvalidOperationException("Resend did not confirm the cancellation.");
            }
            Console.WriteLine($"{(apply ? "Cancelled" : "Matched")} welcome run {run.Id}.");
            matched++;
        }
        return matched;
    }

    private async IAsyncEnumerable<JsonNode> Items(string path, string? status = null)
    {
        var query = path + "?limit=100" + (status == null ? "" : "&status=" + status);
        string? cursor = null;
        do
        {
            var page = await Request(query + (cursor == null ? "" : "&after=" + Uri.EscapeDataString(cursor)));
            var items = page["data"]!.AsArray();
            foreach (var item in items) yield return item!;
            cursor = (bool)page["has_more"]! ? Required(items[^1]!, "id") : null;
        } while (cursor != null);
    }

    private async Task<JsonNode> Request(string path, HttpMethod? method = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1 << attempt));
                continue;
            }
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }
    }

    private static string Required(JsonNode item, string key) =>
        (string?)item[key] ?? throw new InvalidDataException("Missing Resend field " + key);

    private static DateTimeOffset Time(JsonNode item, string key) =>
        DateTimeOffset.Parse(Required(item, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string? Recipient(JsonNode? value)
    {
        if (value is JsonArray recipients) value = recipients.Count == 1 ? recipients[0] : null;
        return ((string?)value)?.Trim().ToLowerInvariant();
    }
}

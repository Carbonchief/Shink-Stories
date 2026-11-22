using System.Net.Http.Headers;
using Schink.WelcomeGuard;

var key = Environment.GetEnvironmentVariable("Resend__ApiKey");
if (string.IsNullOrWhiteSpace(key))
    throw new InvalidOperationException("The Schink Resend API key is missing.");

using var http = new HttpClient { BaseAddress = new Uri("https://api.resend.com"), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
http.DefaultRequestHeaders.UserAgent.ParseAdd("Schink-WelcomeGuard/1.0");
var guard = new WelcomeGuard(http);
await guard.CheckAsync("/home/data/schink-welcome-guard/webhook-cursor");

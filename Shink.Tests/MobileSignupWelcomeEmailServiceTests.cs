using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileSignupWelcomeEmailServiceTests
{
    [TestMethod]
    [DataRow(false, true, false, 0)]
    [DataRow(true, false, false, 0)]
    [DataRow(true, true, true, 0)]
    [DataRow(true, true, false, 1)]
    public async Task TryStartAsync_OnlyEnrolsConsentingNewFreeAccounts(
        bool consent, bool isNew, bool paid, int expectedCalls)
    {
        var ledger = DispatchProxy.Create<ISubscriptionLedgerService, LedgerProxy>();
        ((LedgerProxy)(object)ledger).Paid = paid;
        var emails = new RecordingEmails();
        var service = new MobileSignupWelcomeEmailService(ledger, emails,
            NullLogger<MobileSignupWelcomeEmailService>.Instance);

        await service.TryStartAsync("ouer@example.com", consent, isNew, "Ouer", "Van");

        Assert.AreEqual(expectedCalls, emails.Calls);
        if (expectedCalls > 0)
        {
            Assert.AreEqual("ouer@example.com", emails.Email);
            Assert.AreEqual("Ouer", emails.FirstName);
            Assert.AreEqual("Van", emails.LastName);
        }
        Assert.AreEqual(consent && isNew ? 1 : 0, ((LedgerProxy)(object)ledger).Checks);
    }

    [TestMethod]
    public async Task TryStartAsync_EmailFailureDoesNotFailAuthentication()
    {
        var ledger = DispatchProxy.Create<ISubscriptionLedgerService, LedgerProxy>();
        var emails = new RecordingEmails { Throw = true };
        var service = new MobileSignupWelcomeEmailService(ledger, emails,
            NullLogger<MobileSignupWelcomeEmailService>.Instance);

        await service.TryStartAsync("ouer@example.com", true, true, null, null);

        Assert.AreEqual(1, emails.Calls);
    }

    public class LedgerProxy : DispatchProxy
    {
        public bool Paid { get; set; }
        public int Checks { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.AreEqual(nameof(ISubscriptionLedgerService.HasActivePaidSubscriptionAsync), method?.Name);
            Checks++;
            return Task.FromResult(Paid);
        }
    }

    private sealed class RecordingEmails : IGratisSubscriberEmailSequenceService
    {
        public int Calls { get; private set; }
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public bool Throw { get; init; }
        public Task<GratisSubscriberSequenceStartResult> TryStartAsync(
            string? email, string? firstName, string? lastName, CancellationToken cancellationToken = default)
        {
            Calls++;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            if (Throw) throw new HttpRequestException("Simulated email service failure");
            return Task.FromResult(GratisSubscriberSequenceStartResult.Started);
        }
        public Task<bool> MarkPaidAsync(string? email, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Enrollment must never change a paid contact.");
    }
}

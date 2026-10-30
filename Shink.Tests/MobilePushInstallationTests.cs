using Shink.Mobile.Services;

namespace Shink.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class MobilePushInstallationTests
    {
        [TestInitialize]
        public void ResetStorage()
        {
            Preferences.Default.Values.Clear();
            SecureStorage.Default.Values.Clear();
        }

        [TestMethod]
        public void CanDisplay_OnlyForEnabledSignedInMatchingRegistrationAndAccount()
        {
            var registration = Guid.NewGuid();
            Preferences.Default.Set("mobile_session_is_signed_in", true);
            MobilePushInstallation.Enabled = true;
            MobilePushInstallation.Save(registration, "a@example.test", "device-token");
            MobilePushInstallation.UpdateCurrentAccount("a@example.test");
            Assert.IsTrue(MobilePushInstallation.CanDisplay(registration.ToString("D")));
            Assert.IsFalse(MobilePushInstallation.CanDisplay(Guid.NewGuid().ToString("D")));

            // Session changes before the asynchronous server rebind completes.
            MobilePushInstallation.UpdateCurrentAccount("b@example.test");
            Assert.IsFalse(MobilePushInstallation.CanDisplay(registration.ToString("D")));
            MobilePushInstallation.UpdateCurrentAccount("a@example.test");
            MobilePushInstallation.Enabled = false;
            Assert.IsFalse(MobilePushInstallation.CanDisplay(registration.ToString("D")));
            MobilePushInstallation.Enabled = true;
            Preferences.Default.Set("mobile_session_is_signed_in", false);
            Assert.IsFalse(MobilePushInstallation.CanDisplay(registration.ToString("D")));
        }

        [TestMethod]
        public async Task LostRegistrationResponse_StillLeavesDurableRevocationProof()
        {
            var identity = await MobilePushInstallation.GetIdentityAsync();
            await MobilePushInstallation.MarkRegistrationAttemptAsync(identity);

            Assert.IsFalse(MobilePushInstallation.IsRegistered);
            Assert.AreEqual(identity, await MobilePushInstallation.GetRevocationIdentityAsync());
            Assert.AreEqual(identity, await MobilePushInstallation.GetIdentityAsync());

            MobilePushInstallation.ForgetRegistration();
            Assert.IsNull(await MobilePushInstallation.GetRevocationIdentityAsync());
        }

        [TestMethod]
        public void NeedsRefresh_OnAccountOrTokenChangeAndAfterRevocation()
        {
            MobilePushInstallation.Save(Guid.NewGuid(), "a@example.test", "first-token");
            Assert.IsFalse(MobilePushInstallation.NeedsRefresh(" A@example.test ", "first-token"));
            Assert.IsTrue(MobilePushInstallation.NeedsRefresh("b@example.test", "first-token"));
            Assert.IsTrue(MobilePushInstallation.NeedsRefresh("a@example.test", "rotated-token"));

            MobilePushInstallation.ForgetRegistration();
            Assert.IsTrue(MobilePushInstallation.NeedsRefresh("a@example.test", "first-token"));
            Assert.IsFalse(MobilePushInstallation.CanDisplay(null));
        }

        [TestMethod]
        public void LostRevocationResponse_LaterOptInMustReRegister()
        {
            var registration = Guid.NewGuid();
            MobilePushInstallation.Save(registration, "a@example.test", "device-token");
            MobilePushInstallation.MarkRevocationAttempt();
            Assert.IsTrue(MobilePushInstallation.NeedsRefresh("a@example.test", "device-token"));

            MobilePushInstallation.Save(registration, "a@example.test", "device-token");
            Assert.IsFalse(MobilePushInstallation.NeedsRefresh("a@example.test", "device-token"));
        }
    }
}

// Only the linked, unchanged production persistence class uses these test storage
// adapters. No MAUI platform runtime, real keychain or phone preferences are used.
namespace Shink.Mobile.Services
{
    internal sealed class PushTestPreferences
    {
        public Dictionary<string, object> Values { get; } = [];
        public T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var value) ? (T)value : fallback;
        public void Set<T>(string key, T value) => Values[key] = value!;
        public void Remove(string key) => Values.Remove(key);
    }
    internal static class Preferences
    {
        public static PushTestPreferences Default { get; } = new();
    }
    internal sealed class PushTestSecureStorage
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
        public bool Remove(string key) => Values.Remove(key);
    }
    internal static class SecureStorage
    {
        public static PushTestSecureStorage Default { get; } = new();
    }
}

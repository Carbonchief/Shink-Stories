# Apple App Store Server API configuration

Schink validates iOS subscription transaction identifiers with Apple's App Store Server API. The deprecated `verifyReceipt` endpoint and app-specific shared secret are not used.

## App Store Connect

1. Open **Users and Access**.
2. Open **Integrations**.
3. Under **Keys**, select **In-App Purchase**.
4. Generate an In-App Purchase key and download its `.p8` private key immediately. Apple permits the private key to be downloaded only once.
5. Record the Issuer ID and Key ID shown in App Store Connect.

## Server-only settings

Configure these values on the website/server. Never add the private key to the mobile app or commit it to the repository.

- `MobileStore__AppleIssuerId`: the App Store Connect issuer ID.
- `MobileStore__AppleKeyId`: the In-App Purchase key ID.
- `MobileStore__ApplePrivateKey`: the complete `.p8` PEM text. Escaped `\n` line breaks are accepted.
- `MobileStore__AppleBundleId`: `com.schink.stories.mobile`.

The same configuration validates Production and TestFlight transactions. Verification calls Production first and retries Apple's Sandbox API only when Production reports that the transaction identifier was not found.

After configuring the settings, test a monthly or yearly purchase with a sandbox/TestFlight account and confirm that the resulting Schink account has full access on both the app and `www.schink.co.za`.

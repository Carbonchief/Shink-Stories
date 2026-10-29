# Schink

Install the MAUI app on the paired iPhone with:

```sh
./scripts/install-on-iphone.sh
```

Optional overrides:

```sh
DEVICE_NAME="Another iPhone" ./scripts/install-on-iphone.sh
DEVICE_UDID="00008130-..." ./scripts/install-on-iphone.sh
SIGNING_IDENTITY="Apple Development: Name (TEAMID)" ./scripts/install-on-iphone.sh
```

### Mobile signup email consent

The native signup screen offers an optional, initially checked “Ontvang Schink-e-posse” checkbox
for email/password, Apple, and Google registration. Email registration passes the choice
to `/api/auth/signup`; Apple and Google completion requests carry `marketingConsent`
(default `false` for older clients). Ordinary sign-in never opts a user in.

For social registration, the backend captures whether the account already had active
free access before provisioning it. Google carries that fact and the profile names in
the existing protected, two-minute handoff token. Only a consenting new free account,
with no active paid subscription, enters the existing Resend six-day welcome automation.
Existing Resend unsubscribes remain respected, and store purchases retain the existing
paid-status update that stops subsequent welcome emails. Enrollment failures are logged
without failing a completed social sign-in. No database migration is required.

Ship the backend before the new mobile build. Older clients default to no consent; the
updated app requires a new store build. Verify all three flows with consenting and
non-consenting test accounts before release; live test emails require approval.

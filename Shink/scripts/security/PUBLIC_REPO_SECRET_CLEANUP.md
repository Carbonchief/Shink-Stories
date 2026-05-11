# Public Repo Secret Cleanup

Use this checklist before pushing this repository to a public remote.

## 1) Keep runtime secrets in environment variables

The committed `appsettings*.json` files are now sanitized. Set real values via environment variables:

- `Resend__ApiKey`
- `Resend__FromEmail`
- `Resend__ToEmail`
- `Supabase__Url`
- `Supabase__PublishableKey`
- `Supabase__SecretKey`
- `PayFast__MerchantKey`
- `PayFast__Passphrase`
- `Paystack__SecretKey`
- `PostHog__ProjectApiKey`

Example (PowerShell, current session):

```powershell
$env:Resend__ApiKey = "<resend_key>"
$env:Supabase__SecretKey = "<supabase_secret_key>"
```

## 2) Rewrite git history to purge previously committed secrets

Install `git-filter-repo` if needed:

```powershell
python -m pip install git-filter-repo
```

Create a temporary replacements file (do not commit this file):

```powershell
$replacementFile = Join-Path $env:TEMP "shink-replacements.txt"
@'
<OLD_RESEND_API_KEY>==>REDACTED_RESEND_API_KEY
<OLD_SUPABASE_ANON_KEY>==>REDACTED_SUPABASE_ANON_KEY
<OLD_SUPABASE_SECRET_KEY>==>REDACTED_SUPABASE_SECRET_KEY
<OLD_PAYFAST_MERCHANT_KEY>==>REDACTED_PAYFAST_MERCHANT_KEY
<OLD_PAYFAST_PASSPHRASE>==>REDACTED_PAYFAST_PASSPHRASE
<OLD_PAYSTACK_SECRET_KEY>==>REDACTED_PAYSTACK_SECRET_KEY
<OLD_POSTHOG_PROJECT_API_KEY>==>REDACTED_POSTHOG_PROJECT_API_KEY
'@ | Set-Content -Path $replacementFile -Encoding UTF8
```

From repo root, rewrite history:

```powershell
git filter-repo --force --replace-text $replacementFile
git filter-repo --force --invert-paths --path Shink/.artifacts/
git for-each-ref --format="delete %(refname)" refs/original | git update-ref --stdin
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

Force push rewritten history:

```powershell
git push --force --all origin
git push --force --tags origin
```

## 3) Final actions

- Rotate every exposed key in external services.
- Invalidate old API keys/tokens immediately after rotation.
- Ask collaborators to re-clone after history rewrite.

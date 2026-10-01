# Website Sign in with Apple

The sign-in page `/teken-in` shows `Teken in met Apple` below Google. It opens `/api/auth/apple/start` with any validated local return URL. The server starts Supabase Apple OAuth with PKCE and stores the verifier, provider, expiry, and return URL in the existing protected, HttpOnly callback cookie. `/auth/callback` exchanges the code, provisions access through the existing subscriber ledger, and creates the usual website session.

Apple always uses PKCE. Google's existing PKCE and legacy implicit flows remain supported. Native iOS Apple sign-in continues to use the identity-token endpoint.

## Provider setup and deployment status

A read-only request to the Shink Supabase Apple authorization endpoint initially returned `Unsupported provider: missing OAuth secret`. After the setup below, the endpoint returned HTTP 302 to Apple's authorization page with the website Services ID and correct Supabase callback.

Apple Developer setup was authorized by the user and saved on 1 October 2026:

- Team: `SCHINK PTY. LTD. (6DP8F4CY29)`
- Services ID: `com.schink.stories.web` (`Schink Stories Website`)
- Associated primary app: `com.schink.stories.mobile`
- Domain: `btpsoyiyhtfbeznonygn.supabase.co`
- Apple return URL: `https://btpsoyiyhtfbeznonygn.supabase.co/auth/v1/callback`
- Supabase already allows `https://www.schink.co.za/auth/callback`.
- Sign in with Apple key: `8Q9DZPF37P` (`Schink Stories Website Sign In`), created after explicit approval.
- Private key and generated secret are stored outside the repository in `~/.config/schink/apple-sign-in`, with owner-only permissions. A recovery copy of the private key is stored in that directory's `backup` subdirectory on the same machine.
- The generated ES256 client secret was locally signature-verified and expires on 30 March 2027 at 19:24:37 UTC. Generate a replacement before this expiry.
- The current API token lacks `auth_config_write` and `project_admin_write`; the attempted API update returned HTTP 403 and made no change. The user subsequently entered the generated OAuth secret and saved through the signed-in Supabase dashboard.
- Saved Supabase settings were verified: Apple enabled, client IDs `com.schink.stories.web,com.schink.stories.mobile`, and a populated masked OAuth secret. Google remains enabled.
- Apple's authorization page opened successfully and displayed `Use your Apple Account to sign in to 'Schink Stories'`. This verifies provider routing and service registration; an actual Apple code exchange and authenticated website session still require an end-to-end sign-in test.
- Website deployment remains separately pending explicit approval. The local implementation passed 36 focused tests and was visually checked at desktop and mobile sizes.

Configuration procedure:

1. Use the Schink Apple Developer team and a web Services ID associated with the existing Sign in with Apple app identifier.
2. Configure Apple's web return URL as `https://btpsoyiyhtfbeznonygn.supabase.co/auth/v1/callback`, with the corresponding Supabase domain.
3. Configure the Services ID first in the Shink Supabase Apple provider's client IDs. Retain `com.schink.stories.mobile` as an accepted native client ID so iOS authentication continues to work.
4. Generate and configure Apple's OAuth client secret securely. Do not store the signing key or secret in the repository. Apple's web OAuth secret needs renewal every six months.
5. Ensure Supabase permits the website return URL `https://www.schink.co.za/auth/callback`.
6. Test cancellation, successful sign-in, local return navigation, and access for existing Apple accounts after an approved deployment.

Apple Hide My Email uses the Apple relay identity. This change does not merge separate Apple and email/Google subscriber accounts.

References: [Supabase Apple authentication](https://supabase.com/docs/guides/auth/social-login/auth-apple), [Apple environment configuration](https://developer.apple.com/documentation/signinwithapple/configuring-your-environment-for-sign-in-with-apple).

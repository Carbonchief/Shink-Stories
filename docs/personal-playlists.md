# Personal website playlists

Signed-in website users manage private playlists in **My speellyste** on `/my-stories`. They can create and rename lists, search for published stories or music, add or remove items, change the order with arrow buttons, and delete a list after confirmation. Empty lists are supported. Limits are 50 lists per account, 100 items per list, and 80 characters per name.

Playback opens `/my-stories/speellys/{playlist-id}` in the existing playlist player. It keeps saved item order, autoplay and shuffle. Existing subscription checks filter playable stories before the player creates signed audio URLs. Video items are excluded. Missing or unpublished catalog items are omitted during playback and identified in the editor so the user can remove them.

The Blazor server service resolves identity from `AuthenticationStateProvider`; the form cannot supply an email or account ID. Database RPCs check ownership on every operation, and only `service_role` can access the tables and functions. Saving a name and ordered items is transactional. Account deletion removes the personal playlists and their items.

## Release requirement

The database prerequisite is complete. With the user's explicit approval, `Shink/Database/migrations/20261007053828_subscriber_playlists.sql` was applied to the Schink project (`btpsoyiyhtfbeznonygn`) on 7 October 2026. Supabase recorded migration `subscriber_playlists` at version `20261007053828`; the local filename matches that recorded version. The existing account cleanup body was checked immediately before application, and the migration added only personal-playlist cleanup to that function. The website code is still local and has not been published.

If the database is unavailable, the page reports that personal playlists could not be loaded and offers a retry. It does not pretend that an unavailable database contains no playlists.

## Verification on 6 October 2026

- Website/Razor build and scoped CSS compilation passed, with zero warnings or errors. Unchanged story and branding media were excluded from the verification build because asset hashing stalled on existing image files; source and styles were compiled normally.
- 25 focused tests passed: `SubscriberPlaylistTests`, `MyStoriesSourceTests`, `SecurityHardeningSourceTests`, `StoryCatalogBehaviorTests`, `AudioAccessServiceTests`, and `R2AudioDeliverySourceTests`.
- 32 isolated PostgreSQL checks passed using PGlite 0.5.8, including ownership, invalid/draft/deleted/video items, atomic saves, limits, private API grants, delete cascades, and account deletion. No production data was changed.
- An isolated browser host used the actual compiled editor and scoped CSS with synthetic catalog data and an in-memory playlist service. Creation, name validation, search, reordering, editing, removal, save/reload, and cancelling deletion passed. At 390px the editor used one column and the document width remained 390px.
- Authenticated local page loading was verified against the migrated database on 7 October. Playback and persistence against a published website remain unverified.

## Deployment and local launch on 7 October 2026

The migration was applied through the project-scoped Supabase MCP after the user approved renewing Codex's Schink organization access. Read-only verification confirmed both playlist tables exist with RLS enabled, `anon` and `authenticated` have no direct access, and `service_role` has the required CRUD permissions. All three playlist RPCs are security invoker functions executable only by the server role. The update trigger and migration history were verified, and the resulting account-cleanup body matched the reviewed change exactly. Applying the migration did not update existing story, account, or subscription records.

The duplicate Schink MCP configurations were consolidated under `supabase_shink`, preserving project `btpsoyiyhtfbeznonygn`. Its OAuth login was renewed and stored in macOS Keychain with only `projects:read`, `database:read`, and `database:write` requested. Other MCP connections were preserved; owner-only local configuration backups were retained. Fully restart Codex to reload the corrected MCP connection in existing chats. No credentials were added to this repository.

The local website was restarted at `http://localhost:5262/my-stories`, with billing background jobs disabled and the local process's Resend and mobile store verification credentials cleared. Its health endpoint returned HTTP 200. The authenticated page successfully loaded playlists, the previous database error cleared, and the create button became available. Opening the editor, entering a name, searching the live catalog, adding a story to an unsaved draft, and cancelling were verified. No test playlist was saved to the live database. No website publication or Git remote update occurred.

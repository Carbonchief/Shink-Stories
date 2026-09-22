# iOS 1.0 review corrections

Apple reviewed build 28 on 16 September 2026 using an iPad Air 11-inch (M3).

## Corrections in the next build

- Mobile signup no longer asks for a phone number. The profile phone field is explicitly optional.
- The subscription screen shows Privaatheidsbeleid and Terme en voorwaardes immediately below its header, including during loading and errors. A failed browser handoff reports an error instead of failing silently.
- Menu → Instellings → Rekening contains Verwyder my rekening. Profiel also offers Rekeninginstellings en verwydering, opening these settings. The existing permanent-deletion endpoint and confirmation steps are retained.

## Required device evidence before resubmission

These steps are pending; source checks and a build do not establish end-to-end completion.

1. Install the corrected build on an iPad and a physical iPhone.
2. Register a disposable test account without a phone number; verify successful sign-in.
3. Open the subscription screen and tap both legal links; record that each destination loads.
4. With an explicitly approved disposable test account, record sign-in, Menu → Instellings → Rekening → Verwyder my rekening, both confirmation dialogs, and the success confirmation. Verify the account can no longer sign in. Do not delete the reusable Apple reviewer account or any customer account.
5. Verify monthly/yearly price and duration, restoration, and subscription management remain visible and functional.
6. Add the recordings and exact navigation instructions to App Review Information. Apple explicitly requested physical-device evidence for deletion.
7. Increment the build number and follow the repository's Xcode Organizer upload procedure when upload is authorized. Resubmission and any message to Apple require user authorization.

The current changes are local. No account was created/deleted, no server changes were deployed, and no message was sent to Apple during implementation.

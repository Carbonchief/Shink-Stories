# Mobile onboarding

The four phone JPEGs are the original, unmodified onboarding designs from
[the supplied Drive folder](https://drive.google.com/drive/folders/1LK1EFcr9J6KchoXx1tC1uCtIeDJxJ2EX).
They include the Afrikaans copy, illustrations, logo, and four-step indicator.

| Bundled asset | Drive original |
| --- | --- |
| `schink_onboarding_01.jpg` | `Schink_Stories_onboarding_01_App.jpg` |
| `schink_onboarding_02.jpg` | `Schink_Stories_onboarding_02_app.jpg` |
| `schink_onboarding_03.jpg` | `Schink_Stories_onboarding_03_app.jpg` |
| `schink_onboarding_04.jpg` | `Schink_Stories_onboarding_04 - App.jpg` |

`OnboardingPage` displays the designs in order, with swipe gestures and native
Afrikaans back, next, skip, and finish controls. Each image has a complete
screen-reader description. On phones, artwork fills the viewport width with its original
aspect ratio. The blank lower strip and baked indicator are clipped at source
y=1312; native, tappable progress dots and controls occupy the footer instead.
The logo clears the iPhone camera area, and footer controls respect device safe
areas. Short or landscape phone screens can scroll while navigation stays visible.
The next/finish button is centered below the progress dots. A back arrow appears
at the top left after the first screen, opposite the skip control.
The first screen also has “Het jy reeds ’n rekening? Teken in” below Next.
It completes onboarding and opens the existing account sign-in form directly.
The link is hidden on later screens.
The gold progress marker stretches and glides between dots in either direction
over 420 ms, with its width capped at 24 points (18 points at rest). Rapid
navigation retargets the current shape without queuing old
animations. Reduce Motion uses an immediate state change, and leaving the page
stops the animation. The four native dot buttons retain their accessibility
labels and touch targets.

Tablets use the dedicated 2048 × 2732 designs when the app window is at least
600 points wide. These JPEGs include their own logo, Afrikaans copy, and
illustrations. The view fits the visible content above the shared navigation
in portrait and landscape. Narrow tablet windows use the phone images. Each
tablet slide has a screen-reader description and uses the same native controls.

| Bundled tablet asset | Drive original |
| --- | --- |
| `schink_onboarding_tablet_01.jpg` | `Schink_Stories_App_onboarding_iPad.jpg` |
| `schink_onboarding_tablet_02.jpg` | `Schink_Stories_App_onboarding_iPad_02.jpg` |
| `schink_onboarding_tablet_03.jpg` | `Schink_Stories_App_onboarding_iPad_03.jpg` |
| `schink_onboarding_tablet_04.jpg` | `Schink_Stories_App_onboarding_iPad_04.jpg` |

`AppShell` shows onboarding before the account landing page for signed-out
installs that have not completed it. Finishing or skipping saves
`mobile_onboarding_completed_v1` in device preferences. An interrupted flow
starts again on the welcome screen. Signed-in sessions bypass onboarding and
save the same completion flag, so signing out later does not start it again.
The session refresh can run while onboarding is open without recreating the page.

To verify locally, use a disposable simulator install: advance and swipe through
all four images, go back, finish, and relaunch. Repeat with skip and with a cached
signed-in session. Check a short phone plus small and large tablets in both
orientations, including screen-reader descriptions, uncropped illustrations,
reachable controls, and a phone layout regression. No server or content changes
are required.

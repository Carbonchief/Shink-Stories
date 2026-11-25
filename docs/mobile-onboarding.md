# Mobile onboarding

The five phone JPEGs are the original, unmodified onboarding designs from
[the supplied Drive folder](https://drive.google.com/drive/folders/1LK1EFcr9J6KchoXx1tC1uCtIeDJxJ2EX).
They include the Afrikaans copy, illustrations, and logo. The table lists them
in screen order. “Egte stories. Geen AI.” introduces Martin immediately after the welcome screen.

| Bundled asset | Drive original |
| --- | --- |
| `schink_onboarding_01.jpg` | `Schink_Stories_onboarding_01_App.jpg` |
| `schink_onboarding_05.jpg` | `Schink_Stories_onboarding_05.jpg` |
| `schink_onboarding_02.jpg` | `Schink_Stories_onboarding_02_app.jpg` |
| `schink_onboarding_03.jpg` | `Schink_Stories_onboarding_03_app.jpg` |
| `schink_onboarding_04.jpg` | `Schink_Stories_onboarding_04 - App.jpg` |

`OnboardingPage` displays the designs in order, with swipe gestures and native
Afrikaans back, next, skip, and finish controls. Each image has a complete
screen-reader description. On phones, each slide fits its illustration and copy
above the footer at the original aspect ratio. The blank lower strip and baked
indicator are clipped. The logo clears the iPhone camera area, and the footer
controls respect device safe areas. Scrolling is enabled only when the visible
artwork cannot fit at a readable size, such as in a very short landscape window.
The next button is centered below the progress dots. A back arrow appears
at the top left after the first screen, opposite the skip control.
The first screen also has “Het jy reeds ’n rekening? Teken in” below Next.
It completes onboarding and opens the existing account sign-in form directly.
The final screen shows “Begin gratis luister” and “Kry volle toegang” above the
progress dots. “Begin gratis luister” opens Luister as a guest. Free stories play immediately.
Choosing a locked story opens account sign-up and remembers the story for the paywall.
“Kry volle toegang” opens sign-up directly. After authentication and any required
profile picture selection, listeners without a paid subscription see the parent
check and subscription plans. Guests return to Luister when they reopen the app.
The sign-in link also appears on the final screen.
The gold progress marker stretches and glides between dots in either direction
over 420 ms, with its width capped at 24 points (18 points at rest). Rapid
navigation retargets the current shape without queuing old
animations. Reduce Motion uses an immediate state change, and leaving the page
stops the animation. The five native dot buttons retain their accessibility
labels and touch targets.

Tablet windows at least 600 points wide use dedicated 2048 × 2732 portrait images
and 2732 × 2048 landscape images. These JPEGs include the logo, Afrikaans copy,
and illustrations. The app selects the artwork for the window orientation and
fits the content above the navigation controls. Narrow tablet windows use the
phone images. Each tablet slide has a screen-reader description and uses the
same native controls.

| Bundled tablet asset | Drive original |
| --- | --- |
| `schink_onboarding_tablet_01.jpg` | `Schink_Stories_App_onboarding_iPad.jpg` |
| `schink_onboarding_tablet_05.jpg` | `Schink_Stories_App_onboarding_iPad_05.jpg` |
| `schink_onboarding_tablet_02.jpg` | `Schink_Stories_App_onboarding_iPad_02.jpg` |
| `schink_onboarding_tablet_03.jpg` | `Schink_Stories_App_onboarding_iPad_03.jpg` |
| `schink_onboarding_tablet_04.jpg` | `Schink_Stories_App_onboarding_iPad_04.jpg` |
| `schink_onboarding_tablet_landscape_01.jpg` | `Schink_Stories_App_onboarding_iPad_Landscape_01.jpg` |
| `schink_onboarding_tablet_landscape_05.jpg` | `Schink_Stories_App_onboarding_iPad_Landscape_5.jpg` |
| `schink_onboarding_tablet_landscape_02.jpg` | `Schink_Stories_App_onboarding_iPad_Landscape_02.jpg` |
| `schink_onboarding_tablet_landscape_03.jpg` | `Schink_Stories_App_onboarding_iPad_Landscape_03.jpg` |
| `schink_onboarding_tablet_landscape_04.jpg` | `Schink_Stories_App_onboarding_iPad_Landscape_04.jpg` |

`AppShell` shows onboarding before the account landing page for signed-out
installs that have not completed it. Finishing or skipping saves
`mobile_onboarding_completed_v1` in device preferences. An interrupted flow
starts again on the welcome screen. Signed-in sessions bypass onboarding and
save the same completion flag, so signing out later does not start it again.
The session refresh can run while onboarding is open without recreating the page.

To verify locally, use a disposable simulator install: advance and swipe through
all five images, go back, finish, and relaunch. Repeat with skip and with a cached
signed-in session. Check a short phone plus small and large tablets in both
orientations, including screen-reader descriptions, uncropped illustrations,
reachable controls, and a phone layout regression. No server or content changes
are required.

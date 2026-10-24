# Luister carousel gutter correction — 22 October 2026

The iPhone simulator reproduced the reported spacing problem: carousel artwork could sit against the left screen edge while the showcase artwork kept its normal gutter. Horizontal interaction restored the gap; vertical scrolling exposed the missing gap again.

The app retains MAUI 10.0.120's legacy iOS CollectionView handler for the previously observed optimized-handler cell-lifetime crash. That handler implements a horizontal header as `ContentInset.Left`, with a negative initial content offset. Its item-source reload path resets the content offset to zero. This makes a spacer header vulnerable to disappearing when a nested carousel is rebound or laid out during parent scrolling. See the owning [header/inset implementation](https://github.com/dotnet/maui/blob/10.0.120/src/Controls/src/Core/Handlers/Items/iOS/StructuredItemsViewController.cs) and [reload/offset implementation](https://github.com/dotnet/maui/blob/10.0.120/src/Controls/src/Core/Handlers/Items/iOS/ItemsViewController.cs).

## Change

Luister's horizontal carousels now use a small dedicated CollectionView subtype. On iOS, its handler supplies 18-point leading and trailing section insets through the native flow-layout delegate. The first item therefore starts at x=18 even when the content offset is zero. It no longer needs a spacer header or footer on iOS.

The native delegate extends the existing stable MAUI handler's controller/delegate, preserving its cell recycling, selection and scrolling behavior. This is scoped to Luister carousels; other collection views keep their existing handlers. Inter-card spacing remains 14 points. Android retains its existing 4-point spacer plus 14-point item spacing. Both shared carousel construction paths use the same subtype.

The top and bottom blur implementations are unchanged. No timer, scroll-idle correction, compensating ScrollTo call or frozen blur snapshot was added.

## Verification

- iPhone 17e simulator, iOS 27.0: reproduced the old gap loss; checked the correction on initial display, first vertical swipe, further up/down scrolling through multiple feed rows, and return after recycling. Horizontal movement in both directions remained functional.
- Native inspection after recycling found five mounted carousels containing 78, 10, 5, 4 and 3 items. Every first item started at x=18.0 and every trailing gutter measured 18.0. Their native content insets were zero. A carousel that had been swiped horizontally had offset x=419 while retaining the same 18-point item-layout gutters.
- Android API 35 ARM64 emulator: repeated the signed-in check after the build swap returned to the welcome screen. Initial display, horizontal swipes in both directions, vertical scrolling through multiple rows and return scrolling retained the existing artwork gutter. Welcome/sign-in gestures are excluded from this verification.
- A separate simulator recording shows the corrected gutters and the native top/bottom materials updating while content moves. These are functional layout checks, not an iOS frame-time benchmark or a claim that all other scrolling hitches are resolved.
- iOS simulator Debug build passed with one existing RID-override warning. Android ARM64 Release build passed with 13 existing warnings. All 38 focused Luister safe-area, collection-handler, scrolling and chrome checks passed. `git diff --check` passed.
- Current iOS runtime logs contained no unhandled-exception, NativeHandle, TemplatedCell, native inconsistency or segmentation-fault markers during these checks.
- The corrected version 1.1/build 36 was built and signed for Luan iPhone 15 Pro with the Schink Ad Hoc profile, using the documented FinderInfo clean-copy/signing workaround. In-place installation was verified, preserving the app container. Automatic launch was blocked because the phone was locked; physical-device behavior has not yet been visually confirmed for this correction.

Evidence is retained locally under `artifacts/ios-scroll-review/2026-10-22/luister-gutter/` (Git ignored): before/after screenshots, `ios-final-scroll.mp4`, native gutter inspection, build/install logs and focused test results. Sign-in-screen captures are excluded.

No push, deployment, TestFlight upload or store release was performed.

# Winkel shopping flow

The store uses the existing product catalogue, order storage, Paystack checkout,
payment verification and published order email templates. Account-based saved
checkout details require `Shink/Database/migrations/20261108190140_store_customer_details.sql`
before deploying this version. Product collections require
`Shink/Database/migrations/20261108194610_store_product_collections.sql`.
Neither migration has been applied to the hosted database.

## Customer pages

- `/winkel`: product search, collection filters, price/name sorting and an included-delivery filter.
- `/winkel/produk/{slug}`: product information and quantity selection.
- `/winkel/mandjie`: persistent basket, quantity editing, removal and totals.
- `/winkel/afreken`: guest checkout, delivery information, terms acceptance and
  an itemised order summary. The summary comes first on smaller screens.
- The existing Paystack callback returns to the store with the verified order
  summary. Cancellation and validation failures return to checkout.

The existing `?produk={slug}` product links remain supported. Disabled or missing
product detail pages return 404. The site header and floating bottom-right cart
include synchronised basket counts. The floating cart opens `/winkel/mandjie`
and respects mobile safe-area spacing.
Outside `/winkel`, the header cart appears only when the browser has items in its
basket. Signing in alone does not show it. Empty main-site carts stay hidden before
JavaScript loads, and adding or clearing items in another tab updates visibility.
All customer copy is Afrikaans. Mobile layouts and the site's night mode are
supported. Product publishing and pricing still use the existing admin store panel.

## Catalogue presentation

The catalogue follows the clean structure of the SureCart shop reference: a
centred introduction, search toolbar, left filter sidebar and three product
columns on wider screens. Schink's character images sit on soft pastel panels,
with quick-add buttons on the images and product names and prices below them.
The palette uses warm white, forest green and teal; night mode uses dark green
surfaces and mint accents.

The sidebar contains name/price sorting, Kinderboeke, StorieTjommies and Pakkette
collection choices, and the included-delivery filter. On smaller screens, a labelled toggle expands
the filters above the products. Phones show two product columns. Delivery badges
use the catalogue's actual delivery flag.

In `/admin` → Winkel / Shop, each product has a required Versameling / Collection
selector. Admin labels and choices support Afrikaans and English. New products
require an explicit choice; existing products load their saved assignment. The
editor asks before discarding unsaved changes when switching products, refreshing,
starting a new product or cancelling. Declining keeps the draft, and selecting
the current product leaves its edits in place. Save-triggered refreshes do not
show a discard prompt. The same `collection_slug` drives filter membership,
enabled-product counts, product
card labels, quick-view labels, product details and structured-data categories.
Search and delivery filters combine with the chosen collection; resetting restores
all products. Disabled products do not contribute to public counts.

The collection migration initially assigns the Suurlemoentjie book to Kinderboeke,
the three existing book bundles to Pakkette, and other existing products to
StorieTjommies. Reapplying it preserves later admin assignments. Database constraints
restrict assignments to the three supported slugs and preserve the existing product
RLS/grants. Existing product revision triggers refresh the public catalogue after saves.

The product-card add button opens a product modal with its image, price,
description, delivery information and quantity. `Voeg by mandjie` adds the selected
quantity and returns to browsing; `Koop nou` adds it to the existing basket and
opens checkout. Both actions enforce the existing product and order limits.
The modal supports Escape, its close button and backdrop dismissal, contains
keyboard focus while open and returns focus when closed. No size or colour
variants are shown because the catalogue does not currently provide them.

## Basket and checkout rules

- Maximum 10 units per product and 20 units per order.
- Standard PUDO delivery is R80 per order. Any selected product with the existing
  `waives_delivery_fee` flag waives delivery for the entire order.
- Prices come from the current server catalogue. Checkout rejects a changed total
  or an unavailable product so customers can review the new basket before paying.
- Empty baskets cannot proceed to payment. The checkout POST validates the
  antiforgery token, terms acceptance and existing contact/address rules.
- The submit button prevents repeated submission and recovers after a return
  from payment. The existing endpoint rate limit remains in place.

## Account prefill and saved details

Signed-in checkout prefills available full name, email and phone from the account
profile, and contact/delivery fields from previously saved checkout preferences.
The values stay editable. Guests can follow the sign-in link and return to the
checkout with their basket and entered details intact; blank guest fields then
receive the available account values.

Signed-in shoppers see `Stoor my kontak- en afleweringsbesonderhede ...` checked
by default. It saves the validated contact/address fields when they submit
checkout, before an order is created or payment is started. Unticking it preserves
existing saved preferences without updating them. Order notes and terms consent
are never saved as account preferences. Guests can still purchase without an
account; account saving is offered after signing in.

The `store_customer_details` table is service-role only with RLS enabled and no
anonymous/authenticated API access. Its owner is derived from the server's signed-in
session, independently of the order's contact email. A changed account between
loading and submitting the form is rejected before saving. Account email changes
and deletion cascade from `subscribers`. Save failures return to checkout with a
message allowing retry or purchase with the save option unticked.

## Browser storage and receipts

The `schink.store.cart.v1` storage entry contains product slugs, quantities,
pending checkout snapshots, completion references and a modification timestamp. Local storage keeps the basket
between visits; session storage and a cookie provide fallbacks. Open tabs receive
basket changes. Removed products and invalid quantities are reconciled against the
catalogue when the page opens. Stored prices are never trusted.

Checkout contact/address fields are saved as they are edited in the current tab's
session storage, including before following a link or leaving the page. They
survive basket edits, reloads and failed/cancelled checkout. A verified payment
deletes the draft for that checkout; it does not erase a newer edited draft.
Personal details and terms acceptance are not stored in the persistent basket.
Drafts are scoped to the signed-in account. Switching accounts or signing out
discards another account's draft; guest edits survive the sign-in transition.
The save checkbox choice survives reloads and basket edits, while terms acceptance
always requires a fresh check.

A separate protected, HTTP-only `shink.store.receipt.{order-hash}` cookie
authorises each order's receipt for seven days on the same browser, so starting
another checkout does not replace access to an earlier receipt. Already-issued
single-order cookies remain readable. A payment query
parameter or reference alone cannot confirm a payment or clear the basket. The
existing signed recovery-link flow also grants receipt access. Each submitted
checkout has a random identifier protected with its receipt grant and linked to
its browser basket snapshot. Only an order persisted as paid can remove quantities
still belonging to that snapshot. Clearing the basket, removing a product or
reducing its quantity retires the corresponding submitted quantities; adding the
same product again does not restore them. Completion references prevent repeated
removal on receipt refresh. Older/recovered orders without a matching browser
snapshot leave the current basket alone.

The existing enabled/disabled product switch remains the availability control.
Counted stock reservations, discount codes, courier tracking and an account order
history are separate features and are not represented as working controls here.

## Verification

Run:

```sh
dotnet test Shink.Tests/Shink.Tests.csproj --no-restore
node --test scripts/tests/storefront-cart.test.mjs
python3 scripts/tests/storefront_browser_regressions.py
python3 scripts/tests/store_collections_migration.py
```

Browser verification should cover desktop, 390px and 320px layouts, night mode,
sidebar expansion, filter reset, both name sort directions, quick-add buttons,
search/sort, product quantities, basket reload/navigation, multiple tabs, empty
baskets, forged success URLs, stale prices, removed products, missing antiforgery
tokens and cancelled/failed checkout recovery. The automated browser suite starts
its own in-memory order API and local app, runs through Playwright CLI (Node/npm
required), and shuts both down. It covers the reported receipt, rebuilt-basket and
checkout-draft regressions without requiring any hosted credentials. The expanded
fixture also exercises real application sign-in cookies with fake loopback
profiles, saved-details storage and account switching.

The local preview uses the fallback catalogue with Supabase pointed at loopback,
secret/payment/email credentials removed and background billing disabled. It does
not charge cards, create hosted orders or send communications. A real Paystack
round trip and its receipt/email delivery still require an approved payment test
in the intended environment before production rollout.

# Winkel shopping flow

The store uses the existing product catalogue, order storage, Paystack checkout,
payment verification and published order email templates. No database migration
is required for this upgrade.

## Customer pages

- `/winkel`: product search, price/name sorting and an included-delivery filter.
- `/winkel/produk/{slug}`: product information and quantity selection.
- `/winkel/mandjie`: persistent basket, quantity editing, removal and totals.
- `/winkel/afreken`: guest checkout, delivery information, terms acceptance and
  an itemised order summary. The summary comes first on smaller screens.
- The existing Paystack callback returns to the store with the verified order
  summary. Cancellation and validation failures return to checkout.

The existing `?produk={slug}` product links remain supported. Disabled or missing
product detail pages return 404. The site header includes a basket count.
All customer copy is Afrikaans. Mobile layouts and the site's night mode are
supported. Product publishing and pricing still use the existing admin store panel.

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
```

Browser verification should cover desktop, 390px and 320px layouts, night mode,
search/sort, product quantities, basket reload/navigation, multiple tabs, empty
baskets, forged success URLs, stale prices, removed products, missing antiforgery
tokens and cancelled/failed checkout recovery. The automated browser suite starts
its own in-memory order API and local app, runs through Playwright CLI (Node/npm
required), and shuts both down. It covers the reported receipt, rebuilt-basket and
checkout-draft regressions without requiring any hosted credentials.

The local preview uses the fallback catalogue with Supabase pointed at loopback,
secret/payment/email credentials removed and background billing disabled. It does
not charge cards, create hosted orders or send communications. A real Paystack
round trip and its receipt/email delivery still require an approved payment test
in the intended environment before production rollout.

# Schink Stories System Email Report

Generated: 2026-06-12

Scope: source-code inventory of emails the Shink-Stories application can send from this checkout. This report does not verify live Resend/Supabase provider logs or published template contents.

## Executive Summary

The system sends email through two providers:

- Resend transactional email API at `https://api.resend.com/emails`.
- Supabase Auth email flows for password recovery, email change confirmation, and a possible signup-confirmation fallback.

There are 16 distinct email send paths/families in the current source:

- 12 Resend template-based customer/admin emails.
- 2 Resend inline internal/admin emails.
- 2 definite Supabase Auth emails.
- 1 possible Supabase Auth signup confirmation fallback, only when the admin create-user endpoint is unavailable and Supabase confirmation is enabled.

Most customer-facing Resend emails use published template IDs. Two internal emails still use inline HTML/text payloads: the contact-form notification to the site inbox and the internal store-order paid notification.

## Configuration

Primary config is under `Resend` in `Shink/appsettings.json`.

Default local values:

- `FromEmail`: `no-reply@example.com`
- `ToEmail`: `support@example.com`
- `BillingManageUrl`: `https://www.schink.co.za/intekening-en-betaling`

Root-level `appsettings.json` also contains Resend defaults used by the repository root context:

- `FromEmail`: `no-reply@prioritybit.co.za`
- `ToEmail`: `vanderwaltluan@gmail.com`

Production may override these through environment/app-service configuration, so treat these as code defaults rather than confirmed live values.

Configured Resend templates:

| Area | Template setting | Current default ID |
| --- | --- | --- |
| Contact auto-reply | `Templates.Contact.AutoReplyTemplateId` | `shink-contact-auto-reply` |
| Store customer confirmation | `Templates.StoreOrder.CustomerConfirmationTemplateId` | `shink-store-order-confirmation` |
| Subscription payment recovery day 1 | `Templates.SubscriptionPaymentRecovery.Day1TemplateId` | `shink-subscription-recovery-day-1` |
| Subscription payment recovery day 3 | `Templates.SubscriptionPaymentRecovery.Day3TemplateId` | `shink-subscription-recovery-day-3` |
| Subscription payment recovery day 5 | `Templates.SubscriptionPaymentRecovery.Day5TemplateId` | `shink-subscription-recovery-day-5` |
| Subscription confirmation | `Templates.SubscriptionNotifications.ConfirmationTemplateId` | `shink-subscription-confirmation` |
| Subscription ended | `Templates.SubscriptionNotifications.EndedTemplateId` | `shink-subscription-ended` |
| Admin ops alert | `Templates.AdminOps.AlertTemplateId` | `shink-admin-ops-alert` |
| Abandoned checkout hour 1 | `Templates.AbandonedCartRecovery.Hour1TemplateId` | `efdf7097-0981-457f-b7d6-a9091faef8c2` |
| Abandoned checkout hour 24 | `Templates.AbandonedCartRecovery.Hour24TemplateId` | `1583ea05-da74-4a85-816e-f456db34b46d` |
| Abandoned checkout day 7 | `Templates.AbandonedCartRecovery.Day7TemplateId` | `4dda903a-7a33-45c4-bdbf-616cd0e2cb96` |

## Email Inventory

### Contact Form: Internal Notification

- Provider: Resend.
- Template: none. Inline HTML/text payload.
- Trigger: `POST /api/contact` after validation/rate limiting.
- Recipient: configured `Resend:ToEmail`.
- Sender: configured `Resend:FromEmail`.
- Reply-to: visitor email address.
- Subject: `Kontakvorm: {subject}`.
- Data included: name, email, subject, message.
- Failure behavior: throws if Resend is not configured or the send is rejected, causing the contact request to fail.
- Source: `Shink/Services/ResendContactEmailService.cs:20`, `Shink/Services/ResendContactEmailService.cs:34`, `Shink/Program.cs:3187`.

### Contact Form: Customer Auto-Reply

- Provider: Resend.
- Template: `shink-contact-auto-reply`.
- Trigger: sent immediately after the internal contact-form notification succeeds.
- Recipient: visitor email address.
- Sender: configured `Resend:FromEmail`.
- Reply-to: configured support inbox.
- Variables: `CONTACT_NAME_HTML`, `CONTACT_NAME_TEXT`, `CONTACT_SUBJECT_HTML`, `CONTACT_SUBJECT_TEXT`, `SUPPORT_EMAIL`, `SITE_URL`.
- Idempotency key: hash of email, name, subject, and message.
- Failure behavior: logs warning and does not fail the original contact submission.
- Source: `Shink/Services/ResendContactEmailService.cs:62`, `Shink/Services/ResendContactEmailService.cs:65`, `Shink/Services/ResendContactEmailService.cs:82`.

### Store Order: Internal Paid-Order Notification

- Provider: Resend.
- Template: none. Inline HTML/text payload.
- Trigger: store order payment changes to `paid`.
- Recipient: configured `Resend:ToEmail`.
- Sender: configured `Resend:FromEmail`.
- Reply-to: customer email.
- Subject: `Store bestelling betaal: {quantity} items ({orderReference})`.
- Data included: order reference, item lines, total, customer name/email/phone, delivery address, notes.
- Failure behavior: logs warning. Caller catches and logs, then continues.
- Source: `Shink/Services/ResendStoreOrderNotificationService.cs:20`, `Shink/Services/ResendStoreOrderNotificationService.cs:47`, `Shink/Program.cs:4900`.

### Store Order: Customer Confirmation

- Provider: Resend.
- Template: `shink-store-order-confirmation`.
- Trigger: store order payment changes to `paid`.
- Recipient: customer email.
- Sender: configured `Resend:FromEmail`.
- Reply-to: configured support inbox.
- Variables: `CUSTOMER_NAME_HTML`, `CUSTOMER_NAME_TEXT`, `ORDER_REFERENCE`, `ORDER_ITEMS_HTML`, `ORDER_ITEMS_TEXT`, `ORDER_TOTAL`, `DELIVERY_ADDRESS_HTML`, `DELIVERY_ADDRESS_TEXT`.
- Idempotency key: `store-order-confirmation/{orderReference}`.
- Failure behavior: logs warning. Caller catches and logs, then continues.
- Source: `Shink/Services/ResendStoreOrderNotificationService.cs:109`, `Shink/Services/ResendStoreOrderNotificationService.cs:128`, `Shink/Program.cs:4925`.

### Abandoned Checkout Recovery: Hour 1

- Provider: Resend.
- Template: configured `AbandonedCartRecovery.Hour1TemplateId`.
- Trigger: checkout is initialized for a subscription or store order and a valid customer email/reference/checkout URL exists.
- Recipient: customer email.
- Sender: configured `Resend:FromEmail`.
- Reply-to: configured support inbox if set.
- Schedule: 1 hour after recovery creation.
- Variables: `CUSTOMER_NAME`, `ITEM_NAME`, `ITEM_SUMMARY`, `CART_TOTAL`, `CHECKOUT_URL`, `OPTOUT_URL`, `SUPPORT_EMAIL`.
- Idempotency key: `abandoned-cart/{recoveryId}/hour1`.
- Cancellation: scheduled messages can be cancelled when recovery is resolved or opted out.
- Source: `Shink/Services/SupabaseAbandonedCartRecoveryService.cs:39`, `Shink/Services/SupabaseAbandonedCartRecoveryService.cs:445`, `Shink/Services/SupabaseAbandonedCartRecoveryService.cs:754`.

### Abandoned Checkout Recovery: Hour 24

- Provider: Resend.
- Template: configured `AbandonedCartRecovery.Hour24TemplateId`.
- Trigger: same recovery sequence as hour 1.
- Recipient: customer email.
- Schedule: 24 hours after recovery creation.
- Variables: same as abandoned checkout hour 1.
- Idempotency key: `abandoned-cart/{recoveryId}/hour24`.
- Source: `Shink/Services/SupabaseAbandonedCartRecoveryService.cs:452`.

### Abandoned Checkout Recovery: Day 7

- Provider: Resend.
- Template: configured `AbandonedCartRecovery.Day7TemplateId`.
- Trigger: same recovery sequence as hour 1.
- Recipient: customer email.
- Schedule: 7 days after recovery creation.
- Variables: same as abandoned checkout hour 1.
- Idempotency key: `abandoned-cart/{recoveryId}/day7`.
- Source: `Shink/Services/SupabaseAbandonedCartRecoveryService.cs:459`.

### Subscription Payment Recovery: Day 1 Immediate Email

- Provider: Resend.
- Template: `shink-subscription-recovery-day-1`.
- Trigger: failed subscription payment recovery reaches the customer-email stage. Paystack flows first defer customer emails for one next-day follow-up retry when eligible.
- Recipient: subscriber email.
- Sender: configured `Resend:FromEmail`.
- Reply-to: configured support inbox if set.
- Schedule: immediate when the sequence starts.
- Variables: `CUSTOMER_NAME`, `BILLING_URL`, `RECOVERY_URL`, `RECOVERY_ACTION_LABEL`, `RECOVERY_CONTEXT`, `PLAN_NAME`, `PAYMENT_PROVIDER`.
- Idempotency key: `{recoveryId}:day1` for automatic sequence, custom admin key for manual sends.
- Source: `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:24`, `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:149`, `Shink/Services/SupabaseSubscriptionLedgerService.cs:5966`.

### Subscription Payment Recovery: Day 3 Warning Email

- Provider: Resend.
- Template: `shink-subscription-recovery-day-3`.
- Trigger: automatic payment recovery sequence.
- Recipient: subscriber email.
- Schedule: first failed timestamp plus 2 days.
- Variables: same as day 1 recovery email.
- Idempotency key: `{recoveryId}:day3`.
- Cancellation: cancelled when the payment recovery resolves.
- Source: `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:46`, `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:158`.

### Subscription Payment Recovery: Day 5 Suspension Email

- Provider: Resend.
- Template: `shink-subscription-recovery-day-5`.
- Trigger: automatic payment recovery sequence.
- Recipient: subscriber email.
- Schedule: first failed timestamp plus 4 days.
- Variables: same as day 1 recovery email.
- Idempotency key: `{recoveryId}:day5`.
- Cancellation: cancelled when the payment recovery resolves.
- Source: `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:52`, `Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs:168`.

### Admin-Triggered Payment Recovery Email

- Provider: Resend.
- Template: day 1 recovery template, `shink-subscription-recovery-day-1`.
- Trigger 1: admin clicks `Send recovery email` for an active recovery row.
- Trigger 2: admin clicks `Send subscription recovery` for a currently failed subscription.
- Recipient: subscriber email.
- Schedule: immediate.
- Variables: same as day 1 recovery email, with a Paystack recovery URL/action when resolved.
- Audit events: `recovery.email_sent` or `recovery.subscription_manual_sent`.
- Source: `Shink/Services/SupabaseAdminManagementService.cs:848`, `Shink/Services/SupabaseAdminManagementService.cs:924`, `Shink/Components/Pages/Admin.razor:3158`.

### Subscription Confirmation

- Provider: Resend.
- Template: `shink-subscription-confirmation`.
- Trigger: paid subscription is confirmed in the subscription ledger flow.
- Recipient: subscriber email.
- Sender: configured `Resend:FromEmail`.
- Reply-to: configured support inbox if set.
- Variables: `CUSTOMER_NAME_HTML`, `CUSTOMER_NAME_TEXT`, `PLAN_NAME_HTML`, `PLAN_NAME_TEXT`, `AMOUNT`, `BILLING_LABEL_HTML`, `BILLING_LABEL_TEXT`, `NEXT_RENEWAL_DATE`, `PAYMENT_PROVIDER_HTML`, `PAYMENT_PROVIDER_TEXT`, `PAYMENT_REFERENCE_HTML`, `PAYMENT_REFERENCE_TEXT`, `BILLING_URL`, `SUPPORT_EMAIL`.
- Idempotency key: `subscription-confirmation/{hash(subscriptionId)}`.
- Side effect: also sends an admin ops alert about the confirmed subscription.
- Source: `Shink/Services/ResendSubscriptionNotificationEmailService.cs:23`, `Shink/Services/SupabaseSubscriptionLedgerService.cs:6075`.

### Subscription Ended Confirmation

- Provider: Resend.
- Template: `shink-subscription-ended`.
- Trigger: subscription access ends/cancels/expires through ledger flows.
- Recipient: subscriber email.
- Variables: `CUSTOMER_NAME_HTML`, `CUSTOMER_NAME_TEXT`, `PLAN_NAME_HTML`, `PLAN_NAME_TEXT`, `STATUS_LABEL_HTML`, `STATUS_LABEL_TEXT`, `ACCESS_MESSAGE_HTML`, `ACCESS_MESSAGE_TEXT`, `ENDED_AT`, `BILLING_URL`, `SUPPORT_EMAIL`.
- Idempotency key: `subscription-ended/{hash(subscriptionId/idempotencySuffix)}`.
- Side effect: also sends an admin ops alert about the ended subscription.
- Source: `Shink/Services/ResendSubscriptionNotificationEmailService.cs:66`, `Shink/Services/SupabaseSubscriptionLedgerService.cs:6119`.

### Admin Ops Alert

- Provider: Resend.
- Template: `shink-admin-ops-alert`.
- Trigger: operational subscription/payment conditions in the ledger, including payment recovery started/skipped, subscription confirmed, subscription ended, and other ledger warnings/errors.
- Recipient: configured `Resend:ToEmail`.
- Variables: `ALERT_TITLE_HTML`, `ALERT_TITLE_TEXT`, `ALERT_SEVERITY`, `ALERT_SUMMARY_HTML`, `ALERT_SUMMARY_TEXT`, `ALERT_DETAILS_HTML`, `ALERT_DETAILS_TEXT`, `EVENT_REFERENCE_HTML`, `EVENT_REFERENCE_TEXT`, `OCCURRED_AT`, `ACTION_URL`.
- Idempotency key: `admin-ops-alert/{hash(alertKey)}`.
- Source: `Shink/Services/ResendSubscriptionNotificationEmailService.cs:107`, `Shink/Services/SupabaseSubscriptionLedgerService.cs:6157`.

### Password Reset / Recovery

- Provider: Supabase Auth, not Resend.
- Trigger 1: public `POST /api/auth/password-reset/request`.
- Trigger 2: admin subscriber detail `Send password reset`.
- Trigger 3: admin bulk selected subscribers `Send reset`.
- Recipient: account/subscriber email.
- Template: Supabase Auth recovery email template configured in Supabase, not in this repo.
- Redirect target: public flow uses `/herstel-wagwoord`; admin flow currently builds `/reset-password`.
- Audit: admin reset writes `auth.password_reset_sent`; public reset does not write the same app audit row.
- Source: `Shink/Program.cs:2120`, `Shink/Services/SupabaseAuthService.cs:202`, `Shink/Services/SupabaseAdminManagementService.cs:770`, `Shink/Components/Pages/Admin.razor:214`.

### Email Address Change Confirmation

- Provider: Supabase Auth, not Resend.
- Trigger: signed-in user submits an email change from `/intekening-en-betaling`.
- Recipient: Supabase-controlled confirmation email(s). Depending on Supabase project settings, this can require confirmation on the new email and sometimes both old/new addresses.
- Template: Supabase Auth email-change template configured in Supabase, not in this repo.
- Redirect target: `/intekening-en-betaling?emailChange=complete&emailChangeState=...`.
- Source: `Shink/Program.cs:2199`, `Shink/Services/SupabaseAuthService.cs:410`, `Shink/Services/SupabaseAuthService.cs:928`.

### Possible Signup Confirmation Fallback

- Provider: Supabase Auth, not Resend.
- Trigger: public signup only if the Supabase admin create-user endpoint is unavailable.
- Normal current path: when `Supabase:SecretKey` is configured, signup calls the admin create-user endpoint with `email_confirm: true`, which creates a confirmed user and should not require a confirmation email.
- Fallback path: if admin create-user is not available, the code posts to `auth/v1/signup`; Supabase may send its configured confirmation email depending on project Auth settings.
- Source: `Shink/Services/SupabaseAuthService.cs:130`, `Shink/Services/SupabaseAuthService.cs:139`, `Shink/Services/SupabaseAuthService.cs:775`.

## Trigger Map

| Trigger | Emails sent |
| --- | --- |
| Visitor submits contact form | Internal contact notification, then contact auto-reply |
| Store Paystack order becomes paid | Internal paid-order notification, customer order confirmation |
| Subscription checkout starts but is not completed | Abandoned checkout hour 1, hour 24, day 7 sequence |
| Store checkout starts but is not completed | Abandoned checkout hour 1, hour 24, day 7 sequence |
| Subscription payment fails and recovery proceeds | Subscription payment recovery day 1, day 3, day 5; admin ops alert |
| Admin manually sends active recovery | Subscription payment recovery day 1 template |
| Admin manually sends recovery for failed subscription | Subscription payment recovery day 1 template |
| Paid subscription is confirmed | Customer subscription confirmation, admin ops alert |
| Subscription access ends | Customer subscription ended confirmation, admin ops alert |
| User requests password reset | Supabase Auth recovery email |
| Admin sends password reset | Supabase Auth recovery email |
| User requests email address change | Supabase Auth email-change confirmation email(s) |
| Public signup fallback without admin create-user | Possible Supabase Auth signup confirmation |

## Non-Email Notification Paths

The codebase also contains in-app notification services and UI notification-center code. Those are not email sends and are excluded from this report.

Payment providers such as Paystack/PayFast may send their own receipts outside this app, but this report only covers emails initiated by the Shink-Stories system code.

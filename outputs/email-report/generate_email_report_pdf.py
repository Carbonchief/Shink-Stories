from __future__ import annotations

from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    KeepTogether,
    ListFlowable,
    ListItem,
    PageBreak,
    PageTemplate,
    Paragraph,
    Preformatted,
    Spacer,
    Table,
    TableStyle,
)


OUTPUT_DIR = Path("output/pdf")
OUTPUT_FILE = OUTPUT_DIR / "shink-email-examples-report.pdf"


def build_styles():
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "title",
            parent=base["Title"],
            fontName="Helvetica-Bold",
            fontSize=22,
            leading=27,
            textColor=colors.HexColor("#222222"),
            spaceAfter=8,
        ),
        "subtitle": ParagraphStyle(
            "subtitle",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=10,
            leading=14,
            textColor=colors.HexColor("#555555"),
            spaceAfter=14,
        ),
        "section": ParagraphStyle(
            "section",
            parent=base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=14,
            leading=18,
            textColor=colors.HexColor("#222222"),
            spaceBefore=8,
            spaceAfter=6,
        ),
        "h3": ParagraphStyle(
            "h3",
            parent=base["Heading3"],
            fontName="Helvetica-Bold",
            fontSize=10.5,
            leading=13,
            textColor=colors.HexColor("#222222"),
            spaceBefore=6,
            spaceAfter=4,
        ),
        "body": ParagraphStyle(
            "body",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.2,
            leading=12.8,
            textColor=colors.HexColor("#222222"),
            spaceAfter=5,
        ),
        "small": ParagraphStyle(
            "small",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=7.8,
            leading=10.5,
            textColor=colors.HexColor("#555555"),
        ),
        "table_header": ParagraphStyle(
            "table_header",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=7.8,
            leading=10.5,
            textColor=colors.white,
        ),
        "note": ParagraphStyle(
            "note",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.6,
            leading=11.5,
            textColor=colors.HexColor("#444444"),
            backColor=colors.HexColor("#F7F2EA"),
            borderColor=colors.HexColor("#E6D8C6"),
            borderWidth=0.5,
            borderPadding=6,
            spaceAfter=8,
        ),
        "code": ParagraphStyle(
            "code",
            parent=base["Code"],
            fontName="Courier",
            fontSize=7.8,
            leading=10.2,
            textColor=colors.HexColor("#222222"),
            leftIndent=0,
        ),
    }


def on_page(canvas, doc):
    canvas.saveState()
    width, height = A4
    canvas.setFillColor(colors.HexColor("#222222"))
    canvas.rect(0, height - 13 * mm, width, 13 * mm, stroke=0, fill=1)
    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica-Bold", 9)
    canvas.drawString(16 * mm, height - 8.5 * mm, "Schink Stories - Email Examples Report")
    canvas.setFont("Helvetica", 8)
    canvas.drawRightString(width - 16 * mm, height - 8.5 * mm, f"Page {doc.page}")
    canvas.setStrokeColor(colors.HexColor("#DDDDDD"))
    canvas.line(16 * mm, 13 * mm, width - 16 * mm, 13 * mm)
    canvas.setFillColor(colors.HexColor("#777777"))
    canvas.setFont("Helvetica", 7)
    canvas.drawString(16 * mm, 8 * mm, "Generated from the current Shink-Stories source tree. No emails were sent.")
    canvas.restoreState()


def para(text: str, style):
    return Paragraph(text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"), style)


def pre(text: str, style):
    return Preformatted(text.strip(), style, maxLineLength=92)


def metadata_table(rows, styles):
    data = [[para(label, styles["small"]), para(value, styles["small"])] for label, value in rows]
    table = Table(data, colWidths=[34 * mm, 132 * mm], hAlign="LEFT")
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (0, -1), colors.HexColor("#F2E9DE")),
                ("BACKGROUND", (1, 0), (1, -1), colors.HexColor("#FBFAF8")),
                ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#E0D6CA")),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]
        )
    )
    return table


def email_block(title: str, metadata: list[tuple[str, str]], body: str, styles):
    return KeepTogether(
        [
            para(title, styles["h3"]),
            metadata_table(metadata, styles),
            Spacer(1, 3 * mm),
            pre(body, styles["code"]),
            Spacer(1, 5 * mm),
        ]
    )


def build_story():
    styles = build_styles()
    story = [
        para("Schink Stories Email Examples", styles["title"]),
        para("Generated on 8 June 2026 from the current repository at /Users/luanvanderwalt/Documents/Websites/Shink-Stories.", styles["subtitle"]),
        para(
            "Important: the repo contains exact inline email bodies for contact and internal store notifications. "
            "For published Resend templates and Supabase Auth emails, the repo contains template IDs, timing, recipients, and variables, "
            "but not the provider-hosted rendered template body. Those sections therefore show concrete examples of the payload and a representative rendered copy based on the variables the system sends.",
            styles["note"],
        ),
        para("At-a-glance Send Map", styles["section"]),
    ]

    rows = [
        ("Contact internal notification", "After /api/contact validates and passes rate/spam checks.", "Internal", "Inline Resend HTML/text"),
        ("Contact auto-reply", "Immediately after the internal contact email succeeds.", "Customer", "shink-contact-auto-reply"),
        ("Abandoned checkout recovery", "Scheduled at +1 hour, +24 hours, +7 days after subscription/store checkout starts.", "Customer", "3 published Resend templates"),
        ("Store paid notification", "When a store order first changes to paid.", "Internal", "Inline Resend HTML/text"),
        ("Store order confirmation", "When a store order first changes to paid.", "Customer", "shink-store-order-confirmation"),
        ("Subscription confirmation", "After PayFast or Paystack subscription payment is persisted.", "Customer", "shink-subscription-confirmation"),
        ("Subscription ended", "When paid access is cancelled or expires after recovery failure.", "Customer", "shink-subscription-ended"),
        ("Subscription recovery", "After failed recurring Paystack authorization; immediate/day-3/day-5 sequence.", "Customer", "3 published Resend templates"),
        ("Admin ops alert", "Operational subscription and recovery events.", "Internal", "shink-admin-ops-alert"),
        ("Password reset", "User/admin requested password reset.", "Customer", "Supabase Auth template"),
        ("Email change confirmation", "Signed-in user requests an email address change.", "Customer", "Supabase Auth template"),
    ]
    table_data = [[para("Email", styles["table_header"]), para("When sent", styles["table_header"]), para("Audience", styles["table_header"]), para("Template/source", styles["table_header"])]]
    table_data.extend([[para(a, styles["small"]), para(b, styles["small"]), para(c, styles["small"]), para(d, styles["small"])] for a, b, c, d in rows])
    table = Table(table_data, colWidths=[39 * mm, 72 * mm, 24 * mm, 31 * mm], repeatRows=1)
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#222222")),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("BACKGROUND", (0, 1), (-1, -1), colors.HexColor("#FBFAF8")),
                ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#D8D2CA")),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 5),
                ("RIGHTPADDING", (0, 0), (-1, -1), 5),
                ("TOPPADDING", (0, 0), (-1, -1), 4),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
            ]
        )
    )
    story.extend([table, PageBreak(), para("Actual Examples", styles["section"])])

    examples = [
        (
            "1. Contact form internal notification",
            [
                ("Trigger", "POST /api/contact after validation, honeypot, and rate-limit checks."),
                ("From / To", "Resend:FromEmail -> Resend:ToEmail; reply_to is the visitor email."),
                ("Subject", "Kontakvorm: Stories navraag"),
                ("Source", "ResendContactEmailService.SendContactEmailAsync"),
            ],
            """
Subject: Kontakvorm: Stories navraag

Nuwe boodskap vanaf Schink kontakvorm

Naam: Mia Botha
E-pos: mia@example.com
Onderwerp: Stories navraag

Boodskap:
Goeie dag, ek wil graag hoor watter stories geskik is vir Graad 1 kinders.
Kan julle asseblief meer inligting stuur?
            """,
        ),
        (
            "2. Contact form auto-reply",
            [
                ("Trigger", "Immediately after the internal contact notification succeeds."),
                ("Template", "shink-contact-auto-reply"),
                ("To", "Visitor email address."),
                ("Reply-to", "Resend:ToEmail when configured."),
            ],
            """
Template variables sent to Resend:

CONTACT_NAME_HTML=Mia Botha
CONTACT_NAME_TEXT=Mia Botha
CONTACT_SUBJECT_HTML=Stories navraag
CONTACT_SUBJECT_TEXT=Stories navraag
SUPPORT_EMAIL=support@example.com
SITE_URL=https://www.schink.co.za

Representative rendered copy:

Hallo Mia Botha,

Dankie vir jou boodskap oor Stories navraag. Ons het dit ontvang en sal so gou
moontlik terugkom na jou toe.

Schink Stories
            """,
        ),
        (
            "3. Abandoned checkout recovery - subscription/store",
            [
                ("Trigger", "Checkout is initialized for subscription or store order and recovery prerequisites pass."),
                ("Templates", "Hour1=efdf7097-0981-457f-b7d6-a9091faef8c2; Hour24=1583ea05-da74-4a85-816e-f456db34b46d; Day7=4dda903a-7a33-45c4-bdbf-616cd0e2cb96"),
                ("Schedule", "+1 hour, +24 hours, +7 days from recovery creation."),
                ("Cancellation", "Resolved/cancelled after payment, opt-out, or matching active subscription."),
            ],
            """
Template variables sent to Resend:

CUSTOMER_NAME=Mia
ITEM_NAME=Alle Stories
ITEM_SUMMARY=Onbeperkte toegang tot Schink Stories
CART_TOTAL=R 79.00
CHECKOUT_URL=https://www.schink.co.za/betaalherinneringe/gaan?id=...
OPTOUT_URL=https://www.schink.co.za/betaalherinneringe/stop?id=...
SUPPORT_EMAIL=support@example.com

Representative +1 hour copy:

Hallo Mia,

Jou Alle Stories betaling is nog nie voltooi nie. Jy kan met dieselfde veilige
skakel voortgaan en jou toegang aktiveer.

Voltooi betaling: https://www.schink.co.za/betaalherinneringe/gaan?id=...
            """,
        ),
        (
            "4. Store paid notification to Schink",
            [
                ("Trigger", "Store order payment status first changes to paid from Paystack callback/webhook."),
                ("From / To", "Resend:FromEmail -> Resend:ToEmail; reply_to is customer email."),
                ("Subject", "Store bestelling betaal: 2 items (W260608101500ABC123)"),
                ("Source", "ResendStoreOrderNotificationService.SendPaidOrderNotificationAsync"),
            ],
            """
Subject: Store bestelling betaal: 2 items (W260608101500ABC123)

Nuwe winkel bestelling is betaal

Verwysing: W260608101500ABC123
Aantal items: 2
Totaal: R 398.00

Bestelling:
- Schink Stories Boek x1 - R 199.00
- Aktiwiteitsboek x1 - R 199.00

Naam: Mia Botha
E-pos: mia@example.com
Selfoon: 0821234567
Adres: 12 Kerkstraat
Voorstad: Denneburg
Stad / Dorp: Paarl
Poskode: 7646

Notas:
Los asseblief by die ontvangs.
            """,
        ),
        (
            "5. Store customer order confirmation",
            [
                ("Trigger", "Same paid-order transition as the internal store notification."),
                ("Template", "shink-store-order-confirmation"),
                ("To", "Customer email."),
                ("Idempotency", "store-order-confirmation/{orderReference}"),
            ],
            """
Template variables sent to Resend:

CUSTOMER_NAME_TEXT=Mia Botha
ORDER_REFERENCE=W260608101500ABC123
ORDER_ITEMS_TEXT=- Schink Stories Boek x1 - R 199.00
ORDER_TOTAL=R 398.00
DELIVERY_ADDRESS_TEXT=12 Kerkstraat, Denneburg, Paarl, 7646

Representative rendered copy:

Hallo Mia Botha,

Dankie vir jou bestelling. Jou betaling is ontvang.

Bestelling: W260608101500ABC123
Totaal: R 398.00

Ons stuur jou bestelling na 'n PUDO locker so naby as moontlik aan die adres wat
jy ingevul het.
            """,
        ),
        (
            "6. Subscription confirmation",
            [
                ("Trigger", "PayFast COMPLETE or Paystack charge.success is persisted for a subscription."),
                ("Template", "shink-subscription-confirmation"),
                ("To", "Subscriber email."),
                ("Also sends", "Admin ops alert: Paid subscription confirmed."),
            ],
            """
Template variables sent to Resend:

CUSTOMER_NAME_TEXT=Mia
PLAN_NAME_TEXT=Alle Stories
AMOUNT=R 79.00
BILLING_LABEL_TEXT=maandeliks
NEXT_RENEWAL_DATE=08 Julie 2026
PAYMENT_PROVIDER_TEXT=Paystack
PAYMENT_REFERENCE_TEXT=SUB_abc123
BILLING_URL=https://www.schink.co.za/intekening-en-betaling
SUPPORT_EMAIL=support@example.com

Representative rendered copy:

Hallo Mia,

Jou Alle Stories intekening is bevestig. Jou betaling van R 79.00 is ontvang.
Jou volgende hernuwing is 08 Julie 2026.
            """,
        ),
        (
            "7. Subscription ended or cancelled",
            [
                ("Trigger", "Provider cancellation or payment recovery expiry ends paid access."),
                ("Template", "shink-subscription-ended"),
                ("To", "Subscriber email."),
                ("Also sends", "Admin ops alert: Subscription access ended."),
            ],
            """
Template variables sent to Resend:

CUSTOMER_NAME_TEXT=Mia
PLAN_NAME_TEXT=Alle Stories
STATUS_LABEL_TEXT=gekanselleer
ACCESS_MESSAGE_TEXT=Jou betaalde toegang is gekanselleer. Jou gratis stories bly beskikbaar, en jy kan enige tyd weer 'n plan kies.
ENDED_AT=08 Junie 2026
BILLING_URL=https://www.schink.co.za/intekening-en-betaling
SUPPORT_EMAIL=support@example.com

Representative rendered copy:

Hallo Mia,

Jou Alle Stories intekening is gekanselleer. Jou gratis stories bly beskikbaar,
en jy kan enige tyd weer 'n plan kies.
            """,
        ),
        (
            "8. Subscription payment recovery sequence",
            [
                ("Trigger", "Failed recurring Paystack authorization for an active subscription, followed by recovery email scheduling."),
                ("Templates", "shink-subscription-recovery-day-1, day-3, day-5"),
                ("Schedule", "Day 1 immediate; Day 3 at first failure +2 days; Day 5 at first failure +4 days."),
                ("Manual send", "Admin can send an immediate recovery email for active/failed recovery cases."),
            ],
            """
Template variables sent to Resend:

CUSTOMER_NAME=Mia
BILLING_URL=https://www.schink.co.za/intekening-en-betaling
RECOVERY_URL=https://www.schink.co.za/intekening-en-betaling
RECOVERY_ACTION_LABEL=Werk jou betaling by
RECOVERY_CONTEXT=Ons kon nie jou intekeningbetaling verwerk nie.
PLAN_NAME=Alle Stories
PAYMENT_PROVIDER=paystack

Representative Day 1 copy:

Hallo Mia,

Ons kon nie jou Alle Stories betaling verwerk nie. Jou toegang is nog in 'n
hersteltydperk. Werk asseblief jou betaling by om jou stories aan te hou luister.
            """,
        ),
        (
            "9. Admin ops alert",
            [
                ("Trigger", "Operational subscription/recovery events and selected failure paths."),
                ("Template", "shink-admin-ops-alert"),
                ("To", "Resend:ToEmail."),
                ("Examples", "subscription confirmed, recovery started/skipped, subscription ended, PayFast persist failed."),
            ],
            """
Template variables sent to Resend:

ALERT_TITLE_TEXT=Subscription payment recovery started
ALERT_SEVERITY=warning
ALERT_SUMMARY_TEXT=Payment recovery started for mia@example.com.
ALERT_DETAILS_TEXT=Provider: paystack
Subscription ID: 11111111-2222-3333-4444-555555555555
Recovery ID: rec_123
Provider payment ID: SUB_abc123
Grace ends: 2026-06-12T10:15:00.0000000Z
EVENT_REFERENCE_TEXT=rec_123
ACTION_URL=https://www.schink.co.za/admin
            """,
        ),
        (
            "10. Password reset - Supabase Auth",
            [
                ("Trigger", "User password reset request, admin reset, admin bulk reset, or admin-created subscriber reset."),
                ("Provider", "Supabase Auth, not local Resend."),
                ("Template body", "Configured in Supabase Auth, not stored in this repo."),
                ("Endpoint", "POST /auth/v1/recover"),
            ],
            """
Request example sent by the system:

POST https://<supabase-project>/auth/v1/recover
Headers: apikey=<publishable-key>; Authorization=Bearer <publishable-key>
Payload:
{
  "email": "mia@example.com",
  "redirect_to": "https://www.schink.co.za/herstel-wagwoord"
}

Representative purpose:

Mia receives a Supabase password recovery link that returns to Schink Stories'
/herstel-wagwoord flow.
            """,
        ),
        (
            "11. Email change confirmation - Supabase Auth",
            [
                ("Trigger", "Signed-in user requests a new email and confirms current password."),
                ("Provider", "Supabase Auth, not local Resend."),
                ("Callback", "/intekening-en-betaling?emailChange=complete&emailChangeState=..."),
                ("State lifetime", "24 hours."),
            ],
            """
Request example sent by the system:

PUT https://<supabase-project>/auth/v1/user?redirect_to=https://www.schink.co.za/intekening-en-betaling?emailChange=complete...
Headers: apikey=<publishable-key>; Authorization=Bearer <user-access-token>
Payload:
{
  "email": "new-address@example.com"
}

Representative purpose:

The user receives Supabase's email-change confirmation message and returns to
Schink Stories to complete the account email update.
            """,
        ),
    ]

    for item in examples:
        story.append(email_block(*item, styles))

    story.extend(
        [
            PageBreak(),
            para("Source Pointers", styles["section"]),
            para("Primary code paths used for this report:", styles["body"]),
            ListFlowable(
                [
                    ListItem(para("Shink/Program.cs - route triggers for contact, checkout, callbacks, webhooks, auth reset, and email change.", styles["body"])),
                    ListItem(para("Shink/Services/ResendContactEmailService.cs - contact internal email and auto-reply payload.", styles["body"])),
                    ListItem(para("Shink/Services/SupabaseAbandonedCartRecoveryService.cs - abandoned checkout scheduling and cancellation.", styles["body"])),
                    ListItem(para("Shink/Services/ResendStoreOrderNotificationService.cs - store internal and customer confirmation payloads.", styles["body"])),
                    ListItem(para("Shink/Services/ResendSubscriptionNotificationEmailService.cs - subscription confirmation, ended, and admin alert payloads.", styles["body"])),
                    ListItem(para("Shink/Services/ResendSubscriptionPaymentRecoveryEmailService.cs - subscription recovery sequence payloads.", styles["body"])),
                    ListItem(para("Shink/Services/SupabaseAuthService.cs - Supabase password reset and email change requests.", styles["body"])),
                ],
                bulletType="bullet",
                leftIndent=12,
            ),
        ]
    )

    return story


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    doc = BaseDocTemplate(
        str(OUTPUT_FILE),
        pagesize=A4,
        leftMargin=16 * mm,
        rightMargin=16 * mm,
        topMargin=20 * mm,
        bottomMargin=18 * mm,
        title="Schink Stories Email Examples Report",
        author="Codex",
    )
    frame = Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="normal")
    doc.addPageTemplates([PageTemplate(id="main", frames=[frame], onPage=on_page)])
    doc.build(build_story())
    print(OUTPUT_FILE)


if __name__ == "__main__":
    main()

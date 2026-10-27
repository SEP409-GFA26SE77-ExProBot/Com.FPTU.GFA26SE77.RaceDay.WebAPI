# -*- coding: utf-8 -*-
"""Generate RaceDay Conceptual + Physical ERD as draw.io (.drawio) files."""
from __future__ import annotations

import html
from pathlib import Path

OUT_DIR = Path(r"d:\SEP\SEP_WebAPI\docs")


def attr(s: str) -> str:
    """Escape text for use inside an XML attribute value (draw.io requires this for HTML labels)."""
    return html.escape(s, quote=True).replace("\r\n", "&#xa;").replace("\n", "&#xa;").replace("\r", "&#xa;")


def entity_cell(
    eid: str,
    x: int,
    y: int,
    w: int,
    h: int,
    title: str,
    lines: list[str],
    fill: str,
    stroke: str = "#334155",
) -> str:
    # Build HTML label, then XML-escape the whole string so '<' and nested quotes are valid in value="...".
    safe_title = html.escape(title, quote=False)
    safe_body = "<br/>".join(html.escape(l, quote=False) for l in lines)
    label_html = f"<b>{safe_title}</b><hr/>{safe_body}"
    style = (
        f"rounded=1;whiteSpace=wrap;html=1;align=left;verticalAlign=top;"
        f"spacingLeft=8;spacingRight=8;spacingTop=6;spacingBottom=6;"
        f"fillColor={fill};strokeColor={stroke};fontColor=#0f172a;"
        f"fontFamily=Helvetica;fontSize=11;"
    )
    return (
        f'<mxCell id="{eid}" value="{attr(label_html)}" style="{style}" '
        f'vertex="1" parent="1">'
        f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/>'
        f"</mxCell>"
    )


def edge(
    eid: str,
    source: str,
    target: str,
    label: str = "",
    dashed: bool = False,
    end_arrow: str = "ERmany",
    start_arrow: str = "ERone",
) -> str:
    dash = "dashed=1;dashPattern=6 4;" if dashed else ""
    style = (
        f"edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;"
        f"html=1;endArrow={end_arrow};startArrow={start_arrow};"
        f"strokeColor=#64748b;fontSize=10;{dash}"
    )
    lab = attr(label) if label else ""
    return (
        f'<mxCell id="{eid}" value="{lab}" style="{style}" edge="1" parent="1" '
        f'source="{source}" target="{target}">'
        f'<mxGeometry relative="1" as="geometry"/>'
        f"</mxCell>"
    )


def swimlane(eid: str, x: int, y: int, w: int, h: int, title: str, fill: str) -> str:
    style = (
        f"swimlane;fontStyle=1;align=center;verticalAlign=top;childLayout=stackLayout;"
        f"horizontal=1;startSize=28;horizontalStack=0;resizeParent=1;resizeLast=0;"
        f"collapsible=0;marginBottom=0;fillColor={fill};strokeColor=#475569;"
        f"fontColor=#0f172a;fontSize=12;"
    )
    return (
        f'<mxCell id="{eid}" value="{attr(title)}" style="{style}" vertex="1" parent="1">'
        f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/>'
        f"</mxCell>"
    )


def note(eid: str, x: int, y: int, w: int, h: int, text: str) -> str:
    style = (
        "shape=note;whiteSpace=wrap;html=1;backgroundOutline=1;darkOpacity=0.05;"
        "fillColor=#FEF3C7;strokeColor=#D97706;fontSize=10;align=left;spacing=6;"
    )
    return (
        f'<mxCell id="{eid}" value="{attr(text)}" style="{style}" vertex="1" parent="1">'
        f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/>'
        f"</mxCell>"
    )


def wrap(diagram_name: str, cells: list[str], page_w: int = 1600, page_h: int = 1200) -> str:
    inner = "\n".join(cells)
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<mxfile host="app.diagrams.net" modified="2026-10-26T15:00:00.000Z" agent="RaceDay" version="22.1.0">
  <diagram id="{attr(diagram_name)}" name="{attr(diagram_name)}">
    <mxGraphModel dx="1200" dy="800" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{page_w}" pageHeight="{page_h}" math="0" shadow="0">
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>
{inner}
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
"""


def build_conceptual() -> str:
    cells: list[str] = []
    # Legend
    cells.append(
        note(
            "n1",
            40,
            20,
            420,
            90,
            "CONCEPTUAL ERD — RaceDay Modular Monolith\n"
            "Solid line = ownership / strong association within domain.\n"
            "Dashed line = logical cross-module reference (no cross-schema FK).\n"
            "Source: src/Modules/* Domain + Contracts.",
        )
    )

    # IAM
    cells.append(
        entity_cell(
            "User",
            60,
            150,
            200,
            150,
            "User",
            [
                "username, email",
                "passwordHash, status",
                "roleId, profile",
                "emailVerified",
            ],
            "#DBEAFE",
        )
    )
    cells.append(
        entity_cell(
            "Role",
            60,
            340,
            180,
            90,
            "Role",
            ["roleName", "description"],
            "#DBEAFE",
        )
    )
    cells.append(
        entity_cell(
            "Profile",
            300,
            150,
            180,
            120,
            "OrganizerProfile",
            ["verificationStatus", "contact", "verifiedAt"],
            "#DBEAFE",
        )
    )

    # Event core
    cells.append(
        entity_cell(
            "RaceEvent",
            560,
            140,
            220,
            170,
            "RaceEvent",
            [
                "organizerUserId (logical)",
                "name, location, dates",
                "status (draft→approved)",
                "registration window",
                "packagePurchaseId?",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "EventDistance",
            560,
            360,
            220,
            140,
            "EventDistance",
            ["distanceKm, price", "capacity, bibRange", "start/end time", "routeGeometry?"],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "EventCheckpoint",
            560,
            540,
            220,
            110,
            "EventCheckpoint",
            ["km, lat/lng", "type, cutoff", "belongs Distance"],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "EventDocument",
            820,
            140,
            200,
            110,
            "EventDocument",
            ["documentKey", "fileUrl", "required"],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "EventStaff",
            820,
            280,
            200,
            110,
            "EventStaffMember",
            ["userId (logical)", "staffRole", "status"],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "EventSupply",
            820,
            420,
            200,
            100,
            "EventSupplyItem",
            ["name, quantity", "checkpoint?"],
            "#DCFCE7",
        )
    )
    cells.append(
        entity_cell(
            "PackagePurchase",
            300,
            340,
            220,
            130,
            "EventPackagePurchase",
            ["organizerUserId", "duration, capacity", "totalAmount, status", "promo?"],
            "#DCFCE7",
        )
    )

    # Registration
    cells.append(
        entity_cell(
            "Registration",
            1080,
            160,
            240,
            200,
            "EventRegistration",
            [
                "userId (logical)",
                "eventId, distanceId",
                "status, bibNumber?",
                "medical / shirt / liability",
                "check-in / start / finish",
                "resultSeconds?",
            ],
            "#FEF3C7",
        )
    )
    cells.append(
        entity_cell(
            "RegPassage",
            1080,
            400,
            220,
            100,
            "CheckpointPassage",
            ["registrationId", "checkpointId", "passedAtUtc"],
            "#FEF3C7",
        )
    )

    # Payment / Finance
    cells.append(
        entity_cell(
            "Payment",
            1080,
            540,
            220,
            140,
            "PaymentOrder",
            ["userId (logical)", "registrationId?", "amount, status", "purpose, gateway"],
            "#FCE7F3",
        )
    )
    cells.append(
        entity_cell(
            "PayTxn",
            1340,
            540,
            180,
            100,
            "PaymentTransaction",
            ["paymentId", "type, status", "rawResponse"],
            "#FCE7F3",
        )
    )
    cells.append(
        entity_cell(
            "Ledger",
            1340,
            160,
            200,
            120,
            "EventLedger",
            ["eventId (logical)", "gross / refunded", "payoutPending"],
            "#FCE7F3",
        )
    )
    cells.append(
        entity_cell(
            "Payout",
            1340,
            310,
            200,
            110,
            "PayoutRequest",
            ["eventId, organizer", "amount, bank", "status"],
            "#FCE7F3",
        )
    )

    # Photo
    cells.append(
        entity_cell(
            "Photo",
            60,
            520,
            220,
            150,
            "PhotoAsset",
            ["eventId (logical)", "uploadedByUserId", "url, status", "isPublished"],
            "#E0E7FF",
        )
    )
    cells.append(
        entity_cell(
            "PhotoTag",
            60,
            700,
            200,
            100,
            "PhotoTag",
            ["bibNormalized", "confidence", "source OCR/Manual"],
            "#E0E7FF",
        )
    )
    cells.append(
        entity_cell(
            "PhotoDispute",
            300,
            700,
            220,
            110,
            "PhotoDispute",
            ["photoId, reporter", "reason, status", "reviewer?"],
            "#E0E7FF",
        )
    )

    # Map / Device / Audit
    cells.append(
        entity_cell(
            "RaceCourse",
            820,
            560,
            200,
            110,
            "RaceCourse (Map)",
            ["eventId, distanceId", "courseJson (Geo)", "title"],
            "#CCFBF1",
        )
    )
    cells.append(
        entity_cell(
            "SosAlert",
            560,
            700,
            200,
            110,
            "SosAlert (Device)",
            ["runner/user refs", "status", "locations[]"],
            "#FFEDD5",
        )
    )
    cells.append(
        entity_cell(
            "AuditEntry",
            1340,
            700,
            200,
            110,
            "AuditEntry (Mongo)",
            ["action, actorId", "resourceType/Id", "payloadJson"],
            "#F1F5F9",
        )
    )

    # Relationships
    cells += [
        edge("e1", "User", "Role", "has", end_arrow="ERone", start_arrow="ERmany"),
        edge("e2", "User", "Profile", "1:1 organizer", end_arrow="ERone", start_arrow="ERone"),
        edge("e3", "RaceEvent", "EventDistance", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e4", "EventDistance", "EventCheckpoint", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e5", "RaceEvent", "EventDocument", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e6", "RaceEvent", "EventStaff", "assigns", end_arrow="ERmany", start_arrow="ERone"),
        edge("e7", "RaceEvent", "EventSupply", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e8", "RaceEvent", "Registration", "receives", end_arrow="ERmany", start_arrow="ERone"),
        edge("e9", "EventDistance", "Registration", "for", end_arrow="ERmany", start_arrow="ERone"),
        edge("e10", "Registration", "RegPassage", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e11", "Payment", "PayTxn", "has", end_arrow="ERmany", start_arrow="ERone"),
        edge("e12", "Photo", "PhotoTag", "tagged", end_arrow="ERmany", start_arrow="ERone"),
        edge("e13", "Photo", "PhotoDispute", "may have", end_arrow="ERmany", start_arrow="ERone"),
        # logical dashed
        edge("e14", "User", "RaceEvent", "organizes", dashed=True, end_arrow="ERmany", start_arrow="ERone"),
        edge("e15", "User", "Registration", "registers", dashed=True, end_arrow="ERmany", start_arrow="ERone"),
        edge("e16", "Registration", "Payment", "pays for", dashed=True, end_arrow="ERzeroToOne", start_arrow="ERone"),
        edge("e17", "RaceEvent", "Photo", "gallery of", dashed=True, end_arrow="ERmany", start_arrow="ERone"),
        edge("e18", "RaceEvent", "RaceCourse", "mapped by", dashed=True, end_arrow="ERmany", start_arrow="ERone"),
        edge("e19", "RaceEvent", "Ledger", "financed as", dashed=True, end_arrow="ERone", start_arrow="ERone"),
        edge("e20", "PackagePurchase", "RaceEvent", "enables", dashed=True, end_arrow="ERzeroToOne", start_arrow="ERone"),
    ]
    return wrap("RaceDay Conceptual ERD", cells, 1700, 900)


def phys_table(
    eid: str,
    x: int,
    y: int,
    w: int,
    title: str,
    cols: list[str],
    fill: str,
) -> str:
    h = 28 + 16 * len(cols) + 16
    lines = cols
    return entity_cell(eid, x, y, w, h, title, lines, fill)


def build_physical() -> str:
    cells: list[str] = []
    cells.append(
        note(
            "pn1",
            40,
            20,
            520,
            100,
            "PHYSICAL ERD — PostgreSQL schemas + MongoDB AuditLog\n"
            "PK = primary key · FK = in-schema foreign key\n"
            "LogicalId = Guid reference across modules (NO DB FK — modular monolith rule)\n"
            "Outbox tables per transactional module omitted for clarity (iam/payment/map/device).",
        )
    )

    # IAM schema cluster
    cells.append(
        phys_table(
            "iam_users",
            40,
            150,
            260,
            "iam.users",
            [
                "PK Id uuid",
                "Username, Email unique",
                "PasswordHash, Status",
                "FK RoleId → roles",
                "FullName, Phone, DOB…",
                "EmailVerified, CreatedAt",
            ],
            "#DBEAFE",
        )
    )
    cells.append(
        phys_table(
            "iam_roles",
            40,
            380,
            220,
            "iam.roles",
            ["PK Id uuid", "RoleName unique", "Description"],
            "#DBEAFE",
        )
    )
    cells.append(
        phys_table(
            "iam_profiles",
            40,
            520,
            240,
            "iam.profiles",
            [
                "PK Id = users.Id (1:1)",
                "VerificationStatus",
                "ContactEmail/Phone",
                "VerifiedAt, VerifiedBy",
            ],
            "#DBEAFE",
        )
    )
    cells.append(
        phys_table(
            "iam_otp",
            40,
            700,
            240,
            "iam.email_otps",
            ["PK Id", "Email, CodeHash", "Purpose, ExpiresOnUtc", "UserId?"],
            "#DBEAFE",
        )
    )

    # EVENT
    cells.append(
        phys_table(
            "ev_events",
            360,
            150,
            280,
            "event.race_events",
            [
                "PK Id uuid",
                "Logical OrganizerUserId",
                "Name, Location, EventDate",
                "Status, BannerImage",
                "Reg opens/closes UTC",
                "PackagePurchaseId?",
                "Rules/Rewards/Merch JSON",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_dist",
            360,
            420,
            280,
            "event.event_distances",
            [
                "PK Id",
                "FK EventId → race_events",
                "Name, DistanceKm, Price",
                "Capacity, RegisteredCount",
                "BibPrefix, BibRange*",
                "StartAt/EndAt, RouteGeometryJson",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_cp",
            360,
            680,
            280,
            "event.event_checkpoints",
            [
                "PK Id",
                "FK DistanceId → distances",
                "Name, Km, Lat, Lng",
                "Type, Cutoff, SortOrder",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_doc",
            680,
            150,
            250,
            "event.event_documents",
            [
                "PK Id",
                "FK EventId",
                "DocumentKey, Title",
                "FileUrl, Required",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_staff",
            680,
            320,
            250,
            "event.event_staff_members",
            [
                "PK Id",
                "FK EventId",
                "Logical UserId",
                "Role, Status",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_sup",
            680,
            480,
            250,
            "event.event_supplies",
            ["PK Id", "FK EventId", "Name, Qty, Unit", "Notes"],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_pkg",
            680,
            640,
            260,
            "event.package_purchases",
            [
                "PK Id",
                "Logical OrganizerUserId",
                "DurationMonths, Cap",
                "TotalAmount, Status",
                "PaymentId?, PromoCode",
            ],
            "#DCFCE7",
        )
    )
    cells.append(
        phys_table(
            "ev_promo",
            680,
            860,
            240,
            "event.promo_codes (+ options)",
            ["promo_codes", "package_duration_options", "package_capacity_options"],
            "#DCFCE7",
        )
    )

    # Registration
    cells.append(
        phys_table(
            "ev_reg",
            980,
            150,
            300,
            "event.event_registrations",
            [
                "PK Id",
                "FK EventId, FK DistanceId",
                "Logical UserId",
                "Status, BibNumber?",
                "PII + MedicalCodesJson",
                "HealthCertUrl, Shirt*",
                "CheckedInAtUtc / ByUserId",
                "StartedAtUtc, FinishedAtUtc",
                "ResultSeconds",
                "UQ (Event,Distance,User) active",
            ],
            "#FEF3C7",
        )
    )
    cells.append(
        phys_table(
            "ev_pass",
            980,
            520,
            280,
            "event.event_registration_checkpoint_passages",
            [
                "PK Id",
                "FK RegistrationId",
                "Logical/FK CheckpointId",
                "PassedAtUtc",
            ],
            "#FEF3C7",
        )
    )

    # Payment
    cells.append(
        phys_table(
            "pay",
            980,
            700,
            300,
            "payment.payments",
            [
                "PK Id",
                "Logical UserId",
                "Logical RegistrationId?",
                "ReferenceId?, Purpose",
                "Amount, Currency, Status",
                "OrderCode unique, Gateway",
                "CheckoutUrl, PaidAtUtc",
            ],
            "#FCE7F3",
        )
    )
    cells.append(
        phys_table(
            "pay_txn",
            980,
            980,
            280,
            "payment.payment_transactions",
            [
                "PK Id",
                "FK PaymentId → payments",
                "TransactionType/Code",
                "Amount, Status",
                "RawResponseJson jsonb",
            ],
            "#FCE7F3",
        )
    )

    # Photo
    cells.append(
        phys_table(
            "ph",
            1330,
            150,
            280,
            "photo.photos",
            [
                "PK Id",
                "Logical EventId",
                "Logical UploadedByUserId",
                "Url, ContentSha256",
                "Status, IsPublished",
                "OcrJobId, Zone",
                "IX (EventId, Status/Published)",
            ],
            "#E0E7FF",
        )
    )
    cells.append(
        phys_table(
            "ph_tag",
            1330,
            420,
            260,
            "photo.photo_tags",
            [
                "PK Id",
                "FK PhotoId → photos CASCADE",
                "BibNormalized",
                "Confidence, Source",
                "BboxJson?",
            ],
            "#E0E7FF",
        )
    )
    cells.append(
        phys_table(
            "ph_dis",
            1330,
            620,
            280,
            "photo.photo_disputes",
            [
                "PK Id",
                "Logical PhotoId, EventId",
                "Logical ReporterUserId",
                "Reason, Status",
                "ReviewerUserId?, Note",
            ],
            "#E0E7FF",
        )
    )

    # Finance / Map / Device / Mongo
    cells.append(
        phys_table(
            "fin_led",
            1330,
            860,
            280,
            "finance.event_ledgers",
            [
                "PK Id",
                "Logical EventId unique",
                "OrganizerUserId",
                "Gross/Refunded/Withdrawn",
                "PayoutPending, TicketsSold",
            ],
            "#FCE7F3",
        )
    )
    cells.append(
        phys_table(
            "map_c",
            40,
            900,
            260,
            "map.race_courses",
            [
                "PK Id",
                "Logical EventId, DistanceId",
                "UQ (EventId, DistanceId)",
                "CourseJson jsonb",
                "Title, DistanceKm",
            ],
            "#CCFBF1",
        )
    )
    cells.append(
        phys_table(
            "dev_sos",
            340,
            900,
            280,
            "device.sos_alerts (+ locations)",
            [
                "device_auth_requests",
                "linked_devices",
                "sos_alerts / sos_locations",
                "Logical user/event refs",
            ],
            "#FFEDD5",
        )
    )
    cells.append(
        phys_table(
            "mongo",
            660,
            980,
            280,
            "MongoDB audit_entries",
            [
                "_id / Id Guid",
                "Action, ActorId",
                "ResourceType, ResourceId",
                "PayloadJson, OccurredOnUtc",
                "Append-only · no UoW",
            ],
            "#F1F5F9",
        )
    )

    # FK edges within schemas
    cells += [
        edge("pe1", "iam_users", "iam_roles", "FK RoleId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe2", "iam_profiles", "iam_users", "PK/FK Id", end_arrow="ERone", start_arrow="ERone"),
        edge("pe3", "ev_dist", "ev_events", "FK EventId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe4", "ev_cp", "ev_dist", "FK DistanceId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe5", "ev_doc", "ev_events", "FK EventId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe6", "ev_staff", "ev_events", "FK EventId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe7", "ev_sup", "ev_events", "FK EventId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe8", "ev_reg", "ev_events", "FK EventId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe9", "ev_reg", "ev_dist", "FK DistanceId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe10", "ev_pass", "ev_reg", "FK RegistrationId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe11", "pay_txn", "pay", "FK PaymentId", end_arrow="ERone", start_arrow="ERmany"),
        edge("pe12", "ph_tag", "ph", "FK PhotoId", end_arrow="ERone", start_arrow="ERmany"),
        # logical dashed
        edge("pe13", "ev_events", "iam_users", "OrganizerUserId", dashed=True),
        edge("pe14", "ev_reg", "iam_users", "UserId", dashed=True),
        edge("pe15", "pay", "ev_reg", "RegistrationId", dashed=True),
        edge("pe16", "ph", "ev_events", "EventId", dashed=True),
        edge("pe17", "map_c", "ev_dist", "DistanceId", dashed=True),
        edge("pe18", "fin_led", "ev_events", "EventId", dashed=True),
        edge("pe19", "ph_dis", "ph", "PhotoId (logical)", dashed=True),
    ]
    return wrap("RaceDay Physical ERD", cells, 1800, 1200)


def main() -> None:
    conceptual = OUT_DIR / "RaceDay_Conceptual_ERD.drawio"
    physical = OUT_DIR / "RaceDay_Physical_ERD.drawio"
    conceptual.write_text(build_conceptual(), encoding="utf-8")
    physical.write_text(build_physical(), encoding="utf-8")
    print("Wrote", conceptual)
    print("Wrote", physical)


if __name__ == "__main__":
    main()

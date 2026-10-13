# -*- coding: utf-8 -*-
"""Update D:\\SEP\\RaceDay_BE_ERD.drawio: mark surplus, add staff table + legend."""
from __future__ import annotations

import re
import shutil
from datetime import date
from pathlib import Path

SRC = Path(r"D:\SEP\RaceDay_BE_ERD.drawio")
BAK = Path(r"D:\SEP\RaceDay_BE_ERD.drawio.bak")

# Column name cell value → mark
DEPRECATED_COLS = {
    "MerchandiseJson": "MerchandiseJson [UNUSED]",
    "RouteGeometryJson": "RouteGeometryJson [-> map.CourseJson]",
}
USED_JSON = {
    "TicketPricingJson": "TicketPricingJson [FE]",
    "RulesJson": "RulesJson [FE]",
    "RewardsJson": "RewardsJson [FE]",
    "MedicalCodesJson": "MedicalCodesJson [FE]",
    "CourseJson": "CourseJson [FE SoT]",
    "RawResponseJson": "RawResponseJson [BE audit]",
}

DEP_ROW_STYLE = (
    "shape=partialRectangle;connectable=0;fillColor=#f8cecc;top=0;left=0;bottom=0;right=0;"
    "overflow=hidden;whiteSpace=wrap;html=1;fontSize=10;align=left;spacingLeft=4;fontColor=#b85450;"
)
USED_ROW_STYLE = (
    "shape=partialRectangle;connectable=0;fillColor=#d5e8d4;top=0;left=0;bottom=0;right=0;"
    "overflow=hidden;whiteSpace=wrap;html=1;fontSize=10;align=left;spacingLeft=4;fontColor=#82b366;"
)
DEP_TABLE_STYLE = (
    "shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;fixedRows=1;"
    "rowLines=0;fontStyle=1;align=center;resizeLast=1;html=1;fillColor=#f8cecc;strokeColor=#b85450;"
)
OK_TABLE_STYLE_HINT = "fillColor=#dae8fc;strokeColor=#6c8ebf"


def mark_column(xml: str, old_name: str, new_name: str, style: str) -> str:
    """Find cell whose value is exactly old_name and restyle + rename."""
    pattern = re.compile(
        rf'(<mxCell id="(\d+)" value="{re.escape(old_name)}" style=")([^"]*)(")',
        re.M,
    )

    def repl(m: re.Match) -> str:
        return f'{m.group(1)}{style}"'

    xml2, n = pattern.subn(repl, xml, count=1)
    if n == 0:
        print(f"WARN: column not found: {old_name}")
        return xml
    xml2 = xml2.replace(f'value="{old_name}"', f'value="{new_name}"', 1)
    print(f"OK column {old_name} -> {new_name}")
    return xml2


def mark_table_header(xml: str, table_name: str, new_title: str, style: str) -> str:
    pattern = re.compile(
        rf'(<mxCell id="(\d+)" value="{re.escape(table_name)}" style=")([^"]*)(")',
        re.M,
    )

    def repl(m: re.Match) -> str:
        return f'{m.group(1)}{style}"'

    xml2, n = pattern.subn(repl, xml, count=1)
    if n == 0:
        print(f"WARN: table not found: {table_name}")
        return xml
    xml2 = xml2.replace(f'value="{table_name}"', f'value="{new_title}"', 1)
    print(f"OK table {table_name} -> {new_title}")
    return xml2


def next_id(xml: str) -> int:
    ids = [int(x) for x in re.findall(r'id="(\d+)"', xml)]
    return max(ids) + 1 if ids else 9000


def make_row(pid: str, rid: int, flag: str, name: str, typ: str) -> tuple[str, int]:
    """Return XML snippet and next id."""
    row = rid
    c1, c2, c3 = rid + 1, rid + 2, rid + 3
    xml = f'''<mxCell id="{row}" value="" style="shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;fillColor=none;collapsible=0;dropTarget=0;points=[[0,0.5],[1,0.5]];portConstraint=eastwest;top=0;left=0;right=0;bottom=0;" vertex="1" parent="{pid}"><mxGeometry width="300" height="26" as="geometry"/></mxCell>
<mxCell id="{c1}" value="{flag}" style="shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;overflow=hidden;whiteSpace=wrap;html=1;fontSize=10;fontStyle=1;align=center;" vertex="1" parent="{row}"><mxGeometry width="54" height="26" as="geometry"><mxRectangle width="54" height="26" as="alternateBounds"/></mxGeometry></mxCell>
<mxCell id="{c2}" value="{name}" style="shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;overflow=hidden;whiteSpace=wrap;html=1;fontSize=10;align=left;spacingLeft=4;" vertex="1" parent="{row}"><mxGeometry width="150" height="26" as="geometry"><mxRectangle width="150" height="26" as="alternateBounds"/></mxGeometry></mxCell>
<mxCell id="{c3}" value="{typ}" style="shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;overflow=hidden;whiteSpace=wrap;html=1;fontSize=10;align=left;spacingLeft=2;fontColor=#666666;" vertex="1" parent="{row}"><mxGeometry width="96" height="26" as="geometry"><mxRectangle width="96" height="26" as="alternateBounds"/></mxGeometry></mxCell>
'''
    return xml, rid + 4


def build_staff_table(start_id: int) -> str:
    tid = start_id
    rows = [
        ("PK", "Id", "uuid"),
        ("FK", "EventId", "uuid"),
        ("", "UserId", "uuid?"),
        ("", "Email", "varchar(100)"),
        ("", "FullName", "varchar(100)"),
        ("", "Phone", "varchar(30)?"),
        ("", "Role", "varchar(40)"),
        ("", "Station", "varchar(80)?"),
        ("", "Status", "varchar(40)"),
        ("", "CreatedAtUtc", "timestamptz"),
        ("", "UpdatedAtUtc", "timestamptz"),
    ]
    h = 30 + 26 * len(rows)
    parts = [
        f'<mxCell id="{tid}" value="event_staff_members [FE]" style="shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;fixedRows=1;rowLines=0;fontStyle=1;align=center;resizeLast=1;html=1;{OK_TABLE_STYLE_HINT};" vertex="1" parent="1"><mxGeometry x="420" y="1840" width="300" height="{h}" as="geometry"/></mxCell>\n'
    ]
    rid = tid + 1
    for flag, name, typ in rows:
        chunk, rid = make_row(str(tid), rid, flag, name, typ)
        parts.append(chunk)
    # logical edge from race_events to staff
    edge_id = rid
    parts.append(
        f'<mxCell id="{edge_id}" value="1:N EventId" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=ERmany;startArrow=ERone;dashed=1;strokeColor=#6c8ebf;" edge="1" parent="1" source="137" target="{tid}"><mxGeometry relative="1" as="geometry"/></mxCell>\n'
    )
    return "".join(parts)


def build_legend(start_id: int) -> str:
    today = date.today().isoformat()
    note = (
        f"Audit {today} — FE True User + Admin&#xa;"
        "GREEN json = FE dùng | RED = thừa / thay thế&#xa;"
        "UNUSED: MerchandiseJson; owned Weather/Permit (khong tren ERD cot rieng)&#xa;"
        "RouteGeometryJson + event_checkpoints -> map.race_courses.CourseJson&#xa;"
        "Bank: org_* tren race_events trung finance.organizer_bank_accounts&#xa;"
        "Thieu truoc: event_staff_members (da them)"
    )
    return (
        f'<mxCell id="{start_id}" value="{note}" '
        'style="rounded=1;whiteSpace=wrap;html=1;align=left;verticalAlign=top;spacing=8;'
        "fillColor=#fff2cc;strokeColor=#d6b656;fontSize=11;\" vertex=\"1\" parent=\"1\">"
        '<mxGeometry x="1560" y="1560" width="420" height="160" as="geometry"/></mxCell>\n'
    )


def main() -> None:
    shutil.copy2(SRC, BAK)
    xml = SRC.read_text(encoding="utf-8")

    # Title / subtitle
    xml = xml.replace(
        'value="RaceDay BE ERD"',
        f'value="RaceDay BE ERD (FE-audit {date.today().isoformat()})"',
        1,
    )
    xml = xml.replace(
        'value="Solid = EF FK | Dashed = logical Guid"',
        'value="Solid = EF FK | Dashed = logical Guid | Green=FE used JSON | Red=surplus/deprecated"',
        1,
    )

    for old, new in DEPRECATED_COLS.items():
        xml = mark_column(xml, old, new, DEP_ROW_STYLE)
    for old, new in USED_JSON.items():
        xml = mark_column(xml, old, new, USED_ROW_STYLE)

    xml = mark_table_header(
        xml,
        "event_checkpoints",
        "event_checkpoints [DEPRECATED→Map]",
        DEP_TABLE_STYLE,
    )
    xml = mark_table_header(
        xml,
        "race_courses",
        "race_courses [FE SoT route]",
        "shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;fixedRows=1;rowLines=0;fontStyle=1;align=center;resizeLast=1;html=1;fillColor=#d5e8d4;strokeColor=#82b366;",
    )

    # Insert staff + legend before closing root
    nid = next_id(xml)
    insert = build_staff_table(nid) + build_legend(nid + 80)
    if "</root>" not in xml:
        raise SystemExit("no </root>")
    xml = xml.replace("</root>", insert + "</root>", 1)

    # bump page height a bit
    xml = xml.replace('pageHeight="2200"', 'pageHeight="2400"', 1)

    SRC.write_text(xml, encoding="utf-8")
    print(f"Wrote {SRC} (backup {BAK})")


if __name__ == "__main__":
    main()

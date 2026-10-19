# -*- coding: utf-8 -*-
"""Convert Device_Wear_SOS_API_Flows.md → Device_Wear_SOS_API_Guide.docx."""
from __future__ import annotations

import re
from pathlib import Path

from docx import Document
from docx.enum.text import WD_COLOR_INDEX
from docx.oxml.ns import qn
from docx.shared import Pt, RGBColor

ROOT = Path(__file__).resolve().parent
MD = ROOT / "Device_Wear_SOS_API_Flows.md"
OUT = ROOT / "Device_Wear_SOS_API_Guide.docx"


def set_run_font(run, *, mono: bool = False, bold: bool | None = None, size: int | None = None):
    if bold is not None:
        run.bold = bold
    if size is not None:
        run.font.size = Pt(size)
    run.font.name = "Consolas" if mono else "Calibri"
    r = run._element
    rPr = r.get_or_add_rPr()
    rFonts = rPr.get_or_add_rFonts()
    rFonts.set(qn("w:eastAsia"), "Calibri" if not mono else "Consolas")
    rFonts.set(qn("w:ascii"), "Consolas" if mono else "Calibri")
    rFonts.set(qn("w:hAnsi"), "Consolas" if mono else "Calibri")


def add_inline(paragraph, text: str, *, mono: bool = False):
    pattern = re.compile(r"(\*\*[^*]+\*\*|`[^`]+`)")
    parts = pattern.split(text)
    for part in parts:
        if not part:
            continue
        if part.startswith("**") and part.endswith("**"):
            run = paragraph.add_run(part[2:-2])
            set_run_font(run, bold=True, mono=mono)
        elif part.startswith("`") and part.endswith("`"):
            run = paragraph.add_run(part[1:-1])
            set_run_font(run, mono=True, size=9)
            run.font.highlight_color = WD_COLOR_INDEX.GRAY_25
        else:
            run = paragraph.add_run(part)
            set_run_font(run, mono=mono)


def add_heading(doc: Document, text: str, level: int):
    clean = re.sub(r"[*`]", "", text).strip()
    p = doc.add_heading(clean, level=min(level, 3))
    for run in p.runs:
        set_run_font(run, bold=True)


def add_para(doc: Document, text: str, *, style: str | None = None):
    p = doc.add_paragraph(style=style) if style else doc.add_paragraph()
    add_inline(p, text)
    return p


def add_code_block(doc: Document, code: str):
    for line in code.rstrip("\n").splitlines() or [""]:
        p = doc.add_paragraph()
        p.paragraph_format.space_before = Pt(0)
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.left_indent = Pt(12)
        run = p.add_run(line if line else " ")
        set_run_font(run, mono=True, size=8)
        run.font.color.rgb = RGBColor(0x33, 0x33, 0x33)


def parse_table(lines: list[str]) -> list[list[str]]:
    rows = []
    for line in lines:
        line = line.strip()
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        if all(re.match(r"^:?-+:?$", c or "") for c in cells):
            continue
        rows.append(cells)
    return rows


def add_table(doc: Document, rows: list[list[str]]):
    if not rows:
        return
    cols = max(len(r) for r in rows)
    table = doc.add_table(rows=len(rows), cols=cols)
    table.style = "Table Grid"
    for i, row in enumerate(rows):
        for j in range(cols):
            cell = table.rows[i].cells[j]
            cell.text = ""
            p = cell.paragraphs[0]
            val = row[j] if j < len(row) else ""
            add_inline(p, val)
            if i == 0:
                for run in p.runs:
                    run.bold = True
    doc.add_paragraph()


def convert(md_text: str) -> Document:
    doc = Document()
    style = doc.styles["Normal"]
    style.font.name = "Calibri"
    style.font.size = Pt(11)
    style._element.rPr.rFonts.set(qn("w:eastAsia"), "Calibri")

    title = doc.add_paragraph()
    r = title.add_run("RaceDay — Device Wear Flow + SOS API")
    set_run_font(r, bold=True, size=20)

    subtitle = doc.add_paragraph()
    add_inline(
        subtitle,
        "Nguồn: `docs/Device_Wear_SOS_API_Flows.md` (Claude Device Authorization RFC 8628 + SOS). "
        "Base URL: `http://localhost:5080`. JSON camelCase.",
    )

    add_heading(doc, "Seed nhanh (test)", 1)
    add_para(doc, "Mật khẩu chung: `MatKhau@123`.")
    for bullet in [
        "Runner (link đồng hồ): `binh.tran` / `binh.tran@raceday.vn`",
        "Organizer (list/ack SOS): `cuong.le`",
        "Admin: `admin`",
        "Approve link body: `{}` (optional `deviceName`)",
        "Wear token scope: `sos:create`",
    ]:
        add_para(doc, bullet, style="List Bullet")

    lines = md_text.splitlines()
    i = 0
    if lines and lines[0].startswith("# "):
        i = 1

    in_code = False
    code_lang = ""
    code_buf: list[str] = []
    table_buf: list[str] = []

    def flush_table():
        nonlocal table_buf
        if table_buf:
            add_table(doc, parse_table(table_buf))
            table_buf = []

    while i < len(lines):
        line = lines[i]

        if in_code:
            if line.strip().startswith("```"):
                code = "\n".join(code_buf)
                if code_lang == "mermaid":
                    add_para(doc, "Luồng (tóm tắt):")
                    for cl in code_buf:
                        cl = cl.strip()
                        if "->>" in cl or "→" in cl:
                            add_para(doc, cl.replace("->>", " → "), style="List Bullet")
                else:
                    add_code_block(doc, code)
                in_code = False
                code_buf = []
                code_lang = ""
            else:
                code_buf.append(line)
            i += 1
            continue

        if line.strip().startswith("```"):
            flush_table()
            in_code = True
            code_lang = line.strip()[3:].strip().lower()
            code_buf = []
            i += 1
            continue

        if line.strip().startswith("|"):
            table_buf.append(line)
            i += 1
            if i >= len(lines) or not lines[i].strip().startswith("|"):
                flush_table()
            continue
        else:
            flush_table()

        if line.strip() == "---":
            i += 1
            continue

        if line.startswith("# "):
            add_heading(doc, line[2:], 1)
        elif line.startswith("## "):
            add_heading(doc, line[3:], 1)
        elif line.startswith("### "):
            add_heading(doc, line[4:], 2)
        elif line.startswith("#### "):
            add_heading(doc, line[5:], 3)
        elif line.startswith("> "):
            p = doc.add_paragraph()
            add_inline(p, "Ghi chú: " + line[2:].lstrip("> ").strip())
            for run in p.runs:
                run.italic = True
        elif line.startswith("- "):
            add_para(doc, line[2:], style="List Bullet")
        elif line.strip() == "":
            pass
        else:
            add_para(doc, line)

        i += 1

    flush_table()
    return doc


def main():
    if not MD.exists():
        raise SystemExit(f"Missing {MD}")
    doc = convert(MD.read_text(encoding="utf-8"))
    doc.save(OUT)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()

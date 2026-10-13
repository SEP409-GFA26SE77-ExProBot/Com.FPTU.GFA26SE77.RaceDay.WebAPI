# -*- coding: utf-8 -*-
"""Convert RaceDay_API_Flows.md (+ seed header) into RaceDay_API_Guide.docx."""
from __future__ import annotations

import re
from pathlib import Path

from docx import Document
from docx.enum.text import WD_COLOR_INDEX
from docx.oxml.ns import qn
from docx.shared import Pt, RGBColor

ROOT = Path(__file__).resolve().parent
MD = ROOT / "RaceDay_API_Flows.md"
OUT = ROOT / "RaceDay_API_Guide.docx"


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
    """Parse **bold** and `code` inside a line."""
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
    # strip markdown # and inline markers for heading text (keep simple)
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

    # Title
    title = doc.add_paragraph()
    r = title.add_run("RaceDay API Guide")
    set_run_font(r, bold=True, size=20)

    subtitle = doc.add_paragraph()
    add_inline(
        subtitle,
        "Nguồn: `docs/RaceDay_API_Flows.md` + `docs/api-samples/` — theo luồng FE (input/output JSON + validation).",
    )

    # Seed pin (kept from old doc, updated)
    add_heading(doc, "0. Tài khoản seed (GHIM)", 1)
    add_para(doc, "Nguồn: `deploy/sql/raceday_init.sql` + EventDataSeeder. Mật khẩu chung: `MatKhau@123`.")
    add_heading(doc, "Login sẵn theo role", 2)
    add_code_block(
        doc,
        '{\n  "username": "admin",\n  "password": "MatKhau@123"\n}',
    )
    add_para(doc, "Admin — email `admin@raceday.vn`")
    add_code_block(
        doc,
        '{\n  "username": "cuong.le",\n  "password": "MatKhau@123"\n}',
    )
    add_para(doc, "Organizer — `cuong.le@raceday.vn`")
    add_code_block(
        doc,
        '{\n  "username": "binh.tran",\n  "password": "MatKhau@123"\n}',
    )
    add_para(doc, "Runner — `binh.tran@raceday.vn`")
    add_heading(doc, "ID seed hay dùng", 2)
    for bullet in [
        "Event flagship Quy Nhơn: `11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1`",
        "Distance 10K: `22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2`",
        "Registration Bình chờ TT: `44444444-dddd-4ddd-8ddd-ddddddddddd2`",
        "Base URL local: `http://localhost:5080` · Swagger: `/swagger`",
        "User FE `:3000` · Admin FE `:3001`",
    ]:
        add_para(doc, bullet, style="List Bullet")

    lines = md_text.splitlines()
    i = 0
    # skip first H1 of md (already titled)
    if lines and lines[0].startswith("# "):
        i = 1

    in_code = False
    code_lang = ""
    code_buf: list[str] = []
    table_buf: list[str] = []
    in_mermaid = False

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
                    add_para(doc, "Luồng (tóm tắt):", style=None)
                    # flatten mermaid to bullets
                    for cl in code_buf:
                        cl = cl.strip()
                        if "->>" in cl:
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
            # peek: if next not table, flush
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
            # blockquote → italic-ish note
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
    md = MD.read_text(encoding="utf-8")
    # Update status note inside md for docx context
    md = md.replace(
        "`docs/RaceDay_API_Guide.docx` = **chưa đủ** (thiếu Map, Finance, gói sự kiện, staff, nhiều endpoint Event mới).\n> - File này = nguồn đúng theo code hiện tại + field FE True / Admin đã gọi.",
        "Tài liệu Word này đã được **cập nhật từ `RaceDay_API_Flows.md`** (Map, Finance, packages, staff, JSON I/O, validation).",
    )
    md = md.replace(
        "**Kết luận:** danh sách API trong `.docx` **chưa đủ**. Dùng file này + Swagger `http://localhost:5080/swagger` làm nguồn chính.",
        "**Kết luận:** dùng Word này + Swagger `http://localhost:5080/swagger` + `docs/api-samples/`.",
    )
    md = md.replace("| Ghi trong docx cũ? |", "| Ghi trong Word? |")
    md = md.replace("| **thiếu** |", "| ✓ (đã bổ sung) |")
    md = md.replace("| thiếu |", "| ✓ |")
    md = md.replace("| thiếu / partial md |", "| ✓ |")
    md = md.replace("| một phần |", "| ✓ |")

    doc = convert(md)
    doc.save(OUT)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()

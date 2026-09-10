"""
Renders a ResumeLayout to a PDF using ReportLab.

Deliberately avoids anything that confuses ATS parsers: no images standing
in for text, no multi-column text frames, no headers/footers with content
in them - just real, selectable text in document order. The one Table use
(entry title/subtitle vs. right-aligned dates) still renders as plain
extractable text, it's only there for the two-column alignment.
"""

from __future__ import annotations

import io

from reportlab.lib import colors
from reportlab.lib.pagesizes import LETTER
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    HRFlowable,
    KeepTogether,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)

from app.templates.base import ResumeLayout

ACCENT_COLOR = colors.HexColor("#164fab")
TEXT_COLOR = colors.HexColor("#1a1a1a")
MUTED_COLOR = colors.HexColor("#555555")


def _build_styles() -> dict:
    base = getSampleStyleSheet()

    return {
        "name": ParagraphStyle(
            "ResumeName", parent=base["Title"], fontName="Helvetica-Bold",
            fontSize=20, leading=24, textColor=TEXT_COLOR, spaceAfter=2,
            alignment=1,
        ),
        "contact": ParagraphStyle(
            "ResumeContact", parent=base["Normal"], fontName="Helvetica",
            fontSize=9.5, leading=13, textColor=MUTED_COLOR, alignment=1,
            spaceAfter=14,
        ),
        "section_heading": ParagraphStyle(
            "SectionHeading", parent=base["Heading2"], fontName="Helvetica-Bold",
            fontSize=12, leading=15, textColor=ACCENT_COLOR, spaceBefore=12,
            spaceAfter=4, letterSpacing=0.5,
        ),
        "entry_title": ParagraphStyle(
            "EntryTitle", parent=base["Normal"], fontName="Helvetica-Bold",
            fontSize=10.5, leading=13, textColor=TEXT_COLOR,
        ),
        "entry_subtitle": ParagraphStyle(
            "EntrySubtitle", parent=base["Normal"], fontName="Helvetica-Oblique",
            fontSize=10, leading=13, textColor=MUTED_COLOR,
        ),
        "entry_meta": ParagraphStyle(
            "EntryMeta", parent=base["Normal"], fontName="Helvetica",
            fontSize=9.5, leading=13, textColor=MUTED_COLOR, alignment=2,
        ),
        "paragraph": ParagraphStyle(
            "EntryParagraph", parent=base["Normal"], fontName="Helvetica",
            fontSize=10, leading=14, textColor=TEXT_COLOR, spaceBefore=2,
            spaceAfter=2,
        ),
        "bullet": ParagraphStyle(
            "EntryBullet", parent=base["Normal"], fontName="Helvetica",
            fontSize=10, leading=14, textColor=TEXT_COLOR,
            leftIndent=14, bulletIndent=2, spaceAfter=2,
        ),
    }


def generate_pdf(layout: ResumeLayout) -> bytes:
    buffer = io.BytesIO()

    doc = SimpleDocTemplate(
        buffer,
        pagesize=LETTER,
        leftMargin=0.75 * inch,
        rightMargin=0.75 * inch,
        topMargin=0.6 * inch,
        bottomMargin=0.6 * inch,
        title=f"{layout.header.full_name} - Resume",
    )

    styles = _build_styles()
    story = []

    story.append(Paragraph(_escape(layout.header.full_name), styles["name"]))

    if layout.header.contact_line:
        story.append(Paragraph(_escape(layout.header.contact_line), styles["contact"]))

    story.append(HRFlowable(width="100%", thickness=1.2, color=ACCENT_COLOR, spaceAfter=8))

    for section in layout.sections:
        heading_block = [
            Paragraph(_escape(section.heading.upper()), styles["section_heading"]),
            HRFlowable(width="100%", thickness=0.6, color=colors.HexColor("#dddddd"), spaceAfter=6),
        ]

        if section.entries:
            # Keep the heading glued to its first entry so a heading never
            # ends up alone at the bottom of a page with its content pushed
            # to the next one. Later entries in a long section (e.g. many
            # jobs) can still flow across a page break normally.
            story.append(KeepTogether(heading_block + _render_entry(section.entries[0], styles)))

            for entry in section.entries[1:]:
                story.append(Spacer(1, 6))
                story.extend(_render_entry(entry, styles))
        else:
            story.extend(heading_block)

    doc.build(story)

    return buffer.getvalue()


def _render_entry(entry, styles) -> list:
    flowables = []

    has_title_row = bool(entry.title or entry.meta)

    if has_title_row:
        left_cell = [Paragraph(_escape(entry.title), styles["entry_title"])]

        if entry.subtitle:
            left_cell.append(Paragraph(_escape(entry.subtitle), styles["entry_subtitle"]))

        right_cell = Paragraph(_escape(entry.meta), styles["entry_meta"]) if entry.meta else ""

        table = Table(
            [[left_cell, right_cell]],
            colWidths=[4.6 * inch, 1.9 * inch],
        )
        table.setStyle(TableStyle([
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 0),
            ("RIGHTPADDING", (0, 0), (-1, -1), 0),
            ("TOPPADDING", (0, 0), (-1, -1), 0),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
        ]))
        flowables.append(table)

    elif entry.subtitle:
        flowables.append(Paragraph(_escape(entry.subtitle), styles["entry_subtitle"]))

    if entry.paragraph:
        flowables.append(Paragraph(_escape(entry.paragraph), styles["paragraph"]))

    for bullet in entry.bullets:
        flowables.append(Paragraph(f"&bull;&nbsp;&nbsp;{_escape(bullet)}", styles["bullet"]))

    return flowables


def _escape(text: str) -> str:
    return (
        (text or "")
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )

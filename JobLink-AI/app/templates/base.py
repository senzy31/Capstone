"""
Shared layout structures that every resume template builds and that both
document generators (PDF via ReportLab, DOCX via python-docx) consume.

A template's job is purely structural: which sections exist, in what order,
and how each piece of ResumeData maps into entries. It never touches
rendering (fonts, spacing, page breaks) - that's the generators' job. This
is what lets one template definition drive two very different output
formats without duplicating layout logic.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

from app.models import ResumeData


@dataclass
class EntryBlock:
    """One item within a section - a job, a degree, a project, a
    certification, or a single paragraph (e.g. the summary)."""

    title: str = ""
    subtitle: str = ""
    meta: str = ""  # right-aligned dates/location, e.g. "Jan 2024 - Present"
    paragraph: str = ""
    bullets: List[str] = field(default_factory=list)


@dataclass
class SectionBlock:
    heading: str
    entries: List[EntryBlock] = field(default_factory=list)


@dataclass
class HeaderBlock:
    full_name: str
    contact_line: str  # e.g. "email · phone · location · linkedin · portfolio"


@dataclass
class ResumeLayout:
    header: HeaderBlock
    sections: List[SectionBlock]
    template_id: str


def build_header(resume: ResumeData) -> HeaderBlock:
    info = resume.personal_info

    contact_parts = [
        part
        for part in [info.email, info.phone, info.location, info.linkedin, info.portfolio]
        if part
    ]

    return HeaderBlock(
        full_name=info.full_name,
        contact_line=" · ".join(contact_parts),
    )


def experience_entry(exp) -> EntryBlock:
    meta_parts = [p for p in [exp.start_date, exp.end_date] if p]
    bullets = list(exp.responsibilities) + list(exp.achievements)

    return EntryBlock(
        title=exp.position,
        subtitle=exp.company,
        meta=" - ".join(meta_parts) if len(meta_parts) == 2 else (meta_parts[0] if meta_parts else ""),
        paragraph=exp.description or "",
        bullets=bullets,
    )


def education_entry(edu) -> EntryBlock:
    degree_line = " ".join(part for part in [edu.degree, edu.field_of_study and f"in {edu.field_of_study}"] if part)
    meta_parts = [p for p in [edu.start_year, edu.graduation_year] if p]

    return EntryBlock(
        title=degree_line or edu.school,
        subtitle=edu.school if degree_line else "",
        meta=" - ".join(meta_parts) if len(meta_parts) == 2 else (meta_parts[0] if meta_parts else ""),
        bullets=list(edu.achievements),
    )


def project_entry(project) -> EntryBlock:
    subtitle = project.role or ""
    bullets = []

    if project.description:
        bullets.append(project.description)

    if project.technologies_used:
        bullets.append("Technologies: " + ", ".join(project.technologies_used))

    if project.result:
        bullets.append(f"Result: {project.result}")

    return EntryBlock(title=project.name, subtitle=subtitle, bullets=bullets)


def certification_entry(cert) -> EntryBlock:
    return EntryBlock(
        title=cert.name,
        subtitle=cert.organization or "",
        meta=cert.date or "",
    )


def achievement_entry(item) -> EntryBlock:
    return EntryBlock(
        title=item.title,
        subtitle=item.organization or "",
        meta=item.date or "",
    )


def skills_section(resume: ResumeData, heading: str = "Skills") -> Optional[SectionBlock]:
    skills = resume.skills

    categories = [
        ("Programming Languages", skills.programming_languages),
        ("Technical Skills", skills.technical_skills),
        ("Tools", skills.tools),
        ("Technologies", skills.technologies),
        ("Soft Skills", skills.soft_skills),
    ]

    entries = [
        EntryBlock(title=name, paragraph=", ".join(values))
        for name, values in categories
        if values
    ]

    return SectionBlock(heading=heading, entries=entries) if entries else None


def summary_section(resume: ResumeData, heading: str = "Professional Summary") -> Optional[SectionBlock]:
    summary = resume.career_info.professional_summary

    if not summary:
        return None

    return SectionBlock(heading=heading, entries=[EntryBlock(paragraph=summary)])

"""
Functional (skills-based) resume template - built for career changers or
candidates whose skills matter more than a strict work timeline. Skills are
grouped and pulled to the top, right after the summary; Experience is kept
as a condensed timeline (company, title, dates only) since the detailed
achievements already live under Skills instead of under each job.
"""

from __future__ import annotations

from app.models import ResumeData
from app.templates.base import (
    EntryBlock,
    ResumeLayout,
    SectionBlock,
    achievement_entry,
    build_header,
    certification_entry,
    education_entry,
    project_entry,
    skills_section,
    summary_section,
)

TEMPLATE_ID = "functional"


def _condensed_experience_entry(exp) -> EntryBlock:
    meta_parts = [p for p in [exp.start_date, exp.end_date] if p]

    return EntryBlock(
        title=exp.position,
        subtitle=exp.company,
        meta=" - ".join(meta_parts) if len(meta_parts) == 2 else (meta_parts[0] if meta_parts else ""),
    )


def build_layout(resume: ResumeData) -> ResumeLayout:
    sections = []

    summary = summary_section(resume)
    if summary:
        sections.append(summary)

    skills = skills_section(resume, heading="Core Skills")
    if skills:
        sections.append(skills)

    if resume.projects:
        sections.append(SectionBlock(
            heading="Key Projects",
            entries=[project_entry(p) for p in resume.projects],
        ))

    if resume.experience:
        sections.append(SectionBlock(
            heading="Work History",
            entries=[_condensed_experience_entry(exp) for exp in resume.experience],
        ))

    if resume.education:
        sections.append(SectionBlock(
            heading="Education",
            entries=[education_entry(edu) for edu in resume.education],
        ))

    additional_entries: list[EntryBlock] = []

    for cert in resume.certifications:
        additional_entries.append(certification_entry(cert))

    for ach in resume.achievements:
        additional_entries.append(achievement_entry(ach))

    if additional_entries:
        sections.append(SectionBlock(heading="Certifications & Achievements", entries=additional_entries))

    return ResumeLayout(header=build_header(resume), sections=sections, template_id=TEMPLATE_ID)

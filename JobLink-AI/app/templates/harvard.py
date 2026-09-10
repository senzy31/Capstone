"""
Harvard resume template - the classic academic/professional format.
Conservative and spare: leads with Education (Harvard Extension School's
own guidance puts education before experience for most candidates), keeps
the summary short, and folds certifications/projects/achievements into one
compact "Additional Information" section rather than giving each its own
full section with headings.
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
    experience_entry,
    project_entry,
    skills_section,
    summary_section,
)

TEMPLATE_ID = "harvard"


def build_layout(resume: ResumeData) -> ResumeLayout:
    sections = []

    summary = summary_section(resume)
    if summary:
        sections.append(summary)

    if resume.education:
        sections.append(SectionBlock(
            heading="Education",
            entries=[education_entry(edu) for edu in resume.education],
        ))

    if resume.experience:
        sections.append(SectionBlock(
            heading="Experience",
            entries=[experience_entry(exp) for exp in resume.experience],
        ))

    skills = skills_section(resume)
    if skills:
        sections.append(skills)

    if resume.projects:
        sections.append(SectionBlock(
            heading="Projects",
            entries=[project_entry(p) for p in resume.projects],
        ))

    additional_entries: list[EntryBlock] = []

    for cert in resume.certifications:
        additional_entries.append(certification_entry(cert))

    for ach in resume.achievements:
        additional_entries.append(achievement_entry(ach))

    if additional_entries:
        sections.append(SectionBlock(heading="Additional Information", entries=additional_entries))

    return ResumeLayout(header=build_header(resume), sections=sections, template_id=TEMPLATE_ID)

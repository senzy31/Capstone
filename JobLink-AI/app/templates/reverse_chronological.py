"""
Reverse-chronological resume template - the most common format. Leads with
a professional summary, then Experience (most recent first - callers are
expected to already list experience/education newest-first, which is how
the Resume Builder UI collects them), full detail across every section.
"""

from __future__ import annotations

from app.models import ResumeData
from app.templates.base import (
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

TEMPLATE_ID = "reverse_chronological"


def build_layout(resume: ResumeData) -> ResumeLayout:
    sections = []

    summary = summary_section(resume)
    if summary:
        sections.append(summary)

    if resume.experience:
        sections.append(SectionBlock(
            heading="Work Experience",
            entries=[experience_entry(exp) for exp in resume.experience],
        ))

    if resume.education:
        sections.append(SectionBlock(
            heading="Education",
            entries=[education_entry(edu) for edu in resume.education],
        ))

    skills = skills_section(resume)
    if skills:
        sections.append(skills)

    if resume.projects:
        sections.append(SectionBlock(
            heading="Projects",
            entries=[project_entry(p) for p in resume.projects],
        ))

    if resume.certifications:
        sections.append(SectionBlock(
            heading="Certifications",
            entries=[certification_entry(c) for c in resume.certifications],
        ))

    if resume.achievements:
        sections.append(SectionBlock(
            heading="Achievements",
            entries=[achievement_entry(a) for a in resume.achievements],
        ))

    return ResumeLayout(header=build_header(resume), sections=sections, template_id=TEMPLATE_ID)

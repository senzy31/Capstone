"""
ATS-friendly resume template - Premium only (see JobLinkv2/Services/Subscriptions:
PlanLimits.AdvancedTemplates). Structured for how parsers actually read a resume
rather than how a person skims one: the most literal, conventional section headings
("Work Experience", "Education", "Skills" - never folded together), full detail kept
inline rather than pulled into a separate "Additional Information" bucket the way
Harvard does, and Skills kept as its own prominent section rather than buried under
Experience the way Functional does.

The rendering itself (pdf_generator.py, docx_generator.py) is already ATS-safe for
every template - single column, real selectable text, no images or text boxes, no
header/footer content - so this file only decides section order and headings, the
same as harvard.py / functional.py / reverse_chronological.py.
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

TEMPLATE_ID = "ats"


def build_layout(resume: ResumeData) -> ResumeLayout:
    sections = []

    summary = summary_section(resume)
    if summary:
        sections.append(summary)

    skills = skills_section(resume)
    if skills:
        sections.append(skills)

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

    if resume.certifications:
        sections.append(SectionBlock(
            heading="Certifications",
            entries=[certification_entry(c) for c in resume.certifications],
        ))

    if resume.projects:
        sections.append(SectionBlock(
            heading="Projects",
            entries=[project_entry(p) for p in resume.projects],
        ))

    if resume.achievements:
        sections.append(SectionBlock(
            heading="Achievements",
            entries=[achievement_entry(a) for a in resume.achievements],
        ))

    return ResumeLayout(header=build_header(resume), sections=sections, template_id=TEMPLATE_ID)

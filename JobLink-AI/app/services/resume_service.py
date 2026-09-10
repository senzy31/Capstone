"""
Orchestrates resume-level operations: turning ResumeData into the context
text the AI needs, collecting the "known terms" fabrication-check baseline,
and building the PDF/DOCX documents via the selected template.
"""

from __future__ import annotations

from typing import List, Optional

from app import models
from app.services import ai_service
from app.templates import TEMPLATE_INFO, build_layout as build_template_layout
from app.utils.docx_generator import generate_docx
from app.utils.pdf_generator import generate_pdf


def collect_known_terms(resume: models.ResumeData) -> List[str]:
    """Every proper noun / skill term the user has actually entered anywhere
    on the resume - the fabrication-check baseline for any AI call touching
    this resume's data."""

    terms: List[str] = list(resume.skills.all_terms())

    for exp in resume.experience:
        terms.append(exp.company)
        terms.append(exp.position)

    for edu in resume.education:
        terms.append(edu.school)
        if edu.field_of_study:
            terms.append(edu.field_of_study)

    for cert in resume.certifications:
        terms.append(cert.name)

    for project in resume.projects:
        terms.append(project.name)
        terms.extend(project.technologies_used)

    for ach in resume.achievements:
        terms.append(ach.title)

    return [t for t in terms if t]


def build_summary_context(resume: models.ResumeData) -> str:
    lines = []

    if resume.career_info.target_position:
        lines.append(f"Target role: {resume.career_info.target_position}")

    if resume.career_info.career_objective:
        lines.append(f"Career objective (in the candidate's own words): {resume.career_info.career_objective}")

    if resume.skills.all_terms():
        lines.append(f"Skills: {', '.join(resume.skills.all_terms())}")

    if resume.experience:
        exp_lines = []
        for exp in resume.experience:
            parts = [f"{exp.position} at {exp.company}"]
            if exp.description:
                parts.append(exp.description)
            if exp.achievements:
                parts.append("Achievements: " + "; ".join(exp.achievements))
            exp_lines.append(" - ".join(parts))
        lines.append("Experience:\n" + "\n".join(f"  - {line}" for line in exp_lines))

    if resume.education:
        edu_lines = [
            f"{edu.degree or ''} {edu.field_of_study or ''} at {edu.school}".strip()
            for edu in resume.education
        ]
        lines.append("Education:\n" + "\n".join(f"  - {line}" for line in edu_lines))

    if resume.projects:
        project_lines = [f"{p.name}: {p.description or ''}".strip(": ") for p in resume.projects]
        lines.append("Projects:\n" + "\n".join(f"  - {line}" for line in project_lines))

    if not lines:
        lines.append(
            "The candidate hasn't filled in any details yet beyond their name and contact info."
        )

    return "\n\n".join(lines)


def generate_summary_for_resume(resume: models.ResumeData) -> tuple[str, List[str]]:
    context = build_summary_context(resume)
    known_terms = collect_known_terms(resume)

    return ai_service.generate_summary(context, known_terms)


def improve_text(request: models.ImproveContentRequest) -> tuple[str, List[str]]:
    return ai_service.improve_content(request.text, request.context, request.known_terms)


def generate_achievement_statement(request: models.GenerateAchievementRequest) -> tuple[str, List[str]]:
    return ai_service.generate_achievement(request.raw_description, request.technologies, request.context)


def get_template_catalog() -> List[models.TemplateInfo]:
    return TEMPLATE_INFO


def generate_pdf_bytes(resume: models.ResumeData, template_override: Optional[models.TemplateType] = None) -> bytes:
    resume_for_render = resume.model_copy(update={"template": template_override}) if template_override else resume
    layout = build_template_layout(resume_for_render)

    return generate_pdf(layout)


def generate_docx_bytes(resume: models.ResumeData, template_override: Optional[models.TemplateType] = None) -> bytes:
    resume_for_render = resume.model_copy(update={"template": template_override}) if template_override else resume
    layout = build_template_layout(resume_for_render)

    return generate_docx(layout)

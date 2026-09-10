"""
Job-resume compatibility matching. Shares the same deterministic keyword
extraction/matching as ats_service (same methodology, different framing and
output shape) - "job match" answers "how well do I fit this specific job
and what should I emphasize", where ATS score answers "would an ATS parser
rank this resume well against this posting".
"""

from __future__ import annotations

from app import models
from app.services import ai_service, ats_service


def match(resume: models.ResumeData, job_description: str) -> models.JobMatchResponse:
    resume_text = ats_service._resume_full_text(resume).lower()
    resume_skill_terms = {s.lower() for s in resume.skills.all_terms()}

    jd_keywords = ats_service.extract_keywords(job_description, limit=30)
    jd_skill_terms = [
        term for term in ats_service._SKILL_DICTIONARY
        if ats_service._contains_term(job_description.lower(), term)
    ]

    matching_skills = sorted({
        term for term in jd_skill_terms
        if term in resume_skill_terms or ats_service._contains_term(resume_text, term)
    })

    missing_keywords = sorted({
        kw for kw in jd_keywords
        if not ats_service._contains_term(resume_text, kw) and kw not in matching_skills
    })

    relevant_experience = _find_relevant_experience(resume, jd_keywords)

    skills_score = ats_service._percent(len(matching_skills), len(jd_skill_terms)) if jd_skill_terms else 100
    experience_score = ats_service._percent(len(relevant_experience), max(len(resume.experience), 1)) if resume.experience else 0

    compatibility_score = round(skills_score * 0.5 + experience_score * 0.5)

    # As in ats_service: the score above is fully computed without the AI -
    # don't discard it just because the AI suggestions call failed.
    try:
        recommended_improvements = ai_service.generate_job_match_notes(
            matching_skills, missing_keywords, relevant_experience, job_description
        )
    except ai_service.AIServiceError as exc:
        recommended_improvements = [f"AI-generated recommendations aren't available right now ({exc})."]

    return models.JobMatchResponse(
        compatibility_score=compatibility_score,
        matching_skills=matching_skills,
        missing_keywords=missing_keywords[:15],
        relevant_experience=relevant_experience,
        recommended_improvements=recommended_improvements,
    )


def _find_relevant_experience(resume: models.ResumeData, jd_keywords: list[str]) -> list[str]:
    relevant = []

    for exp in resume.experience:
        entry_text = " ".join(
            [exp.position, exp.company, exp.description or "", *exp.responsibilities, *exp.achievements]
        ).lower()

        hits = [kw for kw in jd_keywords if ats_service._contains_term(entry_text, kw)]

        if hits:
            relevant.append(f"{exp.position} at {exp.company} - relevant to: {', '.join(hits[:5])}")

    return relevant

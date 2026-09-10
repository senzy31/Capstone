"""
ATS (Applicant Tracking System) analysis.

The score itself is computed deterministically from keyword overlap - not
asked of the LLM - so it's explainable and reproducible for the same
resume/job description pair. The AI is only used afterward, grounded in the
already-computed matched/missing lists, to write the qualitative
suggestions (see ai_service.generate_ats_suggestions for the "never suggest
faking a skill" framing).
"""

from __future__ import annotations

import re
from typing import List, Set

from app import models
from app.services import ai_service

_STOPWORDS = {
    "a", "an", "the", "and", "or", "but", "if", "then", "of", "to", "in", "on", "for",
    "with", "as", "at", "by", "from", "is", "are", "was", "were", "be", "been", "being",
    "this", "that", "these", "those", "it", "its", "we", "you", "your", "our", "will",
    "shall", "can", "should", "would", "must", "may", "might", "have", "has", "had",
    "do", "does", "did", "not", "no", "yes", "all", "any", "some", "such", "both",
    "each", "few", "more", "most", "other", "into", "through", "during", "before",
    "after", "above", "below", "up", "down", "out", "off", "over", "under", "again",
    "further", "once", "here", "there", "when", "where", "why", "how", "than", "so",
    "job", "role", "position", "candidate", "candidates", "company", "team", "work",
    "working", "experience", "years", "year", "including", "etc", "strong", "ability",
    "skills", "required", "preferred", "responsibilities", "requirements", "about",
    "know", "known", "looking", "familiarity", "related", "field", "plus", "bonus",
}

# Multi-word and single-word tech/skill terms recognized as strong signals -
# extends the smaller list in ai_service with a few ATS-relevant additions.
_SKILL_DICTIONARY = sorted(set(ai_service._WATCHED_TERMS) | {
    "rest api", "restful", "microservices", "unit testing", "test driven development",
    "object oriented", "data structures", "algorithms", "api", "json", "xml",
    "communication", "leadership", "problem solving", "teamwork", "time management",
}, key=len, reverse=True)  # longer phrases matched first so they aren't shadowed by substrings

_EDUCATION_KEYWORDS = [
    "bachelor", "bachelor's", "master", "master's", "phd", "doctorate", "associate",
    "degree", "diploma",
]


def extract_keywords(text: str, limit: int = 25) -> List[str]:
    """Combines dictionary-hit terms with generic frequent-word extraction."""

    lower = text.lower()

    dictionary_hits = [term for term in _SKILL_DICTIONARY if _contains_term(lower, term)]

    # Inner '.', '+', '#', '-' are kept so "node.js", "c++", "c#" survive as
    # one token; stripped only from the ends so sentence punctuation
    # trailing an ordinary word ("Docker.", "FastAPI,") doesn't create a
    # separate near-duplicate keyword from the real one.
    raw_words = re.findall(r"[a-zA-Z][a-zA-Z+#.\-]{2,}", lower)
    words = [w.strip(".-") for w in raw_words]
    words = [w for w in words if len(w) >= 3]

    generic = [w for w in words if w not in _STOPWORDS and w not in dictionary_hits]

    freq: dict[str, int] = {}
    for w in generic:
        freq[w] = freq.get(w, 0) + 1

    generic_ranked = sorted(freq, key=lambda w: (-freq[w], w))

    combined = dictionary_hits + [w for w in generic_ranked if w not in dictionary_hits]

    return combined[:limit]


def _contains_term(lower_text: str, term: str) -> bool:
    pattern = r"(?<![a-z0-9])" + re.escape(term) + r"(?![a-z0-9])"
    return re.search(pattern, lower_text) is not None


def _resume_full_text(resume: models.ResumeData) -> str:
    parts = [
        resume.career_info.target_position or "",
        resume.career_info.career_objective or "",
        resume.career_info.professional_summary or "",
        " ".join(resume.skills.all_terms()),
    ]

    for exp in resume.experience:
        parts += [exp.company, exp.position, exp.description or ""]
        parts += exp.responsibilities
        parts += exp.achievements

    for edu in resume.education:
        parts += [edu.school, edu.degree or "", edu.field_of_study or ""]
        parts += edu.achievements

    for cert in resume.certifications:
        parts += [cert.name, cert.organization or ""]

    for project in resume.projects:
        parts += [project.name, project.description or "", project.role or "", project.result or ""]
        parts += project.technologies_used

    for ach in resume.achievements:
        parts += [ach.title, ach.organization or ""]

    return " ".join(p for p in parts if p)


def _experience_text(resume: models.ResumeData) -> str:
    parts = []
    for exp in resume.experience:
        parts += [exp.position, exp.company, exp.description or ""]
        parts += exp.responsibilities
        parts += exp.achievements
    return " ".join(p for p in parts if p)


def analyze(resume: models.ResumeData, job_description: str) -> models.AtsScoreResponse:
    resume_text = _resume_full_text(resume).lower()
    resume_skill_terms = {s.lower() for s in resume.skills.all_terms()}

    jd_keywords = extract_keywords(job_description, limit=30)
    jd_skill_terms = [term for term in _SKILL_DICTIONARY if _contains_term(job_description.lower(), term)]

    matched_skills = sorted({
        term for term in jd_skill_terms
        if term in resume_skill_terms or _contains_term(resume_text, term)
    })

    missing_keywords = sorted({
        kw for kw in jd_keywords
        if not _contains_term(resume_text, kw) and kw not in matched_skills
    })

    keyword_hits = sum(1 for kw in jd_keywords if _contains_term(resume_text, kw))
    keyword_match_score = _percent(keyword_hits, len(jd_keywords))

    skills_match_score = _percent(len(matched_skills), len(jd_skill_terms)) if jd_skill_terms else 100

    exp_text = _experience_text(resume).lower()
    exp_hits = sum(1 for kw in jd_keywords if _contains_term(exp_text, kw))
    experience_match_score = _percent(exp_hits, len(jd_keywords))

    education_match_score = _score_education(resume, job_description)

    overall_score = round(
        keyword_match_score * 0.30
        + skills_match_score * 0.35
        + experience_match_score * 0.25
        + education_match_score * 0.10
    )

    # The score itself is fully computed above without the AI - don't let a
    # failed/unconfigured AI call throw away a result that's otherwise ready.
    try:
        suggestions = ai_service.generate_ats_suggestions(matched_skills, missing_keywords, job_description)
    except ai_service.AIServiceError as exc:
        suggestions = [f"AI-generated suggestions aren't available right now ({exc})."]

    return models.AtsScoreResponse(
        overall_score=overall_score,
        keyword_match_score=keyword_match_score,
        skills_match_score=skills_match_score,
        experience_match_score=experience_match_score,
        education_match_score=education_match_score,
        matched_skills=matched_skills,
        missing_keywords=missing_keywords[:15],
        suggestions=suggestions,
    )


def _score_education(resume: models.ResumeData, job_description: str) -> int:
    jd_lower = job_description.lower()
    jd_mentions_education = any(kw in jd_lower for kw in _EDUCATION_KEYWORDS)

    if not jd_mentions_education:
        return 100  # not a stated requirement - not a gap

    if not resume.education:
        return 0

    # If the JD names a field of study, look for overlap; otherwise credit
    # any degree as satisfying a generic education requirement.
    field_terms = [edu.field_of_study.lower() for edu in resume.education if edu.field_of_study]

    if not field_terms:
        return 70  # has a degree, but the field itself isn't specified on the resume

    return 100 if any(term in jd_lower for term in field_terms) else 70


def _percent(hits: int, total: int) -> int:
    if total <= 0:
        return 100
    return round(min(hits, total) / total * 100)

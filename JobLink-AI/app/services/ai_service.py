"""
Wraps Claude for every AI-writing feature in the resume builder.

The one rule that overrides everything else in this file: the AI must never
invent a company, job title, skill, tool, technology, certification,
degree, school, or achievement that the user didn't provide. Every prompt
below states that explicitly, and generate() runs a second, independent
check afterward - scanning the model's own output for known tech/tool
keywords that don't appear anywhere in what the user actually gave it. That
check can't catch everything (it's a curated keyword list, not a full NLP
pipeline), but it catches exactly the failure mode called out in the spec
(a model casually adding "Django" or "Docker" because they're common,
plausible-sounding skills) instead of relying on prompting alone.
"""

from __future__ import annotations

import os
import re
from typing import List, Optional

from anthropic import Anthropic
from anthropic import (
    APIConnectionError,
    APIStatusError,
    InternalServerError,
    RateLimitError,
)
from dotenv import load_dotenv

load_dotenv()

MODEL = "claude-opus-5"

ANTI_FABRICATION_RULE = (
    "CRITICAL RULE: You must never invent, assume, or add any company, job title, "
    "skill, tool, technology, certification, degree, school, metric, or achievement "
    "that was not explicitly given to you below. You may only rephrase, reorganize, "
    "condense, or improve the clarity, grammar, and professional tone of what was "
    "actually provided. If there isn't enough information to write something "
    "substantive, say so plainly instead of making details up to fill the gap."
)


class AIServiceError(Exception):
    """Raised for any Claude call failure - the route layer maps this to an HTTP error."""


# A curated list of common resume-domain terms used only to flag possible
# fabrication - if one of these shows up in the AI's output but nowhere in
# the user's actual input, it's surfaced as a warning for the user to check.
_WATCHED_TERMS = [
    "python", "java", "javascript", "typescript", "c#", "c++", "golang", "ruby", "php",
    "swift", "kotlin", "react", "angular", "vue", "node.js", "nodejs", "django", "flask",
    "fastapi", "spring", "rails", "laravel", ".net", "docker", "kubernetes", "aws",
    "azure", "gcp", "google cloud", "terraform", "jenkins", "ci/cd", "git", "github",
    "gitlab", "bitbucket", "sql", "mysql", "postgresql", "postgres", "mongodb", "redis",
    "graphql", "rest api", "html", "css", "sass", "tailwind", "bootstrap", "redux",
    "tensorflow", "pytorch", "scikit-learn", "pandas", "numpy", "machine learning",
    "deep learning", "nlp", "blockchain", "linux", "bash", "powershell", "excel",
    "photoshop", "figma", "salesforce", "sap", "tableau", "power bi", "jira", "agile",
    "scrum", "kanban",
]


def _client() -> Anthropic:
    api_key = os.environ.get("ANTHROPIC_API_KEY")

    if not api_key:
        raise AIServiceError(
            "ANTHROPIC_API_KEY is not set. Add it to JobLink-AI/.env and restart the service."
        )

    return Anthropic(api_key=api_key)


def _call_claude(system: str, user_message: str, max_tokens: int = 1024) -> str:
    client = _client()

    try:
        response = client.messages.create(
            model=MODEL,
            max_tokens=max_tokens,
            system=system,
            output_config={"effort": "medium"},
            messages=[{"role": "user", "content": user_message}],
        )
    except RateLimitError as exc:
        raise AIServiceError("The AI service is rate-limited right now. Try again in a moment.") from exc
    except InternalServerError as exc:
        raise AIServiceError("The AI service is temporarily unavailable. Try again shortly.") from exc
    except APIConnectionError as exc:
        raise AIServiceError("Couldn't reach the AI service. Check the server's internet connection.") from exc
    except APIStatusError as exc:
        raise AIServiceError(f"The AI service returned an error: {exc.message}") from exc

    text_blocks = [block.text for block in response.content if block.type == "text"]
    text = "\n".join(text_blocks).strip()

    if not text:
        raise AIServiceError("The AI didn't return any text. Try again.")

    return text


def find_unsupported_terms(output_text: str, input_text: str, known_terms: Optional[List[str]] = None) -> List[str]:
    """Flags watched terms present in output_text but absent from both
    input_text and known_terms - a possible fabrication."""

    haystack = (input_text + " " + " ".join(known_terms or [])).lower()
    output_lower = output_text.lower()

    flagged = []

    for term in _WATCHED_TERMS:
        pattern = r"(?<![a-z0-9])" + re.escape(term) + r"(?![a-z0-9])"

        if re.search(pattern, output_lower) and not re.search(pattern, haystack):
            flagged.append(term)

    return flagged


def _warning_message(flagged: List[str]) -> List[str]:
    if not flagged:
        return []

    terms = ", ".join(sorted(set(flagged)))

    return [
        f"The AI mentioned the following that weren't found in what you entered - "
        f"please verify before keeping them: {terms}."
    ]


# ======================================================
# SUMMARY
# ======================================================

def generate_summary(context_text: str, known_terms: List[str]) -> tuple[str, List[str]]:
    system = (
        f"{ANTI_FABRICATION_RULE}\n\n"
        "You write professional resume summaries. Write 3-5 sentences, professional, "
        "clear, ATS-friendly, and relevant to the candidate's target role - using ONLY "
        "the details given below. No markdown, no quotation marks, no preamble like "
        "\"Here is...\" - respond with ONLY the summary text itself."
    )

    summary = _call_claude(system, context_text, max_tokens=512)
    warnings = _warning_message(find_unsupported_terms(summary, context_text, known_terms))

    return summary, warnings


# ======================================================
# IMPROVE CONTENT
# ======================================================

def improve_content(text: str, context: Optional[str], known_terms: List[str]) -> tuple[str, List[str]]:
    system = (
        f"{ANTI_FABRICATION_RULE}\n\n"
        "You improve resume text the candidate already wrote. Fix grammar, sentence "
        "structure, professional wording, clarity, and impact - while preserving the "
        "original meaning exactly. Do not add new claims, numbers, tools, or scope "
        "beyond what's already there. No markdown, no preamble, no quotation marks - "
        "respond with ONLY the improved text."
    )

    context_line = f"This text is for: {context}\n\n" if context else ""
    user_message = f"{context_line}Original text:\n{text}"

    improved = _call_claude(system, user_message, max_tokens=512)
    warnings = _warning_message(find_unsupported_terms(improved, text, known_terms))

    return improved, warnings


# ======================================================
# ACHIEVEMENT STATEMENT
# ======================================================

def generate_achievement(raw_description: str, technologies: List[str], context: Optional[str]) -> tuple[str, List[str]]:
    system = (
        f"{ANTI_FABRICATION_RULE}\n\n"
        "You turn a candidate's plain description of something they did into a single, "
        "stronger resume statement - starting with a strong action verb, specific and "
        "professional, still describing the exact same thing. Only mention a "
        "technology/tool if it was explicitly provided below - never assume a tech "
        "stack from the description alone. No markdown, no preamble, no quotation "
        "marks - respond with ONLY the one statement."
    )

    tech_line = f"Technologies actually used (only mention these, nothing else): {', '.join(technologies)}\n" if technologies else "No specific technologies were provided - do not name any.\n"
    context_line = f"Context: {context}\n" if context else ""
    user_message = f"{context_line}{tech_line}Candidate's description: {raw_description}"

    statement = _call_claude(system, user_message, max_tokens=256)
    warnings = _warning_message(find_unsupported_terms(statement, raw_description, technologies))

    return statement, warnings


# ======================================================
# ATS SUGGESTIONS (grounded - given the exact matched/missing lists)
# ======================================================

def generate_ats_suggestions(matched_skills: List[str], missing_keywords: List[str], job_description: str) -> List[str]:
    system = (
        f"{ANTI_FABRICATION_RULE}\n\n"
        "You write short, actionable resume improvement suggestions based on a "
        "keyword-matching report that has already been computed - you are not "
        "computing the match yourself. For each missing keyword, suggest the "
        "candidate add it ONLY IF they actually have that experience - always phrase "
        "it conditionally (e.g. \"If you have Django experience, mention it explicitly\"), "
        "never as an instruction to fake it. Return 3-6 short bullet suggestions, each "
        "on its own line starting with \"- \". No markdown formatting inside the bullets, "
        "no preamble - respond with ONLY the bullet list."
    )

    user_message = (
        f"Job description:\n{job_description}\n\n"
        f"Skills already matched: {', '.join(matched_skills) or 'none'}\n"
        f"Keywords missing from the resume: {', '.join(missing_keywords) or 'none'}"
    )

    text = _call_claude(system, user_message, max_tokens=512)

    return [line.strip("- ").strip() for line in text.splitlines() if line.strip()]


# ======================================================
# JOB MATCH NARRATIVE (grounded - given the exact computed overlap)
# ======================================================

def generate_job_match_notes(
    matching_skills: List[str],
    missing_keywords: List[str],
    relevant_experience: List[str],
    job_description: str,
) -> List[str]:
    system = (
        f"{ANTI_FABRICATION_RULE}\n\n"
        "You write short, specific recommendations for tailoring an existing resume to "
        "a specific job description, based on a comparison that has already been "
        "computed. Recommend rewording, reordering, or emphasizing existing content - "
        "never recommend adding a skill or experience the candidate doesn't have. "
        "Return 3-6 short bullet recommendations, each on its own line starting with "
        "\"- \". No preamble - respond with ONLY the bullet list."
    )

    user_message = (
        f"Job description:\n{job_description}\n\n"
        f"Matching skills: {', '.join(matching_skills) or 'none'}\n"
        f"Missing keywords: {', '.join(missing_keywords) or 'none'}\n"
        f"Relevant experience already on the resume: {'; '.join(relevant_experience) or 'none identified'}"
    )

    text = _call_claude(system, user_message, max_tokens=512)

    return [line.strip("- ").strip() for line in text.splitlines() if line.strip()]

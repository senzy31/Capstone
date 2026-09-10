"""
Pydantic models for the JobLink AI Resume Builder.

These mirror the resume sections defined in the capstone spec:
personal info, career info, education, work experience, skills,
certifications, projects, and achievements - plus the request/response
shapes for every AI and document-generation endpoint.
"""

from __future__ import annotations

from enum import Enum
from typing import List, Optional

from pydantic import BaseModel, EmailStr, Field


# ======================================================
# TEMPLATE TYPES
# ======================================================

class TemplateType(str, Enum):
    HARVARD = "harvard"
    REVERSE_CHRONOLOGICAL = "reverse_chronological"
    FUNCTIONAL = "functional"


class TemplateInfo(BaseModel):
    id: TemplateType
    name: str
    description: str


# ======================================================
# RESUME SECTIONS
# ======================================================

class PersonalInfo(BaseModel):
    full_name: str = Field(..., min_length=1)
    email: EmailStr
    phone: Optional[str] = None
    location: Optional[str] = None
    linkedin: Optional[str] = None
    portfolio: Optional[str] = None


class CareerInfo(BaseModel):
    target_position: Optional[str] = None
    career_objective: Optional[str] = None
    professional_summary: Optional[str] = None


class Education(BaseModel):
    school: str
    degree: Optional[str] = None
    field_of_study: Optional[str] = None
    start_year: Optional[str] = None
    graduation_year: Optional[str] = None
    achievements: List[str] = Field(default_factory=list)


class Experience(BaseModel):
    company: str
    position: str
    start_date: Optional[str] = None
    end_date: Optional[str] = None
    description: Optional[str] = None
    responsibilities: List[str] = Field(default_factory=list)
    achievements: List[str] = Field(default_factory=list)


class Skills(BaseModel):
    technical_skills: List[str] = Field(default_factory=list)
    soft_skills: List[str] = Field(default_factory=list)
    programming_languages: List[str] = Field(default_factory=list)
    tools: List[str] = Field(default_factory=list)
    technologies: List[str] = Field(default_factory=list)

    def all_terms(self) -> List[str]:
        """Every individual skill/tool/tech term, flattened - used for
        fabrication checks against AI output."""
        return [
            *self.technical_skills,
            *self.soft_skills,
            *self.programming_languages,
            *self.tools,
            *self.technologies,
        ]


class Certification(BaseModel):
    name: str
    organization: Optional[str] = None
    date: Optional[str] = None


class Project(BaseModel):
    name: str
    description: Optional[str] = None
    technologies_used: List[str] = Field(default_factory=list)
    role: Optional[str] = None
    result: Optional[str] = None


class Achievement(BaseModel):
    title: str
    organization: Optional[str] = None
    date: Optional[str] = None


# ======================================================
# FULL RESUME
# ======================================================

class ResumeData(BaseModel):
    personal_info: PersonalInfo
    career_info: CareerInfo = Field(default_factory=CareerInfo)
    education: List[Education] = Field(default_factory=list)
    experience: List[Experience] = Field(default_factory=list)
    skills: Skills = Field(default_factory=Skills)
    certifications: List[Certification] = Field(default_factory=list)
    projects: List[Project] = Field(default_factory=list)
    achievements: List[Achievement] = Field(default_factory=list)
    template: TemplateType = TemplateType.REVERSE_CHRONOLOGICAL


# ======================================================
# AI FEATURE REQUESTS / RESPONSES
# ======================================================

class GenerateSummaryRequest(BaseModel):
    resume: ResumeData


class GenerateSummaryResponse(BaseModel):
    summary: str
    warnings: List[str] = Field(default_factory=list)


class ImproveContentRequest(BaseModel):
    text: str = Field(..., min_length=1)
    context: Optional[str] = Field(
        None, description="What this text is for, e.g. 'work experience bullet point'"
    )
    known_terms: List[str] = Field(
        default_factory=list,
        description="Skills/companies/tools the user has actually provided elsewhere, "
                    "used to flag anything the AI adds that wasn't in that list.",
    )


class ImproveContentResponse(BaseModel):
    original_text: str
    improved_text: str
    warnings: List[str] = Field(default_factory=list)


class GenerateAchievementRequest(BaseModel):
    raw_description: str = Field(..., min_length=1)
    technologies: List[str] = Field(default_factory=list)
    context: Optional[str] = None


class GenerateAchievementResponse(BaseModel):
    statement: str
    warnings: List[str] = Field(default_factory=list)


class AtsScoreRequest(BaseModel):
    resume: ResumeData
    job_description: str = Field(..., min_length=1)


class AtsScoreResponse(BaseModel):
    overall_score: int
    keyword_match_score: int
    skills_match_score: int
    experience_match_score: int
    education_match_score: int
    matched_skills: List[str]
    missing_keywords: List[str]
    suggestions: List[str]


class JobMatchRequest(BaseModel):
    resume: ResumeData
    job_description: str = Field(..., min_length=1)


class JobMatchResponse(BaseModel):
    compatibility_score: int
    matching_skills: List[str]
    missing_keywords: List[str]
    relevant_experience: List[str]
    recommended_improvements: List[str]


class DocumentGenerationRequest(BaseModel):
    resume: ResumeData
    template: Optional[TemplateType] = None  # falls back to resume.template


class AnalysisReport(BaseModel):
    detected_sections: List[str]
    skills_found: List[str]
    experience_found: List[str]
    education_found: List[str]
    keywords: List[str]
    missing_sections: List[str]
    formatting_issues: List[str]
    suggestions: List[str]


class UploadResumeResponse(BaseModel):
    filename: str
    extracted_text: str
    analysis: AnalysisReport


class AnalyzeTextRequest(BaseModel):
    text: str = Field(..., min_length=1)


class TemplatesResponse(BaseModel):
    templates: List[TemplateInfo]


class ErrorResponse(BaseModel):
    message: str
    detail: Optional[str] = None

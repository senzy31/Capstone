"""
All /api/resume/* endpoints. Thin by design - each route validates the
request via Pydantic (handled automatically by FastAPI), delegates to a
service, and maps errors to clean HTTP responses. No business logic lives
here.
"""

from __future__ import annotations

from fastapi import APIRouter, File, HTTPException, UploadFile
from fastapi.responses import Response

from app import models
from app.services import ai_service, ats_service, matching_service, parser_service, resume_service

router = APIRouter(prefix="/resume", tags=["resume"])


def _ai_error_to_http(exc: ai_service.AIServiceError) -> HTTPException:
    return HTTPException(status_code=503, detail=str(exc))


# ======================================================
# AI CONTENT GENERATION
# ======================================================

@router.post("/generate-summary", response_model=models.GenerateSummaryResponse)
def generate_summary(request: models.GenerateSummaryRequest) -> models.GenerateSummaryResponse:
    try:
        summary, warnings = resume_service.generate_summary_for_resume(request.resume)
    except ai_service.AIServiceError as exc:
        raise _ai_error_to_http(exc) from exc

    return models.GenerateSummaryResponse(summary=summary, warnings=warnings)


@router.post("/improve-content", response_model=models.ImproveContentResponse)
def improve_content(request: models.ImproveContentRequest) -> models.ImproveContentResponse:
    try:
        improved, warnings = resume_service.improve_text(request)
    except ai_service.AIServiceError as exc:
        raise _ai_error_to_http(exc) from exc

    return models.ImproveContentResponse(
        original_text=request.text, improved_text=improved, warnings=warnings
    )


@router.post("/generate-achievement", response_model=models.GenerateAchievementResponse)
def generate_achievement(request: models.GenerateAchievementRequest) -> models.GenerateAchievementResponse:
    try:
        statement, warnings = resume_service.generate_achievement_statement(request)
    except ai_service.AIServiceError as exc:
        raise _ai_error_to_http(exc) from exc

    return models.GenerateAchievementResponse(statement=statement, warnings=warnings)


# ======================================================
# ATS SCORE / JOB MATCH
# ======================================================

@router.post("/ats-score", response_model=models.AtsScoreResponse)
def ats_score(request: models.AtsScoreRequest) -> models.AtsScoreResponse:
    try:
        return ats_service.analyze(request.resume, request.job_description)
    except ai_service.AIServiceError as exc:
        raise _ai_error_to_http(exc) from exc


@router.post("/job-match", response_model=models.JobMatchResponse)
def job_match(request: models.JobMatchRequest) -> models.JobMatchResponse:
    try:
        return matching_service.match(request.resume, request.job_description)
    except ai_service.AIServiceError as exc:
        raise _ai_error_to_http(exc) from exc


# ======================================================
# DOCUMENT GENERATION
# ======================================================

@router.post("/generate-pdf")
def generate_pdf(request: models.DocumentGenerationRequest) -> Response:
    pdf_bytes = resume_service.generate_pdf_bytes(request.resume, request.template)
    filename = _resume_filename(request.resume, "pdf")

    return Response(
        content=pdf_bytes,
        media_type="application/pdf",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


@router.post("/generate-docx")
def generate_docx(request: models.DocumentGenerationRequest) -> Response:
    docx_bytes = resume_service.generate_docx_bytes(request.resume, request.template)
    filename = _resume_filename(request.resume, "docx")

    return Response(
        content=docx_bytes,
        media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


def _resume_filename(resume: models.ResumeData, extension: str) -> str:
    safe_name = "".join(c for c in resume.personal_info.full_name if c.isalnum() or c in " -_").strip()
    safe_name = safe_name.replace(" ", "_") or "resume"

    return f"{safe_name}_Resume.{extension}"


# ======================================================
# UPLOAD / ANALYZE
# ======================================================

@router.post("/upload", response_model=models.UploadResumeResponse)
async def upload_resume(file: UploadFile = File(...)) -> models.UploadResumeResponse:
    filename = file.filename or "resume"
    content = await file.read()

    if not content:
        raise HTTPException(status_code=400, detail="The uploaded file is empty.")

    lower_name = filename.lower()

    try:
        if lower_name.endswith(".pdf"):
            text = parser_service.extract_text_from_pdf(content)
        elif lower_name.endswith(".docx"):
            text = parser_service.extract_text_from_docx(content)
        else:
            raise HTTPException(status_code=400, detail="Only .pdf and .docx files are supported.")
    except parser_service.ParserError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    if not text.strip():
        raise HTTPException(
            status_code=400,
            detail="No text could be extracted from this file - it may be an image-based/scanned document.",
        )

    analysis = parser_service.analyze_resume_text(text)

    return models.UploadResumeResponse(filename=filename, extracted_text=text, analysis=analysis)


@router.post("/analyze", response_model=models.AnalysisReport)
def analyze_text(request: models.AnalyzeTextRequest) -> models.AnalysisReport:
    return parser_service.analyze_resume_text(request.text)


# ======================================================
# TEMPLATES
# ======================================================

@router.get("/templates", response_model=models.TemplatesResponse)
def list_templates() -> models.TemplatesResponse:
    return models.TemplatesResponse(templates=resume_service.get_template_catalog())

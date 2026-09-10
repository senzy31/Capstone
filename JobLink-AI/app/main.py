"""
JobLink AI Resume Builder service entry point.

Run with:  uvicorn app.main:app --reload --port 8001
(from the JobLink-AI/ directory, with the venv active)
"""

from __future__ import annotations

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.routes import resume

app = FastAPI(
    title="JobLink AI Resume Builder",
    description="AI-assisted resume generation, ATS analysis, and job matching for JobLink.",
    version="1.0.0",
)

# Permissive CORS for local development - matches the same AllowAnyOrigin
# policy the .NET JobLink API already uses (Program.cs), since this is a
# local capstone project, not a public deployment.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(resume.router, prefix="/api")


@app.get("/api/health")
def health_check() -> dict:
    return {"status": "ok", "service": "joblink-ai"}

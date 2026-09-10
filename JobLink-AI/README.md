# JobLink AI Resume Builder

A standalone Python/FastAPI service that generates, analyzes, and scores
resumes with Claude - built as a separate microservice alongside the
existing .NET JobLink backend.

```
Frontend  ->  (.NET JobLink API, for auth/users/DB - not yet wired here)
Frontend  ->  Python FastAPI AI Service  ->  Claude
```

The Python service is **stateless**: it never touches SQL Server. It takes
resume data in a request body, generates/analyzes/scores it, and returns
the result (JSON, or a PDF/DOCX file). The .NET backend remains the single
source of truth for anything persisted.

## The one rule that shapes everything here

**The AI must never invent a company, title, skill, tool, degree,
certification, or achievement the user didn't provide.** Every AI prompt
in `app/services/ai_service.py` states this explicitly, and every response
is checked afterward against a curated keyword list - if the model's
output mentions something (e.g. "Docker") that appears nowhere in the
user's actual input, that's surfaced back to the user as a warning instead
of silently trusted. ATS scores and job-match scores are computed
deterministically from keyword overlap, not asked of the LLM - the AI only
writes the qualitative suggestions afterward, grounded in the
already-computed matched/missing lists, and is explicitly instructed to
recommend adding a skill only "if you actually have it."

## Project layout

```
JobLink-AI/
├── app/
│   ├── main.py                      FastAPI app, CORS, router mount
│   ├── models.py                    Every Pydantic model (resume sections + requests/responses)
│   ├── routes/resume.py             All /api/resume/* endpoints
│   ├── services/
│   │   ├── ai_service.py            Claude calls + the fabrication-check
│   │   ├── resume_service.py        Orchestration (context building, PDF/DOCX dispatch)
│   │   ├── ats_service.py           Deterministic ATS scoring
│   │   ├── matching_service.py      Deterministic job-match scoring
│   │   └── parser_service.py        PDF/DOCX text extraction + analysis
│   ├── templates/
│   │   ├── base.py                  Shared layout structures (EntryBlock/SectionBlock)
│   │   ├── harvard.py
│   │   ├── reverse_chronological.py
│   │   ├── functional.py
│   │   └── __init__.py              Template registry
│   └── utils/
│       ├── pdf_generator.py         ReportLab renderer
│       └── docx_generator.py        python-docx renderer
├── frontend/
│   ├── index.html
│   ├── style.css
│   └── script.js
├── venv/                            (gitignored)
├── requirements.txt
├── .env.example
└── .env                             (gitignored - your real key goes here)
```

## Setup

```powershell
cd JobLink-AI
python -m venv venv
./venv/Scripts/pip install -r requirements.txt
```

Set your Anthropic key - either reuse the one already set for the .NET
backend (if you ran `setx ANTHROPIC_API_KEY "..."` before, a **new**
terminal already has it, no `.env` needed), or copy `.env.example` to
`.env` and fill it in:

```powershell
copy .env.example .env
notepad .env
```

## Run it

```powershell
cd JobLink-AI
./venv/Scripts/uvicorn app.main:app --reload --port 8001
```

- API: `http://127.0.0.1:8001/api/...`
- Interactive docs (Swagger UI): `http://127.0.0.1:8001/docs`
- Health check: `http://127.0.0.1:8001/api/health`

Then serve the frontend as a static site (it's a separate static folder,
no build step) - e.g. from `JobLink-AI/frontend`:

```powershell
npx http-server -p 5507
```

and open `http://127.0.0.1:5507/index.html`. It talks to the API at
`http://127.0.0.1:8001` (hardcoded at the top of `script.js` as
`API_BASE` - change it there if you run the API on a different port).

## API routes

| Method | Route | What it does |
|---|---|---|
| POST | `/api/resume/generate-summary` | Draft a professional summary from the resume data |
| POST | `/api/resume/improve-content` | Improve grammar/clarity/impact of existing text |
| POST | `/api/resume/generate-achievement` | Turn a plain description into a resume statement |
| POST | `/api/resume/analyze` | Analyze raw resume text (no file) |
| POST | `/api/resume/ats-score` | Score a resume against a job description |
| POST | `/api/resume/job-match` | Compare a resume to a specific job posting |
| POST | `/api/resume/generate-pdf` | Render the resume to PDF (binary response) |
| POST | `/api/resume/generate-docx` | Render the resume to DOCX (binary response) |
| POST | `/api/resume/upload` | Upload a PDF/DOCX resume, extract + analyze it |
| GET | `/api/resume/templates` | List the three available templates |

Full request/response schemas are in `app/models.py` and visible live at
`/docs`.

## What's genuinely tested vs. what needs your API key

Tested end-to-end against the running service (not just read through):
PDF generation (including a page-break/orphaned-heading fix caught during
testing), DOCX generation, ATS scoring math, job-match scoring math, PDF
and DOCX upload + text extraction + analysis, the full frontend (every
section, template switching, downloads, localStorage draft persistence).

**Not testable without your `ANTHROPIC_API_KEY`:** the actual quality of
AI-generated text (summary generation, content improvement, achievement
statements, ATS/job-match suggestions). The error path for a missing key
*is* tested - it returns a clean 503 with an actionable message rather
than crashing, and ATS/job-match scores still return successfully with a
plain-text note in place of AI suggestions.

## Known scope boundaries (deliberate, not oversights)

- **No .NET integration yet.** This service runs standalone; it isn't
  wired into the JobLink login flow or the DASHBOARD frontend. That's a
  distinct next step (a thin proxy in the .NET backend, or the DASHBOARD
  frontend calling this service directly for resume features).
- **"Save" is a local browser draft** (`localStorage`), not a JobLink
  account save - there's no authenticated user context here yet.
- **ATS/job-match keyword extraction is a curated dictionary + frequency
  heuristic**, not an ML model - it's explainable and reproducible, but
  won't catch every synonym or domain-specific term.

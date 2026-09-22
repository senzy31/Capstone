# JobLink AI Resume Builder

A standalone Python/FastAPI service that generates, analyzes, and scores
resumes with Claude - a separate microservice alongside the existing .NET
JobLink backend, which is the only thing that ever calls it directly.

```
Browser  ->  .NET JobLink API (auth, the database, plan limits)
                   |
                   | generate-pdf / generate-docx (Joblink/Services/Resume/PythonResumeService.cs)
                   v
             Python FastAPI AI Service  ->  Claude (for the AI text routes only)
```

**What's wired into the real app today:** the four resume templates
(Harvard, Reverse Chronological, Functional, and ATS-Friendly, Premium
only) and PDF/DOCX generation, through `GET /api/Resume/{id}/export` on
the .NET API. The Resume Builder page in `DASHBOARD/` calls that endpoint;
the .NET side reads the caller's own saved resume from the database, maps
it to the shape below, and calls this service - see
`JobLinkv2/Services/Resumes/Export/ResumeExportMapper.cs` for exactly how,
including what has no matching database column yet (certifications,
projects, top-level achievements, a categorized skill list, a dedicated
portfolio link). Nothing else here is wired in yet (see
[Known scope boundaries](#known-scope-boundaries-deliberate-not-oversights)).

The Python service is **stateless**: it never touches SQL Server. It takes
resume data in a request body, generates/analyzes/scores it, and returns
the result (JSON, or a PDF/DOCX file). The .NET backend remains the single
source of truth for anything persisted, and is the only caller - this
service is never reachable from the browser directly (CORS is wide open
here only because it's a local, trusted service on this machine).

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
│   │   ├── ats.py                   Premium only - standard headings/order for parsers, not people
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
| GET | `/api/resume/templates` | List the four available templates |

Full request/response schemas are in `app/models.py` and visible live at
`/docs`.

## What's genuinely tested vs. what needs your API key

Tested end-to-end against the running service (not just read through):
PDF generation (including a page-break/orphaned-heading fix caught during
testing), DOCX generation, ATS scoring math, job-match scoring math, PDF
and DOCX upload + text extraction + analysis, the full frontend (every
section, template switching, downloads, localStorage draft persistence).

The live PDF/DOCX integration is tested a second time from the .NET side,
against this service actually running: `JobLinkv2/Joblink.Tests` (the
mapper, the gateway client, and the export endpoint - the Premium gate and
that a caller's request body can never change whose resume is rendered)
and `tests/e2e/backend.check.js` (real files downloaded through the real
.NET API for every template, skipped with a clear message if this service
isn't running).

**Not testable without your `ANTHROPIC_API_KEY`:** the actual quality of
AI-generated text (summary generation, content improvement, achievement
statements, ATS/job-match suggestions). The error path for a missing key
*is* tested - it returns a clean 503 with an actionable message rather
than crashing, and ATS/job-match scores still return successfully with a
plain-text note in place of AI suggestions.

## Known scope boundaries (deliberate, not oversights)

- **Only PDF/DOCX generation is wired into the real app** (see above); the
  AI text routes (`generate-summary`, `improve-content`,
  `generate-achievement`) and the scoring routes (`ats-score`,
  `job-match`) are not called by JobLink today. The live JobLink app
  generates resume text with Claude directly from the .NET backend
  (`Joblink/Controllers/AiResumeController.cs`, a separate integration
  with its own 20-requests-an-hour limit) and scores job matches with its
  own C# port of the suitability formula (`JobLinkv2/Services/Matching/`,
  see `docs/scoring.md` in the repo root) rather than this service's
  keyword-overlap ATS/job-match scores. Wiring either of those in is a
  distinct, not-yet-decided step - see the capstone repo's roadmap notes.
- **This service is not exposed to the internet or the browser** - the
  frontend in `frontend/` here is a standalone dev harness for testing
  this service in isolation, separate from the real JobLink DASHBOARD;
  the live app only ever reaches this service through the .NET backend.
- **"Save" in `frontend/` is a local browser draft** (`localStorage`), not
  a JobLink account save - that harness has no authenticated user context.
- **ATS/job-match keyword extraction is a curated dictionary + frequency
  heuristic**, not an ML model - it's explainable and reproducible, but
  won't catch every synonym or domain-specific term.
- **Certifications, Projects and top-level Achievements are accepted but
  never populated** by the live integration - JobLink's database has
  nowhere to store them yet (see `ResumeExportMapper.cs`). A resume with
  none of those still renders correctly; the templates already skip a
  section that's empty.

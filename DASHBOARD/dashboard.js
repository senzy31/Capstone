// ======================================================
// JOBLINK DASHBOARD
// Recommended jobs: live JSearch results, scored by the JobLink backend against the jobseeker's
// resume skills and saved preferences (GET /api/Recommendations). The page only shows what the
// server sends: everyone gets each job's overall score and band; a Premium plan also gets how each
// part scored. The rules are in docs/scoring.md. Searching all jobs lives on the Jobs page.
// Shared helpers live in JobsShared.js.
// ======================================================

// "Job Matches" counts recommended jobs scoring at least this much.
const MATCH_THRESHOLD = 50;

const SKILL_CHIPS_SHOWN = 6;


document.addEventListener("DOMContentLoaded", () => {

    const user = requireUser();

    if (!user) {
        return;
    }

    showUserName(user);

    document.getElementById("welcomeText").textContent = `Welcome, ${user.firstName}!`;

    setupSharedListeners();

    ApplyFlow.initReturnPrompt();

    setupDashboardListeners();

    // The Free plan's placeholder ad in the sidebar (Premium never gets it).
    Ads.mount();

    loadRecommendations();

});


function setupDashboardListeners() {

    // Searching happens on the Jobs page - the navbar search hands over to it.
    document.getElementById("searchInput").addEventListener("keydown", (event) => {

        if (event.key !== "Enter") {
            return;
        }

        const keyword = event.target.value.trim();

        window.location.href = keyword
            ? `Jobs.html?q=${encodeURIComponent(keyword)}`
            : "Jobs.html";

    });

    setupJobCardActions(document.getElementById("jobContainer"));

}


// ======================================================
// LOAD THE RECOMMENDED JOBS
// ======================================================

// One request. The server knows who is asking (the login token), reads their resume and preferences
// itself, searches, scores every job and sends each back with a `joblink_match` - the overall score and
// band for everyone, plus how each part scored for a Premium plan. The page only shows what it is given.
async function loadRecommendations() {

    const container = document.getElementById("jobContainer");

    const resultText = document.getElementById("jobResultText");

    try {

        const response = await ApiClient.authFetch(`${API_BASE}/Recommendations?page=1`);

        if (!response.ok) {

            const errorBody = await response.json().catch(() => null);

            throw new Error(errorBody?.message || `API Error: ${response.status}`);

        }

        const data = await response.json();

        // Nothing to match on: the server made no search.
        if (data.skillCount === 0) {

            resultText.textContent = "Add your skills to get recommendations.";

            container.innerHTML = `
                <div class="no-jobs">
                    <i class="fa-solid fa-file-pen"></i>
                    <h3>We need your skills to find matches</h3>
                    <p>
                        Recommendations are based on the skills on your resume.
                        Add them in the Resume Builder and your matches will appear here.
                    </p>
                    <a href="ResumeBuilder.html" class="notice-btn">Open Resume Builder</a>
                </div>
            `;

            return;

        }

        showPreferenceNotice(data.hasPreferences);

        currentJobs = data.data || [];

        renderRecommendations(currentJobs, data);

    } catch (error) {

        console.error("Error loading recommendations:", error);

        resultText.textContent = "Couldn't load recommendations.";

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-circle-exclamation"></i>
                <p>
                    Unable to load recommended jobs.
                    ${escapeHtml(error.message)}
                </p>
            </div>
        `;

    }

}


// Without preferences the score is skills-only, so say how to sharpen it.
function showPreferenceNotice(hasPreferences) {

    document.getElementById("recommendNotice").innerHTML = hasPreferences ? "" : `
        <div class="recommend-notice">
            <i class="fa-solid fa-sliders"></i>
            <div>
                <strong>Get a more accurate score</strong>
                <p>
                    Tell us your preferred location and salary. Until then,
                    your matches are based on your skills only.
                </p>
            </div>
            <a href="Profile.html#preferences" class="notice-btn">Set preferences</a>
        </div>
    `;

}


// ======================================================
// RENDER
// ======================================================

// What a job is shown with if a server ever sent it without a match.
const NO_MATCH = { score: 0, band: { level: "low", label: "Low match" }, detailed: false };

function matchOf(job) {

    return job.joblink_match || NO_MATCH;

}

function renderRecommendations(jobs, info) {

    const container = document.getElementById("jobContainer");

    const matches = jobs.filter(job => matchOf(job).score >= MATCH_THRESHOLD).length;

    document.getElementById("jobCount").textContent = matches;

    document.getElementById("jobResultText").textContent =
        `${jobs.length} jobs scored against your ${info.skillCount} resume ` +
        `${info.skillCount === 1 ? "skill" : "skills"} · searched "${info.query}"`;


    if (jobs.length === 0) {

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-magnifying-glass"></i>
                <h3>No jobs found for "${escapeHtml(info.query)}"</h3>
                <p>Try the Jobs page to search with your own keywords.</p>
                <a href="Jobs.html" class="notice-btn">Browse all jobs</a>
            </div>
        `;

        return;

    }

    container.innerHTML = jobs
        .map(renderRecommendationCard)
        .join("");

    // One placeholder ad between the job cards, for the Free plan only.
    Ads.placeInList(container);

}


function renderRecommendationCard(job) {

    const match = matchOf(job);

    const score = Number(match.score) || 0;

    const level = escapeHtml(match.band?.level || "low");

    const jobId = escapeHtml(job.job_id || "");

    const listedSalary = (job.job_min_salary || job.job_max_salary)
        ? `<span><i class="fa-solid fa-money-bill-wave"></i> ${escapeHtml(getJobSalary(job))}</span>`
        : "";

    // A Premium plan is sent how each part scored and which skills matched; a Free plan is sent neither,
    // so there is nothing here to show it - only an invitation.
    const detailed = match.detailed === true;

    const matched = detailed ? (match.skills?.matched || []) : [];

    const chips = matched
        .slice(0, SKILL_CHIPS_SHOWN)
        .map(skill => `<span class="skill-chip">${escapeHtml(skill)}</span>`)
        .join("");

    const moreChips = matched.length > SKILL_CHIPS_SHOWN
        ? `<span class="skill-chip more">+${matched.length - SKILL_CHIPS_SHOWN} more</span>`
        : "";

    const breakdown = detailed
        ? `
            <div class="match-breakdown level-${level}">
                ${matchRow("Skills", match.skills)}
                ${matchRow("Location", match.location)}
                ${matchRow("Salary", match.salary)}
            </div>
        `
        : `
            <div class="match-locked">
                <i class="fa-solid fa-lock"></i>
                <span>See how skills, location and salary each scored</span>
                <a href="Plans.html">Upgrade to Premium</a>
            </div>
        `;


    return `
        <div class="job-card recommended-card">
            <div class="job-card-content">

                <div class="score-ring level-${level}" style="--pct:${score}"
                     title="Suitability score: ${score}%">
                    <span>${score}%</span>
                </div>

                <div class="job-main-info">

                    <p class="company-name">${escapeHtml(job.employer_name || "Unknown Company")}</p>

                    <h3 class="job-title">
                        ${escapeHtml(job.job_title || "Job Position")}
                        <span class="match-label level-${level}">${escapeHtml(match.band?.label || "")}</span>
                    </h3>

                    <div class="job-meta">
                        <span>
                            <i class="fa-solid fa-location-dot"></i>
                            ${escapeHtml(getJobLocation(job))}
                        </span>

                        <span>
                            <i class="fa-solid fa-briefcase"></i>
                            ${escapeHtml(formatJobType(job.job_employment_type))}
                        </span>

                        ${getWorkBadge(getWorkSetup(job))}

                        ${listedSalary}
                    </div>

                    ${breakdown}

                    ${chips ? `<div class="matched-skills">${chips}${moreChips}</div>` : ""}

                    ${ApplyFlow.externalNoteHtml(job)}

                </div>

                <div class="job-card-actions">
                    <button class="btn btn-secondary view-details-btn" data-job-id="${jobId}">
                        View Details
                    </button>

                    ${ApplyFlow.applyButtonHtml(job)}
                </div>

            </div>
        </div>
    `;

}


// One line of the score breakdown (Premium). A part the server left out of the score has no score and
// says why in its note.
function matchRow(label, part) {

    if (!part || part.score === null || part.score === undefined) {

        return `
            <div class="match-row muted">
                <span class="match-name">${label}</span>
                <span class="match-note">${escapeHtml(part?.note || "")}</span>
            </div>
        `;

    }

    const score = Number(part.score) || 0;

    return `
        <div class="match-row">
            <span class="match-name">${label}</span>
            <div class="match-bar"><div class="match-fill" style="width:${Math.max(0, Math.min(100, score))}%"></div></div>
            <span class="match-pct">${score}%</span>
            <span class="match-note">${escapeHtml(part.note || "")}</span>
        </div>
    `;

}

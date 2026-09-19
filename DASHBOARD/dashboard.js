// ======================================================
// JOBLINK DASHBOARD
// Recommended jobs: live JSearch results (through the JobLink backend),
// scored against the jobseeker's resume skills and saved preferences
// (see Suitability.js). Searching all jobs lives on the Jobs page.
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

    setupDashboardListeners();

    loadRecommendations(user.userId);

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
// LOAD THE JOBSEEKER'S RESUME + PREFERENCES
// ======================================================

// -> { skills: string[], role: string, preferences: object | null }
// Reads the same resume the Resume Builder edits (the user's first one).
async function loadResumeProfile(userId) {

    const [resumesResponse, skillsResponse, preferenceResponse] = await Promise.all([
        fetch(`${API_BASE}/Resume/by-user/${userId}`),
        fetch(`${API_BASE}/Skills`),
        fetch(`${API_BASE}/JobPreference/by-user/${userId}`)
    ]);

    if (!resumesResponse.ok) {
        throw new Error(`Couldn't load your resume (${resumesResponse.status})`);
    }

    // 404 just means they haven't saved any preferences yet.
    const preferences = preferenceResponse.ok ? await preferenceResponse.json() : null;

    const resumes = await resumesResponse.json();

    if (resumes.length === 0) {
        return { skills: [], role: "", preferences };
    }

    const resumeId = resumes[0].resumeId;

    const [linksResponse, experienceResponse] = await Promise.all([
        fetch(`${API_BASE}/ResumeSkills/by-resume/${resumeId}`),
        fetch(`${API_BASE}/Experience/by-resume/${resumeId}`)
    ]);

    const links = linksResponse.ok ? await linksResponse.json() : [];

    const catalog = skillsResponse.ok ? await skillsResponse.json() : [];

    const experience = experienceResponse.ok ? await experienceResponse.json() : [];

    const skillNames = new Map(catalog.map(skill => [skill.skillId, skill.skillName]));

    const skills = [...new Set(
        links
            .map(link => (skillNames.get(link.skillId) || "").trim())
            .filter(Boolean)
    )];

    return { skills, role: getLatestRole(experience), preferences };

}


// Their current job (no end date), otherwise the most recently ended one.
function getLatestRole(experience) {

    const end = item => item.endDate ? new Date(item.endDate).getTime() : Number.MAX_SAFE_INTEGER;

    const start = item => item.startDate ? new Date(item.startDate).getTime() : 0;

    const withTitle = experience.filter(item => (item.position || "").trim());

    withTitle.sort((a, b) => (end(b) - end(a)) || (start(b) - start(a)));

    return withTitle.length > 0 ? withTitle[0].position.trim() : "";

}


// The search sent to JSearch: their latest role (or top skills) near where
// they want to work.
function buildRecommendationQuery(profile) {

    const preferences = profile.preferences || {};

    const place = (preferences.preferredLocation || "").split(",")[0].trim() || "Philippines";

    const focus = profile.role || profile.skills.slice(0, 3).join(" ");

    const remote = preferences.workArrangement === "remote" ? " remote" : "";

    return `${focus}${remote} jobs in ${place}`;

}


// ======================================================
// LOAD + SCORE RECOMMENDED JOBS
// ======================================================

async function loadRecommendations(userId) {

    const container = document.getElementById("jobContainer");

    const resultText = document.getElementById("jobResultText");

    try {

        const profile = await loadResumeProfile(userId);

        if (profile.skills.length === 0) {

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

        showPreferenceNotice(profile.preferences);

        const query = buildRecommendationQuery(profile);

        const response = await fetch(
            `${JOB_API}/search?query=${encodeURIComponent(query)}&page=1`
        );

        if (!response.ok) {

            const errorBody = await response.json().catch(() => null);

            throw new Error(errorBody?.message || `API Error: ${response.status}`);

        }

        const data = await response.json();

        currentJobs = data.data || [];

        // Best match first; more matched skills breaks a tie.
        const recommendations = currentJobs
            .map(job => ({ job, match: scoreJob(job, profile) }))
            .sort((a, b) =>
                (b.match.score - a.match.score) ||
                (b.match.skills.matched.length - a.match.skills.matched.length)
            );

        renderRecommendations(recommendations, profile, query);

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
function showPreferenceNotice(preferences) {

    const hasPreferences = preferences && (
        preferences.preferredLocation ||
        preferences.workArrangement ||
        preferences.minSalary ||
        preferences.maxSalary
    );

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

function renderRecommendations(recommendations, profile, query) {

    const container = document.getElementById("jobContainer");

    const matches = recommendations.filter(item => item.match.score >= MATCH_THRESHOLD).length;

    document.getElementById("jobCount").textContent = matches;

    document.getElementById("jobResultText").textContent =
        `${recommendations.length} jobs scored against your ${profile.skills.length} resume ` +
        `${profile.skills.length === 1 ? "skill" : "skills"} · searched "${query}"`;


    if (recommendations.length === 0) {

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-magnifying-glass"></i>
                <h3>No jobs found for "${escapeHtml(query)}"</h3>
                <p>Try the Jobs page to search with your own keywords.</p>
                <a href="Jobs.html" class="notice-btn">Browse all jobs</a>
            </div>
        `;

        return;

    }

    container.innerHTML = recommendations
        .map(item => renderRecommendationCard(item, profile))
        .join("");

}


function renderRecommendationCard({ job, match }, profile) {

    const preferences = profile.preferences || {};

    const jobId = escapeHtml(job.job_id || "");

    const listedSalary = (job.job_min_salary || job.job_max_salary)
        ? `<span><i class="fa-solid fa-money-bill-wave"></i> ${escapeHtml(getJobSalary(job))}</span>`
        : "";

    const chips = match.skills.matched
        .slice(0, SKILL_CHIPS_SHOWN)
        .map(skill => `<span class="skill-chip">${escapeHtml(skill)}</span>`)
        .join("");

    const moreChips = match.skills.matched.length > SKILL_CHIPS_SHOWN
        ? `<span class="skill-chip more">+${match.skills.matched.length - SKILL_CHIPS_SHOWN} more</span>`
        : "";


    return `
        <div class="job-card recommended-card">
            <div class="job-card-content">

                <div class="score-ring level-${match.band.level}" style="--pct:${match.score}"
                     title="Suitability score: ${match.score}%">
                    <span>${match.score}%</span>
                </div>

                <div class="job-main-info">

                    <p class="company-name">${escapeHtml(job.employer_name || "Unknown Company")}</p>

                    <h3 class="job-title">
                        ${escapeHtml(job.job_title || "Job Position")}
                        <span class="match-label level-${match.band.level}">${match.band.label}</span>
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

                    <div class="match-breakdown level-${match.band.level}">
                        ${matchRow("Skills", match.skills, "", describeSkills(match.skills))}
                        ${matchRow("Location", match.location, "No location preference set")}
                        ${matchRow("Salary", match.salary, describeMissingSalary(job, preferences))}
                    </div>

                    ${chips ? `<div class="matched-skills">${chips}${moreChips}</div>` : ""}

                </div>

                <div class="job-card-actions">
                    <button class="btn btn-secondary view-details-btn" data-job-id="${jobId}">
                        View Details
                    </button>

                    <button class="btn btn-primary apply-job-btn" data-job-id="${jobId}">
                        Apply
                    </button>
                </div>

            </div>
        </div>
    `;

}


// One line of the score breakdown. A part with nothing to compare shows why
// instead of a bar (see Suitability.js: such parts don't affect the score).
function matchRow(label, part, missingNote, noteOverride) {

    if (!part) {

        return `
            <div class="match-row muted">
                <span class="match-name">${label}</span>
                <span class="match-note">${escapeHtml(missingNote)}</span>
            </div>
        `;

    }

    return `
        <div class="match-row">
            <span class="match-name">${label}</span>
            <div class="match-bar"><div class="match-fill" style="width:${part.score}%"></div></div>
            <span class="match-pct">${part.score}%</span>
            <span class="match-note">${escapeHtml(noteOverride || part.note)}</span>
        </div>
    `;

}


function describeSkills(skills) {

    return skills.matched.length > 0
        ? `Mentions ${skills.matched.length} of your ${skills.total} ${skills.total === 1 ? "skill" : "skills"}`
        : `Mentions none of your ${skills.total} ${skills.total === 1 ? "skill" : "skills"}`;

}


function describeMissingSalary(job, preferences) {

    if (!preferences.minSalary && !preferences.maxSalary) {
        return "No salary preference set";
    }

    if (!job.job_min_salary && !job.job_max_salary) {
        return "Salary not listed";
    }

    return "Listed in a currency we can't compare";

}

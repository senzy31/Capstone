// ======================================================
// SHARED JOB HELPERS
// Used by dashboard.html (recommended jobs) and Jobs.html (job search).
//
// Job data comes from the JobLink backend, which calls JSearch (RapidAPI)
// server-side. The RapidAPI key lives on the server, never in these files.
// ======================================================

const API_BASE = "https://localhost:7142/api";

const JOB_API = `${API_BASE}/JobSearch`;

// The jobs currently on screen - the details popup looks jobs up here by id.
let currentJobs = [];


// ======================================================
// SESSION
// ======================================================

// Returns the logged-in user, or redirects to the login page and returns null.
function requireUser() {

    const loginPage = "../LOGIN/login.html";

    try {

        const user = JSON.parse(localStorage.getItem("user"));

        const userId = user?.userId || user?.user_id;

        if (!userId) {
            throw new Error("No saved session");
        }

        if (!localStorage.getItem("token")) {

            // Signed in before login tokens existed - log in once more to get one.
            localStorage.removeItem("user");

            sessionStorage.setItem("joblink.sessionExpired", "1");

            throw new Error("No login token");

        }

        const fullName = user.fullName || user.full_name || user.username || "User";

        return {
            ...user,
            userId,
            fullName,
            firstName: fullName.split(" ")[0]
        };

    } catch {

        window.location.href = loginPage;

        return null;

    }

}


function showUserName(user) {

    const userName = document.getElementById("userName");

    if (userName) {
        userName.textContent = user.firstName;
    }

}


// Logout confirmation + job popup close handlers (same on every jobs page).
function setupSharedListeners() {

    const logoutModal = document.getElementById("logoutModal");

    document.getElementById("logoutBtn")?.addEventListener("click", (event) => {
        event.preventDefault();
        logoutModal.classList.add("show");
    });

    document.getElementById("cancelLogout")?.addEventListener("click", () => {
        logoutModal.classList.remove("show");
    });

    document.getElementById("confirmLogout")?.addEventListener("click", () => {
        localStorage.clear();
        window.location.href = "../LOGIN/login.html";
    });

    document.getElementById("closeJobPopup")?.addEventListener("click", closePopup);
    document.getElementById("closePopupBtn")?.addEventListener("click", closePopup);

    window.addEventListener("click", (event) => {

        if (event.target === document.getElementById("jobPopup")) {
            closePopup();
        }

        if (event.target === logoutModal) {
            logoutModal.classList.remove("show");
        }

    });

}


// One listener handles every card's View Details / Apply buttons, however
// often the list re-renders. Buttons carry data-job-id.
function setupJobCardActions(container) {

    container.addEventListener("click", (event) => {

        const button = event.target.closest("[data-job-id]");

        if (!button) {
            return;
        }

        const job = currentJobs.find(item => item.job_id === button.dataset.jobId);

        if (!job) {
            return;
        }

        if (button.classList.contains("view-details-btn")) {
            openPopup(job);
        } else if (button.classList.contains("apply-job-btn")) {
            ApplyFlow.apply(job);
        }

    });

}


// ======================================================
// JOB FIELD HELPERS
// ======================================================

function escapeHtml(value) {

    if (value === null || value === undefined) {
        return "";
    }

    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");

}


function getJobLocation(job) {

    const parts = [job.job_city, job.job_state, job.job_country].filter(Boolean);

    return parts.length > 0 ? parts.join(", ") : "Location not specified";

}


function getWorkSetup(job) {

    // An internal (employer-posted) job carries its own authoritative setup - no need to
    // guess it from description text the way an external JSearch listing has to.
    if (job.joblink_work_setup) {

        const setup = String(job.joblink_work_setup).toLowerCase();

        if (setup === "remote") return "Remote / WFH";
        if (setup === "hybrid") return "Hybrid";
        if (setup === "onsite") return "On-site";

    }

    const description = (job.job_description || "").toLowerCase();

    if (
        job.job_is_remote === true ||
        description.includes("remote") ||
        description.includes("work from home") ||
        description.includes("wfh")
    ) {
        return "Remote / WFH";
    }

    if (description.includes("hybrid")) {
        return "Hybrid";
    }

    return "On-site";

}


// A job an employer posted directly on JobLink, not imported from JSearch.
function isInternalJob(job) {
    return job.joblink_source === "Internal";
}


function joblinkBadgeHtml(job) {

    return isInternalJob(job)
        ? `<span class="badge joblink-badge"><i class="fa-solid fa-star"></i> Posted on JobLink</span>`
        : "";

}


function getWorkBadge(workSetup) {

    if (workSetup === "Remote / WFH") {
        return `<span class="badge remote-badge"><i class="fa-solid fa-house"></i> Remote / WFH</span>`;
    }

    if (workSetup === "Hybrid") {
        return `<span class="badge hybrid-badge"><i class="fa-solid fa-arrows-rotate"></i> Hybrid</span>`;
    }

    return `<span class="badge onsite-badge"><i class="fa-solid fa-building"></i> On-site</span>`;

}


function formatJobType(type) {

    if (!type) {
        return "Not specified";
    }

    const labels = {
        FULLTIME: "Full-time",
        PARTTIME: "Part-time",
        CONTRACTOR: "Contract",
        INTERN: "Internship"
    };

    return labels[type.toUpperCase()] || type;

}


// JSearch search results carry no currency field, so fall back to the job's
// country instead of labelling every salary as PHP.
function getJobCurrency(job) {

    return job.job_salary_currency ||
        { PH: "PHP", US: "USD" }[job.job_country] ||
        "";

}


function getJobSalary(job) {

    const min = job.job_min_salary;
    const max = job.job_max_salary;

    const currency = getJobCurrency(job);
    const prefix = currency ? `${currency} ` : "";

    const period = job.job_salary_period
        ? ` per ${String(job.job_salary_period).toLowerCase()}`
        : "";

    // Keep each returned expression on the same line as `return` -
    // a line break right after `return` makes JS return undefined.

    if (min && max) {
        return `${prefix}${Number(min).toLocaleString()} - ${Number(max).toLocaleString()}${period}`;
    }

    if (min) {
        return `Starting at ${prefix}${Number(min).toLocaleString()}${period}`;
    }

    if (max) {
        return `Up to ${prefix}${Number(max).toLocaleString()}${period}`;
    }

    return "Salary not specified";

}


// ======================================================
// JOB DETAILS POPUP
// ======================================================

async function openPopup(job) {

    const setText = (id, text) => {
        document.getElementById(id).textContent = text;
    };

    document.getElementById("jobPopup").classList.add("show");

    setText("popupTitle", job.job_title || "Job Title");
    setText("popupCompany", job.employer_name || "Unknown Company");
    setText("popupLocation", getJobLocation(job));
    setText("popupWorkSetup", getWorkSetup(job));
    setText("popupJobType", formatJobType(job.job_employment_type));
    setText("popupDescription", "Loading job details...");
    setText("popupSalary", "Loading salary information...");

    // "Apply" for employer-posted jobs, "Apply on LinkedIn [icon]" for external ones.
    const applyButton = document.getElementById("applyBtn");

    applyButton.innerHTML = ApplyFlow.isExternal(job)
        ? `Apply on ${escapeHtml(ApplyFlow.publisherLabel(job))} <i class="fa-solid fa-arrow-up-right-from-square"></i>`
        : "Apply";

    applyButton.onclick = () => ApplyFlow.apply(job);

    let applyNote = document.getElementById("popupApplyNote");

    if (!applyNote) {

        applyNote = document.createElement("div");

        applyNote.id = "popupApplyNote";
        applyNote.className = "popup-apply-note";

        const footer = document.querySelector("#jobPopup .popup-footer");

        footer.parentNode.insertBefore(applyNote, footer);

    }

    applyNote.innerHTML = ApplyFlow.externalNoteHtml(job);

    try {

        let description = job.job_description || "";

        if (job.job_id) {

            const response = await ApiClient.authFetch(
                `${JOB_API}/details?jobId=${encodeURIComponent(job.job_id)}`
            );

            const details = response.ok ? await response.json() : null;

            description = details?.data?.[0]?.job_description || description;

        }

        setText("popupDescription", description || "No job description available.");

        loadSalary(job);

    } catch (error) {

        console.error("Error loading job details:", error);

        setText("popupDescription", job.job_description || "Unable to load job description.");
        setText("popupSalary", getJobSalary(job));

    }

}


async function loadSalary(job) {

    const salaryBox = document.getElementById("popupSalary");

    try {

        // Use the salary already on the listing when there is one.
        if (job.job_min_salary || job.job_max_salary) {
            salaryBox.textContent = getJobSalary(job);
            return;
        }

        const response = await ApiClient.authFetch(
            `${JOB_API}/salary` +
            `?jobTitle=${encodeURIComponent(job.job_title)}` +
            `&location=${encodeURIComponent(getJobLocation(job))}`
        );

        const data = response.ok ? await response.json() : null;

        const salary = data?.data?.[0];

        if (!salary) {
            salaryBox.textContent = "Salary information not available.";
            return;
        }

        const currency = salary.salary_currency || "PHP";
        const period = String(salary.salary_period || "year").toLowerCase();

        let text = "Not available";

        if (salary.min_salary && salary.max_salary) {
            text = `${currency} ${Number(salary.min_salary).toLocaleString()} - ${Number(salary.max_salary).toLocaleString()} per ${period}`;
        } else if (salary.median_salary) {
            text = `${currency} ${Number(salary.median_salary).toLocaleString()} per ${period}`;
        }

        salaryBox.textContent = text;

    } catch (error) {

        console.error("Salary error:", error);

        salaryBox.textContent = "Salary information unavailable.";

    }

}


function closePopup() {

    document.getElementById("jobPopup").classList.remove("show");

}


// ======================================================
// SCORED JOB CARD
// Shared by dashboard.html (recommendations) and Jobs.html (search) - both show a job's
// joblink_match the same way: everyone gets the overall score and band, a Premium plan also
// gets how each part scored. The rules are in docs/scoring.md.
// ======================================================

const SKILL_CHIPS_SHOWN = 6;

// What a job is shown with if a server ever sent it without a match.
const NO_MATCH = { score: 0, band: { level: "low", label: "Low match" }, detailed: false };

function matchOf(job) {

    return job.joblink_match || NO_MATCH;

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


function renderScoredJobCard(job) {

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

                        ${joblinkBadgeHtml(job)}

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

// ======================================================
// JOBLINK JOBS
// Live job search (JSearch, through the JobLink backend) with filters.
// Shared helpers live in JobsShared.js.
// ======================================================

const DEFAULT_SEARCH = "jobs philippines";

let debounceTimer;

// Guards against a slow, older search overwriting a newer one.
let latestSearchId = 0;


document.addEventListener("DOMContentLoaded", () => {

    const user = requireUser();

    if (!user) {
        return;
    }

    showUserName(user);

    setupSharedListeners();

    setupJobListeners();


    // The dashboard's search box sends people here as Jobs.html?q=...
    const keyword = new URLSearchParams(window.location.search).get("q")?.trim();

    if (keyword) {

        document.getElementById("searchInput").value = keyword;

        applyFilters();

    } else {

        loadJobs(DEFAULT_SEARCH);

    }

});


// ======================================================
// EVENT LISTENERS
// ======================================================

function setupJobListeners() {

    const searchInput = document.getElementById("searchInput");

    searchInput.addEventListener("input", () => {

        clearTimeout(debounceTimer);

        debounceTimer = setTimeout(applyFilters, 600);

    });

    searchInput.addEventListener("keydown", (event) => {

        if (event.key === "Enter") {

            clearTimeout(debounceTimer);

            applyFilters();

        }

    });

    document.getElementById("applyFilters").addEventListener("click", applyFilters);

    document.getElementById("resetFilters").addEventListener("click", resetFilters);


    setupJobCardActions(document.getElementById("jobContainer"));

}


// ======================================================
// APPLY / RESET FILTERS
// ======================================================

function applyFilters() {

    const keyword = document.getElementById("searchInput").value.trim();
    const location = document.getElementById("locationInput").value.trim();
    const workSetup = document.getElementById("workSetup").value;
    const jobType = document.getElementById("jobType").value;


    // Build the search text sent to JSearch

    let searchQuery = keyword || "jobs";

    if (workSetup === "remote") {
        searchQuery += " remote work from home";
    } else if (workSetup === "onsite") {
        searchQuery += " onsite";
    } else if (workSetup === "hybrid") {
        searchQuery += " hybrid";
    }

    if (jobType) {
        searchQuery += " " + jobType.toLowerCase();
    }

    searchQuery += location ? " in " + location : " philippines";


    loadJobs(searchQuery);

}


function resetFilters() {

    for (const id of ["searchInput", "locationInput", "workSetup", "jobType", "minSalary", "maxSalary"]) {
        document.getElementById(id).value = "";
    }

    loadJobs(DEFAULT_SEARCH);

}


// ======================================================
// LOAD JOBS FROM THE BACKEND
// ======================================================

async function loadJobs(search = DEFAULT_SEARCH) {

    const container = document.getElementById("jobContainer");

    const searchId = ++latestSearchId;

    container.innerHTML = `
        <div class="loading-jobs">
            <i class="fa-solid fa-spinner fa-spin"></i>
            <p>Searching for available jobs...</p>
        </div>
    `;

    try {

        const response = await fetch(
            `${JOB_API}/search?query=${encodeURIComponent(search)}&page=1`
        );

        if (!response.ok) {

            const errorBody = await response.json().catch(() => null);

            throw new Error(errorBody?.message || `API Error: ${response.status}`);

        }

        const data = await response.json();

        if (searchId !== latestSearchId) {
            return;
        }

        currentJobs = data.data || [];

        renderJobs(filterJobs(currentJobs));

    } catch (error) {

        if (searchId !== latestSearchId) {
            return;
        }

        console.error("Error fetching jobs:", error);

        document.getElementById("jobResultText").textContent = "Couldn't load jobs.";

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-circle-exclamation"></i>
                <p>
                    Unable to load jobs.
                    ${escapeHtml(error.message)}
                </p>
            </div>
        `;

    }

}


// ======================================================
// LOCAL FILTERING (on top of the search results)
// ======================================================

function filterJobs(jobs) {

    const workSetup = document.getElementById("workSetup").value.toLowerCase();
    const location = document.getElementById("locationInput").value.trim().toLowerCase();
    const minSalary = parseFloat(document.getElementById("minSalary").value);
    const maxSalary = parseFloat(document.getElementById("maxSalary").value);
    const jobType = document.getElementById("jobType").value;


    return jobs.filter(job => {

        // WORK SETUP

        if (workSetup) {

            const isRemote = job.job_is_remote;

            const description = (job.job_description || "").toLowerCase();

            if (workSetup === "remote") {

                if (
                    !isRemote &&
                    !description.includes("remote") &&
                    !description.includes("work from home") &&
                    !description.includes("wfh")
                ) {
                    return false;
                }

            }

            if (workSetup === "onsite" && isRemote) {
                return false;
            }

            if (workSetup === "hybrid" && !description.includes("hybrid")) {
                return false;
            }

        }


        // LOCATION

        if (location) {

            const jobLocation =
                `${job.job_city || ""} ${job.job_state || ""} ${job.job_country || ""}`
                    .toLowerCase();

            if (!jobLocation.includes(location)) {
                return false;
            }

        }


        // JOB TYPE
        // JSearch's job_employment_type is a display label ("Full-time");
        // the machine-readable codes (FULLTIME, PARTTIME, ...) live in
        // job_employment_types.

        if (jobType) {

            const types =
                Array.isArray(job.job_employment_types) && job.job_employment_types.length > 0
                    ? job.job_employment_types
                    : [job.job_employment_type || ""];

            if (
                !types
                    .map(type => String(type).toUpperCase().replace(/[^A-Z]/g, ""))
                    .includes(jobType)
            ) {
                return false;
            }

        }


        // SALARY - jobs that list no (comparable) salary always pass

        const pay = getMonthlySalaryRange(job);

        if (pay) {

            if (!isNaN(minSalary) && pay.max < minSalary) {
                return false;
            }

            if (!isNaN(maxSalary) && pay.min > maxSalary) {
                return false;
            }

        }

        return true;

    });

}


// ======================================================
// RENDER JOBS
// ======================================================

function renderJobs(jobs) {

    const container = document.getElementById("jobContainer");

    document.getElementById("jobResultText").textContent =
        `${jobs.length} ${jobs.length === 1 ? "job" : "jobs"} found`;


    if (jobs.length === 0) {

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-magnifying-glass"></i>
                <h3>No matching jobs found</h3>
                <p>Try adjusting your filters or search preferences.</p>
            </div>
        `;

        return;

    }


    container.innerHTML = jobs.map(job => {

        const jobId = escapeHtml(job.job_id || "");

        return `
            <div class="job-card">
                <div class="job-card-content">

                    <div class="job-main-info">

                        <p class="company-name">${escapeHtml(job.employer_name || "Unknown Company")}</p>

                        <h3 class="job-title">${escapeHtml(job.job_title || "Job Position")}</h3>

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
                        </div>

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

    }).join("");

}

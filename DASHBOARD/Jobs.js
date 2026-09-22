// ======================================================
// JOBLINK JOBS
// Live job search, scored against the caller's own resume (GET /api/Recommendations/search) -
// the same scoring GET /api/Recommendations uses, just driven by this page's own search text
// and filters instead of the resume alone. Jobs an employer posted directly on JobLink are
// scored the same way and always listed first, with a "Posted on JobLink" badge; filtering and
// card rendering both happen on the server now - this page only shows what it is given.
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

    Navbar.mount(user.userId, user.fullName);

    setupSharedListeners();

    ApplyFlow.initReturnPrompt();

    setupJobListeners();


    // The dashboard's search box sends people here as Jobs.html?q=...
    const keyword = new URLSearchParams(window.location.search).get("q")?.trim();

    if (keyword) {
        document.getElementById("searchInput").value = keyword;
    }

    loadJobs();

});


// ======================================================
// EVENT LISTENERS
// ======================================================

function setupJobListeners() {

    const searchInput = document.getElementById("searchInput");

    searchInput.addEventListener("input", () => {

        clearTimeout(debounceTimer);

        debounceTimer = setTimeout(loadJobs, 600);

    });

    searchInput.addEventListener("keydown", (event) => {

        if (event.key === "Enter") {

            clearTimeout(debounceTimer);

            loadJobs();

        }

    });

    document.getElementById("applyFilters").addEventListener("click", loadJobs);

    document.getElementById("resetFilters").addEventListener("click", resetFilters);


    setupJobCardActions(document.getElementById("jobContainer"));

}


function resetFilters() {

    for (const id of ["searchInput", "locationInput", "workSetup", "jobType", "minSalary", "maxSalary", "minScore"]) {
        document.getElementById(id).value = "";
    }

    loadJobs();

}


// ======================================================
// LOAD JOBS FROM THE BACKEND
// ======================================================

async function loadJobs() {

    const container = document.getElementById("jobContainer");

    const searchId = ++latestSearchId;

    container.innerHTML = `
        <div class="loading-jobs">
            <i class="fa-solid fa-spinner fa-spin"></i>
            <p>Searching for available jobs...</p>
        </div>
    `;

    const keyword = document.getElementById("searchInput").value.trim();

    const params = new URLSearchParams();

    params.set("q", keyword || DEFAULT_SEARCH);
    params.set("page", "1");

    const workSetup = document.getElementById("workSetup").value;
    const location = document.getElementById("locationInput").value.trim();
    const minSalary = document.getElementById("minSalary").value;
    const maxSalary = document.getElementById("maxSalary").value;
    const jobType = document.getElementById("jobType").value;
    const minScore = document.getElementById("minScore").value;

    if (workSetup) params.set("workSetup", workSetup);
    if (location) params.set("location", location);
    if (minSalary) params.set("minSalary", minSalary);
    if (maxSalary) params.set("maxSalary", maxSalary);
    if (jobType) params.set("jobType", jobType);
    if (minScore) params.set("minScore", minScore);

    try {

        const response = await ApiClient.authFetch(`${API_BASE}/Recommendations/search?${params.toString()}`);

        if (!response.ok) {

            const errorBody = await response.json().catch(() => null);

            throw new Error(errorBody?.message || `API Error: ${response.status}`);

        }

        const data = await response.json();

        if (searchId !== latestSearchId) {
            return;
        }

        currentJobs = data.data || [];

        renderJobs(currentJobs);

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

    container.innerHTML = jobs.map(renderScoredJobCard).join("");

}

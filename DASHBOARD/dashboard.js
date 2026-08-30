// ======================================================
// JOBLINK DASHBOARD
// ======================================================

// IMPORTANT:
// Replace this with your NEW RapidAPI key.
// Do not expose production API keys in frontend applications.

const RAPIDAPI_KEY = "YOUR_RAPIDAPI_KEY";

const RAPIDAPI_HOST = "jsearch.p.rapidapi.com";

let currentJobs = [];
let debounceTimer;


// ======================================================
// DOM READY
// ======================================================

document.addEventListener("DOMContentLoaded", () => {

    loadUser();

    loadJobs();

    setupEventListeners();

});


// ======================================================
// LOAD USER
// ======================================================

function loadUser() {

    const storedUser = localStorage.getItem("user");

    if (!storedUser) {

        window.location.href = "../LOGIN/login.html";

        return;
    }

    try {

        const user = JSON.parse(storedUser);

        const fullName =
            user.fullName ||
            user.full_name ||
            user.username ||
            "User";

        const firstName = fullName.split(" ")[0];

        const welcomeText =
            document.getElementById("welcomeText");

        const userName =
            document.getElementById("userName");


        if (welcomeText) {

            welcomeText.textContent =
                `Welcome, ${firstName}!`;

        }


        if (userName) {

            userName.textContent =
                firstName;

        }

    } catch (error) {

        console.error("Error loading user:", error);

    }

}


// ======================================================
// EVENT LISTENERS
// ======================================================

function setupEventListeners() {

    // SEARCH

    const searchInput =
        document.getElementById("searchInput");

    searchInput.addEventListener("input", function () {

        clearTimeout(debounceTimer);

        debounceTimer = setTimeout(() => {

            applyFilters();

        }, 600);

    });


    // APPLY FILTERS

    document
        .getElementById("applyFilters")
        .addEventListener("click", () => {

            applyFilters();

        });


    // RESET FILTERS

    document
        .getElementById("resetFilters")
        .addEventListener("click", resetFilters);


    // LOGOUT

    document
        .getElementById("logoutBtn")
        ?.addEventListener("click", (event) => {

            event.preventDefault();

            document
                .getElementById("logoutModal")
                .classList.add("show");

        });


    document
        .getElementById("cancelLogout")
        ?.addEventListener("click", () => {

            document
                .getElementById("logoutModal")
                .classList.remove("show");

        });


    document
        .getElementById("confirmLogout")
        ?.addEventListener("click", () => {

            localStorage.clear();

            window.location.href =
                "../LOGIN/login.html";

        });


    // CLOSE JOB POPUP

    document
        .getElementById("closeJobPopup")
        ?.addEventListener("click", closePopup);


    document
        .getElementById("closePopupBtn")
        ?.addEventListener("click", closePopup);


    // CLOSE POPUP WHEN CLICKING OUTSIDE

    window.addEventListener("click", (event) => {

        const jobPopup =
            document.getElementById("jobPopup");

        const logoutModal =
            document.getElementById("logoutModal");


        if (event.target === jobPopup) {

            closePopup();

        }


        if (event.target === logoutModal) {

            logoutModal.classList.remove("show");

        }

    });

}


// ======================================================
// APPLY FILTERS
// ======================================================

function applyFilters() {

    const keyword =
        document
            .getElementById("searchInput")
            .value
            .trim();

    const location =
        document
            .getElementById("locationInput")
            .value
            .trim();

    const workSetup =
        document
            .getElementById("workSetup")
            .value;

    const jobType =
        document
            .getElementById("jobType")
            .value;


    // Build search query

    let searchQuery =
        keyword || "jobs";


    // Add work arrangement

    if (workSetup === "remote") {

        searchQuery += " remote work from home";

    }

    else if (workSetup === "onsite") {

        searchQuery += " onsite";

    }

    else if (workSetup === "hybrid") {

        searchQuery += " hybrid";

    }


    // Add job type

    if (jobType) {

        searchQuery += " " +
            jobType.toLowerCase();

    }


    // Add location

    if (location) {

        searchQuery +=
            " in " + location;

    }

    else {

        searchQuery +=
            " philippines";

    }


    loadJobs(searchQuery);

}


// ======================================================
// RESET FILTERS
// ======================================================

function resetFilters() {

    document
        .getElementById("searchInput")
        .value = "";

    document
        .getElementById("locationInput")
        .value = "";

    document
        .getElementById("workSetup")
        .value = "";

    document
        .getElementById("jobType")
        .value = "";

    document
        .getElementById("minSalary")
        .value = "";

    document
        .getElementById("maxSalary")
        .value = "";


    loadJobs("jobs philippines");

}


// ======================================================
// LOAD JOBS FROM API
// ======================================================

async function loadJobs(search = "jobs philippines") {

    const container =
        document.getElementById("jobContainer");


    container.innerHTML = `

        <div class="loading-jobs">

            <i class="fa-solid fa-spinner fa-spin"></i>

            <p>
                Searching for available jobs...
            </p>

        </div>

    `;


    try {

        const url =
            `https://jsearch.p.rapidapi.com/search` +
            `?query=${encodeURIComponent(search)}` +
            `&page=1`;


        const response =
            await fetch(url, {

                method: "GET",

                headers: {

                    "X-RapidAPI-Key":
                        RAPIDAPI_KEY,

                    "X-RapidAPI-Host":
                        RAPIDAPI_HOST

                }

            });


        if (!response.ok) {

            throw new Error(
                `API Error: ${response.status}`
            );

        }


        const data =
            await response.json();


        currentJobs =
            data.data || [];


        // APPLY LOCAL FILTERS

        const filteredJobs =
            filterJobs(currentJobs);


        renderJobs(filteredJobs);


    } catch (error) {

        console.error(
            "Error fetching jobs:",
            error
        );


        container.innerHTML = `

            <div class="no-jobs">

                <i class="fa-solid fa-circle-exclamation"></i>

                <p>
                    Unable to load jobs.
                    Please check your API key or internet connection.
                </p>

            </div>

        `;

    }

}


// ======================================================
// LOCAL FILTERING
// ======================================================

function filterJobs(jobs) {

    const workSetup =
        document
            .getElementById("workSetup")
            .value
            .toLowerCase();

    const location =
        document
            .getElementById("locationInput")
            .value
            .trim()
            .toLowerCase();

    const minSalary =
        parseFloat(
            document
                .getElementById("minSalary")
                .value
        );

    const maxSalary =
        parseFloat(
            document
                .getElementById("maxSalary")
                .value
        );

    const jobType =
        document
            .getElementById("jobType")
            .value;


    return jobs.filter(job => {


        // ==========================================
        // WORK SETUP FILTER
        // ==========================================

        if (workSetup) {

            const jobIsRemote =
                job.job_is_remote;

            const jobDescription =
                (
                    job.job_description ||
                    ""
                ).toLowerCase();


            if (workSetup === "remote") {

                if (!jobIsRemote &&
                    !jobDescription.includes("remote") &&
                    !jobDescription.includes("work from home") &&
                    !jobDescription.includes("wfh")) {

                    return false;

                }

            }


            if (workSetup === "onsite") {

                if (jobIsRemote) {

                    return false;

                }

            }


            if (workSetup === "hybrid") {

                if (
                    !jobDescription.includes("hybrid")
                ) {

                    return false;

                }

            }

        }


        // ==========================================
        // LOCATION FILTER
        // ==========================================

        if (location) {

            const jobLocation =
                `${job.job_city || ""}
                 ${job.job_state || ""}
                 ${job.job_country || ""}`
                    .toLowerCase();


            if (
                !jobLocation.includes(location)
            ) {

                return false;

            }

        }


        // ==========================================
        // JOB TYPE FILTER
        // ==========================================

        if (jobType) {

            const employmentType =
                (
                    job.job_employment_type ||
                    ""
                ).toUpperCase();


            if (
                employmentType !== jobType
            ) {

                return false;

            }

        }


        // ==========================================
        // SALARY FILTER
        // ==========================================

        const jobMinSalary =
            Number(job.job_min_salary);

        const jobMaxSalary =
            Number(job.job_max_salary);


        if (
            !isNaN(minSalary) &&
            jobMaxSalary
        ) {

            if (
                jobMaxSalary < minSalary
            ) {

                return false;

            }

        }


        if (
            !isNaN(maxSalary) &&
            jobMinSalary
        ) {

            if (
                jobMinSalary > maxSalary
            ) {

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

    const container =
        document.getElementById("jobContainer");

    const jobCount =
        document.getElementById("jobCount");

    const jobResultText =
        document.getElementById("jobResultText");


    container.innerHTML = "";


    jobCount.textContent =
        jobs.length;


    jobResultText.textContent =
        `${jobs.length} jobs found based on your preferences`;


    if (jobs.length === 0) {

        container.innerHTML = `

            <div class="no-jobs">

                <i class="fa-solid fa-magnifying-glass"></i>

                <h3>
                    No matching jobs found
                </h3>

                <p>
                    Try adjusting your filters or search preferences.
                </p>

            </div>

        `;

        return;

    }


    jobs
        .slice(0, 20)
        .forEach(job => {


            const company =
                job.employer_name ||
                "Unknown Company";


            const title =
                job.job_title ||
                "Job Position";


            const location =
                getJobLocation(job);


            const workSetup =
                getWorkSetup(job);


            const employmentType =
                formatJobType(
                    job.job_employment_type
                );


            const workBadge =
                getWorkBadge(
                    workSetup
                );


            const safeTitle =
                escapeHtml(title);


            const safeCompany =
                escapeHtml(company);


            container.innerHTML += `

                <div class="job-card">

                    <div class="job-card-content">


                        <div class="job-main-info">

                            <p class="company-name">

                                ${safeCompany}

                            </p>


                            <h3 class="job-title">

                                ${safeTitle}

                            </h3>


                            <div class="job-meta">


                                <span>

                                    <i class="fa-solid fa-location-dot"></i>

                                    ${escapeHtml(location)}

                                </span>


                                <span>

                                    <i class="fa-solid fa-briefcase"></i>

                                    ${employmentType}

                                </span>


                                ${workBadge}


                            </div>

                        </div>


                        <div class="job-card-actions">


                            <button
                                class="btn btn-secondary view-details-btn"
                                data-job-id="${escapeHtml(job.job_id || "")}"
                            >

                                View Details

                            </button>


                            <button
                                class="btn btn-primary apply-job-btn"
                                data-apply-link="${escapeHtml(job.job_apply_link || "")}"
                            >

                                Apply

                            </button>


                        </div>


                    </div>

                </div>

            `;

        });


    // VIEW DETAILS BUTTONS

    document
        .querySelectorAll(".view-details-btn")
        .forEach(button => {

            button.addEventListener(
                "click",
                () => {

                    const jobId =
                        button.dataset.jobId;


                    const selectedJob =
                        currentJobs.find(
                            job =>
                                job.job_id === jobId
                        );


                    if (selectedJob) {

                        openPopup(
                            selectedJob
                        );

                    }

                }
            );

        });


    // APPLY BUTTONS

    document
        .querySelectorAll(".apply-job-btn")
        .forEach(button => {

            button.addEventListener(
                "click",
                () => {

                    const applyLink =
                        button.dataset.applyLink;


                    if (applyLink) {

                        window.open(
                            applyLink,
                            "_blank"
                        );

                    }

                }
            );

        });

}


// ======================================================
// GET JOB LOCATION
// ======================================================

function getJobLocation(job) {

    const locationParts = [];


    if (job.job_city) {

        locationParts.push(
            job.job_city
        );

    }


    if (job.job_state) {

        locationParts.push(
            job.job_state
        );

    }


    if (job.job_country) {

        locationParts.push(
            job.job_country
        );

    }


    if (locationParts.length === 0) {

        return "Location not specified";

    }


    return locationParts.join(", ");

}


// ======================================================
// GET WORK SETUP
// ======================================================

function getWorkSetup(job) {

    const description =
        (
            job.job_description ||
            ""
        ).toLowerCase();


    if (
        job.job_is_remote === true ||
        description.includes("remote") ||
        description.includes("work from home") ||
        description.includes("wfh")
    ) {

        return "Remote / WFH";

    }


    if (
        description.includes("hybrid")
    ) {

        return "Hybrid";

    }


    return "On-site";

}


// ======================================================
// WORK BADGE
// ======================================================

function getWorkBadge(workSetup) {

    if (workSetup === "Remote / WFH") {

        return `

            <span class="badge remote-badge">

                <i class="fa-solid fa-house"></i>

                Remote / WFH

            </span>

        `;

    }


    if (workSetup === "Hybrid") {

        return `

            <span class="badge hybrid-badge">

                <i class="fa-solid fa-arrows-rotate"></i>

                Hybrid

            </span>

        `;

    }


    return `

        <span class="badge onsite-badge">

            <i class="fa-solid fa-building"></i>

            On-site

        </span>

    `;

}


// ======================================================
// FORMAT JOB TYPE
// ======================================================

function formatJobType(type) {

    if (!type) {

        return "Not specified";

    }


    const types = {

        "FULLTIME":
            "Full-time",

        "PARTTIME":
            "Part-time",

        "CONTRACTOR":
            "Contract",

        "INTERN":
            "Internship"

    };


    return types[type.toUpperCase()]
        || type;

}


// ======================================================
// OPEN JOB DETAILS POPUP
// ======================================================

async function openPopup(job) {

    const popup =
        document.getElementById("jobPopup");


    popup.classList.add("show");


    document
        .getElementById("popupTitle")
        .textContent =
            job.job_title ||
            "Job Title";


    document
        .getElementById("popupCompany")
        .textContent =
            job.employer_name ||
            "Unknown Company";


    document
        .getElementById("popupLocation")
        .textContent =
            getJobLocation(job);


    document
        .getElementById("popupWorkSetup")
        .textContent =
            getWorkSetup(job);


    document
        .getElementById("popupJobType")
        .textContent =
            formatJobType(
                job.job_employment_type
            );


    document
        .getElementById("popupDescription")
        .textContent =
            "Loading job details...";


    document
        .getElementById("popupSalary")
        .textContent =
            "Loading salary information...";


    // APPLY BUTTON

    const applyButton =
        document.getElementById("applyBtn");


    applyButton.onclick = () => {

        if (job.job_apply_link) {

            window.open(
                job.job_apply_link,
                "_blank"
            );

        }

    };


    // LOAD DETAILS

    try {

        const headers = {

            "X-RapidAPI-Key":
                RAPIDAPI_KEY,

            "X-RapidAPI-Host":
                RAPIDAPI_HOST

        };


        // JOB DETAILS

        if (job.job_id) {

            const detailsResponse =
                await fetch(

                    `https://jsearch.p.rapidapi.com/job-details?job_id=${encodeURIComponent(job.job_id)}`,

                    {
                        method: "GET",
                        headers
                    }

                );


            const detailsData =
                await detailsResponse.json();


            const jobDetails =
                detailsData?.data?.[0];


            const description =
                jobDetails?.job_description ||
                job.job_description ||
                "No job description available.";


            document
                .getElementById("popupDescription")
                .textContent =
                    description;

        }


        // SALARY

        loadSalary(
            job
        );


    } catch (error) {

        console.error(
            "Error loading job details:",
            error
        );


        document
            .getElementById("popupDescription")
            .textContent =
                job.job_description ||
                "Unable to load job description.";


        document
            .getElementById("popupSalary")
            .textContent =
                getJobSalary(job);

    }

}


// ======================================================
// LOAD ESTIMATED SALARY
// ======================================================

async function loadSalary(job) {

    try {

        // Use API salary if already available

        if (
            job.job_min_salary ||
            job.job_max_salary
        ) {

            document
                .getElementById("popupSalary")
                .textContent =
                    getJobSalary(job);

            return;

        }


        const headers = {

            "X-RapidAPI-Key":
                RAPIDAPI_KEY,

            "X-RapidAPI-Host":
                RAPIDAPI_HOST

        };


        const location =
            getJobLocation(job);


        const salaryURL =

            `https://jsearch.p.rapidapi.com/estimated-salary` +

            `?job_title=${encodeURIComponent(job.job_title)}` +

            `&location=${encodeURIComponent(location)}` +

            `&location_type=ANY` +

            `&years_of_experience=ALL`;


        const response =
            await fetch(

                salaryURL,

                {
                    method: "GET",
                    headers
                }

            );


        const data =
            await response.json();


        const salary =
            data?.data?.[0];


        if (!salary) {

            document
                .getElementById("popupSalary")
                .textContent =
                    "Salary information not available.";

            return;

        }


        const min =
            salary.min_salary;

        const max =
            salary.max_salary;

        const median =
            salary.median_salary;

        const currency =
            salary.salary_currency ||
            "PHP";

        const period =
            salary.salary_period ||
            "year";


        let salaryText =
            "Not available";


        if (min && max) {

            salaryText =

                `${currency} ` +

                `${Number(min).toLocaleString()}` +

                ` - ` +

                `${Number(max).toLocaleString()}` +

                ` per ${period}`;

        }

        else if (median) {

            salaryText =

                `${currency} ` +

                `${Number(median).toLocaleString()}` +

                ` per ${period}`;

        }


        document
            .getElementById("popupSalary")
            .textContent =
                salaryText;


    } catch (error) {

        console.error(
            "Salary error:",
            error
        );


        document
            .getElementById("popupSalary")
            .textContent =
                "Salary information unavailable.";

    }

}


// ======================================================
// GET JOB SALARY
// ======================================================

function getJobSalary(job) {

    const min =
        job.job_min_salary;

    const max =
        job.job_max_salary;

    const currency =
        job.job_salary_currency ||
        "PHP";


    if (min && max) {

        return

            `${currency} ` +

            `${Number(min).toLocaleString()}` +

            ` - ` +

            `${Number(max).toLocaleString()}`;

    }


    if (min) {

        return

            `Starting at ${currency} ` +

            `${Number(min).toLocaleString()}`;

    }


    if (max) {

        return

            `Up to ${currency} ` +

            `${Number(max).toLocaleString()}`;

    }


    return "Salary not specified";

}


// ======================================================
// CLOSE POPUP
// ======================================================

function closePopup() {

    document
        .getElementById("jobPopup")
        .classList.remove("show");

}


// ======================================================
// ESCAPE HTML
// ======================================================

function escapeHtml(value) {

    if (value === null ||
        value === undefined) {

        return "";

    }


    return String(value)

        .replace(/&/g, "&amp;")

        .replace(/</g, "&lt;")

        .replace(/>/g, "&gt;")

        .replace(/"/g, "&quot;")

        .replace(/'/g, "&#039;");

}
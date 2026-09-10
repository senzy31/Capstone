/* ==========================================
   JOBLINK APPLICATION PAGE
   Connected to the ASP.NET backend:
     - Applications  -> GET/POST/PUT/DELETE /api/Application
     - Job listings   -> GET/POST /api/Joblisting
       ("Log Application" creates a minimal listing on the fly,
       since every application needs a real job to point at -
       there's no live job search wired up on this page yet.)
========================================== */

document.addEventListener("DOMContentLoaded", () => {

    const API_BASE = "https://localhost:7142/api";


    /* ======================================
       WHO'S LOGGED IN?
    ======================================= */

    const savedUser = localStorage.getItem("user");

    if (!savedUser) {

        window.location.href = "../LOGIN/login.html";

        return;

    }

    let currentUser;

    try {

        currentUser = JSON.parse(savedUser);

    } catch (error) {

        window.location.href = "../LOGIN/login.html";

        return;

    }

    const userId = currentUser.userId || currentUser.user_id;

    if (!userId) {

        window.location.href = "../LOGIN/login.html";

        return;

    }


    /* ======================================
       STATE
    ======================================= */

    let applications = []; // enriched: { ...applicationModel, job }
    let primaryResumeId = null;


    /* ======================================
       ELEMENTS
    ======================================= */

    const applicationsList = document.getElementById("applicationsList");
    const totalApplied = document.getElementById("totalApplied");
    const activeApplications = document.getElementById("activeApplications");
    const underReview = document.getElementById("underReview");
    const filterButtons = document.querySelectorAll(".filter-btn");
    const searchInput = document.getElementById("searchInput");
    const navUser = document.getElementById("navUser");

    const logApplicationBtn = document.getElementById("logApplicationBtn");
    const logApplicationOverlay = document.getElementById("logApplicationOverlay");
    const cancelLogApplication = document.getElementById("cancelLogApplication");
    const submitLogApplication = document.getElementById("submitLogApplication");
    const logCompany = document.getElementById("logCompany");
    const logPosition = document.getElementById("logPosition");
    const logLocation = document.getElementById("logLocation");
    const logStatus = document.getElementById("logStatus");
    const logDate = document.getElementById("logDate");


    /* ======================================
       LOAD USER + APPLICATIONS
    ======================================= */

    loadUser();

    loadApplications();


    /* ======================================
       FILTER BUTTONS
    ======================================= */

    filterButtons.forEach(button => {

        button.addEventListener("click", () => {

            filterButtons.forEach(btn => btn.classList.remove("active"));

            button.classList.add("active");

            renderApplications(button.dataset.status);

        });

    });


    /* ======================================
       SEARCH
    ======================================= */

    searchInput.addEventListener("input", () => {

        const currentFilter = document.querySelector(".filter-btn.active")?.dataset.status || "all";

        renderApplications(currentFilter);

    });


    /* ======================================
       LOG APPLICATION MODAL
    ======================================= */

    logApplicationBtn.addEventListener("click", () => {

        logCompany.value = "";
        logPosition.value = "";
        logLocation.value = "";
        logStatus.value = "Applied";
        logDate.value = new Date().toISOString().slice(0, 10);

        logApplicationOverlay.classList.add("show");

        setTimeout(() => logCompany.focus(), 100);

    });

    cancelLogApplication.addEventListener("click", () => {

        logApplicationOverlay.classList.remove("show");

    });

    logApplicationOverlay.addEventListener("click", event => {

        if (event.target === logApplicationOverlay) {

            logApplicationOverlay.classList.remove("show");

        }

    });

    submitLogApplication.addEventListener("click", submitLoggedApplication);


    /* ======================================
       LOGOUT
    ======================================= */

    const logoutBtn = document.getElementById("logoutBtn");
    const logoutOverlay = document.getElementById("logoutOverlay");
    const cancelLogout = document.getElementById("cancelLogout");
    const confirmLogout = document.getElementById("confirmLogout");

    logoutBtn.addEventListener("click", event => {

        event.preventDefault();

        logoutOverlay.classList.add("show");

    });

    cancelLogout.addEventListener("click", () => {

        logoutOverlay.classList.remove("show");

    });

    confirmLogout.addEventListener("click", () => {

        localStorage.removeItem("user");

        localStorage.removeItem("token");

        window.location.href = "../LOGIN/login.html";

    });

    logoutOverlay.addEventListener("click", event => {

        if (event.target === logoutOverlay) {

            logoutOverlay.classList.remove("show");

        }

    });


    /* ======================================
       FUNCTIONS
    ======================================= */


    function loadUser() {

        const name = currentUser.fullName || currentUser.full_name || currentUser.name || "User";

        navUser.textContent = getInitials(name);

    }


    function getInitials(name) {

        if (!name) {
            return "U";
        }

        const parts = name.trim().split(/\s+/);

        if (parts.length >= 2) {

            return parts[0].charAt(0) + parts[parts.length - 1].charAt(0);

        }

        return parts[0].charAt(0).toUpperCase();

    }


    /* ======================================
       LOAD APPLICATIONS (+ resolve each job)
    ======================================= */

    async function loadApplications() {

        applicationsList.innerHTML = `
            <div class="no-results">
                <i class="fa-solid fa-spinner fa-spin"></i>
                Loading your applications...
            </div>
        `;

        try {

            const [applicationsResponse, resumesResponse] = await Promise.all([
                fetch(`${API_BASE}/Application/by-user/${userId}`),
                fetch(`${API_BASE}/Resume/by-user/${userId}`)
            ]);

            if (!applicationsResponse.ok) {
                throw new Error(`Failed to load applications (${applicationsResponse.status})`);
            }

            const rawApplications = await applicationsResponse.json();

            if (resumesResponse.ok) {

                const resumes = await resumesResponse.json();

                primaryResumeId = resumes[0]?.resumeId || null;

            }

            applications = await Promise.all(rawApplications.map(async application => {

                let job = null;

                if (application.jobId) {

                    try {

                        const jobResponse = await fetch(`${API_BASE}/Joblisting/${application.jobId}`);

                        if (jobResponse.ok) {
                            job = await jobResponse.json();
                        }

                    } catch (error) {

                        console.error("Unable to load job listing:", error);

                    }

                }

                return { ...application, job };

            }));

            updateStatistics();

            const currentFilter = document.querySelector(".filter-btn.active")?.dataset.status || "all";

            renderApplications(currentFilter);

        } catch (error) {

            console.error("Unable to load applications:", error);

            applicationsList.innerHTML = `
                <div class="no-results">
                    Couldn't reach the server. Make sure the API is running, then refresh.
                </div>
            `;

        }

    }


    /* ======================================
       STATISTICS
    ======================================= */

    function updateStatistics() {

        totalApplied.textContent = applications.length;

        activeApplications.textContent = applications.filter(
            a => (a.status || "Applied") !== "Rejected"
        ).length;

        underReview.textContent = applications.filter(
            a => a.status === "Under Review"
        ).length;

    }


    /* ======================================
       RENDER APPLICATIONS
    ======================================= */

    function renderApplications(filter) {

        const searchTerm = searchInput.value.trim().toLowerCase();

        const filtered = applications.filter(application => {

            const status = application.status || "Applied";

            const statusMatches =
                filter === "all" ||
                (filter === "active" && status !== "Rejected") ||
                (filter === "review" && status === "Under Review");

            const company = (application.job?.company || "").toLowerCase();
            const position = (application.job?.title || "").toLowerCase();

            const searchMatches =
                !searchTerm ||
                company.includes(searchTerm) ||
                position.includes(searchTerm);

            return statusMatches && searchMatches;

        });

        applicationsList.innerHTML = "";

        if (filtered.length === 0) {

            applicationsList.innerHTML = `
                <div class="no-results">
                    ${applications.length === 0
                        ? `No applications logged yet. Click "Log Application" to add one.`
                        : `No applications found.`}
                </div>
            `;

            return;

        }

        filtered.forEach(application => {

            const company = application.job?.company || "Unknown Company";
            const position = application.job?.title || "Unknown Position";
            const location = application.job?.location || "Not specified";
            const status = application.status || "Applied";

            const card = document.createElement("div");

            card.className = "application-card";

            card.innerHTML = `

                <div class="company-logo generic">
                    ${escapeHTML(company.charAt(0).toUpperCase() || "?")}
                </div>

                <div class="application-info">

                    <div class="company-row">

                        <h3>${escapeHTML(company)}</h3>

                        <span class="status-badge ${statusClass(status)}">
                            ${escapeHTML(status)}
                        </span>

                    </div>

                    <div class="job-details">

                        <span>${escapeHTML(position)}</span>

                        <span class="separator">•</span>

                        <span>${escapeHTML(location)}</span>

                    </div>

                </div>

                <div class="application-actions">

                    <select class="status-select" data-application-id="${application.applicationId}">
                        ${["Applied", "Under Review", "Interview", "Offer", "Rejected"].map(option => `
                            <option value="${option}" ${option === status ? "selected" : ""}>${option}</option>
                        `).join("")}
                    </select>

                    <button
                        type="button"
                        class="withdraw-btn"
                        data-application-id="${application.applicationId}"
                        title="Withdraw application"
                    >
                        <i class="fa-solid fa-trash"></i>
                    </button>

                </div>

            `;

            card.querySelector(".status-select").addEventListener("click", event => {
                event.stopPropagation();
            });

            card.querySelector(".status-select").addEventListener("change", event => {

                updateApplicationStatus(application.applicationId, event.target.value);

            });

            card.querySelector(".withdraw-btn").addEventListener("click", event => {

                event.stopPropagation();

                withdrawApplication(application.applicationId, company);

            });

            applicationsList.appendChild(card);

        });

    }


    function statusClass(status) {

        return "status-" + status.toLowerCase().replace(/\s+/g, "-");

    }


    /* ======================================
       UPDATE STATUS
    ======================================= */

    async function updateApplicationStatus(applicationId, newStatus) {

        const application = applications.find(a => a.applicationId === applicationId);

        if (!application) {
            return;
        }

        try {

            const response = await fetch(`${API_BASE}/Application`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ...application, status: newStatus })
            });

            if (!response.ok) {
                throw new Error(`Update failed (${response.status})`);
            }

            application.status = newStatus;

            updateStatistics();

            const currentFilter = document.querySelector(".filter-btn.active")?.dataset.status || "all";

            renderApplications(currentFilter);

        } catch (error) {

            console.error("Unable to update status:", error);

            alert("Couldn't update the status. Check that the API is running and try again.");

            loadApplications();

        }

    }


    /* ======================================
       WITHDRAW APPLICATION
    ======================================= */

    async function withdrawApplication(applicationId, company) {

        const confirmed = confirm(`Withdraw your application to ${company}? This can't be undone.`);

        if (!confirmed) {
            return;
        }

        try {

            const response = await fetch(`${API_BASE}/Application?id=${applicationId}`, {
                method: "DELETE"
            });

            if (!response.ok) {
                throw new Error(`Delete failed (${response.status})`);
            }

            applications = applications.filter(a => a.applicationId !== applicationId);

            updateStatistics();

            const currentFilter = document.querySelector(".filter-btn.active")?.dataset.status || "all";

            renderApplications(currentFilter);

        } catch (error) {

            console.error("Unable to withdraw application:", error);

            alert("Couldn't withdraw the application. Check that the API is running and try again.");

        }

    }


    /* ======================================
       LOG A NEW APPLICATION
    ======================================= */

    async function submitLoggedApplication() {

        const company = logCompany.value.trim();
        const position = logPosition.value.trim();
        const location = logLocation.value.trim();
        const status = logStatus.value;
        const appliedAt = logDate.value ? new Date(logDate.value).toISOString() : new Date().toISOString();

        if (!company) {

            alert("Please enter a company name.");

            logCompany.focus();

            return;

        }

        if (!position) {

            alert("Please enter a position.");

            logPosition.focus();

            return;

        }

        submitLogApplication.disabled = true;

        submitLogApplication.textContent = "Logging...";

        try {

            const externalJobId = `manual-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

            const createJobResponse = await fetch(`${API_BASE}/Joblisting`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    externalJobId,
                    title: position,
                    company,
                    location: location || null,
                    sourceApi: "manual",
                    isDeleted: false
                })
            });

            if (!createJobResponse.ok) {
                throw new Error(`Create job listing failed (${createJobResponse.status})`);
            }

            const allJobsResponse = await fetch(`${API_BASE}/Joblisting`);

            const allJobs = await allJobsResponse.json();

            const createdJob = allJobs.find(job => job.externalJobId === externalJobId);

            if (!createdJob) {
                throw new Error("Created job listing could not be found afterward.");
            }

            const createApplicationResponse = await fetch(`${API_BASE}/Application`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    userId,
                    jobId: createdJob.jobId,
                    resumeId: primaryResumeId,
                    status,
                    appliedAt,
                    isDeleted: false
                })
            });

            if (!createApplicationResponse.ok) {
                throw new Error(`Create application failed (${createApplicationResponse.status})`);
            }

            logApplicationOverlay.classList.remove("show");

            await loadApplications();

        } catch (error) {

            console.error("Unable to log application:", error);

            alert("Couldn't log that application. Check that the API is running and try again.");

        } finally {

            submitLogApplication.disabled = false;

            submitLogApplication.textContent = "Log Application";

        }

    }


    /* ======================================
       ESCAPE HTML
    ======================================= */

    function escapeHTML(value) {

        const div = document.createElement("div");

        div.textContent = value;

        return div.innerHTML;

    }

});

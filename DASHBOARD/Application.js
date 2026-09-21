/* ==========================================
   JOBLINK APPLICATION PAGE
   Connected to the ASP.NET backend:
     - Applications  -> GET/POST/PUT/DELETE /api/Application
     - Job listings   -> GET/POST /api/Joblisting
       ("Log Application" creates a minimal listing on the fly,
       since every application needs a real job to point at.)
     - Confirm       -> PATCH /api/applications/{id}/confirm-external

   Three kinds of application show up here:
     - Internal   applied to a JobLink employer's job; the employer
                  owns the status (Submitted / Viewed / Shortlisted /
                  Rejected), so there's no status dropdown.
     - Redirected sent to the original posting (e.g. LinkedIn);
                  Redirected until the user says they finished
                  ("Mark as applied" -> Applied Externally).
     - Logged     typed in by hand; the user owns the status.

   Every request that reads or changes applications carries the
   login token (ApplyFlow.authFetch).
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

    // "Did you finish applying?" - and refresh when it (or the Apply button
    // elsewhere on the page) changes an application.
    ApplyFlow.initReturnPrompt();

    window.addEventListener("joblink:application-updated", loadApplications);


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

            const applicationsResponse = await ApplyFlow.authFetch(`${API_BASE}/Application/by-user/${userId}`);

            if (!applicationsResponse.ok) {
                throw new Error(`Failed to load applications (${applicationsResponse.status})`);
            }

            const rawApplications = await applicationsResponse.json();

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

            // Newest first: by the date applied, or - for someone who was sent to
            // the job site but hasn't confirmed yet - the date they were redirected.
            applications.sort((a, b) => (sortTime(b) - sortTime(a)) || (b.applicationId - a.applicationId));

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


    function sortTime(application) {

        const time = new Date(application.appliedAt || application.redirectedAt || 0).getTime();

        return Number.isNaN(time) ? 0 : time;

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

            // Sent to the original posting (e.g. LinkedIn) - JobLink tracks it
            // but doesn't own the outcome.
            const isRedirect = application.applicationType === "External" &&
                (status === "Redirected" || status === "Applied Externally");

            const publisher = isRedirect ? (application.job?.publisher || "") : "";

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

                        ${publisher ? `<span class="separator">•</span><span>via ${escapeHTML(publisher)}</span>` : ""}

                    </div>

                </div>

                <div class="application-actions">

                    ${statusControlHtml(application, status)}

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

            card.querySelector(".status-select")?.addEventListener("click", event => {
                event.stopPropagation();
            });

            card.querySelector(".status-select")?.addEventListener("change", event => {

                updateApplicationStatus(application.applicationId, event.target.value);

            });

            card.querySelector(".mark-applied-btn")?.addEventListener("click", event => {

                event.stopPropagation();

                markApplied(application, event.currentTarget);

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


    // Who may change the status decides the control:
    //   logged by hand   -> a dropdown (the user owns the status)
    //   redirected       -> "Mark as applied" until they confirm
    //   confirmed / internal -> nothing (JobLink or the employer owns it)
    function statusControlHtml(application, status) {

        if (application.applicationType === "External" && status === "Redirected") {

            return `
                <button
                    type="button"
                    class="mark-applied-btn"
                    data-application-id="${application.applicationId}"
                >
                    Mark as applied
                </button>
            `;

        }

        if (application.applicationType === "External" && status !== "Applied Externally") {

            return `
                <select class="status-select" data-application-id="${application.applicationId}">
                    ${["Applied", "Under Review", "Interview", "Offer", "Rejected"].map(option => `
                        <option value="${option}" ${option === status ? "selected" : ""}>${option}</option>
                    `).join("")}
                </select>
            `;

        }

        return "";

    }


    /* ======================================
       MARK AS APPLIED (after being sent to the job site)
    ======================================= */

    async function markApplied(application, button) {

        button.disabled = true;

        try {

            const response = await ApplyFlow.authFetch(
                `${API_BASE}/applications/${application.applicationId}/confirm-external`,
                {
                    method: "PATCH",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ applied: true })
                }
            );

            // 409: it was already confirmed (e.g. from the pop-up in another tab).
            if (!response.ok && response.status !== 409) {

                const body = await response.json().catch(() => ({}));

                throw new Error(body.message || `Update failed (${response.status})`);

            }

            ApplyFlow.forgetPending(application.applicationId);

            await loadApplications();

        } catch (error) {

            console.error("Unable to mark as applied:", error);

            button.disabled = false;

            alert(error.message || "Couldn't update that application. Please try again.");

        }

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

            // Only the status can change, so that's all we send.
            const response = await ApplyFlow.authFetch(`${API_BASE}/Application`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ applicationId, status: newStatus })
            });

            if (!response.ok) {
                const body = await response.json().catch(() => ({}));
                throw new Error(body.message || `Update failed (${response.status})`);
            }

            application.status = newStatus;

            updateStatistics();

            const currentFilter = document.querySelector(".filter-btn.active")?.dataset.status || "all";

            renderApplications(currentFilter);

        } catch (error) {

            console.error("Unable to update status:", error);

            alert(error.message && !error.message.startsWith("Update failed")
                ? error.message
                : "Couldn't update the status. Check that the API is running and try again.");

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

            const response = await ApplyFlow.authFetch(`${API_BASE}/Application?id=${applicationId}`, {
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

            // The server sets everything about the listing except what you typed,
            // and hands back the saved job.
            const createJobResponse = await ApplyFlow.authFetch(`${API_BASE}/Joblisting`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    title: position,
                    company,
                    location: location || null
                })
            });

            if (!createJobResponse.ok) {
                const body = await createJobResponse.json().catch(() => ({}));
                throw new Error(body.message || `Create job listing failed (${createJobResponse.status})`);
            }

            const createdJob = await createJobResponse.json();

            // The user and resume come from the login token; only the job,
            // status and date are ours to say.
            const createApplicationResponse = await ApplyFlow.authFetch(`${API_BASE}/Application`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    jobId: createdJob.jobId,
                    status,
                    appliedAt
                })
            });

            if (!createApplicationResponse.ok) {
                const body = await createApplicationResponse.json().catch(() => ({}));
                throw new Error(body.message || `Create application failed (${createApplicationResponse.status})`);
            }

            logApplicationOverlay.classList.remove("show");

            await loadApplications();

        } catch (error) {

            console.error("Unable to log application:", error);

            alert(error.message && !/failed \(\d+\)$/.test(error.message)
                ? error.message
                : "Couldn't log that application. Check that the API is running and try again.");

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

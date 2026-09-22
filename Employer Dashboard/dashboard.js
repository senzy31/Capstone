// ======================================================
// EMPLOYER JOB POSTS
// A real employer login is required - see requireEmployer(). Every job/credit read or write
// goes through /api/employer/... (EmployerJobsController), scoped to the caller's own employer
// id on the server; nothing here can act on anyone else's jobs. ApiClient.js (shared with the
// job seeker pages) supplies the login token and authFetch().
// ======================================================

const API = ApiClient.API;

let currentJobs = [];
let currentPackages = [];
let currentUserId = null;
let currentFullName = null;
let currentPhotoUrl = null;

// Set right before a publish/renew is refused for lack of a credit, so a purchase can retry it.
let pendingAction = null;


document.addEventListener("DOMContentLoaded", () => {

    const employer = requireEmployer();

    if (!employer) {
        return;
    }

    currentUserId = employer.userId;
    currentFullName = employer.fullName;

    document.getElementById("welcomeText").textContent = `Welcome, ${employer.fullName}!`;

    // Instant initials; loadPhoto() (below) fetches the real photo, if any, and updates both
    // this avatar and the big one in the Company Photo panel from the same response.
    Navbar.render(employer.fullName, null);

    setupListeners();

    loadEverything();

    loadPhoto();

});


// ======================================================
// SESSION - a real employer login only, no demo fallback
// ======================================================

function requireEmployer() {

    const loginPage = "../LOGIN/login.html";

    try {

        const user = JSON.parse(localStorage.getItem("user"));

        const userId = user?.userId || user?.user_id;

        if (!userId || user.role !== "employer") {
            throw new Error("Not an employer session");
        }

        if (!localStorage.getItem("token")) {

            localStorage.removeItem("user");

            sessionStorage.setItem("joblink.sessionExpired", "1");

            throw new Error("No login token");

        }

        const fullName = user.fullName || user.full_name || user.companyName || "Employer";

        return { ...user, userId, fullName };

    } catch {

        window.location.href = loginPage;

        return null;

    }

}


// ======================================================
// HELPERS
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

function formatMoney(amount) {
    return `PHP ${Number(amount).toLocaleString()}`;
}

function formatSalary(job) {

    if (job.salaryMin && job.salaryMax) {
        return `${formatMoney(job.salaryMin)} - ${formatMoney(job.salaryMax)}/month`;
    }

    if (job.salaryMin) {
        return `From ${formatMoney(job.salaryMin)}/month`;
    }

    if (job.salaryMax) {
        return `Up to ${formatMoney(job.salaryMax)}/month`;
    }

    return "Salary not specified";

}

function formatWorkSetup(setup) {
    return { onsite: "On-site", remote: "Remote", hybrid: "Hybrid" }[setup] || "Not specified";
}

function formatJobType(type) {
    return { FULLTIME: "Full-time", PARTTIME: "Part-time", CONTRACTOR: "Contract", INTERN: "Internship" }[type] || "Not specified";
}

async function readError(response, fallback) {

    const body = await response.json().catch(() => null);

    return body?.message || fallback;

}


// ======================================================
// LOAD
// ======================================================

async function loadEverything() {

    await Promise.all([loadCredits(), loadJobs(), loadPackages()]);

}

async function loadCredits() {

    try {

        const response = await ApiClient.authFetch(`${API}/employer/credits`);

        if (!response.ok) {
            return;
        }

        const credits = await response.json();

        document.getElementById("postCreditsCount").textContent = credits.postCredits;
        document.getElementById("renewalCreditsCount").textContent = credits.renewalCredits;

    } catch (error) {
        console.error("Error loading credits:", error);
    }

}

async function loadPackages() {

    try {

        const response = await ApiClient.authFetch(`${API}/employer/posting-packages`);

        currentPackages = response.ok ? await response.json() : [];

        renderPackages();

    } catch (error) {
        console.error("Error loading posting packages:", error);
    }

}

async function loadJobs() {

    const container = document.getElementById("jobListContainer");

    try {

        const response = await ApiClient.authFetch(`${API}/employer/jobs`);

        if (!response.ok) {
            throw new Error(await readError(response, `Couldn't load your job posts (${response.status}).`));
        }

        currentJobs = await response.json();

        document.getElementById("activeCount").textContent = currentJobs.filter(j => j.status === "Active").length;

        renderJobs();

    } catch (error) {

        console.error("Error loading job posts:", error);

        container.innerHTML = `
            <div class="no-jobs">
                <i class="fa-solid fa-circle-exclamation"></i>
                <p>Unable to load your job posts. ${escapeHtml(error.message)}</p>
            </div>
        `;

    }

}


// ======================================================
// RENDER
// ======================================================

function renderJobs() {

    const container = document.getElementById("jobListContainer");

    if (currentJobs.length === 0) {
        container.innerHTML = "<p>You haven't posted any jobs yet. Use the form above to create your first draft.</p>";
        return;
    }

    container.innerHTML = currentJobs.map(renderJobCard).join("");

}

function renderJobCard(job) {

    const statusClass = `status-${job.status.toLowerCase()}`;

    const daysLeft = job.status === "Active" && job.daysLeft !== null
        ? `<span><i class="fa-solid fa-clock"></i> ${job.daysLeft} ${job.daysLeft === 1 ? "day" : "days"} left</span>`
        : "";

    const skills = (job.skills || []).length > 0
        ? `<p class="job-card-meta"><i class="fa-solid fa-list-check"></i> ${escapeHtml(job.skills.join(", "))}</p>`
        : "";

    return `
        <div class="job-card">
            <div class="job-card-top">
                <div>
                    <h3>${escapeHtml(job.title)}</h3>
                    <p class="job-card-meta">
                        ${escapeHtml(job.company)} &middot; ${escapeHtml(job.location)}
                    </p>
                </div>
                <span class="status-pill ${statusClass}">${escapeHtml(job.status)}</span>
            </div>

            <p class="job-card-meta">
                <i class="fa-solid fa-money-bill-wave"></i> ${escapeHtml(formatSalary(job))}
                &middot; <i class="fa-solid fa-building"></i> ${escapeHtml(formatWorkSetup(job.workSetup))}
                &middot; <i class="fa-solid fa-briefcase"></i> ${escapeHtml(formatJobType(job.jobType))}
                &middot; <i class="fa-solid fa-users"></i> ${job.applicantCount} ${job.applicantCount === 1 ? "applicant" : "applicants"}
                ${daysLeft ? ` &middot; ${daysLeft}` : ""}
            </p>

            ${skills}

            <div class="job-card-actions">
                <button class="btn btn-secondary btn-edit" data-job-id="${job.jobId}">Edit</button>
                ${job.status === "Draft" ? `<button class="btn btn-primary btn-publish" data-job-id="${job.jobId}">Publish</button>` : ""}
                ${job.status === "Active" ? `<button class="btn btn-secondary btn-close" data-job-id="${job.jobId}">Close</button>` : ""}
                ${job.status === "Active" || job.status === "Closed" || job.status === "Expired"
                    ? `<button class="btn btn-secondary btn-renew" data-job-id="${job.jobId}">Renew (+30 days)</button>`
                    : ""}
            </div>
        </div>
    `;

}

function renderPackages() {

    const container = document.getElementById("packageList");

    container.innerHTML = currentPackages.map(pkg => `
        <div class="package-card">
            <div>
                <h4>${escapeHtml(pkg.package)} &middot; ${formatMoney(pkg.pricePhp)}</h4>
                <p>${escapeHtml(pkg.description)}</p>
            </div>
            <button class="btn btn-primary btn-buy-package" data-package="${escapeHtml(pkg.package)}">Buy</button>
        </div>
    `).join("");

}


// ======================================================
// FORM: CREATE / EDIT
// ======================================================

function readForm() {

    return {
        title: document.getElementById("jobTitle").value.trim(),
        company: document.getElementById("jobCompany").value.trim(),
        location: document.getElementById("jobLocationInput").value.trim(),
        description: document.getElementById("jobDescription").value.trim(),
        salaryMin: document.getElementById("jobSalaryMin").value ? Number(document.getElementById("jobSalaryMin").value) : null,
        salaryMax: document.getElementById("jobSalaryMax").value ? Number(document.getElementById("jobSalaryMax").value) : null,
        workSetup: document.getElementById("jobWorkSetup").value || null,
        jobType: document.getElementById("jobTypeInput").value || null,
        skills: document.getElementById("jobSkills").value.split(",").map(s => s.trim()).filter(Boolean)
    };

}

function fillForm(job) {

    document.getElementById("jobId").value = job.jobId;
    document.getElementById("jobTitle").value = job.title || "";
    document.getElementById("jobCompany").value = job.company || "";
    document.getElementById("jobLocationInput").value = job.location || "";
    document.getElementById("jobDescription").value = job.description || "";
    document.getElementById("jobSalaryMin").value = job.salaryMin ?? "";
    document.getElementById("jobSalaryMax").value = job.salaryMax ?? "";
    document.getElementById("jobWorkSetup").value = job.workSetup || "";
    document.getElementById("jobTypeInput").value = job.jobType || "";
    document.getElementById("jobSkills").value = (job.skills || []).join(", ");

    document.getElementById("formTitle").textContent = `Editing "${job.title}"`;
    document.getElementById("saveJobBtn").textContent = "Save Changes";
    document.getElementById("cancelEditBtn").hidden = false;

    document.getElementById("post-job").scrollIntoView({ behavior: "smooth", block: "start" });

}

function resetForm() {

    document.getElementById("jobForm").reset();
    document.getElementById("jobId").value = "";
    document.getElementById("formTitle").textContent = "Post a New Job";
    document.getElementById("saveJobBtn").textContent = "Save as Draft";
    document.getElementById("cancelEditBtn").hidden = true;
    hideFormError();

}

function showFormError(message) {
    const error = document.getElementById("formError");
    error.textContent = message;
    error.hidden = false;
}

function hideFormError() {
    document.getElementById("formError").hidden = true;
}

async function submitForm(event) {

    event.preventDefault();

    hideFormError();

    const jobId = document.getElementById("jobId").value;
    const body = readForm();

    const saveBtn = document.getElementById("saveJobBtn");
    saveBtn.disabled = true;

    try {

        const response = await ApiClient.authFetch(
            jobId ? `${API}/employer/jobs/${jobId}` : `${API}/employer/jobs`,
            {
                method: jobId ? "PUT" : "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body)
            }
        );

        if (!response.ok) {
            throw new Error(await readError(response, `Couldn't save this job post (${response.status}).`));
        }

        resetForm();

        await loadJobs();

    } catch (error) {

        showFormError(error.message);

    } finally {

        saveBtn.disabled = false;

    }

}


// ======================================================
// PUBLISH / CLOSE / RENEW
// ======================================================

async function handleJobAction(jobId, action) {

    try {

        const response = await ApiClient.authFetch(
            `${API}/employer/jobs/${jobId}/${action}`,
            { method: "POST", noUpgradePrompt: true }
        );

        if (response.status === 403) {

            const body = await response.json().catch(() => ({}));

            if (body.purchaseRequired) {
                pendingAction = { jobId, action };
                openPurchaseModal();
                return;
            }

        }

        if (!response.ok) {
            throw new Error(await readError(response, `That didn't work (${response.status}).`));
        }

        await loadEverything();

    } catch (error) {

        alert(error.message);

    }

}

async function editJob(jobId) {

    try {

        const response = await ApiClient.authFetch(`${API}/employer/jobs/${jobId}`);

        if (!response.ok) {
            throw new Error(await readError(response, `Couldn't load this job (${response.status}).`));
        }

        fillForm(await response.json());

    } catch (error) {

        alert(error.message);

    }

}


// ======================================================
// PURCHASE MODAL
// ======================================================

function openPurchaseModal() {
    hidePurchaseError();
    document.getElementById("purchaseModal").style.display = "flex";
}

function closePurchaseModal() {
    document.getElementById("purchaseModal").style.display = "none";
    pendingAction = null;
}

function showPurchaseError(message) {
    const error = document.getElementById("purchaseError");
    error.textContent = message;
    error.hidden = false;
}

function hidePurchaseError() {
    document.getElementById("purchaseError").hidden = true;
}

async function buyPackage(packageName) {

    hidePurchaseError();

    try {

        const response = await ApiClient.authFetch(`${API}/employer/purchase`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ package: packageName })
        });

        if (!response.ok) {
            throw new Error(await readError(response, `Couldn't complete that purchase (${response.status}).`));
        }

        await loadCredits();

        const retry = pendingAction;
        pendingAction = null;

        closePurchaseModal();

        if (retry) {
            await handleJobAction(retry.jobId, retry.action);
        }

    } catch (error) {

        showPurchaseError(error.message);

    }

}


// ======================================================
// PROFILE PHOTO
// ======================================================

async function loadPhoto() {

    try {

        const response = await ApiClient.authFetch(`${API}/Profile/by-user/${currentUserId}`, { noUpgradePrompt: true });

        currentPhotoUrl = response.ok ? (await response.json()).photoUrl || null : null;

        renderPhoto();

    } catch (error) {
        console.error("Error loading photo:", error);
    }

}

function renderPhoto() {

    const img = document.getElementById("profileAvatarImg");
    const fallback = document.getElementById("profileAvatarInitials");
    const removeBtn = document.getElementById("removePhotoBtn");

    if (currentPhotoUrl) {

        img.src = currentPhotoUrl;
        img.alt = "Your company photo";
        img.hidden = false;
        fallback.hidden = true;

    } else {

        img.hidden = true;
        img.removeAttribute("src");
        fallback.hidden = false;
        fallback.textContent = Navbar.initials(currentFullName);

    }

    removeBtn.hidden = !currentPhotoUrl;

    Navbar.render(currentFullName, currentPhotoUrl);

}

function setPhotoStatus(text, isError = false) {

    const status = document.getElementById("photoStatus");
    status.textContent = text;
    status.hidden = !text;
    status.classList.toggle("error", isError);
    status.classList.toggle("success", !isError && Boolean(text));

}

async function uploadPhoto(file) {

    setPhotoStatus("Uploading...");

    const body = new FormData();
    body.append("file", file);

    try {

        const response = await ApiClient.authFetch(`${API}/Profile/photo`, { method: "POST", body });

        const result = await response.json().catch(() => ({}));

        if (!response.ok) {
            throw new Error(result.message || `Couldn't upload that photo (${response.status}).`);
        }

        currentPhotoUrl = result.photoUrl;

        renderPhoto();

        setPhotoStatus("Photo updated.");

    } catch (error) {

        console.error("Unable to upload photo:", error);

        setPhotoStatus(error.message, true);

    } finally {

        document.getElementById("photoInput").value = "";

    }

}

async function removePhoto() {

    const removeBtn = document.getElementById("removePhotoBtn");
    removeBtn.disabled = true;

    try {

        const response = await ApiClient.authFetch(`${API}/Profile/photo`, { method: "DELETE" });

        if (!response.ok) {
            throw new Error(`Couldn't remove your photo (${response.status}).`);
        }

        currentPhotoUrl = null;

        renderPhoto();

        setPhotoStatus("Photo removed.");

    } catch (error) {

        console.error("Unable to remove photo:", error);

        setPhotoStatus(error.message, true);

    } finally {

        removeBtn.disabled = false;

    }

}


// ======================================================
// EVENT LISTENERS
// ======================================================

function setupListeners() {

    document.getElementById("jobForm").addEventListener("submit", submitForm);

    document.getElementById("photoInput").addEventListener("change", () => {

        const file = document.getElementById("photoInput").files?.[0];

        if (file) {
            uploadPhoto(file);
        }

    });

    document.getElementById("removePhotoBtn").addEventListener("click", removePhoto);

    document.getElementById("cancelEditBtn").addEventListener("click", resetForm);

    document.getElementById("openPurchaseBtn").addEventListener("click", openPurchaseModal);

    document.getElementById("cancelPurchase").addEventListener("click", closePurchaseModal);

    document.getElementById("purchaseModal").addEventListener("click", (event) => {
        if (event.target.id === "purchaseModal") {
            closePurchaseModal();
        }
    });

    document.getElementById("packageList").addEventListener("click", (event) => {

        const button = event.target.closest(".btn-buy-package");

        if (button) {
            buyPackage(button.dataset.package);
        }

    });

    document.getElementById("jobListContainer").addEventListener("click", (event) => {

        const button = event.target.closest("button[data-job-id]");

        if (!button) {
            return;
        }

        const jobId = button.dataset.jobId;

        if (button.classList.contains("btn-edit")) {
            editJob(jobId);
        } else if (button.classList.contains("btn-publish")) {
            handleJobAction(jobId, "publish");
        } else if (button.classList.contains("btn-close")) {
            if (confirm("Close this job post? It will stop appearing to job seekers.")) {
                handleJobAction(jobId, "close");
            }
        } else if (button.classList.contains("btn-renew")) {
            handleJobAction(jobId, "renew");
        }

    });

    // Logout (same confirmation pattern as the job seeker pages).
    const logoutModal = document.getElementById("logoutModal");

    document.getElementById("logoutBtn").addEventListener("click", () => {
        logoutModal.style.display = "flex";
    });

    document.getElementById("cancelLogout").addEventListener("click", () => {
        logoutModal.style.display = "none";
    });

    document.getElementById("confirmLogout").addEventListener("click", () => {
        localStorage.clear();
        window.location.href = "../LOGIN/login.html";
    });

}

// ================= LOAD USER =================
function loadUser() {
    const userData = localStorage.getItem("user");

    if (!userData) {
        window.location.href = "../LOGIN/login.html";
        return;
    }

    const user = JSON.parse(userData);
    const name = user.fullName || "User";

    document.getElementById("userName").innerText = name;
    document.getElementById("welcomeText").innerText = `Welcome, ${name}!`;
}

// ================= LOGOUT =================
document.getElementById("logoutBtn")?.addEventListener("click", () => {
    document.getElementById("logoutModal").style.display = "block";
});

document.getElementById("cancelLogout")?.addEventListener("click", () => {
    document.getElementById("logoutModal").style.display = "none";
});

document.getElementById("confirmLogout")?.addEventListener("click", () => {
    localStorage.clear();
    window.location.href = "../LOGIN/login.html";
});

// ================= LOAD JOBS =================
async function loadJobs() {
    const res = await fetch("https://jsearch.p.rapidapi.com/search?query=jobs%20philippines&page=1", {
        headers: {
            "X-RapidAPI-Key": "1f0a340b56msh6a8602fdf3e910bp1d3bb2jsn0abd16958aa8",
            "X-RapidAPI-Host": "jsearch.p.rapidapi.com"
        }
    });

    const data = await res.json();
    renderJobs(data.data);
}

// ================= RENDER JOBS =================
function renderJobs(jobs) {
    const container = document.getElementById("jobContainer");
    container.innerHTML = "";

    jobs.slice(0, 20).forEach(job => {
        container.innerHTML += `
        <div class="card job-card">
            <h4>${job.employer_name}</h4>
            <p>${job.job_title}</p>
            <p>${job.job_city || "N/A"}</p>

            <div class="job-card-actions">
                <button class="btn btn-secondary"
                    onclick="openPopup('${job.job_id}','${job.job_title}','${job.employer_name}','${job.job_city}')">
                    View Details
                </button>

                <button class="btn btn-primary"
                    onclick="window.open('${job.job_apply_link}')">
                    Apply
                </button>
            </div>
        </div>`;
    });
}

// ================= POPUP WITH JSEARCH API =================
async function openPopup(jobId, title, company, location) {
    // Show popup with loading placeholders
    document.getElementById("jobPopup").style.display = "block";
    document.getElementById("popupTitle").innerText = "Loading...";
    document.getElementById("popupCompany").innerText = company;
    document.getElementById("popupLocation").innerText = location || "Not specified";
    document.getElementById("popupDescription").innerText = "Fetching job details...";
    document.getElementById("popupSalary").innerText = "Loading salary data...";

    const headers = {
        "X-RapidAPI-Key": "1f0a340b56msh6a8602fdf3e910bp1d3bb2jsn0abd16958aa8",
        "X-RapidAPI-Host": "jsearch.p.rapidapi.com",
        "Content-Type": "application/json"
    };

    try {
        // 🔹 JOB DETAILS API
        const detailsUrl = `https://jsearch.p.rapidapi.com/job-details?job_id=${encodeURIComponent(jobId)}&country=ph`;

        const detailsRes = await fetch(detailsUrl, {
            method: "GET",
            headers: headers
        });

        if (!detailsRes.ok) {
            throw new Error("Failed to fetch job details.");
        }

        const detailsData = await detailsRes.json();
        const jobDetails = detailsData?.data?.[0];

        const description =
            jobDetails?.job_description || "No description available.";

        // 🔹 SALARY ESTIMATION API
        const salaryUrl = `https://jsearch.p.rapidapi.com/estimated-salary?job_title=${encodeURIComponent(title)}&location=${encodeURIComponent(location || "Philippines")}&location_type=ANY&years_of_experience=ALL`;

        const salaryRes = await fetch(salaryUrl, {
            method: "GET",
            headers: headers
        });

        if (!salaryRes.ok) {
            throw new Error("Failed to fetch salary data.");
        }

        const salaryData = await salaryRes.json();
        const salaryInfo = salaryData?.data?.[0];

        let salaryText = "Not available";

        if (salaryInfo) {
            const min = salaryInfo.min_salary;
            const max = salaryInfo.max_salary;
            const median = salaryInfo.median_salary;
            const currency = salaryInfo.salary_currency || "PHP";
            const period = salaryInfo.salary_period || "year";

            if (min && max) {
                salaryText = `${currency} ${min.toLocaleString()} - ${max.toLocaleString()} per ${period}`;
            } else if (median) {
                salaryText = `${currency} ${median.toLocaleString()} per ${period}`;
            }
        }

        // 🔹 UPDATE POPUP CONTENT
        document.getElementById("popupTitle").innerText = title;
        document.getElementById("popupDescription").innerText =
            description.substring(0, 500) + "...";
        document.getElementById("popupSalary").innerText =
            `Estimated Salary: ${salaryText}`;

    } catch (err) {
        console.error("Error fetching job data:", err);
        document.getElementById("popupDescription").innerText =
            "Failed to load job details.";
        document.getElementById("popupSalary").innerText =
            "Salary information unavailable.";
    }
}

// ================= CLOSE POPUP =================
function closePopup() {
    document.getElementById("jobPopup").style.display = "none";
}


// ================= INIT =================
document.addEventListener("DOMContentLoaded", () => {
    loadUser();
    loadJobs();
});
// ===== API CONFIG =====
const url = "https://jsearch.p.rapidapi.com/search?query=jobs%20philippines&page=1&num_pages=2";

const options = {
    method: "GET",
    headers: {
        "X-RapidAPI-Key": "1f0a340b56msh6a8602fdf3e910bp1d3bb2jsn0abd16958aa8",
        "X-RapidAPI-Host": "jsearch.p.rapidapi.com"
    }
};

// ===== LOAD JOBS =====
async function loadJobs() {
    try {
        const cached = localStorage.getItem("jobsData");

        if (cached) {
            renderJobs(JSON.parse(cached));
            return;
        }

        const res = await fetch(url, options);
        const data = await res.json();

        localStorage.setItem("jobsData", JSON.stringify(data.data));
        renderJobs(data.data);

    } catch (e) {
        console.error("Error loading jobs:", e);
    }
}

// ===== RENDER JOBS =====
function renderJobs(jobs) {
    const container = document.getElementById("jobContainer");
    container.innerHTML = "";

    jobs.slice(0, 20).forEach(job => {

        let logo = job.employer_logo;

        const logoHTML = logo
            ? `<img src="${logo}" style="width:100%;height:100%;object-fit:cover;">`
            : `<div>🏢</div>`;

        container.innerHTML += `
            <div class="card job-card">
                <div class="job-card-top">
                    <div class="company-logo-placeholder">${logoHTML}</div>
                    <div class="job-info">
                        <h4>${job.employer_name}</h4>
                        <p>${job.job_title}</p>
                        <p>Location: ${job.job_city || "N/A"}</p>
                    </div>
                </div>

                <div class="job-card-actions">
                    <button class="btn btn-secondary"
                        onclick="openPopup('${job.job_title}','${job.employer_name}','${job.job_city}','${(job.job_description || "").substring(0,120)}')">
                        View Details
                    </button>

                    <button class="btn btn-primary"
                        onclick="window.open('${job.job_apply_link}')">
                        Apply
                    </button>
                </div>
            </div>
        `;
    });
}

// ===== POPUP =====
function openPopup(title, company, location, description) {
    document.getElementById("popupTitle").innerText = title;
    document.getElementById("popupCompany").innerText = company;
    document.getElementById("popupLocation").innerText = location;
    document.getElementById("popupDescription").innerText = description;

    document.getElementById("jobPopup").style.display = "block";
}

function closePopup() {
    document.getElementById("jobPopup").style.display = "none";
}

// ===== SCROLL BUTTONS (FIXED) =====
document.addEventListener("DOMContentLoaded", () => {
    const wrapper = document.getElementById("scrollWrapper");
    const leftBtn = document.querySelector(".scroll-btn.left");
    const rightBtn = document.querySelector(".scroll-btn.right");

    if (!wrapper || !leftBtn || !rightBtn) {
        console.error("Scroll elements not found!");
        return;
    }

    leftBtn.addEventListener("click", () => {
        wrapper.scrollLeft -= 350;
    });

    rightBtn.addEventListener("click", () => {
        wrapper.scrollLeft += 350;
    });
});

// ===== INIT =====
loadJobs();
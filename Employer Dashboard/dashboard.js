document.addEventListener("DOMContentLoaded", () => {
    let user = null;
    let demoMode = false;

    try {
        user = JSON.parse(localStorage.getItem("user"));
    }
    catch {
        user = null;
    }

    if (!user || user.role !== "employer") {
        demoMode = true;
        user = {
            fullName: "Employer Demo"
        };
    }

    const welcomeText = document.getElementById("welcomeText");
    if (welcomeText) {
        welcomeText.textContent = `Welcome, ${user.fullName}!`;
    }

    if (demoMode) {
        const demoBanner = document.getElementById("demoBanner");
        if (demoBanner) {
            demoBanner.style.display = "inline-block";
        }
    }

    document.getElementById("activeJobs").textContent = "3";
    document.getElementById("totalApplicants").textContent = "27";
    document.getElementById("openInterviews").textContent = "5";
    renderPastListings();
});

const jobForm = document.getElementById("jobForm");
const applicantList = document.getElementById("applicantList");
const pastListingContainer = document.getElementById("pastListingContainer");

let demoPastListings = [
    {
        title: "Junior WordPress Developer",
        location: "Remote",
        status: "Closed",
        posted: "2 weeks ago"
    },
    {
        title: "Senior Project Engineer",
        location: "On-Site",
        status: "Filled",
        posted: "1 month ago"
    },
    {
        title: "Pastry Chef",
        location: "Downtown Kitchen",
        status: "Closed",
        posted: "3 weeks ago"
    }
];

function renderPastListings() {
    if (!pastListingContainer) {
        return;
    }

    if (demoPastListings.length === 0) {
        pastListingContainer.innerHTML = '<p>No past listings available.</p>';
        return;
    }

    pastListingContainer.innerHTML = demoPastListings.map((listing, index) => `
        <div class="job-card">
            <h3>${listing.title}</h3>
            <p>${listing.location}</p>
            <span class="job-status">${listing.status} · ${listing.posted}</span>
            <button class="btn btn-secondary btn-toggle-status" data-index="${index}">
                ${listing.status === 'Open' ? 'Close Listing' : 'Reopen Listing'}
            </button>
        </div>
    `).join('');
}

pastListingContainer?.addEventListener('click', (event) => {
    const button = event.target.closest('.btn-toggle-status');
    if (!button) {
        return;
    }

    const index = Number(button.dataset.index);
    if (Number.isNaN(index) || !demoPastListings[index]) {
        return;
    }

    const listing = demoPastListings[index];
    if (listing.status === 'Open' || listing.status === 'Published') {
        listing.status = 'Closed';
    } else {
        listing.status = 'Open';
    }

    renderPastListings();
});

if (jobForm) {
    jobForm.addEventListener("submit", (e) => {
        e.preventDefault();

        const title = document.getElementById("jobTitle").value.trim();
        const location = document.getElementById("jobLocation").value.trim();
        const description = document.getElementById("jobDescription").value.trim();

        if (!title || !location) {
            alert("Please enter a job title and location.");
            return;
        }

        const jobCard = document.createElement("div");
        jobCard.className = "job-card";
        jobCard.innerHTML = `
            <h3>${title}</h3>
            <p><strong>Location:</strong> ${location}</p>
            <p>${description || "No description provided."}</p>
            <span class="job-status">Published</span>
        `;

        applicantList.prepend(jobCard);

        demoPastListings.unshift({
            title,
            location,
            status: "Open",
            posted: "Just now"
        });
        renderPastListings();

        document.getElementById("jobTitle").value = "";
        document.getElementById("jobLocation").value = "";
        document.getElementById("jobDescription").value = "";

        document.getElementById("activeJobs").textContent =
            parseInt(document.getElementById("activeJobs").textContent || "0") + 1;
    });
}

const logoutBtn = document.getElementById("logoutBtn");
const logoutModal = document.getElementById("logoutModal");
const cancelLogout = document.getElementById("cancelLogout");
const confirmLogout = document.getElementById("confirmLogout");

logoutBtn?.addEventListener("click", () => {
    logoutModal.style.display = "flex";
});

cancelLogout?.addEventListener("click", () => {
    logoutModal.style.display = "none";
});

confirmLogout?.addEventListener("click", () => {
    localStorage.removeItem("user");
    localStorage.removeItem("token");
    window.location.href = "../LOGIN/login.html";
});
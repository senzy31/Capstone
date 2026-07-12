document.querySelectorAll("a[href^='#']").forEach(anchor => {
    anchor.addEventListener("click", function(e) {
        e.preventDefault();

        document.querySelector(this.getAttribute("href"))
            .scrollIntoView({
                behavior: "smooth"
            });
    });
});

window.addEventListener("scroll", () => {

    const navbar = document.querySelector(".navbar");

    if(window.scrollY > 50){
        navbar.style.background = "#ffffff";
        navbar.style.boxShadow = "0 5px 15px rgba(0,0,0,.1)";
    }
    else{
        navbar.style.boxShadow = "0 2px 10px rgba(0,0,0,.08)";
    }
});

const allJobs = [
    { title: 'Frontend Developer', company: 'Google', type: 'Remote', category: 'IT' },
    { title: 'Backend Developer', company: 'Microsoft', type: 'Hybrid', category: 'IT' },
    { title: 'UI/UX Designer', company: 'Meta', type: 'Remote', category: 'Design' },
    { title: 'Electrical Engineer', company: 'Siemens', type: 'On-Site', category: 'Engineering' },
    { title: 'Chef de Partie', company: 'Le Gourmet', type: 'Full-time', category: 'Chef' },
    { title: 'Civil Engineer', company: 'BuildPro', type: 'On-Site', category: 'Engineering' },
    { title: 'Database Administrator', company: 'Oracle', type: 'Remote', category: 'IT' },
    { title: 'Pastry Chef', company: 'SweetBakes', type: 'Part-time', category: 'Chef' }
];

const jobContainer = document.getElementById('jobContainer');
const filterBtn = document.getElementById('filterBtn');
const filterModal = document.getElementById('filterModal');
const closeFilter = document.getElementById('closeFilter');
const jobSearchInput = document.getElementById('jobSearchInput');
const jobsSubtitle = document.getElementById('jobsSubtitle');
const startNowBtn = document.getElementById('startNowBtn');
let currentCategory = 'All';

function renderJobs(category = 'All', searchTerm = '') {
    const normalizedSearch = searchTerm.trim().toLowerCase();

    const jobs = allJobs.filter(job => {
        const categoryMatch = category === 'All' || job.category === category;
        const textMatch = normalizedSearch === '' || [job.title, job.company, job.category, job.type]
            .some(value => value.toLowerCase().includes(normalizedSearch));

        return categoryMatch && textMatch;
    });

    jobsSubtitle.textContent = category === 'All'
        ? (normalizedSearch === ''
            ? 'Explore jobs in different categories.'
            : `Search results for "${searchTerm}".`)
        : `Showing ${category} jobs${normalizedSearch ? ` matching "${searchTerm}"` : ''}.`;

    if (!jobContainer) {
        return;
    }

    if (jobs.length === 0) {
        jobContainer.innerHTML = '<p class="no-jobs">No jobs found for this search.</p>';
        return;
    }

    jobContainer.innerHTML = jobs.map(job => `
        <div class="job-card">
            <h3>${job.title}</h3>
            <p>${job.company}</p>
            <span>${job.type}</span>
            <button class="btn btn-primary">Apply Now</button>
        </div>
    `).join('');
}

function openFilter() {
    if (filterModal) {
        filterModal.style.display = 'flex';
    }
}

function closeFilterModal() {
    if (filterModal) {
        filterModal.style.display = 'none';
    }
}

function setFilterCategory(category) {
    currentCategory = category;
    document.querySelectorAll('.filter-chip').forEach(button => {
        button.classList.toggle('active', button.dataset.category === category);
    });
    renderJobs(currentCategory, jobSearchInput?.value || '');
    closeFilterModal();
}

filterBtn?.addEventListener('click', openFilter);
closeFilter?.addEventListener('click', closeFilterModal);
filterModal?.addEventListener('click', event => {
    if (event.target === filterModal) {
        closeFilterModal();
    }
});
document.querySelectorAll('.filter-chip').forEach(button => {
    button.addEventListener('click', () => {
        setFilterCategory(button.dataset.category);
    });
});
jobSearchInput?.addEventListener('input', () => {
    renderJobs(currentCategory, jobSearchInput.value);
});
jobSearchInput?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
        event.preventDefault();
        renderJobs(currentCategory, jobSearchInput.value);
        closeFilterModal();
    }
});
document.getElementById('searchFilter')?.addEventListener('click', () => {
    renderJobs(currentCategory, jobSearchInput?.value || '');
    closeFilterModal();
});
startNowBtn?.addEventListener('click', () => {
    document.getElementById('home')?.scrollIntoView({ behavior: 'smooth' });
});

document.addEventListener('DOMContentLoaded', () => {
    renderJobs(currentCategory, jobSearchInput?.value || '');
});


// ================= LOGIN BUTTON =================

document.getElementById("loginBtn")
?.addEventListener("click", () => {

    window.location.href =
        "../LOGIN/login.html";

});


// ================= REGISTER BUTTON =================

document.getElementById("registerBtn")
?.addEventListener("click", () => {

    window.location.href =
        "../LOGIN/signup.html";

});
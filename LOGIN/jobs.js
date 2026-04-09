const jobsGrid = document.getElementById("jobsGrid");

async function fetchJobs() {
  try {
    const res = await fetch(
      "https://jsearch.p.rapidapi.com/search?query=developer%20jobs%20in%20philippines&page=1&num_pages=1",
      {
        headers: {
          "X-RapidAPI-Key": "1f0a340b56msh6a8602fdf3e910bp1d3bb2jsn0abd16958aa8",
          "X-RapidAPI-Host": "jsearch.p.rapidapi.com"
        }
      }
    );

    const data = await res.json();
    displayJobs(data.data || []);

  } catch (err) {
    jobsGrid.innerHTML = "Error loading jobs";
  }
}

function displayJobs(jobs) {
  if (!jobs.length) {
    jobsGrid.innerHTML = "No jobs found";
    return;
  }

  jobsGrid.innerHTML = jobs.map(job => `
    <div class="job-card">
      <h4>${job.job_title}</h4>
      <p>${job.employer_name}</p>
      <p>${job.job_city || "N/A"}</p>
      <a href="#" onclick="goToLogin(event)">Apply</a>
    </div>
  `).join("");
}

/* 🔥 AUTO SLIDE */
setInterval(() => {
  jobsGrid.scrollBy({
    left: 300,
    behavior: "smooth"
  });
}, 60000); // 1 minute

function goToLogin(e) {
  e.preventDefault();
  document.getElementById("loginSection").scrollIntoView({
    behavior: "smooth"
  });
} 

fetchJobs();
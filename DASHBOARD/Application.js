/* ==========================================
   JOBLINK APPLICATION PAGE
========================================== */

document.addEventListener("DOMContentLoaded", () => {

    /* ======================================
       APPLICATION DATA
    ======================================= */

    let applications = [
        {
            company: "Netflix",
            position: "Data Engineer",
            arrangement: "Remote",
            status: "active",
            logo: "N",
            logoClass: "netflix"
        },

        {
            company: "Grab",
            position: "Front end Engineer",
            arrangement: "Remote",
            status: "active",
            logo: "G",
            logoClass: "grab"
        },

        {
            company: "Lazada",
            position: "Front end Engineer",
            arrangement: "Remote",
            status: "review",
            logo: "L",
            logoClass: "lazada"
        },

        {
            company: "Shopee",
            position: "Front end Engineer",
            arrangement: "Remote",
            status: "review",
            logo: "S",
            logoClass: "shopee"
        }
    ];


    /* ======================================
       ELEMENTS
    ======================================= */

    const applicationsList =
        document.getElementById("applicationsList");

    const totalApplied =
        document.getElementById("totalApplied");

    const activeApplications =
        document.getElementById("activeApplications");

    const underReview =
        document.getElementById("underReview");

    const filterButtons =
        document.querySelectorAll(".filter-btn");

    const searchInput =
        document.getElementById("searchInput");

    const navUser =
        document.getElementById("navUser");


    /* ======================================
       LOAD USER
    ======================================= */

    loadUser();


    /* ======================================
       INITIAL RENDER
    ======================================= */

    updateStatistics();

    renderApplications("all");


    /* ======================================
       FILTER BUTTONS
    ======================================= */

    filterButtons.forEach(button => {

        button.addEventListener("click", () => {

            filterButtons.forEach(btn => {
                btn.classList.remove("active");
            });

            button.classList.add("active");

            const status =
                button.dataset.status;

            renderApplications(status);

        });

    });


    /* ======================================
       SEARCH
    ======================================= */

    searchInput.addEventListener("input", () => {

        const currentFilter =
            document
                .querySelector(".filter-btn.active")
                ?.dataset.status || "all";

        renderApplications(currentFilter);

    });


    /* ======================================
       LOGOUT
    ======================================= */

    const logoutBtn =
        document.getElementById("logoutBtn");

    const logoutOverlay =
        document.getElementById("logoutOverlay");

    const cancelLogout =
        document.getElementById("cancelLogout");

    const confirmLogout =
        document.getElementById("confirmLogout");


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

        window.location.href = "login.html";

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

        const savedUser =
            localStorage.getItem("user");

        if (!savedUser) {
            return;
        }

        try {

            const user =
                JSON.parse(savedUser);

            const name =
                user.fullName ||
                user.full_name ||
                user.name ||
                "User";

            navUser.textContent =
                getInitials(name);

        } catch (error) {

            console.error(
                "Unable to load user:",
                error
            );

        }

    }


    /* ======================================
       GET INITIALS
    ======================================= */

    function getInitials(name) {

        if (!name) {
            return "U";
        }

        const parts =
            name.trim().split(/\s+/);

        if (parts.length >= 2) {

            return (
                parts[0].charAt(0) +
                parts[parts.length - 1].charAt(0)
            );

        }

        return parts[0]
            .charAt(0)
            .toUpperCase();

    }


    /* ======================================
       UPDATE STATISTICS
    ======================================= */

    function updateStatistics() {

        /*
         * Total Applied
         */
        totalApplied.textContent =
            applications.length;


        /*
         * Active
         */
        const active =
            applications.filter(
                application =>
                    application.status === "active"
            ).length;

        activeApplications.textContent =
            active;


        /*
         * Under Review
         */
        const review =
            applications.filter(
                application =>
                    application.status === "review"
            ).length;

        underReview.textContent =
            review;

    }


    /* ======================================
       RENDER APPLICATIONS
    ======================================= */

    function renderApplications(filter) {

        const searchTerm =
            searchInput.value
                .trim()
                .toLowerCase();


        let filtered =
            applications.filter(application => {

                /*
                 * Status filter
                 */
                const statusMatches =
                    filter === "all" ||
                    application.status === filter;


                /*
                 * Search filter
                 */
                const searchMatches =
                    !searchTerm ||
                    application.company
                        .toLowerCase()
                        .includes(searchTerm) ||

                    application.position
                        .toLowerCase()
                        .includes(searchTerm);


                return (
                    statusMatches &&
                    searchMatches
                );

            });


        applicationsList.innerHTML = "";


        /* ==================================
           NO RESULTS
        =================================== */

        if (filtered.length === 0) {

            applicationsList.innerHTML = `
                <div class="no-results">
                    No applications found.
                </div>
            `;

            return;

        }


        /* ==================================
           CREATE APPLICATION CARDS
        =================================== */

        filtered.forEach(application => {

            const card =
                document.createElement("div");

            card.className =
                "application-card";


            card.innerHTML = `

                <div class="company-logo ${application.logoClass}">
                    ${escapeHTML(application.logo)}
                </div>

                <div class="application-info">

                    <div class="company-row">

                        <h3>
                            ${escapeHTML(application.company)}
                        </h3>

                        <span class="status-badge">
                            Applied
                        </span>

                    </div>

                    <div class="job-details">

                        <span>
                            ${escapeHTML(application.position)}
                        </span>

                        <span class="separator">
                            •
                        </span>

                        <span>
                            ${escapeHTML(application.arrangement)}
                        </span>

                    </div>

                </div>

            `;


            /* ==================================
               CARD CLICK
            =================================== */

            card.addEventListener("click", () => {

                showApplicationDetails(
                    application
                );

            });


            applicationsList.appendChild(card);

        });

    }


    /* ======================================
       APPLICATION DETAILS
    ======================================= */

    function showApplicationDetails(application) {

        alert(
            `Company: ${application.company}\n` +
            `Position: ${application.position}\n` +
            `Work Arrangement: ${application.arrangement}\n` +
            `Status: Applied`
        );

    }


    /* ======================================
       ESCAPE HTML
    ======================================= */

    function escapeHTML(value) {

        const div =
            document.createElement("div");

        div.textContent =
            value;

        return div.innerHTML;

    }

});
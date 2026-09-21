/* =========================================
   JOBLINK PROFILE
   Connected to the ASP.NET backend for
   Full Name / Email (Users table) and
   Phone / Location (Profiles table).

   Role, Industry, Experience, Availability,
   Connections, Profile Score and Skills have
   no matching columns in the database yet, so
   they stay saved locally per-user for now.
========================================= */

document.addEventListener("DOMContentLoaded", () => {

    const API_BASE = "https://localhost:7142/api";


    /* =====================================
       WHO'S LOGGED IN?
    ===================================== */

    const savedUser = localStorage.getItem("user");

    if (!savedUser) {

        window.location.href = "../LOGIN/login.html";

        return;

    }

    let currentUser;

    try {

        currentUser = JSON.parse(savedUser);

    } catch (error) {

        console.error("Unable to read saved user:", error);

        window.location.href = "../LOGIN/login.html";

        return;

    }

    const userId = currentUser.userId || currentUser.user_id;

    if (!userId) {

        window.location.href = "../LOGIN/login.html";

        return;

    }


    /* =====================================
       LOCAL-ONLY EXTRAS
       (no backend column yet - kept per-user)
    ===================================== */

    const LOCAL_KEY = `joblinkProfileExtras_${userId}`;

    const defaultExtras = {
        role: "",
        industry: "",
        experience: "",
        availability: "",
        connections: 0,
        profileScore: 0,
        skills: []
    };


    /* =====================================
       STATE
       Filled in by loadProfile() once the
       real user + profile records are back.
    ===================================== */

    let userRecord = null;      // your account from the API (name, email, role, company - never a password hash)
    let profileRecord = null;   // full ProfileModel from the API, or null if none yet
    let extras = loadExtras();

    let profile = {
        fullName: currentUser.fullName || "",
        email: currentUser.email || "",
        phone: "",
        location: "",
        ...extras
    };


    /* =====================================
       ELEMENTS
    ===================================== */

    const editProfileBtn = document.getElementById("editProfileBtn");
    const saveBtn = document.getElementById("saveBtn");
    const cancelBtn = document.getElementById("cancelBtn");
    const addSkillBtn = document.getElementById("addSkillBtn");
    const skillsContainer = document.getElementById("skillsContainer");
    const skillModal = document.getElementById("skillModal");
    const newSkillInput = document.getElementById("newSkillInput");
    const skillAddBtn = document.getElementById("skillAddBtn");
    const skillCancelBtn = document.getElementById("skillCancelBtn");
    const navUser = document.getElementById("navUser");


    /* =====================================
       INPUTS
    ===================================== */

    const inputs = {
        fullName: document.getElementById("fullNameInput"),
        email: document.getElementById("emailInput"),
        phone: document.getElementById("phoneInput"),
        location: document.getElementById("locationInput"),
        role: document.getElementById("roleInput"),
        industry: document.getElementById("industryInput"),
        experience: document.getElementById("experienceInput"),
        availability: document.getElementById("availabilityInput")
    };


    /* =====================================
       DISPLAY ELEMENTS
    ===================================== */

    const displays = {
        fullName: document.getElementById("fullNameDisplay"),
        email: document.getElementById("emailDisplay"),
        phone: document.getElementById("phoneDisplay"),
        location: document.getElementById("locationDisplay"),
        role: document.getElementById("roleDisplay"),
        industry: document.getElementById("industryDisplay"),
        experience: document.getElementById("experienceDisplay"),
        availability: document.getElementById("availabilityDisplay")
    };


    /* =====================================
       SUMMARY ELEMENTS
    ===================================== */

    const summaryName = document.getElementById("summaryName");
    const summaryRole = document.getElementById("summaryRole");
    const summaryExperience = document.getElementById("summaryExperience");
    const summaryConnections = document.getElementById("summaryConnections");
    const summaryScore = document.getElementById("summaryScore");
    const profileAvatar = document.getElementById("profileAvatar");


    /* =====================================
       JOB PREFERENCE ELEMENTS
    ===================================== */

    const prefInputs = {
        location: document.getElementById("prefLocation"),
        arrangement: document.getElementById("prefArrangement"),
        minSalary: document.getElementById("prefMinSalary"),
        maxSalary: document.getElementById("prefMaxSalary")
    };

    const prefSaveBtn = document.getElementById("prefSaveBtn");
    const prefStatus = document.getElementById("prefStatus");


    /* =====================================
       INITIAL LOAD
    ===================================== */

    renderProfile();

    loadProfile();

    loadPreferences();


    /* =====================================
       SAVE JOB PREFERENCES
    ===================================== */

    prefSaveBtn.addEventListener("click", () => {

        savePreferences();

    });


    /* =====================================
       EDIT PROFILE
    ===================================== */

    editProfileBtn.addEventListener("click", () => {

        enterEditMode();

    });


    /* =====================================
       SAVE PROFILE
    ===================================== */

    saveBtn.addEventListener("click", () => {

        saveProfile();

    });


    /* =====================================
       CANCEL EDITING
    ===================================== */

    cancelBtn.addEventListener("click", () => {

        cancelEditing();

    });


    /* =====================================
       ADD SKILL
    ===================================== */

    addSkillBtn.addEventListener("click", () => {

        newSkillInput.value = "";

        skillModal.classList.add("show");

        setTimeout(() => {
            newSkillInput.focus();
        }, 100);

    });


    /* =====================================
       CANCEL SKILL MODAL
    ===================================== */

    skillCancelBtn.addEventListener("click", () => {

        closeSkillModal();

    });


    /* =====================================
       ADD NEW SKILL
    ===================================== */

    skillAddBtn.addEventListener("click", () => {

        addSkill();

    });


    /* =====================================
       ENTER KEY FOR SKILL
    ===================================== */

    newSkillInput.addEventListener("keydown", (event) => {

        if (event.key === "Enter") {

            event.preventDefault();

            addSkill();

        }

        if (event.key === "Escape") {

            closeSkillModal();

        }

    });


    /* =====================================
       CLICK OUTSIDE MODAL
    ===================================== */

    skillModal.addEventListener("click", (event) => {

        if (event.target === skillModal) {

            closeSkillModal();

        }

    });


    /* =====================================
       LOGOUT
       SAME PATTERN AS APPLICATION / RESUME BUILDER
    ===================================== */

    const logoutBtn = document.getElementById("logoutBtn");
    const logoutConfirmOverlay = document.getElementById("logoutConfirmOverlay");
    const cancelLogout = document.getElementById("cancelLogout");
    const confirmLogout = document.getElementById("confirmLogout");

    logoutBtn.addEventListener("click", (event) => {

        event.preventDefault();

        logoutConfirmOverlay.classList.add("show");

    });

    cancelLogout.addEventListener("click", () => {

        logoutConfirmOverlay.classList.remove("show");

    });

    confirmLogout.addEventListener("click", () => {

        localStorage.removeItem("user");
        localStorage.removeItem("token");

        window.location.href = "../LOGIN/login.html";

    });

    logoutConfirmOverlay.addEventListener("click", (event) => {

        if (event.target === logoutConfirmOverlay) {

            logoutConfirmOverlay.classList.remove("show");

        }

    });


    /* =====================================
       LOAD PROFILE FROM THE BACKEND
    ===================================== */

    async function loadProfile() {

        try {

            const userResponse = await ApiClient.authFetch(`${API_BASE}/User/${userId}`);

            if (!userResponse.ok) {
                throw new Error(`Failed to load user (${userResponse.status})`);
            }

            userRecord = await userResponse.json();


            const profileResponse = await fetch(`${API_BASE}/Profile/by-user/${userId}`);

            if (profileResponse.ok) {

                profileRecord = await profileResponse.json();

            } else if (profileResponse.status === 404) {

                profileRecord = null;

            } else {

                throw new Error(`Failed to load profile (${profileResponse.status})`);

            }


            profile = {
                fullName: userRecord.fullName || "",
                email: userRecord.email || "",
                phone: profileRecord?.phone || "",
                location: profileRecord?.address || "",
                ...extras
            };


            /* Keep the saved user snapshot (navbar/greeting) fresh */

            localStorage.setItem("user", JSON.stringify({
                ...currentUser,
                fullName: profile.fullName,
                email: profile.email
            }));


            renderProfile();

        } catch (error) {

            console.error("Unable to load profile from server:", error);

            showToast(
                "Couldn't reach the server - showing your last saved info. Make sure the API is running.",
                "error"
            );

        }

    }


    /* =====================================
       RENDER PROFILE
    ===================================== */

    function renderProfile() {

        /* Inputs */

        inputs.fullName.value = profile.fullName;
        inputs.email.value = profile.email;
        inputs.phone.value = profile.phone;
        inputs.location.value = profile.location;
        inputs.role.value = profile.role;
        inputs.industry.value = profile.industry;
        inputs.experience.value = profile.experience;
        inputs.availability.value = profile.availability;


        /* Displays */

        displays.fullName.textContent = profile.fullName || "-";
        displays.email.textContent = profile.email || "-";
        displays.phone.textContent = profile.phone || "Not set";
        displays.location.textContent = profile.location || "Not set";
        displays.role.textContent = profile.role || "Not set";
        displays.industry.textContent = profile.industry || "Not set";
        displays.experience.textContent = profile.experience || "Not set";
        displays.availability.textContent = profile.availability || "Not set";


        /* Summary */

        summaryName.textContent = profile.fullName || "Unnamed User";

        summaryRole.textContent =
            [profile.role, profile.location].filter(Boolean).join(" - ") || "Complete your profile";

        summaryExperience.textContent =
            profile.experience ? `${profile.experience} Experience` : "Experience not set";

        summaryConnections.textContent = `${profile.connections} Connections`;

        summaryScore.textContent = `${profile.profileScore}% profile score`;


        /* Avatar */

        profileAvatar.textContent = getInitial(profile.fullName);


        /* Navbar */

        updateNavbar();


        /* Skills */

        renderSkills();

    }


    /* =====================================
       UPDATE NAVBAR
    ===================================== */

    function updateNavbar() {

        const name = profile.fullName || "User";

        const parts = name.trim().split(/\s+/);

        let initials = "";

        if (parts.length >= 2) {

            initials = parts[0].charAt(0) + parts[parts.length - 1].charAt(0);

        } else {

            initials = parts[0]?.charAt(0) || "U";

        }

        navUser.textContent = initials;

    }


    /* =====================================
       GET INITIAL
    ===================================== */

    function getInitial(name) {

        if (!name) {
            return "U";
        }

        return name.trim().charAt(0).toUpperCase();

    }


    /* =====================================
       ENTER EDIT MODE
    ===================================== */

    function enterEditMode() {

        document.body.classList.add("editing");

        editProfileBtn.innerHTML = '<i class="fa-solid fa-pen"></i> Editing Profile';

        editProfileBtn.disabled = true;

    }


    /* =====================================
       SAVE PROFILE
       PUTs the User record (full name/email)
       and creates-or-updates the Profile
       record (phone/address) on the backend.
    ===================================== */

    async function saveProfile() {

        const updatedProfile = {

            ...profile,

            fullName: inputs.fullName.value.trim(),
            email: inputs.email.value.trim(),
            phone: inputs.phone.value.trim(),
            location: inputs.location.value.trim(),
            role: inputs.role.value.trim(),
            industry: inputs.industry.value.trim(),
            experience: inputs.experience.value.trim(),
            availability: inputs.availability.value.trim()

        };


        if (!updatedProfile.fullName) {

            alert("Please enter your full name.");

            inputs.fullName.focus();

            return;

        }


        if (!updatedProfile.email) {

            alert("Please enter your email.");

            inputs.email.focus();

            return;

        }


        saveBtn.disabled = true;

        saveBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Saving...';

        try {

            await saveUserRecord(updatedProfile);

            await saveProfileRecord(updatedProfile);


            /* Local-only extras always save, even if the API call above failed */

            extras = {
                role: updatedProfile.role,
                industry: updatedProfile.industry,
                experience: updatedProfile.experience,
                availability: updatedProfile.availability,
                connections: profile.connections,
                profileScore: profile.profileScore,
                skills: profile.skills
            };

            saveExtras();


            profile = updatedProfile;


            localStorage.setItem("user", JSON.stringify({
                ...currentUser,
                fullName: profile.fullName,
                email: profile.email
            }));


            exitEditMode();

            renderProfile();

            showToast("Profile updated successfully!", "success");

        } catch (error) {

            console.error("Unable to save profile:", error);

            /* The server (or the password dialog) explained itself - say that. */

            showToast(
                error.cancelled || error.fromServer
                    ? error.message
                    : "Couldn't save to the server. Check that the API is running and try again.",
                "error"
            );

        } finally {

            saveBtn.disabled = false;

            saveBtn.innerHTML = '<i class="fa-solid fa-check"></i> Save Changes';

        }

    }


    /* Only name and email go up. The server knows who you are from your login,
       and asks for your current password if the email is changing. */

    async function saveUserRecord(updatedProfile) {

        userRecord = await ApiClient.saveAccount({
            fullName: updatedProfile.fullName,
            email: updatedProfile.email
        });

    }


    async function saveProfileRecord(updatedProfile) {

        const basePayload = {
            userId: userId,
            phone: updatedProfile.phone,
            address: updatedProfile.location,
            linkedinUrl: profileRecord?.linkedinUrl || "",
            githubUrl: profileRecord?.githubUrl || "",
            isDeleted: false
        };

        if (profileRecord?.profileId) {

            const response = await fetch(`${API_BASE}/Profile`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ...basePayload, profileId: profileRecord.profileId })
            });

            if (!response.ok) {
                throw new Error(`Profile update failed (${response.status})`);
            }

            profileRecord = { ...profileRecord, ...basePayload };

            return;

        }


        const createResponse = await fetch(`${API_BASE}/Profile`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(basePayload)
        });

        if (!createResponse.ok) {
            throw new Error(`Profile create failed (${createResponse.status})`);
        }


        /* Add() only returns true/false, not the new row - fetch it back for its id */

        const refetch = await fetch(`${API_BASE}/Profile/by-user/${userId}`);

        if (refetch.ok) {
            profileRecord = await refetch.json();
        }

    }


    /* =====================================
       CANCEL EDITING
    ===================================== */

    function cancelEditing() {

        renderProfile();

        exitEditMode();

    }


    function exitEditMode() {

        document.body.classList.remove("editing");

        editProfileBtn.innerHTML = '<i class="fa-solid fa-pen"></i> Edit Profile';

        editProfileBtn.disabled = false;

    }


    /* =====================================
       LOCAL EXTRAS PERSISTENCE
    ===================================== */

    function loadExtras() {

        const saved = localStorage.getItem(LOCAL_KEY);

        if (!saved) {
            return { ...defaultExtras };
        }

        try {

            const parsed = JSON.parse(saved);

            return {
                ...defaultExtras,
                ...parsed,
                skills: Array.isArray(parsed.skills) ? parsed.skills : []
            };

        } catch (error) {

            console.error("Unable to load local profile extras:", error);

            return { ...defaultExtras };

        }

    }


    function saveExtras() {

        localStorage.setItem(LOCAL_KEY, JSON.stringify(extras));

    }


    /* =====================================
       RENDER SKILLS
    ===================================== */

    function renderSkills() {

        skillsContainer.innerHTML = "";


        profile.skills.forEach((skill, index) => {

            const skillElement = document.createElement("div");

            skillElement.className = "skill";

            skillElement.innerHTML = `

                <span>
                    ${escapeHTML(skill)}
                </span>

                <button
                    class="remove-skill"
                    type="button"
                    data-index="${index}"
                    title="Remove skill"
                >
                    ×
                </button>

            `;

            skillsContainer.appendChild(skillElement);

        });


        document.querySelectorAll(".remove-skill").forEach(button => {

            button.addEventListener("click", () => {

                const index = Number(button.dataset.index);

                removeSkill(index);

            });

        });

    }


    /* =====================================
       ADD SKILL
    ===================================== */

    function addSkill() {

        const skill = newSkillInput.value.trim();


        if (!skill) {

            alert("Please enter a skill.");

            newSkillInput.focus();

            return;

        }


        const exists = profile.skills.some(
            existingSkill => existingSkill.toLowerCase() === skill.toLowerCase()
        );


        if (exists) {

            alert("This skill is already in your profile.");

            return;

        }


        profile.skills.push(skill);

        extras.skills = profile.skills;

        saveExtras();


        renderSkills();

        closeSkillModal();

    }


    /* =====================================
       REMOVE SKILL
    ===================================== */

    function removeSkill(index) {

        if (index < 0 || index >= profile.skills.length) {
            return;
        }


        profile.skills.splice(index, 1);

        extras.skills = profile.skills;

        saveExtras();


        renderSkills();

    }


    /* =====================================
       CLOSE MODAL
    ===================================== */

    function closeSkillModal() {

        skillModal.classList.remove("show");

        newSkillInput.value = "";

    }


    /* =====================================
       TOAST
    ===================================== */

    function showToast(text, type = "success") {

        const message = document.createElement("div");

        message.textContent = text;

        message.style.position = "fixed";
        message.style.right = "25px";
        message.style.bottom = "25px";
        message.style.maxWidth = "320px";
        message.style.padding = "13px 20px";
        message.style.background = type === "error" ? "#c0392b" : "#1f8f55";
        message.style.color = "white";
        message.style.borderRadius = "7px";
        message.style.fontSize = "13px";
        message.style.fontWeight = "600";
        message.style.zIndex = "5000";
        message.style.boxShadow = "0 4px 12px rgba(0,0,0,0.2)";

        document.body.appendChild(message);


        setTimeout(() => {

            message.remove();

        }, type === "error" ? 4000 : 2500);

    }


    /* =====================================
       JOB PREFERENCES
       Saved through their own endpoint, independent of the profile form
       above. The dashboard scores recommended jobs against them.
    ===================================== */

    function setPrefStatus(text, isError = false) {

        prefStatus.textContent = text;

        prefStatus.classList.toggle("error", isError);

    }


    async function loadPreferences() {

        try {

            const response = await fetch(`${API_BASE}/JobPreference/by-user/${userId}`);

            /* 404 = nothing saved yet - leave the form empty */

            if (response.status === 404) {
                return;
            }

            if (!response.ok) {
                throw new Error(`Failed to load preferences (${response.status})`);
            }

            const saved = await response.json();

            prefInputs.location.value = saved.preferredLocation || "";
            prefInputs.arrangement.value = saved.workArrangement || "";
            prefInputs.minSalary.value = saved.minSalary ?? "";
            prefInputs.maxSalary.value = saved.maxSalary ?? "";

        } catch (error) {

            console.error("Unable to load job preferences:", error);

            setPrefStatus("Couldn't load your saved preferences.", true);

        }

    }


    async function savePreferences() {

        const minText = prefInputs.minSalary.value.trim();
        const maxText = prefInputs.maxSalary.value.trim();

        const minSalary = minText === "" ? null : Number(minText);
        const maxSalary = maxText === "" ? null : Number(maxText);


        if (
            [minSalary, maxSalary].some(value => value !== null && (Number.isNaN(value) || value < 0))
        ) {

            setPrefStatus("Salary must be a positive number.", true);

            return;

        }

        if (minSalary !== null && maxSalary !== null && minSalary > maxSalary) {

            setPrefStatus("Minimum salary can't be higher than maximum salary.", true);

            return;

        }


        prefSaveBtn.disabled = true;

        prefSaveBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Saving...';

        setPrefStatus("");

        try {

            const response = await fetch(`${API_BASE}/JobPreference/by-user/${userId}`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    preferredLocation: prefInputs.location.value.trim(),
                    workArrangement: prefInputs.arrangement.value,
                    minSalary,
                    maxSalary
                })
            });

            if (!response.ok) {

                const errorBody = await response.json().catch(() => null);

                throw new Error(errorBody?.message || `Save failed (${response.status})`);

            }

            setPrefStatus("Saved - your dashboard matches will use these.");

            showToast("Job preferences saved!", "success");

        } catch (error) {

            console.error("Unable to save job preferences:", error);

            setPrefStatus(error.message, true);

            showToast("Couldn't save your preferences.", "error");

        } finally {

            prefSaveBtn.disabled = false;

            prefSaveBtn.innerHTML = '<i class="fa-solid fa-check"></i> Save Preferences';

        }

    }


    /* =====================================
       SECURITY
       Prevent HTML injection in skills
    ===================================== */

    function escapeHTML(value) {

        const div = document.createElement("div");

        div.textContent = value;

        return div.innerHTML;

    }

});

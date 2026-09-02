/* =========================================
   JOBLINK PROFILE
========================================= */

document.addEventListener("DOMContentLoaded", () => {

    /* =====================================
       DEFAULT PROFILE
    ===================================== */

    const defaultProfile = {
        fullName: "Sean Andrew Fuertes",
        email: "Trevormaykelfranks@gmail.com",
        phone: "09696969696",
        location: "Tanauan, Batangas - Philippines",

        role: "Software Developer",
        industry: "Technology",
        experience: "5 Years",
        availability: "Immediately",

        connections: 12,
        profileScore: 85,

        skills: [
            "React",
            "TypeScript",
            "Figma",
            "Java",
            "Python"
        ]
    };


    /* =====================================
       GET SAVED PROFILE
    ===================================== */

    let profile = getSavedProfile();


    /* =====================================
       ELEMENTS
    ===================================== */

    const editProfileBtn =
        document.getElementById("editProfileBtn");

    const saveBtn =
        document.getElementById("saveBtn");

    const cancelBtn =
        document.getElementById("cancelBtn");

    const addSkillBtn =
        document.getElementById("addSkillBtn");

    const skillsContainer =
        document.getElementById("skillsContainer");

    const skillModal =
        document.getElementById("skillModal");

    const newSkillInput =
        document.getElementById("newSkillInput");

    const skillAddBtn =
        document.getElementById("skillAddBtn");

    const skillCancelBtn =
        document.getElementById("skillCancelBtn");

    const navUser =
        document.getElementById("navUser");


    /* =====================================
       INPUTS
    ===================================== */

    const inputs = {

        fullName:
            document.getElementById("fullNameInput"),

        email:
            document.getElementById("emailInput"),

        phone:
            document.getElementById("phoneInput"),

        location:
            document.getElementById("locationInput"),

        role:
            document.getElementById("roleInput"),

        industry:
            document.getElementById("industryInput"),

        experience:
            document.getElementById("experienceInput"),

        availability:
            document.getElementById("availabilityInput")

    };


    /* =====================================
       DISPLAY ELEMENTS
    ===================================== */

    const displays = {

        fullName:
            document.getElementById("fullNameDisplay"),

        email:
            document.getElementById("emailDisplay"),

        phone:
            document.getElementById("phoneDisplay"),

        location:
            document.getElementById("locationDisplay"),

        role:
            document.getElementById("roleDisplay"),

        industry:
            document.getElementById("industryDisplay"),

        experience:
            document.getElementById("experienceDisplay"),

        availability:
            document.getElementById("availabilityDisplay")

    };


    /* =====================================
       SUMMARY ELEMENTS
    ===================================== */

    const summaryName =
        document.getElementById("summaryName");

    const summaryRole =
        document.getElementById("summaryRole");

    const summaryExperience =
        document.getElementById("summaryExperience");

    const summaryConnections =
        document.getElementById("summaryConnections");

    const summaryScore =
        document.getElementById("summaryScore");

    const profileAvatar =
        document.getElementById("profileAvatar");


    /* =====================================
       INITIAL DISPLAY
    ===================================== */

    renderProfile();


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
       FUNCTIONS
    ===================================== */


    function getSavedProfile() {

        const saved =
            localStorage.getItem("joblinkProfile");

        if (!saved) {

            return structuredClone(defaultProfile);

        }

        try {

            const parsed =
                JSON.parse(saved);

            return {
                ...structuredClone(defaultProfile),
                ...parsed,

                skills:
                    Array.isArray(parsed.skills)
                        ? parsed.skills
                        : [...defaultProfile.skills]
            };

        } catch (error) {

            console.error(
                "Unable to load profile:",
                error
            );

            return structuredClone(defaultProfile);

        }

    }


    /* =====================================
       RENDER PROFILE
    ===================================== */

    function renderProfile() {

        /* Inputs */

        inputs.fullName.value =
            profile.fullName;

        inputs.email.value =
            profile.email;

        inputs.phone.value =
            profile.phone;

        inputs.location.value =
            profile.location;

        inputs.role.value =
            profile.role;

        inputs.industry.value =
            profile.industry;

        inputs.experience.value =
            profile.experience;

        inputs.availability.value =
            profile.availability;


        /* Displays */

        displays.fullName.textContent =
            profile.fullName;

        displays.email.textContent =
            profile.email;

        displays.phone.textContent =
            profile.phone;

        displays.location.textContent =
            profile.location;

        displays.role.textContent =
            profile.role;

        displays.industry.textContent =
            profile.industry;

        displays.experience.textContent =
            profile.experience;

        displays.availability.textContent =
            profile.availability;


        /* Summary */

        summaryName.textContent =
            profile.fullName;

        summaryRole.textContent =
            `${profile.role} - ${profile.location}`;

        summaryExperience.textContent =
            `${profile.experience} Experience`;

        summaryConnections.textContent =
            `${profile.connections} Connections`;

        summaryScore.textContent =
            `${profile.profileScore}% profile score`;


        /* Avatar */

        profileAvatar.textContent =
            getInitial(profile.fullName);


        /* Navbar */

        updateNavbar();


        /* Skills */

        renderSkills();

    }


    /* =====================================
       UPDATE NAVBAR
    ===================================== */

    function updateNavbar() {

        const name =
            profile.fullName || "User";

        const parts =
            name.trim().split(/\s+/);

        let initials = "";

        if (parts.length >= 2) {

            initials =
                parts[0].charAt(0) +
                parts[parts.length - 1].charAt(0);

        } else {

            initials =
                parts[0]?.charAt(0) || "U";

        }

        navUser.textContent =
            initials;

    }


    /* =====================================
       GET INITIAL
    ===================================== */

    function getInitial(name) {

        if (!name) {
            return "U";
        }

        return name
            .trim()
            .charAt(0)
            .toUpperCase();

    }


    /* =====================================
       ENTER EDIT MODE
    ===================================== */

    function enterEditMode() {

        document.body.classList.add("editing");

        editProfileBtn.innerHTML =
            '<i class="fa-solid fa-pen"></i> Editing Profile';

        editProfileBtn.disabled = true;

    }


    /* =====================================
       SAVE PROFILE
    ===================================== */

    function saveProfile() {

        /* Read inputs */

        const updatedProfile = {

            ...profile,

            fullName:
                inputs.fullName.value.trim(),

            email:
                inputs.email.value.trim(),

            phone:
                inputs.phone.value.trim(),

            location:
                inputs.location.value.trim(),

            role:
                inputs.role.value.trim(),

            industry:
                inputs.industry.value.trim(),

            experience:
                inputs.experience.value.trim(),

            availability:
                inputs.availability.value.trim()

        };


        /* Basic validation */

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


        /* Save */

        profile =
            updatedProfile;


        localStorage.setItem(
            "joblinkProfile",
            JSON.stringify(profile)
        );


        /* Exit edit mode */

        document.body.classList.remove("editing");

        editProfileBtn.innerHTML =
            '<i class="fa-solid fa-pen"></i> Edit Profile';

        editProfileBtn.disabled = false;


        /* Update page */

        renderProfile();


        /* Notification */

        showSaveMessage();

    }


    /* =====================================
       CANCEL EDITING
    ===================================== */

    function cancelEditing() {

        /* Restore original values */

        renderProfile();


        /* Exit edit mode */

        document.body.classList.remove("editing");

        editProfileBtn.innerHTML =
            '<i class="fa-solid fa-pen"></i> Edit Profile';

        editProfileBtn.disabled = false;

    }


    /* =====================================
       RENDER SKILLS
    ===================================== */

    function renderSkills() {

        skillsContainer.innerHTML = "";


        profile.skills.forEach((skill, index) => {

            const skillElement =
                document.createElement("div");

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

            skillsContainer.appendChild(
                skillElement
            );

        });


        /* Remove skill listeners */

        document
            .querySelectorAll(".remove-skill")
            .forEach(button => {

                button.addEventListener(
                    "click",
                    () => {

                        const index =
                            Number(
                                button.dataset.index
                            );

                        removeSkill(index);

                    }
                );

            });

    }


    /* =====================================
       ADD SKILL
    ===================================== */

    function addSkill() {

        const skill =
            newSkillInput.value.trim();


        if (!skill) {

            alert("Please enter a skill.");

            newSkillInput.focus();

            return;

        }


        /* Prevent duplicate */

        const exists =
            profile.skills.some(
                existingSkill =>
                    existingSkill.toLowerCase() ===
                    skill.toLowerCase()
            );


        if (exists) {

            alert("This skill is already in your profile.");

            return;

        }


        profile.skills.push(skill);


        renderSkills();

        closeSkillModal();

    }


    /* =====================================
       REMOVE SKILL
    ===================================== */

    function removeSkill(index) {

        if (
            index < 0 ||
            index >= profile.skills.length
        ) {
            return;
        }


        profile.skills.splice(index, 1);


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
       SAVE MESSAGE
    ===================================== */

    function showSaveMessage() {

        const message =
            document.createElement("div");

        message.textContent =
            "Profile updated successfully!";

        message.style.position = "fixed";
        message.style.right = "25px";
        message.style.bottom = "25px";
        message.style.padding = "13px 20px";
        message.style.background = "#1f8f55";
        message.style.color = "white";
        message.style.borderRadius = "7px";
        message.style.fontSize = "13px";
        message.style.fontWeight = "600";
        message.style.zIndex = "5000";
        message.style.boxShadow =
            "0 4px 12px rgba(0,0,0,0.2)";

        document.body.appendChild(message);


        setTimeout(() => {

            message.remove();

        }, 2500);

    }


    /* =====================================
       SECURITY
       Prevent HTML injection in skills
    ===================================== */

    function escapeHTML(value) {

        const div =
            document.createElement("div");

        div.textContent = value;

        return div.innerHTML;

    }

});
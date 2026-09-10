/* ==========================================
   JOBLINK RESUME BUILDER
   Connected to the ASP.NET backend:
     - Full Name / Email       -> Users
     - Phone / Location        -> Profiles
     - Summary                 -> Resumes.ai_generated_content
     - Work Experience         -> Experience (per resume)
     - Education                -> Education (per resume)
     - Skills                   -> Skills + Resume_Skills

   Headline and LinkedIn/Portfolio have no matching column
   anywhere in the schema, so they stay saved locally per-user.
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
       FIELD CONFIG
       Only columns that actually exist on
       Experience / Education.
    ======================================= */

    const ENTRY_FIELDS = {

        experience: [
            { key: "position", label: "Job Title", type: "text", placeholder: "e.g. Software Developer" },
            { key: "companyName", label: "Company", type: "text", placeholder: "e.g. JobLink Inc." },
            { key: "startDate", label: "Start Date", type: "month" },
            { key: "endDate", label: "End Date", type: "month" },
            { key: "description", label: "Description", type: "textarea", placeholder: "Key responsibilities and achievements..." }
        ],

        education: [
            { key: "schoolName", label: "School", type: "text", placeholder: "e.g. University of the Philippines" },
            { key: "degree", label: "Degree", type: "text", placeholder: "e.g. BS Computer Science" },
            { key: "startDate", label: "Start Date", type: "month" },
            { key: "endDate", label: "End Date", type: "month" }
        ]

    };

    const ENTRY_ENDPOINT = { experience: "Experience", education: "Education" };
    const ENTRY_ID_KEY = { experience: "experienceId", education: "educationId" };


    /* ======================================
       LOCAL-ONLY EXTRAS
       (no backend column - kept per-user)
    ======================================= */

    const LOCAL_KEY = `joblinkResumeExtras_${userId}`;

    let extras = loadExtras();


    /* ======================================
       STATE
    ======================================= */

    let userRecord = null;
    let profileRecord = null;
    let resumeRecord = null;

    let state = {
        personal: {
            fullName: currentUser.fullName || "",
            headline: extras.headline || "",
            email: currentUser.email || "",
            phone: "",
            location: "",
            links: extras.links || "",
            summary: ""
        },
        experience: [],
        education: [],
        skillsCatalog: [],   // full Skills table: [{skillId, skillName}]
        resumeSkills: []     // links for this resume: [{resumeId, skillId}]
    };


    /* ======================================
       ELEMENTS
    ======================================= */

    const navUser = document.getElementById("navUser");

    const personalInputs = {
        fullName: document.getElementById("fullName"),
        headline: document.getElementById("headline"),
        email: document.getElementById("email"),
        phone: document.getElementById("phone"),
        location: document.getElementById("location"),
        links: document.getElementById("links"),
        summary: document.getElementById("summary")
    };

    const experienceList = document.getElementById("experienceList");
    const educationList = document.getElementById("educationList");

    const addExperienceBtn = document.getElementById("addExperienceBtn");
    const addEducationBtn = document.getElementById("addEducationBtn");

    const skillInput = document.getElementById("skillInput");
    const addSkillBtn = document.getElementById("addSkillBtn");
    const skillsContainer = document.getElementById("skillsContainer");

    const resumeDoc = document.getElementById("resumeDoc");

    const saveBtn = document.getElementById("saveBtn");
    const saveStatus = document.getElementById("saveStatus");
    const printBtn = document.getElementById("printBtn");
    const resetBtn = document.getElementById("resetBtn");

    const generateSummaryBtn = document.getElementById("generateSummaryBtn");


    /* ======================================
       INITIAL LOAD
    ======================================= */

    setFormDisabled(true);
    fillPersonalInputs();
    renderPreview();

    loadEverything();


    /* ======================================
       PERSONAL INFO BINDINGS
    ======================================= */

    Object.keys(personalInputs).forEach(key => {

        personalInputs[key].addEventListener("input", () => {

            state.personal[key] = personalInputs[key].value;

            renderPreview();

            schedulePersonalSync();

        });

    });


    /* ======================================
       ADD EXPERIENCE / EDUCATION
    ======================================= */

    addExperienceBtn.addEventListener("click", () => addEntry("experience"));
    addEducationBtn.addEventListener("click", () => addEntry("education"));


    /* ======================================
       ENTRY LIST EVENTS (delegated)
    ======================================= */

    experienceList.addEventListener("input", event => handleEntryInput(event, "experience"));
    experienceList.addEventListener("change", event => handleEntryInput(event, "experience"));
    experienceList.addEventListener("click", event => handleEntryRemove(event, "experience"));

    educationList.addEventListener("input", event => handleEntryInput(event, "education"));
    educationList.addEventListener("change", event => handleEntryInput(event, "education"));
    educationList.addEventListener("click", event => handleEntryRemove(event, "education"));


    /* ======================================
       AI GENERATE
    ======================================= */

    generateSummaryBtn.addEventListener("click", generateSummary);

    experienceList.addEventListener("click", handleGenerateDescriptionClick);


    /* ======================================
       SKILLS
    ======================================= */

    addSkillBtn.addEventListener("click", addSkill);

    skillInput.addEventListener("keydown", event => {

        if (event.key === "Enter") {

            event.preventDefault();

            addSkill();

        }

    });

    skillsContainer.addEventListener("click", event => {

        const removeBtn = event.target.closest(".remove-skill");

        if (!removeBtn) {
            return;
        }

        removeSkill(Number(removeBtn.dataset.skillId));

    });


    /* ======================================
       SAVE / RESET / PRINT
    ======================================= */

    saveBtn.addEventListener("click", async () => {

        saveBtn.disabled = true;

        saveBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Saving...';

        try {

            await syncPersonalInfo();

            flashSaveStatus("Saved!");

        } catch (error) {

            console.error("Manual save failed:", error);

            flashSaveStatus("Couldn't save - check the API is running.", true);

        } finally {

            saveBtn.disabled = false;

            saveBtn.innerHTML = '<i class="fa-solid fa-floppy-disk"></i> Save Resume';

        }

    });

    printBtn.addEventListener("click", () => {

        window.print();

    });

    resetBtn.addEventListener("click", () => {

        alert(
            "Resume Builder now saves straight to your account, so there's no " +
            "local draft to reset. Remove entries you don't want with each " +
            "card's × button instead."
        );

    });


    /* ======================================
       LOGOUT
       SAME AS APPLICATION / DASHBOARD / PROFILE
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
       LOAD EVERYTHING FROM THE BACKEND
    ======================================= */

    async function loadEverything() {

        try {

            const userResponse = await fetch(`${API_BASE}/User/${userId}`);

            if (!userResponse.ok) {
                throw new Error(`Failed to load user (${userResponse.status})`);
            }

            userRecord = await userResponse.json();


            const profileResponse = await fetch(`${API_BASE}/Profile/by-user/${userId}`);

            profileRecord = profileResponse.ok ? await profileResponse.json() : null;


            resumeRecord = await ensureResume();


            const [experienceRes, educationRes, skillsRes, resumeSkillsRes] = await Promise.all([
                fetch(`${API_BASE}/Experience/by-resume/${resumeRecord.resumeId}`),
                fetch(`${API_BASE}/Education/by-resume/${resumeRecord.resumeId}`),
                fetch(`${API_BASE}/Skills`),
                fetch(`${API_BASE}/ResumeSkills/by-resume/${resumeRecord.resumeId}`)
            ]);

            state.experience = experienceRes.ok ? await experienceRes.json() : [];
            state.education = educationRes.ok ? await educationRes.json() : [];
            state.skillsCatalog = skillsRes.ok ? await skillsRes.json() : [];
            state.resumeSkills = resumeSkillsRes.ok ? await resumeSkillsRes.json() : [];

            state.personal = {
                fullName: userRecord.fullName || "",
                headline: extras.headline || "",
                email: userRecord.email || "",
                phone: profileRecord?.phone || "",
                location: profileRecord?.address || "",
                links: extras.links || "",
                summary: resumeRecord.aiGeneratedContent || ""
            };

            setFormDisabled(false);

            fillPersonalInputs();
            renderEntries("experience");
            renderEntries("education");
            renderSkills();
            renderPreview();

        } catch (error) {

            console.error("Unable to load resume data:", error);

            flashSaveStatus("Couldn't reach the server - make sure the API is running.", true);

        }

    }


    async function ensureResume() {

        const response = await fetch(`${API_BASE}/Resume/by-user/${userId}`);

        if (!response.ok) {
            throw new Error(`Failed to load resumes (${response.status})`);
        }

        const resumes = await response.json();

        if (resumes.length > 0) {
            return resumes[0];
        }


        /* No resume yet - create one so Experience/Education/Skills have
           somewhere to attach to. */

        const createResponse = await fetch(`${API_BASE}/Resume`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                userId,
                title: "My Resume",
                templateType: "standard"
            })
        });

        if (!createResponse.ok) {
            throw new Error(`Failed to create resume (${createResponse.status})`);
        }

        const refetch = await fetch(`${API_BASE}/Resume/by-user/${userId}`);

        const created = await refetch.json();

        return created[0];

    }


    function setFormDisabled(disabled) {

        document
            .querySelectorAll(".form-panel input, .form-panel textarea, .form-panel button")
            .forEach(el => {
                el.disabled = disabled;
            });

    }


    /* ======================================
       PERSONAL INFO SYNC
    ======================================= */

    let personalSyncTimer = null;

    function schedulePersonalSync() {

        clearTimeout(personalSyncTimer);

        personalSyncTimer = setTimeout(() => {

            syncPersonalInfo().catch(error => {

                console.error("Autosave failed:", error);

                flashSaveStatus("Couldn't save - check the API is running.", true);

            });

        }, 800);

    }


    async function syncPersonalInfo() {

        const p = state.personal;


        /* Users table */

        const userPayload = {
            ...(userRecord || {}),
            userId,
            fullName: p.fullName,
            email: p.email
        };

        const userResponse = await fetch(`${API_BASE}/User`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(userPayload)
        });

        if (!userResponse.ok) {
            throw new Error(`User sync failed (${userResponse.status})`);
        }

        userRecord = userPayload;


        /* Profiles table (create-or-update, same as Profile.html) */

        const profilePayload = {
            userId,
            phone: p.phone,
            address: p.location,
            linkedinUrl: profileRecord?.linkedinUrl || "",
            githubUrl: profileRecord?.githubUrl || "",
            isDeleted: false
        };

        if (profileRecord?.profileId) {

            const response = await fetch(`${API_BASE}/Profile`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ...profilePayload, profileId: profileRecord.profileId })
            });

            if (!response.ok) {
                throw new Error(`Profile sync failed (${response.status})`);
            }

            profileRecord = { ...profileRecord, ...profilePayload };

        } else {

            const response = await fetch(`${API_BASE}/Profile`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(profilePayload)
            });

            if (!response.ok) {
                throw new Error(`Profile create failed (${response.status})`);
            }

            const refetch = await fetch(`${API_BASE}/Profile/by-user/${userId}`);

            if (refetch.ok) {
                profileRecord = await refetch.json();
            }

        }


        /* Resumes table - summary lives in ai_generated_content */

        const resumePayload = {
            ...resumeRecord,
            resumeId: resumeRecord.resumeId,
            userId,
            title: p.headline ? `${p.headline} Resume` : (resumeRecord.title || "My Resume"),
            aiGeneratedContent: p.summary
        };

        const resumeResponse = await fetch(`${API_BASE}/Resume`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(resumePayload)
        });

        if (!resumeResponse.ok) {
            throw new Error(`Resume sync failed (${resumeResponse.status})`);
        }

        resumeRecord = resumePayload;


        /* Local-only extras */

        extras = { headline: p.headline, links: p.links };

        saveExtras();


        /* Keep the saved user snapshot fresh (navbar/greeting elsewhere) */

        localStorage.setItem("user", JSON.stringify({
            ...currentUser,
            fullName: p.fullName,
            email: p.email
        }));


        fillPersonalInputs();

        updateNavbar();

        flashSaveStatus("Saved");

    }


    /* ======================================
       FILL PERSONAL INPUTS FROM STATE
    ======================================= */

    function fillPersonalInputs() {

        Object.keys(personalInputs).forEach(key => {

            if (document.activeElement !== personalInputs[key]) {

                personalInputs[key].value = state.personal[key] || "";

            }

        });

        updateNavbar();

    }


    function updateNavbar() {

        const name = state.personal.fullName || "User";

        const parts = name.trim().split(/\s+/);

        navUser.textContent = parts.length >= 2
            ? parts[0].charAt(0) + parts[parts.length - 1].charAt(0)
            : (parts[0]?.charAt(0) || "U");

    }


    /* ======================================
       EXPERIENCE / EDUCATION CRUD
    ======================================= */

    async function addEntry(type) {

        const button = type === "experience" ? addExperienceBtn : addEducationBtn;

        button.disabled = true;

        try {

            const endpoint = ENTRY_ENDPOINT[type];

            const payload = { resumeId: resumeRecord.resumeId, isDeleted: false };

            const response = await fetch(`${API_BASE}/${endpoint}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw new Error(`Create ${type} failed (${response.status})`);
            }

            const refetch = await fetch(`${API_BASE}/${endpoint}/by-resume/${resumeRecord.resumeId}`);

            state[type] = refetch.ok ? await refetch.json() : state[type];

            renderEntries(type);
            renderPreview();

        } catch (error) {

            console.error(`Unable to add ${type}:`, error);

            flashSaveStatus("Couldn't reach the server - check the API is running.", true);

        } finally {

            button.disabled = false;

        }

    }


    function renderEntries(type) {

        const container = type === "experience" ? experienceList : educationList;
        const entries = state[type];
        const fields = ENTRY_FIELDS[type];
        const idKey = ENTRY_ID_KEY[type];

        if (entries.length === 0) {

            container.innerHTML = `
                <div class="empty-entry-hint">
                    Nothing added yet. Click "Add ${type === "experience" ? "Experience" : "Education"}" above to get started.
                </div>
            `;

            return;

        }

        container.innerHTML = entries.map(entry => {

            const entryId = entry[idKey];

            const isCurrent = !entry.endDate;

            const fieldsHtml = fields.map(field => {

                let value = entry[field.key];

                if (field.type === "month" && value) {
                    value = String(value).slice(0, 7); // "2024-01-15T..." -> "2024-01"
                }

                value = escapeHTML(value || "");

                const disabled = field.key === "endDate" && isCurrent ? "disabled" : "";

                if (field.type === "textarea") {

                    const aiButton = field.key === "description" && type === "experience" ? `
                        <button
                            type="button"
                            class="ai-generate-btn small ai-generate-description-btn"
                            data-entry-id="${entryId}"
                        >
                            <i class="fa-solid fa-wand-magic-sparkles"></i>
                            Generate with AI
                        </button>
                    ` : "";

                    return `
                        <div class="form-field">
                            <div class="field-label-row">
                                <label>${field.label}</label>
                                ${aiButton}
                            </div>
                            <textarea
                                rows="3"
                                data-entry-id="${entryId}"
                                data-field="${field.key}"
                                placeholder="${escapeHTML(field.placeholder || "")}"
                            >${value}</textarea>
                        </div>
                    `;

                }

                return `
                    <div class="form-field half">
                        <label>${field.label}</label>
                        <input
                            type="${field.type}"
                            data-entry-id="${entryId}"
                            data-field="${field.key}"
                            placeholder="${escapeHTML(field.placeholder || "")}"
                            value="${value}"
                            ${disabled}
                        >
                    </div>
                `;

            }).join("");

            return `
                <div class="entry-card">

                    <button type="button" class="remove-entry-btn" data-entry-id="${entryId}" title="Remove">
                        <i class="fa-solid fa-xmark"></i>
                    </button>

                    <div class="form-grid">
                        ${fieldsHtml}
                    </div>

                    <label class="checkbox-row">
                        <input
                            type="checkbox"
                            data-entry-id="${entryId}"
                            data-field="isCurrent"
                            ${isCurrent ? "checked" : ""}
                        >
                        I currently ${type === "experience" ? "work here" : "study here"}
                    </label>

                </div>
            `;

        }).join("");

    }


    const entrySyncTimers = new Map();

    function handleEntryInput(event, type) {

        const target = event.target;
        const field = target.dataset.field;

        if (!field) {
            return;
        }

        const entryId = Number(target.dataset.entryId);
        const idKey = ENTRY_ID_KEY[type];
        const entry = state[type].find(e => e[idKey] === entryId);

        if (!entry) {
            return;
        }

        if (field === "isCurrent") {

            if (target.checked) {
                entry.endDate = null;
            }

            renderEntries(type);

        } else {

            entry[field] = target.value;

        }

        renderPreview();


        clearTimeout(entrySyncTimers.get(entryId));

        entrySyncTimers.set(entryId, setTimeout(() => {

            syncEntry(type, entry).catch(error => {

                console.error(`Unable to save ${type} entry:`, error);

                flashSaveStatus("Couldn't save that entry - check the API is running.", true);

            });

        }, 800));

    }


    async function syncEntry(type, entry) {

        const endpoint = ENTRY_ENDPOINT[type];

        const response = await fetch(`${API_BASE}/${endpoint}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(entry)
        });

        if (!response.ok) {
            throw new Error(`${type} save failed (${response.status})`);
        }

        flashSaveStatus("Saved");

    }


    async function handleEntryRemove(event, type) {

        const removeBtn = event.target.closest(".remove-entry-btn");

        if (!removeBtn) {
            return;
        }

        const entryId = Number(removeBtn.dataset.entryId);
        const idKey = ENTRY_ID_KEY[type];
        const endpoint = ENTRY_ENDPOINT[type];

        removeBtn.disabled = true;

        try {

            const response = await fetch(`${API_BASE}/${endpoint}?id=${entryId}`, {
                method: "DELETE"
            });

            if (!response.ok) {
                throw new Error(`Delete ${type} failed (${response.status})`);
            }

            state[type] = state[type].filter(e => e[idKey] !== entryId);

            renderEntries(type);
            renderPreview();

        } catch (error) {

            console.error(`Unable to remove ${type} entry:`, error);

            flashSaveStatus("Couldn't reach the server - check the API is running.", true);

            removeBtn.disabled = false;

        }

    }


    /* ======================================
       AI GENERATE
    ======================================= */

    async function generateSummary() {

        generateSummaryBtn.disabled = true;

        generateSummaryBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Generating...';

        try {

            const payload = {
                headline: state.personal.headline,
                skills: getSkillNames().map(s => s.skillName),
                experienceHighlights: state.experience
                    .map(e => [e.position, e.companyName].filter(Boolean).join(" at "))
                    .filter(Boolean),
                educationHighlights: state.education
                    .map(e => [e.degree, e.schoolName].filter(Boolean).join(" from "))
                    .filter(Boolean)
            };

            const response = await fetch(`${API_BASE}/AiResume/summary`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                throw new Error(data.message || `AI request failed (${response.status})`);
            }

            state.personal.summary = data.summary;

            personalInputs.summary.value = data.summary;

            renderPreview();

            await syncPersonalInfo();

        } catch (error) {

            console.error("Unable to generate summary:", error);

            flashSaveStatus(error.message || "Couldn't generate a summary.", true);

        } finally {

            generateSummaryBtn.disabled = false;

            generateSummaryBtn.innerHTML = '<i class="fa-solid fa-wand-magic-sparkles"></i> Generate with AI';

        }

    }


    async function handleGenerateDescriptionClick(event) {

        const button = event.target.closest(".ai-generate-description-btn");

        if (!button) {
            return;
        }

        const entryId = Number(button.dataset.entryId);
        const entry = state.experience.find(e => e.experienceId === entryId);

        if (!entry) {
            return;
        }

        button.disabled = true;

        const originalHtml = button.innerHTML;

        button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i>';

        try {

            const payload = {
                position: entry.position,
                companyName: entry.companyName,
                notes: entry.description || undefined
            };

            const response = await fetch(`${API_BASE}/AiResume/experience-description`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                throw new Error(data.message || `AI request failed (${response.status})`);
            }

            entry.description = data.description;

            renderEntries("experience");
            renderPreview();

            await syncEntry("experience", entry);

        } catch (error) {

            console.error("Unable to generate description:", error);

            flashSaveStatus(error.message || "Couldn't generate a description.", true);

            button.disabled = false;

            button.innerHTML = originalHtml;

        }

    }


    /* ======================================
       SKILLS
    ======================================= */

    async function addSkill() {

        const value = skillInput.value.trim();

        if (!value) {
            return;
        }

        addSkillBtn.disabled = true;

        try {

            let skill = state.skillsCatalog.find(
                s => (s.skillName || "").toLowerCase() === value.toLowerCase()
            );

            if (!skill) {

                const createResponse = await fetch(`${API_BASE}/Skills`, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ skillName: value, isDeleted: false })
                });

                if (!createResponse.ok) {
                    throw new Error(`Create skill failed (${createResponse.status})`);
                }

                const catalogResponse = await fetch(`${API_BASE}/Skills`);

                state.skillsCatalog = await catalogResponse.json();

                skill = state.skillsCatalog.find(
                    s => (s.skillName || "").toLowerCase() === value.toLowerCase()
                );

            }

            const alreadyLinked = state.resumeSkills.some(link => link.skillId === skill.skillId);

            if (alreadyLinked) {

                skillInput.value = "";

                return;

            }

            const linkResponse = await fetch(`${API_BASE}/ResumeSkills`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ resumeId: resumeRecord.resumeId, skillId: skill.skillId, isDeleted: false })
            });

            if (!linkResponse.ok) {
                throw new Error(`Link skill failed (${linkResponse.status})`);
            }

            const linksResponse = await fetch(`${API_BASE}/ResumeSkills/by-resume/${resumeRecord.resumeId}`);

            state.resumeSkills = await linksResponse.json();

            skillInput.value = "";

            renderSkills();
            renderPreview();

        } catch (error) {

            console.error("Unable to add skill:", error);

            flashSaveStatus("Couldn't reach the server - check the API is running.", true);

        } finally {

            addSkillBtn.disabled = false;

        }

    }


    async function removeSkill(skillId) {

        try {

            const response = await fetch(
                `${API_BASE}/ResumeSkills/${resumeRecord.resumeId}/${skillId}`,
                { method: "DELETE" }
            );

            if (!response.ok) {
                throw new Error(`Remove skill failed (${response.status})`);
            }

            state.resumeSkills = state.resumeSkills.filter(link => link.skillId !== skillId);

            renderSkills();
            renderPreview();

        } catch (error) {

            console.error("Unable to remove skill:", error);

            flashSaveStatus("Couldn't reach the server - check the API is running.", true);

        }

    }


    function renderSkills() {

        const names = getSkillNames();

        if (names.length === 0) {

            skillsContainer.innerHTML = `<span class="no-skills-hint">No skills added yet.</span>`;

            return;

        }

        skillsContainer.innerHTML = names.map(({ skillId, skillName }) => `
            <div class="skill">
                <span>${escapeHTML(skillName)}</span>
                <button type="button" class="remove-skill" data-skill-id="${skillId}">×</button>
            </div>
        `).join("");

    }


    function getSkillNames() {

        return state.resumeSkills
            .map(link => {

                const skill = state.skillsCatalog.find(s => s.skillId === link.skillId);

                return skill ? { skillId: skill.skillId, skillName: skill.skillName } : null;

            })
            .filter(Boolean);

    }


    /* ======================================
       LOCAL EXTRAS PERSISTENCE
    ======================================= */

    function loadExtras() {

        const saved = localStorage.getItem(LOCAL_KEY);

        if (!saved) {
            return { headline: "", links: "" };
        }

        try {

            const parsed = JSON.parse(saved);

            return { headline: parsed.headline || "", links: parsed.links || "" };

        } catch (error) {

            return { headline: "", links: "" };

        }

    }


    function saveExtras() {

        localStorage.setItem(LOCAL_KEY, JSON.stringify(extras));

    }


    /* ======================================
       LIVE PREVIEW
    ======================================= */

    function renderPreview() {

        const personal = state.personal;

        const name = personal.fullName || "Your Name";

        const contactParts = [personal.email, personal.phone, personal.location, personal.links].filter(Boolean);

        const contactHtml = contactParts.length
            ? contactParts.map(part => `<span>${escapeHTML(part)}</span>`).join("")
            : `<span class="resume-empty-text">Add your contact details to see them here</span>`;

        const summaryHtml = personal.summary
            ? escapeHTML(personal.summary)
            : `<span class="resume-empty-text">Your professional summary will appear here.</span>`;

        resumeDoc.innerHTML = `

            <div class="resume-name">${escapeHTML(name)}</div>

            ${personal.headline ? `<div class="resume-headline">${escapeHTML(personal.headline)}</div>` : ""}

            <div class="resume-contact-line">
                ${contactHtml}
            </div>

            <div class="resume-section">
                <h4>Summary</h4>
                <p class="resume-summary-text">${summaryHtml}</p>
            </div>

            <div class="resume-section">
                <h4>Experience</h4>
                ${renderPreviewEntries(state.experience, "position", "companyName")}
            </div>

            <div class="resume-section">
                <h4>Education</h4>
                ${renderPreviewEntries(state.education, "degree", "schoolName")}
            </div>

            <div class="resume-section">
                <h4>Skills</h4>
                ${renderPreviewSkills()}
            </div>

        `;

    }


    function renderPreviewEntries(entries, titleKey, subtitleKey) {

        const filled = entries.filter(entry => entry[titleKey] || entry[subtitleKey] || entry.description);

        if (filled.length === 0) {
            return `<p class="resume-empty-text">Nothing added yet.</p>`;
        }

        return filled.map(entry => {

            const dateRange = formatDateRange(entry.startDate, entry.endDate);

            return `
                <div class="resume-entry">

                    <div class="resume-entry-header">

                        <div>
                            <div class="resume-entry-title">${escapeHTML(entry[titleKey] || "Untitled")}</div>
                            <div class="resume-entry-subtitle">${escapeHTML(entry[subtitleKey] || "")}</div>
                        </div>

                        ${dateRange ? `<div class="resume-entry-date">${escapeHTML(dateRange)}</div>` : ""}

                    </div>

                    ${entry.description ? `<div class="resume-entry-description">${escapeHTML(entry.description)}</div>` : ""}

                </div>
            `;

        }).join("");

    }


    function renderPreviewSkills() {

        const names = getSkillNames();

        if (names.length === 0) {
            return `<p class="resume-empty-text">No skills added yet.</p>`;
        }

        return `
            <div class="resume-skills-preview">
                ${names.map(({ skillName }) => `<span class="resume-skill-chip">${escapeHTML(skillName)}</span>`).join("")}
            </div>
        `;

    }


    function formatDateRange(startDate, endDate) {

        const start = formatMonth(startDate);
        const end = endDate ? formatMonth(endDate) : (startDate ? "Present" : "");

        if (!start && !end) {
            return "";
        }

        return [start, end].filter(Boolean).join(" - ");

    }


    function formatMonth(value) {

        if (!value) {
            return "";
        }

        const date = new Date(value);

        if (Number.isNaN(date.getTime())) {
            return "";
        }

        return date.toLocaleDateString("en-US", { month: "short", year: "numeric" });

    }


    /* ======================================
       SAVE STATUS TOAST
    ======================================= */

    let statusTimer = null;

    function flashSaveStatus(text, isError = false) {

        saveStatus.textContent = text;

        saveStatus.style.color = isError ? "#c0392b" : "";

        saveStatus.classList.add("show");

        clearTimeout(statusTimer);

        statusTimer = setTimeout(() => {

            saveStatus.classList.remove("show");

        }, isError ? 4000 : 1800);

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

/* ==========================================
   JOBLINK AI RESUME BUILDER - FRONTEND
========================================== */

const API_BASE = "http://127.0.0.1:8001/api";
const DRAFT_KEY = "joblinkAiResumeDraft";

document.addEventListener("DOMContentLoaded", () => {

    /* ======================================
       ENTRY FIELD CONFIG
       Drives both the repeatable-section forms
       and the live preview.
    ======================================= */

    const ENTRY_CONFIG = {

        education: {
            listKey: "education",
            fields: [
                { key: "school", label: "School", type: "text" },
                { key: "degree", label: "Degree", type: "text" },
                { key: "field_of_study", label: "Field of Study", type: "text" },
                { key: "start_year", label: "Start Year", type: "text" },
                { key: "graduation_year", label: "Graduation Year", type: "text" },
                { key: "achievements", label: "Academic Achievements (one per line)", type: "list", fullWidth: true },
            ],
            blank: () => ({ school: "", degree: "", field_of_study: "", start_year: "", graduation_year: "", achievements: [] }),
        },

        experience: {
            listKey: "experience",
            fields: [
                { key: "position", label: "Job Position", type: "text" },
                { key: "company", label: "Company", type: "text" },
                { key: "start_date", label: "Start Date", type: "text" },
                { key: "end_date", label: "End Date", type: "text" },
                { key: "description", label: "Job Description", type: "textarea", fullWidth: true, improvable: true },
                { key: "responsibilities", label: "Responsibilities (one per line)", type: "list", fullWidth: true },
                { key: "achievements", label: "Achievements (one per line)", type: "list", fullWidth: true, aiAchievement: true },
            ],
            blank: () => ({ company: "", position: "", start_date: "", end_date: "", description: "", responsibilities: [], achievements: [] }),
        },

        certifications: {
            listKey: "certifications",
            fields: [
                { key: "name", label: "Certification Name", type: "text" },
                { key: "organization", label: "Organization", type: "text" },
                { key: "date", label: "Date", type: "text" },
            ],
            blank: () => ({ name: "", organization: "", date: "" }),
        },

        projects: {
            listKey: "projects",
            fields: [
                { key: "name", label: "Project Name", type: "text" },
                { key: "role", label: "Role", type: "text" },
                { key: "description", label: "Description", type: "textarea", fullWidth: true, improvable: true },
                { key: "technologies_used", label: "Technologies Used (comma separated)", type: "tags", fullWidth: true },
                { key: "result", label: "Project Result", type: "text", fullWidth: true },
            ],
            blank: () => ({ name: "", description: "", technologies_used: [], role: "", result: "" }),
        },

        achievements: {
            listKey: "achievements",
            fields: [
                { key: "title", label: "Achievement", type: "text", fullWidth: true },
                { key: "organization", label: "Organization", type: "text" },
                { key: "date", label: "Date", type: "text" },
            ],
            blank: () => ({ title: "", organization: "", date: "" }),
        },
    };

    const SKILL_CATEGORIES = ["technical_skills", "soft_skills", "programming_languages", "tools", "technologies"];


    /* ======================================
       STATE
    ======================================= */

    let state = loadDraft();


    /* ======================================
       ELEMENTS
    ======================================= */

    const el = id => document.getElementById(id);

    const personalInputs = {
        full_name: el("fullName"), email: el("email"), phone: el("phone"),
        location: el("location"), linkedin: el("linkedin"), portfolio: el("portfolio"),
    };

    const careerInputs = {
        target_position: el("targetPosition"),
        career_objective: el("careerObjective"),
        professional_summary: el("professionalSummary"),
    };

    const templateSelect = el("templateSelect");
    const previewDoc = el("previewDoc");
    const statusBanner = el("statusBanner");
    const progressBarFill = el("progressBarFill");
    const progressPercent = el("progressPercent");
    const progressChecklist = el("progressChecklist");


    /* ======================================
       INIT
    ======================================= */

    fillPersonalAndCareerInputs();
    renderAllEntryLists();
    renderSkills();
    renderPreview();
    renderProgress();
    loadTemplatesIntoSelect();

    templateSelect.value = state.template;


    /* ======================================
       SIDEBAR NAVIGATION
    ======================================= */

    document.querySelectorAll(".nav-link").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".nav-link").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");

            document.querySelectorAll(".panel").forEach(p => p.classList.remove("active"));
            el(`panel-${btn.dataset.panel}`).classList.add("active");

            el("sidebar").classList.remove("open");
        });
    });

    el("mobileNavToggle").addEventListener("click", () => {
        el("sidebar").classList.toggle("open");
    });


    /* ======================================
       PERSONAL / CAREER INPUT BINDINGS
    ======================================= */

    Object.keys(personalInputs).forEach(key => {
        personalInputs[key].addEventListener("input", () => {
            state.personal_info[key] = personalInputs[key].value;
            afterStateChange({ preview: true, progress: true });
        });
    });

    Object.keys(careerInputs).forEach(key => {
        careerInputs[key].addEventListener("input", () => {
            state.career_info[key] = careerInputs[key].value;
            afterStateChange({ preview: true, progress: true });
        });
    });

    templateSelect.addEventListener("change", () => {
        state.template = templateSelect.value;
        afterStateChange({ preview: true });
    });


    /* ======================================
       REPEATABLE SECTIONS (add / render / remove / input)
    ======================================= */

    document.querySelectorAll("[data-add]").forEach(button => {
        button.addEventListener("click", () => {
            const type = button.dataset.add;
            state[type].push(ENTRY_CONFIG[type].blank());
            renderEntryList(type);
            afterStateChange({ preview: true, progress: true });
        });
    });

    ["education", "experience", "certifications", "projects", "achievements"].forEach(type => {
        const container = el(`${type}List`);

        container.addEventListener("input", event => handleEntryFieldChange(event, type));
        container.addEventListener("change", event => handleEntryFieldChange(event, type));

        container.addEventListener("click", event => {
            const removeBtn = event.target.closest(".remove-entry-btn");
            if (removeBtn) {
                const index = Number(removeBtn.closest(".entry-card").dataset.index);
                state[type].splice(index, 1);
                renderEntryList(type);
                afterStateChange({ preview: true, progress: true });
                return;
            }

            const improveBtn = event.target.closest(".ai-improve-btn");
            if (improveBtn) {
                const index = Number(improveBtn.closest(".entry-card").dataset.index);
                const field = improveBtn.dataset.field;
                openImproveModal({
                    text: state[type][index][field] || "",
                    context: improveBtn.dataset.label,
                    onAccept: (improvedText) => {
                        state[type][index][field] = improvedText;
                        renderEntryList(type);
                        afterStateChange({ preview: true });
                    },
                });
                return;
            }

            const achievementBtn = event.target.closest(".ai-achievement-btn");
            if (achievementBtn) {
                const index = Number(achievementBtn.closest(".entry-card").dataset.index);
                const field = achievementBtn.dataset.field;
                generateAchievementForEntry(type, index, field);
            }
        });
    });

    function handleEntryFieldChange(event, type) {
        const target = event.target;
        const field = target.dataset.field;
        if (!field) return;

        const index = Number(target.closest(".entry-card").dataset.index);
        const config = ENTRY_CONFIG[type].fields.find(f => f.key === field);

        if (config.type === "list") {
            state[type][index][field] = target.value.split("\n").map(s => s.trim()).filter(Boolean);
        } else if (config.type === "tags") {
            state[type][index][field] = target.value.split(",").map(s => s.trim()).filter(Boolean);
        } else {
            state[type][index][field] = target.value;
        }

        afterStateChange({ preview: true, progress: true });
    }


    function renderAllEntryLists() {
        ["education", "experience", "certifications", "projects", "achievements"].forEach(renderEntryList);
    }

    function renderEntryList(type) {
        const container = el(`${type}List`);
        const config = ENTRY_CONFIG[type];
        const entries = state[type];

        if (entries.length === 0) {
            container.innerHTML = `<div class="empty-hint">Nothing added yet. Click "Add" above to get started.</div>`;
            return;
        }

        container.innerHTML = entries.map((entry, index) => {
            const fieldsHtml = config.fields.map(field => {
                const rawValue = entry[field.key];
                let value = rawValue;

                if (field.type === "list") value = (rawValue || []).join("\n");
                if (field.type === "tags") value = (rawValue || []).join(", ");

                const widthClass = field.fullWidth ? "field full-width" : "field";

                const aiButtons = [
                    field.improvable ? `<button type="button" class="ai-btn small ai-improve-btn" data-field="${field.key}" data-label="${escapeAttr(field.label)}"><i class="fa-solid fa-wand-magic-sparkles"></i> Improve</button>` : "",
                    field.aiAchievement ? `<button type="button" class="ai-btn small ai-achievement-btn" data-field="${field.key}"><i class="fa-solid fa-wand-magic-sparkles"></i> Add via AI</button>` : "",
                ].join("");

                const inputHtml = field.type === "textarea" || field.type === "list"
                    ? `<textarea rows="${field.type === "list" ? 3 : 2}" data-field="${field.key}" placeholder="${escapeAttr(field.label)}">${escapeHtml(value || "")}</textarea>`
                    : `<input type="text" data-field="${field.key}" value="${escapeAttr(value || "")}" placeholder="${escapeAttr(field.label)}">`;

                return `
                    <div class="${widthClass}">
                        <div class="field-label-row">
                            <label>${escapeHtml(field.label)}</label>
                            ${aiButtons}
                        </div>
                        ${inputHtml}
                    </div>
                `;
            }).join("");

            return `
                <div class="entry-card" data-index="${index}">
                    <button type="button" class="remove-entry-btn"><i class="fa-solid fa-xmark"></i></button>
                    <div class="entry-fields">${fieldsHtml}</div>
                </div>
            `;
        }).join("");
    }


    /* ======================================
       SKILLS
    ======================================= */

    document.querySelectorAll(".skill-category").forEach(categoryEl => {
        const category = categoryEl.dataset.category;
        const input = categoryEl.querySelector(".skill-input");
        const addBtn = categoryEl.querySelector(".add-tag-btn");

        const addSkill = () => {
            const value = input.value.trim();
            if (!value) return;

            if (!state.skills[category].some(s => s.toLowerCase() === value.toLowerCase())) {
                state.skills[category].push(value);
                renderSkills();
                afterStateChange({ preview: true, progress: true });
            }

            input.value = "";
        };

        addBtn.addEventListener("click", addSkill);
        input.addEventListener("keydown", event => {
            if (event.key === "Enter") {
                event.preventDefault();
                addSkill();
            }
        });
    });

    function renderSkills() {
        document.querySelectorAll(".skill-category").forEach(categoryEl => {
            const category = categoryEl.dataset.category;
            const container = categoryEl.querySelector(".tag-container");

            container.innerHTML = state.skills[category].map((skill, index) => `
                <div class="tag">
                    <span>${escapeHtml(skill)}</span>
                    <button type="button" data-category="${category}" data-index="${index}">×</button>
                </div>
            `).join("");
        });
    }

    document.querySelector(".skills-grid").addEventListener("click", event => {
        const button = event.target.closest("button[data-category]");
        if (!button) return;

        const category = button.dataset.category;
        const index = Number(button.dataset.index);
        state.skills[category].splice(index, 1);
        renderSkills();
        afterStateChange({ preview: true, progress: true });
    });


    /* ======================================
       AI: GENERATE SUMMARY
    ======================================= */

    el("generateSummaryBtn").addEventListener("click", async () => {
        const button = el("generateSummaryBtn");
        const warningEl = el("summaryWarning");

        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Generating...';
        warningEl.hidden = true;

        try {
            const response = await fetch(`${API_BASE}/resume/generate-summary`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ resume: buildResumePayload() }),
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            state.career_info.professional_summary = data.summary;
            careerInputs.professional_summary.value = data.summary;

            if (data.warnings && data.warnings.length) {
                warningEl.textContent = data.warnings.join(" ");
                warningEl.hidden = false;
            }

            afterStateChange({ preview: true, progress: true });
            showStatus("Summary generated.", "success");

        } catch (error) {
            showStatus(error.message || "Couldn't generate a summary.", "error");
        } finally {
            button.disabled = false;
            button.innerHTML = '<i class="fa-solid fa-wand-magic-sparkles"></i> Generate with AI';
        }
    });


    /* ======================================
       AI: IMPROVE CONTENT (modal)
    ======================================= */

    const improveModal = el("improveModal");
    let improveContext = null;

    async function openImproveModal({ text, context, onAccept }) {
        if (!text || !text.trim()) {
            showStatus("There's nothing in this field to improve yet.", "error");
            return;
        }

        improveContext = { onAccept };

        el("improveOriginalText").textContent = text;
        el("improveResultText").value = "Improving...";
        el("improveWarning").hidden = true;
        improveModal.classList.add("show");

        try {
            const response = await fetch(`${API_BASE}/resume/improve-content`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ text, context, known_terms: collectKnownTerms() }),
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            el("improveResultText").value = data.improved_text;

            if (data.warnings && data.warnings.length) {
                el("improveWarning").textContent = data.warnings.join(" ");
                el("improveWarning").hidden = false;
            }

        } catch (error) {
            el("improveResultText").value = text;
            showStatus(error.message || "Couldn't improve this text.", "error");
        }
    }

    el("improveCancelBtn").addEventListener("click", () => improveModal.classList.remove("show"));

    el("improveAcceptBtn").addEventListener("click", () => {
        if (improveContext) improveContext.onAccept(el("improveResultText").value);
        improveModal.classList.remove("show");
    });

    improveModal.addEventListener("click", event => {
        if (event.target === improveModal) improveModal.classList.remove("show");
    });


    /* ======================================
       AI: GENERATE ACHIEVEMENT STATEMENT
    ======================================= */

    async function generateAchievementForEntry(type, index, field) {
        const rawDescription = window.prompt(
            "Briefly describe what you did (in your own words) - the AI will turn it into a stronger resume statement:"
        );

        if (!rawDescription || !rawDescription.trim()) return;

        const entry = state[type][index];
        const technologies = type === "projects" ? (entry.technologies_used || []) : [];

        try {
            const response = await fetch(`${API_BASE}/resume/generate-achievement`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ raw_description: rawDescription, technologies, context: `${type} entry` }),
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            entry[field] = [...(entry[field] || []), data.statement];
            renderEntryList(type);
            afterStateChange({ preview: true });

            if (data.warnings && data.warnings.length) {
                showStatus(data.warnings.join(" "), "error");
            } else {
                showStatus("Achievement statement added.", "success");
            }

        } catch (error) {
            showStatus(error.message || "Couldn't generate that statement.", "error");
        }
    }


    /* ======================================
       SAVE (local draft)
    ======================================= */

    el("saveBtn").addEventListener("click", () => {
        saveDraft();
        showStatus("Draft saved on this device.", "success");
    });


    /* ======================================
       DOWNLOAD PDF / DOCX
    ======================================= */

    el("downloadPdfBtn").addEventListener("click", () => downloadDocument("generate-pdf", "pdf"));
    el("downloadDocxBtn").addEventListener("click", () => downloadDocument("generate-docx", "docx"));

    async function downloadDocument(endpoint, extension) {
        if (!state.personal_info.full_name || !state.personal_info.email) {
            showStatus("Add at least your name and email before downloading.", "error");
            return;
        }

        try {
            const response = await fetch(`${API_BASE}/resume/${endpoint}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ resume: buildResumePayload() }),
            });

            if (!response.ok) {
                const data = await response.json().catch(() => ({}));
                throw new Error(data.detail || `Request failed (${response.status})`);
            }

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");

            link.href = url;
            link.download = `${(state.personal_info.full_name || "resume").replace(/\s+/g, "_")}_Resume.${extension}`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);

            showStatus(`${extension.toUpperCase()} downloaded.`, "success");

        } catch (error) {
            showStatus(error.message || "Couldn't generate that file.", "error");
        }
    }


    /* ======================================
       ATS ANALYZER
    ======================================= */

    el("runAtsBtn").addEventListener("click", async () => {
        const jobDescription = el("atsJobDescription").value.trim();
        if (!jobDescription) {
            showStatus("Paste a job description first.", "error");
            return;
        }

        const button = el("runAtsBtn");
        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Analyzing...';

        try {
            const response = await fetch(`${API_BASE}/resume/ats-score`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ resume: buildResumePayload(), job_description: jobDescription }),
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            el("atsOverallScore").textContent = `${data.overall_score}%`;
            el("atsBreakdown").innerHTML = `
                <p>Keyword Match: <strong>${data.keyword_match_score}%</strong></p>
                <p>Skills Match: <strong>${data.skills_match_score}%</strong></p>
                <p>Experience Match: <strong>${data.experience_match_score}%</strong></p>
                <p>Education Match: <strong>${data.education_match_score}%</strong></p>
            `;
            el("atsMatchedSkills").innerHTML = data.matched_skills.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span class="p-empty">None found</span>`;
            el("atsMissingKeywords").innerHTML = data.missing_keywords.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span>None - great match!</span>`;
            el("atsSuggestions").innerHTML = data.suggestions.map(s => `<li>${escapeHtml(s)}</li>`).join("");

            el("atsResults").hidden = false;

        } catch (error) {
            showStatus(error.message || "Couldn't run the ATS analysis.", "error");
        } finally {
            button.disabled = false;
            button.innerHTML = '<i class="fa-solid fa-magnifying-glass-chart"></i> Analyze';
        }
    });


    /* ======================================
       JOB MATCH
    ======================================= */

    el("runMatchBtn").addEventListener("click", async () => {
        const jobDescription = el("matchJobDescription").value.trim();
        if (!jobDescription) {
            showStatus("Paste a job description first.", "error");
            return;
        }

        const button = el("runMatchBtn");
        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Comparing...';

        try {
            const response = await fetch(`${API_BASE}/resume/job-match`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ resume: buildResumePayload(), job_description: jobDescription }),
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            el("matchScore").textContent = `${data.compatibility_score}%`;
            el("matchSkills").innerHTML = data.matching_skills.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span class="p-empty">None found</span>`;
            el("matchMissingKeywords").innerHTML = data.missing_keywords.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span>None - great match!</span>`;
            el("matchExperience").innerHTML = data.relevant_experience.map(s => `<li>${escapeHtml(s)}</li>`).join("") || `<li>No directly relevant experience identified.</li>`;
            el("matchSuggestions").innerHTML = data.recommended_improvements.map(s => `<li>${escapeHtml(s)}</li>`).join("");

            el("matchResults").hidden = false;

        } catch (error) {
            showStatus(error.message || "Couldn't run the job match.", "error");
        } finally {
            button.disabled = false;
            button.innerHTML = '<i class="fa-solid fa-magnifying-glass-chart"></i> Compare';
        }
    });


    /* ======================================
       UPLOAD & ANALYZE
    ======================================= */

    const dropzone = el("uploadDropzone");
    const uploadInput = el("uploadInput");

    dropzone.addEventListener("click", () => uploadInput.click());

    dropzone.addEventListener("dragover", event => {
        event.preventDefault();
        dropzone.classList.add("drag-over");
    });

    dropzone.addEventListener("dragleave", () => dropzone.classList.remove("drag-over"));

    dropzone.addEventListener("drop", event => {
        event.preventDefault();
        dropzone.classList.remove("drag-over");
        if (event.dataTransfer.files.length) {
            uploadInput.files = event.dataTransfer.files;
            handleUpload(event.dataTransfer.files[0]);
        }
    });

    uploadInput.addEventListener("change", () => {
        if (uploadInput.files.length) handleUpload(uploadInput.files[0]);
    });

    async function handleUpload(file) {
        el("uploadFileName").textContent = `Analyzing ${file.name}...`;

        const formData = new FormData();
        formData.append("file", file);

        try {
            const response = await fetch(`${API_BASE}/resume/upload`, { method: "POST", body: formData });
            const data = await response.json();
            if (!response.ok) throw new Error(data.detail || "Request failed");

            el("uploadFileName").textContent = `Analyzed: ${data.filename}`;

            const a = data.analysis;
            el("uploadSections").innerHTML = a.detected_sections.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span class="p-empty">None detected</span>`;
            el("uploadSkills").innerHTML = a.skills_found.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span class="p-empty">None detected</span>`;
            el("uploadMissingSections").innerHTML = a.missing_sections.map(s => `<span>${escapeHtml(s)}</span>`).join("") || `<span>None - all key sections found!</span>`;
            el("uploadFormattingIssues").innerHTML = a.formatting_issues.map(s => `<li>${escapeHtml(s)}</li>`).join("") || `<li>No formatting issues detected.</li>`;
            el("uploadSuggestions").innerHTML = a.suggestions.map(s => `<li>${escapeHtml(s)}</li>`).join("");

            el("uploadResults").hidden = false;

        } catch (error) {
            el("uploadFileName").textContent = "";
            showStatus(error.message || "Couldn't analyze this file.", "error");
        }
    }


    /* ======================================
       PREVIEW
    ======================================= */

    function renderPreview() {
        const p = state.personal_info;
        const c = state.career_info;

        const contactParts = [p.email, p.phone, p.location, p.linkedin, p.portfolio].filter(Boolean);

        previewDoc.innerHTML = `
            <div class="p-name">${escapeHtml(p.full_name) || "Your Name"}</div>
            <div class="p-contact">${contactParts.map(part => `<span>${escapeHtml(part)}</span>`).join("") || "Add your contact details"}</div>

            ${c.professional_summary ? `
                <div class="p-section">
                    <h4>Summary</h4>
                    <p>${escapeHtml(c.professional_summary)}</p>
                </div>
            ` : ""}

            ${previewListSection("Experience", state.experience, e => ({
                title: e.position, subtitle: e.company,
                meta: [e.start_date, e.end_date].filter(Boolean).join(" - "),
                bullets: [...(e.responsibilities || []), ...(e.achievements || [])],
                paragraph: e.description,
            }))}

            ${previewListSection("Education", state.education, e => ({
                title: [e.degree, e.field_of_study && `in ${e.field_of_study}`].filter(Boolean).join(" "),
                subtitle: e.school,
                meta: [e.start_year, e.graduation_year].filter(Boolean).join(" - "),
                bullets: e.achievements || [],
            }))}

            ${previewSkillsSection()}

            ${previewListSection("Projects", state.projects, p => ({
                title: p.name, subtitle: p.role,
                bullets: [p.description, p.technologies_used?.length ? `Technologies: ${p.technologies_used.join(", ")}` : "", p.result ? `Result: ${p.result}` : ""].filter(Boolean),
            }))}

            ${previewListSection("Certifications", state.certifications, c => ({
                title: c.name, subtitle: c.organization, meta: c.date,
            }))}

            ${previewListSection("Achievements", state.achievements, a => ({
                title: a.title, subtitle: a.organization, meta: a.date,
            }))}
        `;
    }

    function previewListSection(heading, items, mapFn) {
        if (!items.length) return "";

        return `
            <div class="p-section">
                <h4>${escapeHtml(heading)}</h4>
                ${items.map(item => {
                    const e = mapFn(item);
                    return `
                        <div class="p-entry">
                            <div class="p-entry-header">
                                <div>
                                    <div class="p-entry-title">${escapeHtml(e.title || "Untitled")}</div>
                                    ${e.subtitle ? `<div class="p-entry-subtitle">${escapeHtml(e.subtitle)}</div>` : ""}
                                </div>
                                ${e.meta ? `<div class="p-entry-meta">${escapeHtml(e.meta)}</div>` : ""}
                            </div>
                            ${e.paragraph ? `<p>${escapeHtml(e.paragraph)}</p>` : ""}
                            ${e.bullets && e.bullets.length ? `<ul class="p-bullets">${e.bullets.map(b => `<li>${escapeHtml(b)}</li>`).join("")}</ul>` : ""}
                        </div>
                    `;
                }).join("")}
            </div>
        `;
    }

    function previewSkillsSection() {
        const labels = { programming_languages: "Programming Languages", technical_skills: "Technical Skills", tools: "Tools", technologies: "Technologies", soft_skills: "Soft Skills" };
        const groups = SKILL_CATEGORIES.filter(cat => state.skills[cat].length > 0);

        if (!groups.length) return "";

        return `
            <div class="p-section">
                <h4>Skills</h4>
                ${groups.map(cat => `<p><strong>${labels[cat]}:</strong> ${escapeHtml(state.skills[cat].join(", "))}</p>`).join("")}
            </div>
        `;
    }


    /* ======================================
       PROGRESS INDICATOR
    ======================================= */

    function renderProgress() {
        const checks = [
            { label: "Personal info", done: !!(state.personal_info.full_name && state.personal_info.email) },
            { label: "Professional summary", done: !!state.career_info.professional_summary },
            { label: "Education", done: state.education.length > 0 },
            { label: "Work experience", done: state.experience.length > 0 },
            { label: "Skills", done: SKILL_CATEGORIES.some(c => state.skills[c].length > 0) },
            { label: "Certifications", done: state.certifications.length > 0 },
            { label: "Projects", done: state.projects.length > 0 },
            { label: "Achievements", done: state.achievements.length > 0 },
        ];

        const doneCount = checks.filter(c => c.done).length;
        const percent = Math.round((doneCount / checks.length) * 100);

        progressBarFill.style.width = `${percent}%`;
        progressPercent.textContent = `${percent}%`;

        progressChecklist.innerHTML = checks.map(c => `
            <li class="${c.done ? "done" : ""}">
                <i class="fa-solid ${c.done ? "fa-circle-check" : "fa-circle"}"></i>
                ${escapeHtml(c.label)}
            </li>
        `).join("");
    }


    /* ======================================
       TEMPLATES
    ======================================= */

    async function loadTemplatesIntoSelect() {
        try {
            const response = await fetch(`${API_BASE}/resume/templates`);
            if (!response.ok) return;

            const data = await response.json();
            templateSelect.innerHTML = data.templates.map(t =>
                `<option value="${t.id}" title="${escapeAttr(t.description)}">${escapeHtml(t.name)}</option>`
            ).join("");
            templateSelect.value = state.template;

        } catch (error) {
            // Non-fatal - the hardcoded <option> list in index.html already covers this.
            console.error("Couldn't load templates from the API:", error);
        }
    }


    /* ======================================
       HELPERS
    ======================================= */

    function fillPersonalAndCareerInputs() {
        Object.keys(personalInputs).forEach(key => { personalInputs[key].value = state.personal_info[key] || ""; });
        Object.keys(careerInputs).forEach(key => { careerInputs[key].value = state.career_info[key] || ""; });
    }

    function buildResumePayload() {
        return JSON.parse(JSON.stringify(state));
    }

    function collectKnownTerms() {
        const terms = [...SKILL_CATEGORIES.flatMap(c => state.skills[c])];
        state.experience.forEach(e => terms.push(e.company, e.position));
        state.education.forEach(e => terms.push(e.school, e.field_of_study));
        state.certifications.forEach(c => terms.push(c.name));
        state.projects.forEach(p => { terms.push(p.name); terms.push(...(p.technologies_used || [])); });
        state.achievements.forEach(a => terms.push(a.title));
        return terms.filter(Boolean);
    }

    function afterStateChange({ preview, progress } = {}) {
        if (preview) renderPreview();
        if (progress) renderProgress();
        saveDraft();
    }

    function saveDraft() {
        try {
            localStorage.setItem(DRAFT_KEY, JSON.stringify(state));
        } catch (error) {
            console.error("Unable to save draft:", error);
        }
    }

    function loadDraft() {
        const defaultState = {
            personal_info: { full_name: "", email: "", phone: "", location: "", linkedin: "", portfolio: "" },
            career_info: { target_position: "", career_objective: "", professional_summary: "" },
            education: [], experience: [],
            skills: { technical_skills: [], soft_skills: [], programming_languages: [], tools: [], technologies: [] },
            certifications: [], projects: [], achievements: [],
            template: "reverse_chronological",
        };

        try {
            const saved = localStorage.getItem(DRAFT_KEY);
            if (!saved) return defaultState;

            const parsed = JSON.parse(saved);
            return { ...defaultState, ...parsed, skills: { ...defaultState.skills, ...parsed.skills } };

        } catch (error) {
            return defaultState;
        }
    }

    let statusTimer = null;

    function showStatus(message, type) {
        statusBanner.textContent = message;
        statusBanner.className = `status-banner show ${type}`;

        clearTimeout(statusTimer);
        statusTimer = setTimeout(() => statusBanner.classList.remove("show"), 5000);
    }

    function escapeHtml(value) {
        const div = document.createElement("div");
        div.textContent = value == null ? "" : String(value);
        return div.innerHTML;
    }

    function escapeAttr(value) {
        return escapeHtml(value).replace(/"/g, "&quot;");
    }

});

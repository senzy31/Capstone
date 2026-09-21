// ======================================================
// APPLY FLOW - shared by dashboard.html, Jobs.html and Application.html
//
//   Internal jobs (posted by a JobLink employer):
//     POST /api/jobs/{id}/apply creates an application the employer receives.
//   External jobs (imported from JSearch - no employer on JobLink):
//     the same call records the click and returns the original posting's URL.
//     We open it in a new tab, then - when the user comes back - ask whether
//     they finished applying there.
//
// Everything lives on one global, ApplyFlow, so it can't clash with page scripts.
// ======================================================
(function () {
    "use strict";

    // The login token and authFetch are shared with the other pages: see ApiClient.js.
    if (!window.ApiClient) {
        throw new Error("ApplyFlow needs ApiClient.js loaded first.");
    }

    const { API, EXPIRED_KEY, getToken, authFetch } = window.ApiClient;

    const PENDING_KEY = "joblink.pendingApply";

    const escapeText = (value) => String(value ?? "")
        .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;").replace(/'/g, "&#039;");


    // ======================================================
    // WHAT THE BUTTON LOOKS LIKE
    // ======================================================

    // Jobs from JSearch are external. Internal jobs (employer-posted) say so.
    function isExternal(job) {
        return job.joblink_source !== "Internal";
    }

    function publisherLabel(job) {
        return (job.job_publisher || "").trim() || "the original site";
    }

    function applyButtonHtml(job) {

        const jobId = escapeText(job.job_id || "");

        if (!isExternal(job)) {
            return `<button class="btn btn-primary apply-job-btn" data-job-id="${jobId}">Apply</button>`;
        }

        const publisher = escapeText(publisherLabel(job));

        return `
            <button class="btn btn-primary apply-job-btn apply-external" data-job-id="${jobId}"
                    title="Opens ${publisher} in a new tab">
                Apply on ${publisher}
                <i class="fa-solid fa-arrow-up-right-from-square"></i>
            </button>
        `;

    }

    function externalNoteHtml(job) {

        if (!isExternal(job)) {
            return "";
        }

        return `
            <p class="external-note">
                <i class="fa-solid fa-circle-info"></i>
                This job is posted on ${escapeText(publisherLabel(job))}.
                You'll finish your application there.
            </p>
        `;

    }


    // ======================================================
    // TOAST
    // ======================================================

    function showToast(text, type = "success", link = null) {

        const toast = document.createElement("div");

        toast.className = `af-toast af-toast-${type}`;
        toast.setAttribute("role", type === "error" ? "alert" : "status");

        const message = document.createElement("span");
        message.textContent = text;
        toast.appendChild(message);

        // Only https links ever get shown (the server only returns https).
        if (link && /^https:\/\//i.test(link.href)) {

            const anchor = document.createElement("a");

            anchor.href = link.href;
            anchor.target = "_blank";
            anchor.rel = "noopener noreferrer";
            anchor.textContent = link.label;

            toast.appendChild(anchor);

        }

        document.body.appendChild(toast);

        setTimeout(() => toast.remove(), link ? 12000 : type === "error" ? 6000 : 4000);

    }


    // ======================================================
    // APPLY
    // ======================================================

    const inFlight = new Set();

    // When the user last left this tab / window. Used to tell whether they
    // went off to the job site between clicking Apply and the answer coming back.
    let lastLeftAt = 0;

    // Call this straight from a click handler. Returns the API's answer, or null.
    async function apply(job) {

        const key = job.joblink_job_id || job.job_id;

        if (inFlight.has(key)) {
            return null;
        }

        const clickedAt = Date.now();
        const external = isExternal(job);

        // Open the tab NOW, synchronously inside the click, so popup blockers
        // allow it. It's pointed at the job site once we know where that is.
        let tab = null;

        if (external) {
            tab = window.open("", "_blank");

            if (tab) {
                prepareTab(tab, publisherLabel(job));
            }
        }

        inFlight.add(key);

        try {

            if (!job.joblink_job_id) {
                throw new Error("This job isn't ready to apply to yet. Please run your search again.");
            }

            const response = await authFetch(
                `${API}/jobs/${encodeURIComponent(job.joblink_job_id)}/apply`,
                { method: "POST" }
            );

            const result = await response.json().catch(() => ({}));

            if (!response.ok) {
                throw new Error(result.message || `Couldn't apply (${response.status}).`);
            }

            return handleResult(job, result, tab, clickedAt);

        } catch (error) {

            if (tab && !tab.closed) {
                tab.close();
            }

            showToast(error.message || "Something went wrong. Please try again.", "error");

            return null;

        } finally {

            inFlight.delete(key);

        }

    }

    // A blank tab that says what's happening, and can't reach back into JobLink.
    function prepareTab(tab, publisher) {

        try {

            // Same protection as rel="noopener" - but window.open(..., "noopener")
            // returns null, which would leave us unable to navigate the tab later.
            tab.opener = null;

            tab.document.title = `Opening ${publisher}...`;

            const message = tab.document.createElement("p");

            message.textContent = `Taking you to ${publisher}...`;

            tab.document.body.style.cssText = "font-family:sans-serif;padding:40px;color:#555";
            tab.document.body.appendChild(message);

        } catch {
            // Cosmetic only.
        }

    }

    function handleResult(job, result, tab, clickedAt) {

        if (result.type === "external") {

            const url = String(result.redirectUrl || "");

            // The server only ever returns https links; refuse anything else anyway.
            if (!/^https:\/\//i.test(url)) {
                throw new Error("The job site sent back a link that can't be opened safely.");
            }

            const publisher = (result.publisher || "").trim() || publisherLabel(job);

            // Only ask "did you finish?" while they haven't confirmed yet.
            if (result.status === "Redirected") {

                addPending({
                    applicationId: result.applicationId,
                    jobTitle: job.job_title || "this job",
                    publisher,
                    left: document.hidden || lastLeftAt >= clickedAt
                });

            }

            if (tab && !tab.closed) {

                tab.location.href = url;

                showToast(`Opening ${publisher}. Come back here when you've finished applying.`, "success");

            } else {

                // Popup blocked (or the tab was closed): let them open it themselves.
                showToast(`Your browser blocked the new tab.`, "info", { href: url, label: `Open ${publisher}` });

            }

        } else if (result.alreadyApplied) {

            showToast("You already applied to this job.", "info");

        } else {

            showToast("Application submitted! The employer has been notified.", "success");

        }

        window.dispatchEvent(new CustomEvent("joblink:application-updated", { detail: result }));

        return result;

    }


    // ======================================================
    // "DID YOU FINISH APPLYING?"
    // ======================================================

    function getPending() {

        try {

            const list = JSON.parse(localStorage.getItem(PENDING_KEY));

            return Array.isArray(list) ? list : [];

        } catch {

            return [];

        }

    }

    function savePending(list) {

        if (list.length === 0) {
            localStorage.removeItem(PENDING_KEY);
        } else {
            localStorage.setItem(PENDING_KEY, JSON.stringify(list));
        }

    }

    function addPending(item) {

        savePending([...getPending().filter(p => p.applicationId !== item.applicationId), item]);

    }

    function removePending(applicationId) {

        savePending(getPending().filter(p => p.applicationId !== applicationId));

    }

    // They left JobLink after clicking Apply - so when they return, ask.
    function noteLeft() {

        lastLeftAt = Date.now();

        const list = getPending();

        if (list.some(p => !p.left)) {
            savePending(list.map(p => ({ ...p, left: true })));
        }

    }

    let modalOpen = false;

    function maybePrompt() {

        if (modalOpen || !getToken()) {
            return;
        }

        const next = getPending().find(p => p.left);

        if (next) {
            showConfirmModal(next);
        }

    }

    function showConfirmModal(item) {

        modalOpen = true;

        const overlay = document.createElement("div");

        overlay.className = "af-overlay";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.setAttribute("aria-labelledby", "afConfirmTitle");

        overlay.innerHTML = `
            <div class="af-modal">
                <h3 id="afConfirmTitle">Did you finish applying?</h3>
                <p>
                    Did you finish applying for
                    <strong>${escapeText(item.jobTitle)}</strong>
                    on <strong>${escapeText(item.publisher)}</strong>?
                </p>
                <p class="af-error" hidden></p>
                <div class="af-actions">
                    <button type="button" class="af-btn af-btn-secondary" data-answer="no">Not yet</button>
                    <button type="button" class="af-btn af-btn-primary" data-answer="yes">Yes, I applied</button>
                </div>
            </div>
        `;

        document.body.appendChild(overlay);

        const buttons = [...overlay.querySelectorAll("button")];
        const error = overlay.querySelector(".af-error");

        buttons[1].focus();

        function close() {
            document.removeEventListener("keydown", onKey);
            overlay.remove();
            modalOpen = false;
        }

        async function answer(applied) {

            buttons.forEach(button => { button.disabled = true; });
            error.hidden = true;

            try {

                const response = await authFetch(
                    `${API}/applications/${encodeURIComponent(item.applicationId)}/confirm-external`,
                    {
                        method: "PATCH",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ applied })
                    }
                );

                // 404 / 409: it was already confirmed or withdrawn elsewhere - nothing left to ask.
                if (!response.ok && response.status !== 404 && response.status !== 409) {
                    const body = await response.json().catch(() => ({}));
                    throw new Error(body.message || `Couldn't save that (${response.status}).`);
                }

                removePending(item.applicationId);

                close();

                showToast(
                    applied
                        ? "Marked as applied. Good luck!"
                        : "No problem - you can mark it as applied later from your Applications page.",
                    "success"
                );

                window.dispatchEvent(new CustomEvent("joblink:application-updated", { detail: { applicationId: item.applicationId } }));

                maybePrompt();

            } catch (failure) {

                buttons.forEach(button => { button.disabled = false; });

                error.textContent = failure.message || "Couldn't save that. Please try again.";
                error.hidden = false;

            }

        }

        function onKey(event) {

            if (event.key === "Escape" && !buttons[0].disabled) {
                answer(false);
            }

        }

        document.addEventListener("keydown", onKey);

        overlay.querySelector('[data-answer="no"]').addEventListener("click", () => answer(false));
        overlay.querySelector('[data-answer="yes"]').addEventListener("click", () => answer(true));

    }

    // Watches for the user leaving and coming back. Call once per page.
    function initReturnPrompt() {

        document.addEventListener("visibilitychange", () => {

            if (document.hidden) {
                noteLeft();
            } else {
                maybePrompt();
            }

        });

        window.addEventListener("blur", noteLeft);
        window.addEventListener("focus", maybePrompt);

        // e.g. they closed JobLink, then opened it again later.
        maybePrompt();

    }


    window.ApplyFlow = {
        API,
        authFetch,
        getToken,
        isExternal,
        publisherLabel,
        applyButtonHtml,
        externalNoteHtml,
        apply,
        showToast,
        initReturnPrompt,
        forgetPending: removePending,
        EXPIRED_KEY
    };

})();

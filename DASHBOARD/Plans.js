// ======================================================
// PLANS PAGE
//
// Free vs Premium, your current plan and usage, and the SIMULATED checkout: "Activate
// Premium (Demo)" grants Premium and charges nothing. What each plan allows is decided
// by the API (GET /api/subscription is the source of truth for your plan); this page only
// shows it and calls upgrade / cancel.
// ======================================================
document.addEventListener("DOMContentLoaded", () => {

    const API = `${ApiClient.API}/Subscription`;

    const escapeText = (value) => String(value ?? "")
        .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;").replace(/'/g, "&#039;");


    // ------------------------------------------------------
    // WHO'S HERE? (job seekers only - employers have no plans)
    // ------------------------------------------------------

    let currentUser = null;

    try {
        currentUser = JSON.parse(localStorage.getItem("user") || "null");
    } catch (error) {
        currentUser = null;
    }

    if (!currentUser || !ApiClient.getToken()) {
        ApiClient.endSession();
        return;
    }

    if (currentUser.role === "employer") {
        window.location.replace("../Employer Dashboard/dashboard.html");
        return;
    }

    document.getElementById("userName").textContent = currentUser.fullName || "User";

    document.getElementById("logoutBtn").addEventListener("click", (event) => {
        event.preventDefault();
        localStorage.removeItem("user");
        localStorage.removeItem("token");
        window.location.href = "../LOGIN/login.html";
    });


    // ------------------------------------------------------
    // WHAT EACH PLAN GETS
    //
    // One row per feature. `soon: true` marks a feature whose page or screen isn't built yet, so the
    // comparison doesn't promise more than the app delivers today - drop the flag when it ships.
    // ------------------------------------------------------

    const FEATURES = [
        { label: "Job listings, filters and recommendations", free: "Included", premium: "Included" },
        { label: "Overall suitability score", free: "Included", premium: "Included" },
        { label: "Standard resume formats (Harvard, Functional, Reverse Chronological)", free: "Included", premium: "Included", soon: true },
        { label: "Resume PDF downloads", free: "Unlimited", premium: "Unlimited" },
        { label: "Saved resume versions", free: "1", premium: "Up to 10", soon: true },
        { label: "Saved jobs", free: "Up to 10", premium: "Unlimited", soon: true },
        { label: "Advanced resume templates", free: "Locked", premium: "Included", soon: true },
        { label: "Detailed score breakdown (skills, salary, location) and matched skills", free: "Overall % only", premium: "Included" },
        { label: "Missing skills analysis", free: "Locked", premium: "Included", soon: true },
        { label: "Dashboard ads", free: "Shown", premium: "Hidden" },
        { label: "Priority Application (jobs posted on JobLink)", free: "Not included", premium: "Included", soon: true }
    ];

    const LOCKED_VALUES = new Set(["Locked", "Not included", "Overall % only", "Shown"]);

    function renderFeatures(listId, plan) {

        document.getElementById(listId).innerHTML = FEATURES.map(row => {

            const value = row[plan];

            const locked = LOCKED_VALUES.has(value);

            return `
                <li class="${locked ? "is-locked" : ""}">
                    <i class="fa-solid ${locked ? "fa-lock" : "fa-check"}"></i>
                    <span class="feature-label">
                        ${escapeText(row.label)}
                        ${row.soon ? '<span class="soon-badge">Coming soon</span>' : ""}
                    </span>
                    <span class="feature-value">${escapeText(value)}</span>
                </li>
            `;

        }).join("");

    }

    renderFeatures("freeFeatures", "free");
    renderFeatures("premiumFeatures", "premium");


    // ------------------------------------------------------
    // YOUR PLAN
    // ------------------------------------------------------

    const statusBox = document.getElementById("planStatus");
    const optionsBox = document.getElementById("billingOptions");
    const activateBtn = document.getElementById("activateBtn");
    const cancelBtn = document.getElementById("cancelBtn");
    const message = document.getElementById("checkoutMessage");
    const demoNote = document.getElementById("demoNote");
    const checkoutTitle = document.getElementById("checkoutTitle");

    let plan = null;              // the last GET /api/subscription answer
    let chosenBilling = "Monthly";
    let cancelArmed = false;
    let cancelTimer = null;

    function formatDate(iso) {

        return new Date(iso).toLocaleDateString("en-PH", { year: "numeric", month: "long", day: "numeric" });

    }

    function usageLine(label, used, limit) {

        if (limit === null) {
            return `<li><strong>${label}:</strong> ${used} <span class="usage-note">(no limit)</span></li>`;
        }

        const over = used > limit;

        return `
            <li class="${over ? "usage-over" : ""}">
                <strong>${label}:</strong> ${used} of ${limit}
                ${over ? '<span class="usage-note">- more than a Free plan keeps. You keep what you have; you just can\'t add more.</span>' : ""}
            </li>
        `;

    }

    function renderStatus() {

        let headline;

        if (plan.isPremium && plan.cancelled) {
            headline = `Premium is cancelled. You keep it until <strong>${formatDate(plan.premiumUntil)}</strong>, then you're on Free.`;
        } else if (plan.isPremium) {
            headline = `You're on <strong>Premium</strong> (${escapeText(plan.billing)}) until <strong>${formatDate(plan.premiumUntil)}</strong>.`;
        } else if (plan.premiumUntil) {
            headline = `Your Premium ended on <strong>${formatDate(plan.premiumUntil)}</strong>. You're on the <strong>Free</strong> plan.`;
        } else {
            headline = "You're on the <strong>Free</strong> plan.";
        }

        statusBox.className = `plan-status ${plan.isPremium ? "is-premium" : ""}`;

        statusBox.innerHTML = `
            <p class="plan-headline">
                <i class="fa-solid ${plan.isPremium ? "fa-crown" : "fa-user"}"></i>
                <span>${headline}</span>
            </p>
            <ul class="plan-usage">
                ${usageLine("Saved resumes", plan.usage.resumeVersions, plan.limits.resumeVersions)}
                ${usageLine("Saved jobs", plan.usage.savedJobs, plan.limits.savedJobs)}
            </ul>
        `;

        document.getElementById("freeCard").classList.toggle("is-current", !plan.isPremium);
        document.getElementById("premiumCard").classList.toggle("is-current", plan.isPremium);

    }


    // ------------------------------------------------------
    // CHECKOUT (simulated)
    // ------------------------------------------------------

    function savingText(option) {

        const monthly = plan.plans.find(p => p.billing === "Monthly");

        if (!monthly || option.months === 1) {
            return "";
        }

        const full = monthly.pricePhp * option.months;

        const percent = Math.round((1 - option.pricePhp / full) * 100);

        return percent > 0 ? `Save ${percent}%` : "";

    }

    function renderOptions() {

        optionsBox.innerHTML = plan.plans.map(option => {

            const saving = savingText(option);

            const checked = option.billing === chosenBilling;

            return `
                <label class="billing-option ${checked ? "is-chosen" : ""}">
                    <input type="radio" name="billing" value="${escapeText(option.billing)}" ${checked ? "checked" : ""}>
                    <span class="billing-name">${escapeText(option.billing)}</span>
                    <span class="billing-price">₱${option.pricePhp}</span>
                    <span class="billing-per">${option.months === 1 ? "for 1 month" : `for ${option.months} months`}</span>
                    ${saving ? `<span class="billing-saving">${saving}</span>` : ""}
                </label>
            `;

        }).join("");

    }

    optionsBox.addEventListener("change", (event) => {

        if (event.target.name === "billing") {
            chosenBilling = event.target.value;
            renderOptions();
        }

    });

    function renderCheckout() {

        renderOptions();

        checkoutTitle.textContent = plan.isPremium ? "Extend Premium" : "Activate Premium";

        activateBtn.textContent = plan.isPremium ? "Extend Premium (Demo)" : "Activate Premium (Demo)";

        // The server can switch the demo checkout off (Subscription:DemoCheckout).
        const enabled = plan.demoCheckout !== false;

        activateBtn.hidden = !enabled;
        optionsBox.hidden = !enabled;
        demoNote.hidden = !enabled;

        if (!enabled) {
            showMessage("Checkout isn't available on this server right now.", true);
        }

        cancelBtn.hidden = !(plan.isPremium && !plan.cancelled);

        disarmCancel();

    }

    function showMessage(text, isError = false) {

        message.textContent = text;
        message.className = `checkout-message ${isError ? "is-error" : "is-ok"}`;
        message.hidden = false;

    }

    function hideMessage() {

        message.hidden = true;

    }

    function disarmCancel() {

        cancelArmed = false;
        clearTimeout(cancelTimer);
        cancelBtn.textContent = "Cancel Premium";
        cancelBtn.classList.remove("is-armed");

    }

    function apply(newPlan) {

        plan = newPlan;

        renderStatus();
        renderCheckout();

    }

    async function readError(response, fallback) {

        const body = await response.json().catch(() => ({}));

        return body.message || fallback;

    }

    activateBtn.addEventListener("click", async () => {

        activateBtn.disabled = true;
        hideMessage();

        try {

            const response = await ApiClient.authFetch(`${API}/upgrade`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ billing: chosenBilling })
            });

            if (!response.ok) {
                showMessage(await readError(response, `Couldn't activate Premium (${response.status}).`), true);
                return;
            }

            apply(await response.json());

            showMessage(`Premium is active until ${formatDate(plan.premiumUntil)}. This was a demo - nothing was charged.`);

        } catch (error) {

            showMessage("Couldn't reach the server. Check that the API is running and try again.", true);

        } finally {

            activateBtn.disabled = false;

        }

    });

    // Cancelling asks twice (the second click within a few seconds confirms it).
    cancelBtn.addEventListener("click", async () => {

        if (!cancelArmed) {

            cancelArmed = true;
            cancelBtn.textContent = "Click again to confirm";
            cancelBtn.classList.add("is-armed");
            cancelTimer = setTimeout(disarmCancel, 5000);

            return;

        }

        disarmCancel();
        cancelBtn.disabled = true;
        hideMessage();

        try {

            const response = await ApiClient.authFetch(`${API}/cancel`, { method: "POST" });

            if (!response.ok) {
                showMessage(await readError(response, `Couldn't cancel (${response.status}).`), true);
                return;
            }

            apply(await response.json());

            showMessage(`Premium is cancelled. You keep it until ${formatDate(plan.premiumUntil)}.`);

        } catch (error) {

            showMessage("Couldn't reach the server. Check that the API is running and try again.", true);

        } finally {

            cancelBtn.disabled = false;

        }

    });


    // ------------------------------------------------------
    // LOAD
    // ------------------------------------------------------

    async function load() {

        try {

            const response = await ApiClient.authFetch(API);

            if (response.status === 403) {
                // A logged-in employer session that got here anyway.
                window.location.replace("../Employer Dashboard/dashboard.html");
                return;
            }

            if (!response.ok) {
                throw new Error(`Couldn't load your plan (${response.status})`);
            }

            apply(await response.json());

        } catch (error) {

            console.error(error);

            statusBox.innerHTML = '<p class="plan-loading is-error">Couldn\'t load your plan. Check that the API is running.</p>';

        }

    }

    load();

});

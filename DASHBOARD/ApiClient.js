// ======================================================
// API CLIENT - shared by every page that calls the API as the logged-in user
//
//   authFetch(url, options)  fetch() with the login token attached. A missing
//                            or rejected token sends the user back to log in.
//   saveAccount(details)     PUT /api/User (name / email). Changing the email
//                            needs the current password: the server says so,
//                            we ask for it, then try again.
//   askPassword(options)     the small "enter your password" dialog
//   getPlan()                your plan, from GET /api/subscription (asked once per page)
//   showUpgradePrompt(body)  the "upgrade to Premium" dialog - shown for you whenever an API
//                            call answers 403 with upgradeRequired: true
//
// Everything lives on one global, ApiClient, so it can't clash with page scripts.
// ======================================================
(function () {
    "use strict";

    const API = "https://localhost:7142/api";
    const LOGIN_PAGE = "../LOGIN/login.html";
    const EXPIRED_KEY = "joblink.sessionExpired";


    // ======================================================
    // LOGIN TOKEN
    // ======================================================

    function getToken() {
        return localStorage.getItem("token");
    }

    // The saved session has no valid token (never had one, or it expired).
    function endSession() {
        localStorage.removeItem("token");
        localStorage.removeItem("user");
        sessionStorage.setItem(EXPIRED_KEY, "1");
        window.location.href = LOGIN_PAGE;
    }

    // fetch() with the login token attached. A missing or rejected token sends
    // the user back to log in.
    async function authFetch(url, options = {}) {

        const token = getToken();

        if (!token) {
            endSession();
            throw new Error("Please log in again.");
        }

        const response = await fetch(url, {
            ...options,
            headers: { ...(options.headers || {}), Authorization: `Bearer ${token}` }
        });

        if (response.status === 401) {
            endSession();
            throw new Error("Your session expired. Please log in again.");
        }

        // A plan limit (a Free user past their cap): show the upgrade prompt instead of leaving
        // the page with a raw error. The response itself is returned untouched, so the page can
        // still say what it wants. Pass { noUpgradePrompt: true } to handle it yourself.
        if (response.status === 403 && !options.noUpgradePrompt) {
            response.clone().json()
                .then(body => { if (body && body.upgradeRequired === true) { showUpgradePrompt(body); } })
                .catch(() => {});
        }

        return response;
    }


    // ======================================================
    // "ENTER YOUR PASSWORD" DIALOG
    // ======================================================

    // Resolves with what they typed, or null if they cancel.
    function askPassword({ title = "Confirm your password", message = "", error = "" } = {}) {

        return new Promise(resolve => {

            const overlay = document.createElement("div");

            overlay.className = "ac-overlay";
            overlay.setAttribute("role", "dialog");
            overlay.setAttribute("aria-modal", "true");
            overlay.setAttribute("aria-labelledby", "acPasswordTitle");

            overlay.innerHTML = `
                <form class="ac-modal">
                    <h3 id="acPasswordTitle"></h3>
                    <p class="ac-message"></p>
                    <input type="password" class="ac-input" autocomplete="current-password"
                           aria-label="Current password" placeholder="Current password" required>
                    <p class="ac-error" hidden></p>
                    <div class="ac-actions">
                        <button type="button" class="ac-btn ac-btn-secondary" data-cancel>Cancel</button>
                        <button type="submit" class="ac-btn ac-btn-primary">Confirm</button>
                    </div>
                </form>
            `;

            // Set as text, never as HTML: the message can come from the server.
            overlay.querySelector("h3").textContent = title;
            overlay.querySelector(".ac-message").textContent = message;

            const errorLine = overlay.querySelector(".ac-error");

            if (error) {
                errorLine.textContent = error;
                errorLine.hidden = false;
            }

            const input = overlay.querySelector(".ac-input");

            function finish(value) {
                document.removeEventListener("keydown", onKey);
                overlay.remove();
                resolve(value);
            }

            function onKey(event) {
                if (event.key === "Escape") {
                    finish(null);
                }
            }

            overlay.querySelector("form").addEventListener("submit", event => {
                event.preventDefault();
                finish(input.value);
            });

            overlay.querySelector("[data-cancel]").addEventListener("click", () => finish(null));

            overlay.addEventListener("mousedown", event => {
                if (event.target === overlay) {
                    finish(null);
                }
            });

            document.addEventListener("keydown", onKey);
            document.body.appendChild(overlay);

            input.focus();

        });

    }


    // ======================================================
    // YOUR ACCOUNT
    // ======================================================

    // Saves name and/or email. Resolves with the saved account. Rejects with:
    //   error.cancelled   they closed the password dialog - nothing was saved
    //   error.fromServer  the server said why (bad email, email taken...) and
    //                     error.message is fit to show as it is
    async function saveAccount(details) {

        let currentPassword;   // stays undefined until the server asks for it
        let complaint = "";

        for (;;) {

            const response = await authFetch(`${API}/User`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ...details, currentPassword })
            });

            if (response.ok) {
                return (await response.json()).user;
            }

            const body = await response.json().catch(() => ({}));

            if (body.code === "password_required" || body.code === "wrong_password") {

                complaint = body.code === "wrong_password" ? body.message : "";

                currentPassword = await askPassword({
                    message: "You're changing the email you log in with. Enter your current password to confirm it's you.",
                    error: complaint
                });

                if (currentPassword === null) {
                    throw Object.assign(new Error("Nothing was changed - your email stays the same."), { cancelled: true });
                }

                continue;

            }

            throw Object.assign(
                new Error(body.message || `Couldn't save your details (${response.status}).`),
                { fromServer: Boolean(body.message) }
            );

        }

    }


    // ======================================================
    // YOUR PLAN
    // ======================================================

    let planRequest = null;

    // The answer of GET /api/subscription (or null if it can't be read). Asked once per page
    // load; pass true to ask again (after an upgrade, say). The server is the only judge of
    // the plan - a page only uses this to decide what to show.
    function getPlan(refresh = false) {

        if (!planRequest || refresh) {

            planRequest = authFetch(`${API}/Subscription`, { noUpgradePrompt: true })
                .then(response => response.ok ? response.json() : null)
                .catch(() => null);

        }

        return planRequest;

    }


    // ======================================================
    // "UPGRADE TO PREMIUM" DIALOG
    // ======================================================

    function showUpgradePrompt(body = {}) {

        if (document.querySelector(".ac-upgrade")) {
            return;
        }

        const overlay = document.createElement("div");

        overlay.className = "ac-overlay ac-upgrade";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.setAttribute("aria-labelledby", "acUpgradeTitle");

        overlay.innerHTML = `
            <div class="ac-modal">
                <h3 id="acUpgradeTitle"><i class="fa-solid fa-crown"></i> Upgrade to Premium</h3>
                <p class="ac-message"></p>
                <div class="ac-actions">
                    <button type="button" class="ac-btn ac-btn-secondary" data-close>Not now</button>
                    <a class="ac-btn ac-btn-primary" href="Plans.html">See plans</a>
                </div>
            </div>
        `;

        // Set as text, never as HTML: the message comes from the server.
        overlay.querySelector(".ac-message").textContent =
            body.message || "That is part of the Premium plan.";

        function close() {
            document.removeEventListener("keydown", onKey);
            overlay.remove();
        }

        function onKey(event) {
            if (event.key === "Escape") {
                close();
            }
        }

        overlay.querySelector("[data-close]").addEventListener("click", close);

        overlay.addEventListener("mousedown", event => {
            if (event.target === overlay) {
                close();
            }
        });

        document.addEventListener("keydown", onKey);
        document.body.appendChild(overlay);

        overlay.querySelector("a").focus();

    }


    window.ApiClient = {
        API,
        EXPIRED_KEY,
        getToken,
        endSession,
        authFetch,
        askPassword,
        saveAccount,
        getPlan,
        showUpgradePrompt
    };

})();

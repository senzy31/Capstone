// Free / Premium in the browser: the Plans page (prices, the simulated checkout, cancel, every
// state and error), the "Plans" link in the sidebar, the placeholder ads on the dashboard
// (shown on Free, gone on Premium, nothing loaded from another site) and the upgrade prompt
// that appears when the API says a Free limit was reached. API faked - what each plan is
// ALLOWED to do is decided by the API and tested there; this checks what the pages show and send.
const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob, REPO_ROOT } = require("./helpers");

const PRICES = [{ billing: "Monthly", months: 1, pricePhp: 99 }, { billing: "Quarterly", months: 3, pricePhp: 249 }, { billing: "Annual", months: 12, pricePhp: 899 }];
const ANNUAL_UNTIL = "2027-09-22T12:00:00Z";
const LONG_DATE = "September 22, 2027";

// GET /api/subscription as the server answers it.
function plan({ premium = false, billing = null, until = null, cancelled = false, usage = { resumeVersions: 1, savedJobs: 4 }, demoCheckout = true } = {}) {
    return {
        plan: premium ? "Premium" : "Free", isPremium: premium, billing, premiumStartedAt: until ? "2026-09-22T12:00:00Z" : null, premiumUntil: until, cancelled,
        limits: premium ? { resumeVersions: 10, savedJobs: null } : { resumeVersions: 1, savedJobs: 10 },
        usage,
        features: { showAds: !premium, detailedScore: premium, missingSkills: premium, advancedTemplates: premium, priorityApplication: premium },
        plans: PRICES, demoCheckout,
    };
}

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Plans page, ads and the upgrade prompt");

    // ----- the Plans page -------------------------------------------------------

    // `state` is what the fake server holds; upgrade / cancel change it the way the real one does.
    async function openPlans({ state = plan(), upgrade, cancel, getStatus = 200, user, token } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { user, token });
        const api = await mockApi(session.context);
        const errors = [];
        session.page.on("pageerror", e => errors.push(String(e)));
        api.on("GET", /^\/Subscription$/, () => getStatus === 200 ? { json: state } : { status: getStatus, json: { message: "no" } });
        api.on("POST", /^\/Subscription\/upgrade$/, upgrade || (call => {
            Object.assign(state, plan({ premium: true, billing: call.body.billing, until: ANNUAL_UNTIL, usage: state.usage }));
            return { json: state };
        }));
        api.on("POST", /^\/Subscription\/cancel$/, cancel || (() => {
            Object.assign(state, plan({ premium: true, billing: state.billing, until: state.premiumUntil, cancelled: true, usage: state.usage }));
            return { json: state };
        }));
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Plans.html`);
        return { ...session, api, errors, state };
    }
    const headline = page => page.locator(".plan-headline").innerText().then(s => s.replace(/\s+/g, " ").trim());
    const loaded = page => page.waitForSelector(".plan-headline, .plan-loading.is-error");
    const usage = page => page.$$eval(".plan-usage li", lis => lis.map(li => li.innerText.replace(/\s+/g, " ").trim()));

    t.section("Plans page: a Free member");
    {
        const { context, page, api, errors } = await openPlans();
        await loaded(page);
        t.check("the plan is read with the login token", api.callsTo("GET", /^\/Subscription$/).map(c => c.headers.authorization), ["Bearer test-token"]);
        t.check("says they're on Free", await headline(page), "You're on the Free plan.");
        t.check("shows their use against the Free limits", await usage(page), ["Saved resumes: 1 of 1", "Saved jobs: 4 of 10"]);
        t.check("the Free card is marked as current, the Premium card is not",
            [await page.locator("#freeCard.is-current").count(), await page.locator("#premiumCard.is-current").count()], [1, 0]);
        t.check("the three billing options show the prices in pesos, with the saving on the longer ones",
            await page.$$eval(".billing-option", os => os.map(o => o.innerText.replace(/\s+/g, " ").trim())),
            ["Monthly ₱99 for 1 month", "Quarterly ₱249 for 3 months Save 16%", "Annual ₱899 for 12 months Save 24%"]);
        t.check("Monthly is chosen to start with", await page.locator(".billing-option.is-chosen .billing-name").innerText(), "Monthly");
        t.check("the checkout button says Activate Premium (Demo)", (await page.locator("#activateBtn").innerText()).trim(), "Activate Premium (Demo)");
        t.check("the demo note is on the page, in full", (await page.locator("#demoNote").innerText()).trim(), "Demo only. No real payment is processed.");
        t.check("there is nothing to cancel on Free", await page.locator("#cancelBtn").isHidden(), true);
        t.check("no card number, expiry or CVV field anywhere (nothing real is charged)",
            await page.locator("input[type=text], input[type=tel], input[type=number], input[autocomplete^='cc-'], input[name*='card' i]").count(), 0);

        const rows = await page.$$eval("#freeFeatures li", lis => lis.map(li => [li.querySelector(".feature-label").innerText.replace(/\s+/g, " ").replace(" Coming soon", "").trim(), li.querySelector(".feature-value").innerText.trim()]));
        const premiumRows = await page.$$eval("#premiumFeatures li", lis => lis.map(li => li.querySelector(".feature-value").innerText.trim()));
        t.check("Free: what it includes and what it doesn't", rows.map(r => r.join(": ")), [
            "Job listings, filters and recommendations: Included",
            "Overall suitability score: Included",
            "Standard resume formats (Harvard, Functional, Reverse Chronological): Included",
            "Resume PDF downloads: Unlimited",
            "Saved resume versions: 1",
            "Saved jobs: Up to 10",
            "Advanced resume templates (ATS-Friendly): Locked",
            "Detailed score breakdown (skills, salary, location) and matched skills: Overall % only",
            "Missing skills analysis: Locked",
            "Dashboard ads: Shown",
            "Priority Application (jobs posted on JobLink): Not included",
        ]);
        t.check("Premium: the same rows, upgraded (PDF downloads stay unlimited on both)", premiumRows,
            ["Included", "Included", "Included", "Unlimited", "Up to 10", "Unlimited", "Included", "Included", "Included", "Hidden", "Included"]);
        t.check("features that aren't built yet say so instead of promising them (4 rows, in both columns) - resume formats and advanced templates shipped, so they no longer say Coming soon", [await page.locator("#freeFeatures .soon-badge").count(), await page.locator("#premiumFeatures .soon-badge").count()], [4, 4]);
        t.check("locked rows get a lock, included rows a tick", [await page.locator("#freeFeatures li.is-locked").count(), await page.locator("#premiumFeatures li.is-locked").count()], [5, 0]);
        t.check("the Plans link is the active one in the sidebar", (await page.locator(".nav-links li.active").innerText()).trim(), "Plans");
        t.check("no script errors; nothing was called that we didn't fake", [errors, api.unmocked], [[], []]);
        await context.close();
    }

    t.section("Plans page: activating Premium (simulated) and cancelling");
    {
        const { context, page, api } = await openPlans();
        await loaded(page);
        await page.click(".billing-option:has-text('Annual')");
        t.check("clicking a billing option chooses it", await page.locator(".billing-option.is-chosen .billing-name").innerText(), "Annual");

        await page.click("#activateBtn");
        await page.getByText("This was a demo - nothing was charged.").waitFor();

        const calls = api.callsTo("POST", /^\/Subscription\/upgrade$/);
        t.check("exactly one upgrade call, carrying only the billing period - no plan, date or price", calls.map(c => c.body), [{ billing: "Annual" }]);
        t.check("...with the login token", calls[0].headers.authorization, "Bearer test-token");
        t.check("the page now says Premium until the end date", await headline(page), `You're on Premium (Annual) until ${LONG_DATE}.`);
        t.check("the confirmation says nothing was charged", (await page.locator("#checkoutMessage").innerText()).trim(), `Premium is active until ${LONG_DATE}. This was a demo - nothing was charged.`);
        t.check("usage now shows no limit on saved jobs and 10 resumes", await usage(page), ["Saved resumes: 1 of 10", "Saved jobs: 4 (no limit)"]);
        t.check("the Premium card is current; the button now extends; the demo note is still there",
            [await page.locator("#premiumCard.is-current").count(), (await page.locator("#activateBtn").innerText()).trim(), (await page.locator("#demoNote").innerText()).trim(), (await page.locator("#checkoutTitle").innerText()).trim()],
            [1, "Extend Premium (Demo)", "Demo only. No real payment is processed.", "Extend Premium"]);
        t.check("Cancel Premium appears", await page.locator("#cancelBtn").isVisible(), true);

        // cancelling needs a second click, so it can't happen by accident
        await page.click("#cancelBtn");
        t.check("the first click only asks to confirm - nothing was sent", [(await page.locator("#cancelBtn").innerText()).trim(), api.callsTo("POST", /^\/Subscription\/cancel$/).length], ["Click again to confirm", 0]);
        await page.click("#cancelBtn");
        await page.getByText(`Premium is cancelled. You keep it until ${LONG_DATE}.`).first().waitFor();
        t.check("the second click cancels (with the token, no body)", api.callsTo("POST", /^\/Subscription\/cancel$/).map(c => [c.headers.authorization, c.body]), [["Bearer test-token", null]]);
        t.check("the page says they keep Premium until the end date, then go to Free", await headline(page), `Premium is cancelled. You keep it until ${LONG_DATE}, then you're on Free.`);
        t.check("nothing left to cancel", await page.locator("#cancelBtn").isHidden(), true);
        t.check("every call carried the login token", api.calls.every(c => c.headers.authorization === "Bearer test-token"), true);
        t.check("nothing was called that we didn't fake", api.unmocked, []);
        await context.close();
    }

    t.section("Plans page: an armed Cancel button disarms itself");
    {
        const { context, page, api } = await openPlans({ state: plan({ premium: true, billing: "Monthly", until: ANNUAL_UNTIL }) });
        await loaded(page);
        await page.click("#cancelBtn");
        await page.waitForFunction(() => document.getElementById("cancelBtn").textContent.trim() === "Cancel Premium", null, { timeout: 8000 });
        t.check("after a few seconds it goes back to Cancel Premium and nothing was sent", api.callsTo("POST", /^\/Subscription\/cancel$/).length, 0);
        await context.close();
    }

    t.section("Plans page: a lapsed Premium, and someone over the Free limits");
    {
        const { context, page } = await openPlans({ state: plan({ until: "2026-08-01T12:00:00Z", usage: { resumeVersions: 6, savedJobs: 14 } }) });
        await loaded(page);
        t.check("says Premium ended and they're on Free", await headline(page), "Your Premium ended on August 1, 2026. You're on the Free plan.");
        const lines = await usage(page);
        t.check("what they kept is shown, with why they can't add more",
            [lines[0].startsWith("Saved resumes: 6 of 1 - more than a Free plan keeps"), lines[1].startsWith("Saved jobs: 14 of 10 - more than a Free plan keeps"), lines[0].includes("You keep what you have")], [true, true, true]);
        t.check("they can buy again", (await page.locator("#activateBtn").innerText()).trim(), "Activate Premium (Demo)");
        await context.close();
    }

    t.section("Plans page: when things go wrong");
    {
        const off = await openPlans({ upgrade: () => ({ status: 403, json: { message: "Demo checkout is switched off on this server.", code: "demo_checkout_off" } }) });
        await loaded(off.page);
        await off.page.click("#activateBtn");
        await off.page.locator(".checkout-message.is-error").waitFor();
        t.check("a refused upgrade shows the server's message as an error, stays on Free, and the button works again",
            [(await off.page.locator("#checkoutMessage").innerText()).trim(), await headline(off.page), await off.page.locator("#activateBtn").isEnabled()],
            ["Demo checkout is switched off on this server.", "You're on the Free plan.", true]);
        await off.context.close();

        const broken = await openPlans({ upgrade: () => ({ status: 500, json: {} }) });
        await loaded(broken.page);
        await broken.page.click("#activateBtn");
        await broken.page.locator(".checkout-message.is-error").waitFor();
        t.check("a server error says what happened", (await broken.page.locator("#checkoutMessage").innerText()).trim(), "Couldn't activate Premium (500).");
        await broken.context.close();

        const down = await openPlans({ upgrade: () => ({ abort: true }) });
        await loaded(down.page);
        await down.page.click("#activateBtn");
        await down.page.locator(".checkout-message.is-error").waitFor();
        t.check("an unreachable server says so", (await down.page.locator("#checkoutMessage").innerText()).trim(), "Couldn't reach the server. Check that the API is running and try again.");
        await down.context.close();

        const cantLoad = await openPlans({ getStatus: 500 });
        await loaded(cantLoad.page);
        t.check("if the plan can't be read the page says so instead of guessing", (await cantLoad.page.locator(".plan-loading.is-error").innerText()).trim(), "Couldn't load your plan. Check that the API is running.");
        await cantLoad.context.close();

        const switchedOff = await openPlans({ state: plan({ demoCheckout: false }) });
        await loaded(switchedOff.page);
        t.check("with demo checkout switched off on the server the buy button, options and demo note are gone and it says why",
            [await switchedOff.page.locator("#activateBtn").isHidden(), await switchedOff.page.locator("#billingOptions").isHidden(), await switchedOff.page.locator("#demoNote").isHidden(), (await switchedOff.page.locator("#checkoutMessage").innerText()).trim()],
            [true, true, true, "Checkout isn't available on this server right now."]);
        await switchedOff.context.close();
    }

    t.section("Plans page: who can open it");
    {
        const employer = await openPlans({ user: { userId: 9, fullName: "Acme HR", email: "hr@acme.example", role: "employer" } });
        await employer.page.waitForURL("**/Employer%20Dashboard/dashboard.html", { timeout: 8000 });
        t.check("an employer is sent to the employer dashboard without asking for a plan", [employer.page.url().endsWith("/Employer%20Dashboard/dashboard.html"), employer.api.calls.length], [true, 0]);
        await employer.context.close();

        const signedOut = await openPlans({ token: null });
        await signedOut.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("no login goes to the login page, and no API call is made", [signedOut.page.url().endsWith("/LOGIN/login.html"), signedOut.api.calls.length], [true, 0]);
        await signedOut.context.close();

        const forbidden = await openPlans({ getStatus: 403 });
        await forbidden.page.waitForURL("**/Employer%20Dashboard/dashboard.html", { timeout: 8000 });
        t.check("if the API answers 403 (an employer account) the page leaves for the employer dashboard", forbidden.page.url().endsWith("/Employer%20Dashboard/dashboard.html"), true);
        await forbidden.context.close();
    }

    // ----- the Plans link -------------------------------------------------------

    t.section("the Plans link is in the sidebar of every job seeker page");
    {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        api.on("GET", /^\/Subscription$/, () => ({ json: plan() }));
        session.page.on("dialog", d => d.accept());

        const links = {};
        for (const name of ["dashboard.html", "Jobs.html", "Application.html", "ResumeBuilder.html", "Profile.html", "Plans.html"]) {
            await session.page.goto(`${server.baseUrl}/DASHBOARD/${name}`);
            await session.page.waitForSelector(".sidebar");
            const link = session.page.locator('.nav-links a[href="Plans.html"]');   // the menu, not the ad's "remove ads" link
            links[name] = [await link.count(), (await link.first().innerText()).trim(), await link.first().locator("i.fa-crown").count()];
        }
        t.check("each page has exactly one Plans link with the crown icon", links, Object.fromEntries(
            ["dashboard.html", "Jobs.html", "Application.html", "ResumeBuilder.html", "Profile.html", "Plans.html"].map(n => [n, [1, "Plans", 1]])));

        await session.page.goto(`${server.baseUrl}/DASHBOARD/dashboard.html`);
        await session.page.click('.nav-links a[href="Plans.html"]');
        await session.page.waitForURL("**/DASHBOARD/Plans.html");
        t.check("the link opens the Plans page", session.page.url().endsWith("/DASHBOARD/Plans.html"), true);
        await session.context.close();
    }

    // ----- the ads on the dashboard ---------------------------------------------

    async function openDashboard({ planStatus = 200, state = plan(), jobs = 5, hosts } = {}) {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        const errors = [];
        session.page.on("pageerror", e => errors.push(String(e)));
        session.page.on("dialog", d => d.accept());
        if (hosts) session.page.on("request", r => { try { hosts.add(new URL(r.url()).host); } catch { /* data: urls */ } });
        api.on("GET", /^\/Subscription$/, () => planStatus === 200 ? { json: state } : { status: planStatus, json: {} });
        // The server scores the jobs; the page only shows them. (Free plan: overall score and band.)
        api.on("GET", /^\/Recommendations$/, () => ({
            json: {
                status: "OK", query: "React jobs in Makati", page: 1, skillCount: 1, hasPreferences: true, detailed: false,
                data: Array.from({ length: jobs }, (_, i) => ({ ...searchJob(i + 1, { job_description: "React work." }), joblink_match: { detailed: false, score: 90 - i, band: { level: "excellent", label: "Excellent match" } } })),
            },
        }));
        await session.page.goto(`${server.baseUrl}/DASHBOARD/dashboard.html`);
        await session.page.waitForFunction(() => !document.querySelector(".loading-jobs"), null, { timeout: 30000 });
        return { ...session, api, errors };
    }
    // How many job cards come before the list ad (null if there is none).
    const adPosition = page => page.evaluate(() => {
        const ad = document.querySelector("#jobContainer #ad-list");
        if (!ad) return null;
        return [...document.querySelectorAll("#jobContainer > *")].indexOf(ad);
    });
    const adState = page => page.evaluate(() => ({
        sidebar: document.querySelectorAll("#ad-sidebar").length,
        list: document.querySelectorAll("#ad-list").length,
        anywhere: document.querySelectorAll(".ad-card").length,
    }));

    t.section("Ads: the Free plan sees two placeholder slots, labelled Advertisement");
    {
        const hosts = new Set();
        const { context, page, api, errors } = await openDashboard({ hosts });
        await page.waitForSelector("#ad-sidebar");
        t.check("one card under the sidebar menu (outside the menu list) and one in the job list", [
            await page.locator(".sidebar > #ad-sidebar").count(), await page.locator(".nav-links #ad-sidebar").count(), await page.locator("#jobContainer #ad-list").count(),
        ], [1, 0, 1]);
        t.check("only those two ad cards exist", (await adState(page)).anywhere, 2);
        t.check("both are labelled Advertisement (visibly and for screen readers)", [
            await page.locator("#ad-sidebar .ad-label").textContent(), await page.locator("#ad-list .ad-label").textContent(),
            await page.locator("#ad-sidebar").getAttribute("aria-label"), await page.locator("#ad-list").getAttribute("aria-label"),
        ], ["Advertisement", "Advertisement", "Advertisement", "Advertisement"]);
        t.check("the list ad sits between job cards: after the 3rd of 5", [await adPosition(page), await page.locator("#jobContainer .recommended-card").count()], [3, 5]);
        t.check("the ad is not a job: it has no Apply / View details buttons", await page.locator(".ad-card [data-job-id], .ad-card .apply-job-btn").count(), 0);
        t.check("both offer a way to remove ads: the Plans page", await page.$$eval(".ad-card .ad-remove", as => as.map(a => a.getAttribute("href"))), ["Plans.html", "Plans.html"]);

        // Static placeholders: nothing from an ad network.
        t.check("ad cards contain no iframe, script, image, link, object or embed", await page.locator(".ad-card iframe, .ad-card script, .ad-card img, .ad-card link, .ad-card object, .ad-card embed, .ad-card video").count(), 0);
        t.check("the page only ever talked to this server, the JobLink API and the icon font (no ad network)",
            [...hosts].filter(h => !/^127\.0\.0\.1:\d+$/.test(h) && h !== "localhost:7142" && h !== "cdnjs.cloudflare.com"), []);
        t.check("neither Ads.js nor dashboard.html loads a script from anywhere else", [
            /https?:\/\//.test(fs.readFileSync(path.join(REPO_ROOT, "DASHBOARD", "Ads.js"), "utf8").replace(/\/\/.*$/gm, "")),
            /<script[^>]+src=["']https?:/i.test(fs.readFileSync(path.join(REPO_ROOT, "DASHBOARD", "dashboard.html"), "utf8")),
        ], [false, false]);
        t.check("the plan was read once, with the login token", api.callsTo("GET", /^\/Subscription$/).map(c => c.headers.authorization), ["Bearer test-token"]);
        t.check("no script errors", errors, []);
        await context.close();
    }

    t.section("Ads: where the list ad goes depends on how many jobs there are");
    {
        const positions = {};
        for (const jobs of [0, 1, 2, 3, 4, 8]) {
            const { context, page } = await openDashboard({ jobs });
            positions[jobs] = await adPosition(page);
            await context.close();
        }
        t.check("never first, never last: after the 3rd card, or the one before the last in a short list; none for 0-1 jobs",
            positions, { 0: null, 1: null, 2: 1, 3: 2, 4: 3, 8: 3 });
    }

    t.section("Ads: Premium never sees them");
    {
        const { context, page, api, errors } = await openDashboard({ state: plan({ premium: true, billing: "Monthly", until: ANNUAL_UNTIL }) });
        await page.waitForTimeout(600);
        t.check("no sidebar ad, no list ad, no ad card at all", await adState(page), { sidebar: 0, list: 0, anywhere: 0 });
        t.check("the job cards are all there", await page.locator("#jobContainer .recommended-card").count(), 5);
        t.check("the plan was read from the server (not guessed)", api.callsTo("GET", /^\/Subscription$/).length, 1);
        t.check("no script errors", errors, []);
        await context.close();
    }

    t.section("Ads: if the plan can't be read there are no ads (never an ad for someone who paid)");
    {
        for (const status of [500, 401, 403, 404]) {
            const { context, page } = await openDashboard({ planStatus: status });
            await page.waitForTimeout(400);
            t.check(`plan answers ${status}: no ads`, await adState(page), { sidebar: 0, list: 0, anywhere: 0 });
            await context.close();
        }
    }

    t.section("Ads: an ad shows only when the server says showAds is true");
    {
        const { context, page } = await openDashboard({ state: { ...plan(), features: { ...plan().features, showAds: "yes" } } });
        await page.waitForTimeout(400);
        t.check("a value that isn't exactly true shows nothing", await adState(page), { sidebar: 0, list: 0, anywhere: 0 });
        await context.close();
    }

    // ----- the upgrade prompt ---------------------------------------------------

    t.section("The upgrade prompt: shown when the API answers 403 with upgradeRequired");
    {
        const { context, page, api } = await openPlans();
        await loaded(page);
        const MESSAGE = "Your Free plan keeps 1 saved resume. Upgrade to Premium to save up to 10.";
        api.on("POST", /^\/Resume$/, () => ({ status: 403, json: { message: MESSAGE, code: "upgrade_required", upgradeRequired: true, feature: "resumeVersions", limit: 1 } }));
        const post = () => page.evaluate(async () => (await ApiClient.authFetch(`${ApiClient.API}/Resume`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" })).status);

        t.check("the call still answers 403 to the page that made it", await post(), 403);
        await page.locator(".ac-upgrade").waitFor();
        t.check("a dialog offers Premium and shows the server's message", [
            (await page.locator(".ac-upgrade h3").innerText()).trim(), (await page.locator(".ac-upgrade .ac-message").innerText()).trim(),
        ], ["Upgrade to Premium", MESSAGE]);
        t.check("it links to the Plans page", await page.locator(".ac-upgrade a.ac-btn-primary").getAttribute("href"), "Plans.html");

        await post();
        t.check("a second limit hit doesn't stack a second dialog", await page.locator(".ac-upgrade").count(), 1);

        await page.click(".ac-upgrade [data-close]");
        t.check("Not now closes it", await page.locator(".ac-upgrade").count(), 0);

        await post();
        await page.locator(".ac-upgrade").waitFor();
        await page.keyboard.press("Escape");
        t.check("Escape closes it too", await page.locator(".ac-upgrade").count(), 0);

        await post();
        await page.locator(".ac-upgrade").waitFor();
        await page.click(".ac-upgrade a.ac-btn-primary");
        await page.waitForURL("**/DASHBOARD/Plans.html");
        t.check("See plans goes to the Plans page", page.url().endsWith("/DASHBOARD/Plans.html"), true);
        await context.close();
    }

    t.section("The upgrade prompt: only for a limit that Premium would lift");
    {
        const { context, page, api } = await openPlans();
        await loaded(page);
        const call = (path, options = {}) => page.evaluate(async ({ path, options }) => (await ApiClient.authFetch(`${ApiClient.API}${path}`, options)).status, { path, options });

        api.on("POST", /^\/Resume$/, () => ({ status: 403, json: { message: "Premium keeps up to 10 saved resumes.", code: "limit_reached", upgradeRequired: false, limit: 10 } }));
        await call("/Resume", { method: "POST" });
        await page.waitForTimeout(300);
        t.check("a Premium member at their maximum gets no upgrade prompt (there's nothing to upgrade to)", await page.locator(".ac-upgrade").count(), 0);

        api.on("POST", /^\/Resume$/, () => ({ status: 403, json: { message: "Not yours." } }));
        await call("/Resume", { method: "POST" });
        await page.waitForTimeout(300);
        t.check("an ordinary 403 (not yours, wrong role) gets no upgrade prompt", await page.locator(".ac-upgrade").count(), 0);

        api.on("POST", /^\/Resume$/, () => ({ status: 403, text: "Forbidden" }));
        await call("/Resume", { method: "POST" });
        await page.waitForTimeout(300);
        t.check("a 403 that isn't JSON gets none either, and nothing breaks", await page.locator(".ac-upgrade").count(), 0);

        api.on("POST", /^\/Resume$/, () => ({ status: 400, json: { message: "Bad.", upgradeRequired: true } }));
        await call("/Resume", { method: "POST" });
        await page.waitForTimeout(300);
        t.check("only a 403 opens it", await page.locator(".ac-upgrade").count(), 0);

        api.on("POST", /^\/Resume$/, () => ({ status: 403, json: { message: "Limit.", upgradeRequired: true } }));
        await call("/Resume", { method: "POST", noUpgradePrompt: true });
        await page.waitForTimeout(300);
        t.check("a page that handles the limit itself can turn the prompt off", await page.locator(".ac-upgrade").count(), 0);

        api.on("POST", /^\/Resume$/, () => ({ status: 403, json: { message: "<img src=x onerror=window.__pwned=1><b>bold</b>", upgradeRequired: true } }));
        await call("/Resume", { method: "POST" });
        await page.locator(".ac-upgrade").waitFor();
        t.check("the server's message is shown as text, never as HTML", [
            await page.locator(".ac-upgrade .ac-message img, .ac-upgrade .ac-message b").count(),
            (await page.locator(".ac-upgrade .ac-message").innerText()).includes("<b>bold</b>"),
            await page.evaluate(() => window.__pwned === undefined),
        ], [0, true, true]);
        await context.close();
    }

    await browser.close();
    await server.close();

    t.done();
})().catch(err => { console.error("CHECK CRASHED:", err); process.exit(1); });

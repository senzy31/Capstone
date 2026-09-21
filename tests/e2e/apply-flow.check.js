// The Apply button: labels, the new tab, the redirect, failures, and the
// "Did you finish applying?" prompt. The API is faked - no backend needed.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob, sleep, nextTab } = require("./helpers");

const EXT = searchJob(501, { job_title: "Fullstack Developer", employer_name: "Ascendion", job_publisher: "LinkedIn",
    job_apply_link: "https://evil.example/must-never-be-used" });
const INT = searchJob(502, { job_id: "internal-502", joblink_source: "Internal", job_publisher: null,
    job_title: "Backend Developer", employer_name: "Acme Corp", job_apply_link: null });
const NOPUB = searchJob(503, { job_publisher: null, job_title: "No Publisher Job" });
const NOID = searchJob(504, { joblink_job_id: undefined, job_title: "No Saved Id" });
const HTMLPUB = searchJob(505, { job_publisher: "<b>Bold</b> & Co", job_title: "Html Publisher" });

const EXTERNAL_OK = (applicationId = 9001, extra = {}) => ({
    json: { type: "external", applicationId, redirectUrl: "https://www.linkedin.com/jobs/view/501", publisher: "LinkedIn", status: "Redirected", ...extra },
});

const cardId = id => (id === 502 ? "internal-502" : `jsearch-${id}`);
const applyBtn = id => `.apply-job-btn[data-job-id="${cardId(id)}"]`;
const PENDING = "joblink.pendingApply";

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Apply flow");

    async function openJobs({ apiSetup, init, page: pageName = "Jobs.html", jobs = [EXT, INT, NOPUB, NOID, HTMLPUB] } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { init });
        const api = await mockApi(session.context);
        api.on("GET", /^\/JobSearch\/search$/, () => ({ json: { status: "OK", data: jobs } }));
        api.on("GET", /^\/JobSearch\/details$/, () => ({ json: { status: "OK", data: [{ job_description: "Details" }] } }));
        api.on("GET", /^\/JobSearch\/salary$/, () => ({ json: { status: "OK", data: [] } }));
        apiSetup?.(api);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/${pageName}`);
        await session.page.waitForSelector(".job-card");
        return { ...session, api };
    }

    const toast = page => page.locator(".af-toast").last();
    const leave = page => page.evaluate(() => window.dispatchEvent(new Event("blur")));
    const comeBack = page => page.evaluate(() => window.dispatchEvent(new Event("focus")));
    const pending = page => page.evaluate(key => JSON.parse(localStorage.getItem(key)), PENDING);

    // ================= what the buttons and notes say =================
    t.section("cards");
    {
        const { context, page } = await openJobs();
        const label = async id => (await page.locator(applyBtn(id)).innerText()).replace(/\s+/g, " ").trim();
        const icon = id => page.locator(`${applyBtn(id)} i.fa-arrow-up-right-from-square`).count();
        const note = id => page.locator(`.job-card:has(${applyBtn(id)}) .external-note`);

        t.check("external job: 'Apply on {publisher}' with the external-link icon", [await label(501), await icon(501)], ["Apply on LinkedIn", 1]);
        t.check("internal job keeps a plain 'Apply' (no icon)", [await label(502), await icon(502)], ["Apply", 0]);
        t.check("external job shows the 'you'll finish there' note",
            (await note(501).innerText()).replace(/\s+/g, " ").trim(), "This job is posted on LinkedIn. You'll finish your application there.");
        t.check("internal job has no such note", await note(502).count(), 0);
        t.check("no publisher -> a sensible fallback in both places",
            [await label(503), (await note(503).innerText()).includes("posted on the original site")], ["Apply on the original site", true]);
        t.check("the publisher name is never treated as HTML",
            [await label(505), await page.locator(`.job-card:has(${applyBtn(505)}) .external-note b`).count()], ["Apply on <b>Bold</b> & Co", 0]);
        t.check("nothing about 'Priority Application' or employer tools on an external card",
            await page.locator(`.job-card:has(${applyBtn(501)}) :text-matches("priority|employer", "i")`).count(), 0);

        // details popup
        await page.locator(`.view-details-btn[data-job-id="${cardId(501)}"]`).click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "Details");
        t.check("popup (external): button label + note",
            [(await page.locator("#applyBtn").innerText()).replace(/\s+/g, " ").trim(), await page.locator("#applyBtn i.fa-arrow-up-right-from-square").count(),
             (await page.locator("#popupApplyNote").innerText()).includes("posted on LinkedIn")], ["Apply on LinkedIn", 1, true]);
        await page.click("#closePopupBtn");
        await page.locator('.view-details-btn[data-job-id="internal-502"]').click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "Details");
        t.check("popup (internal): plain 'Apply' and the note is cleared",
            [(await page.locator("#applyBtn").innerText()).trim(), await page.locator("#popupApplyNote .external-note").count()], ["Apply", 0]);
        await context.close();
    }

    // ================= external apply =================
    t.section("external apply: the tab");
    {
        const { context, page, api } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => ({ delay: 700, ...EXTERNAL_OK() })) });

        const tabPromise = nextTab(context, 3000);
        const clickedAt = Date.now();
        await page.locator(applyBtn(501)).click();
        const tab = await tabPromise;

        t.check("a tab opens straight away - before the API has answered",
            [tab !== null, tab?.url(), Date.now() - clickedAt < 600], [true, "about:blank", true]);
        t.check("...and it tells the user what's happening while they wait",
            (await tab.locator("body").innerText()).includes("Taking you to LinkedIn"), true);

        await tab.waitForURL("https://www.linkedin.com/jobs/view/501", { timeout: 5000 });
        t.check("it then goes to the URL the API returned", tab.url(), "https://www.linkedin.com/jobs/view/501");
        t.check("the new tab cannot reach back into JobLink (window.opener is null)", await tab.evaluate(() => window.opener), null);

        const call = api.callsTo("POST", /\/apply$/)[0];
        t.check("request: POST /jobs/501/apply with the login token and no body",
            [call.path, call.headers.authorization, call.body], ["/jobs/501/apply", "Bearer test-token", null]);
        t.check("the job's own apply link (evil.example) was never used", tab.url().includes("evil.example"), false);
        t.check("a toast points them back here for the next step", (await toast(page).innerText()).includes("Come back here when you've finished applying"), true);
        t.check("nothing unexpected was called", api.unmocked, []);
        await context.close();
    }

    t.section("external apply: failures close the tab and explain");
    for (const [name, response, expectText] of [
        ["410 expired", { status: 410, json: { type: "external", expired: true, message: "Sorry, this job posting has expired or been taken down, so it can't be applied to anymore." } }, "expired or been taken down"],
        ["429 rate limit", { status: 429, json: { message: "You've reached the limit of 20 applications per 24 hours." } }, "limit of 20 applications"],
        ["500 with no body", { status: 500 }, "Couldn't apply (500)"],
        ["a non-https redirect", EXTERNAL_OK(9001, { redirectUrl: "http://linkedin.com/jobs/1" }), "can't be opened safely"],
        ["a javascript: redirect", EXTERNAL_OK(9001, { redirectUrl: "javascript:alert(1)" }), "can't be opened safely"],
        ["the network failing", { abort: true }, ""],
    ]) {
        const { context, page } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => response) });
        const tabPromise = nextTab(context, 3000);
        await page.locator(applyBtn(501)).click();
        const tab = await tabPromise;
        await page.waitForSelector(".af-toast-error");
        await sleep(150);
        t.check(`${name}: error toast${expectText ? " says so" : ""}, tab closed, nothing to confirm later`,
            [(await toast(page).innerText()).includes(expectText), tab.isClosed(), await pending(page)], [true, true, null]);
        await context.close();
    }

    {
        const { context, page, api } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/504\/apply$/, () => EXTERNAL_OK()) });
        const tabPromise = nextTab(context, 3000);
        await page.locator(applyBtn(504)).click();
        const tab = await tabPromise;
        await page.waitForSelector(".af-toast-error");
        t.check("a job with no saved id makes no request, closes the tab and asks for a new search",
            [(await toast(page).innerText()).includes("run your search again"), api.callsTo("POST", /apply$/).length, tab.isClosed()], [true, 0, true]);
        await context.close();
    }

    {
        const dialogs = [];
        const { context, page } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => ({ status: 401, json: { message: "no" } })) });
        page.on("dialog", d => { dialogs.push(d.message()); d.accept(); });
        const tabPromise = nextTab(context, 3000);
        await page.locator(applyBtn(501)).click();
        const tab = await tabPromise;
        await page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("401 (expired login): back to the login page, session cleared, tab closed, told why",
            [page.url().endsWith("/LOGIN/login.html"), await page.evaluate(() => [localStorage.getItem("token"), localStorage.getItem("user")]), tab.isClosed(), dialogs],
            [true, [null, null], true, ["Your session expired. Please log in again."]]);
        await context.close();
    }

    t.section("external apply: popup blocked, double clicks, popup button");
    {
        const { context, page, api } = await openJobs({ init: () => { window.open = () => null; }, apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK()) });
        await page.locator(applyBtn(501)).click();
        await page.waitForSelector(".af-toast a");
        const link = page.locator(".af-toast a");
        t.check("blocked tab: a toast offers an 'Open LinkedIn' link to the API's URL, with noopener",
            [await link.innerText(), await link.getAttribute("href"), (await link.getAttribute("rel")).includes("noopener"), await link.getAttribute("target")],
            ["Open LinkedIn", "https://www.linkedin.com/jobs/view/501", true, "_blank"]);
        t.check("...and the application is still recorded and waiting for confirmation", [api.callsTo("POST", /apply$/).length, (await pending(page))[0].applicationId], [1, 9001]);
        await context.close();
    }
    {
        const { context, page, api } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => ({ delay: 500, ...EXTERNAL_OK() })) });
        const tabs = [];
        context.on("page", p => tabs.push(p));
        await page.locator(applyBtn(501)).dblclick();
        await page.waitForSelector(".af-toast");
        await sleep(300);
        t.check("double-clicking Apply sends one request", api.callsTo("POST", /apply$/).length, 1);
        await context.close();
    }
    {
        const { context, page, api } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK()) });
        await page.locator('.view-details-btn[data-job-id="jsearch-501"]').click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "Details");
        const tabPromise = nextTab(context, 3000);
        await page.click("#applyBtn");
        const tab = await tabPromise;
        await tab.waitForURL("https://www.linkedin.com/jobs/view/501", { timeout: 5000 });
        t.check("the popup's Apply button does the same thing", [api.callsTo("POST", /apply$/).length, tab.url()], [1, "https://www.linkedin.com/jobs/view/501"]);
        await context.close();
    }

    // ================= internal apply =================
    t.section("internal apply");
    for (const [name, json, expected] of [
        ["applied", { type: "internal", applicationId: 77, status: "Submitted", alreadyApplied: false }, "Application submitted! The employer has been notified."],
        ["already applied", { type: "internal", applicationId: 77, status: "Submitted", alreadyApplied: true }, "You already applied to this job."],
    ]) {
        const { context, page, api } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/502\/apply$/, () => ({ json })) });
        const tab = nextTab(context, 800);
        await page.locator(applyBtn(502)).click();
        await page.waitForSelector(".af-toast");
        t.check(`${name}: toast says so, no new tab, nothing to confirm`,
            [(await toast(page).innerText()).trim(), await tab, await pending(page), api.callsTo("POST", /apply$/)[0].headers.authorization], [expected, null, null, "Bearer test-token"]);
        await context.close();
    }
    {
        const { context, page } = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/502\/apply$/, () => ({ status: 429, json: { message: "You've reached the limit of 20 applications per 24 hours. You can apply again in about 3 hours." } })) });
        await page.locator(applyBtn(502)).click();
        await page.waitForSelector(".af-toast-error");
        t.check("internal 429: the server's message is shown", (await toast(page).innerText()).includes("apply again in about 3 hours"), true);
        await context.close();
    }

    // ================= "Did you finish applying?" =================
    t.section("return prompt");
    async function applyAndLeave(env, id = 501) {
        const tabPromise = nextTab(env.context, 3000);
        await env.page.locator(applyBtn(id)).click();
        await tabPromise;
        await env.page.waitForSelector(".af-toast");
        await leave(env.page);
        await comeBack(env.page);
    }
    const confirmCall = (api, id = 9001) => api.callsTo("PATCH", new RegExp(`/applications/${id}/confirm-external$`));
    const withConfirm = (status = 200, json = { applicationId: 9001, status: "Applied Externally" }) => a => {
        a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK());
        a.on("PATCH", /confirm-external$/, () => ({ status, json }));
    };
    const modalText = page => page.locator(".af-modal").innerText().then(s => s.replace(/\s+/g, " ").trim());

    {
        const env = await openJobs({ apiSetup: withConfirm() });
        await applyAndLeave(env);
        await env.page.waitForSelector(".af-overlay");
        t.check("after going to the job site and coming back, a modal asks",
            await modalText(env.page), "Did you finish applying? Did you finish applying for Fullstack Developer on LinkedIn? Not yet Yes, I applied");
        t.check("only one modal, even if focus fires again", (await comeBack(env.page), await comeBack(env.page), await env.page.locator(".af-overlay").count()), 1);
        await env.page.getByRole("button", { name: "Yes, I applied" }).click();
        await env.page.waitForFunction(() => !document.querySelector(".af-overlay"));
        const call = confirmCall(env.api)[0];
        t.check("Yes -> PATCH confirm-external {applied:true} with the token; modal closes; nothing left pending",
            [call.body, call.headers.authorization, await pending(env.page)], [{ applied: true }, "Bearer test-token", null]);
        t.check("...and a thank-you toast", (await toast(env.page).innerText()).includes("Marked as applied"), true);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm(200, { applicationId: 9001, status: "Redirected" }) });
        await applyAndLeave(env);
        await env.page.getByRole("button", { name: "Not yet" }).click();
        await env.page.waitForFunction(() => !document.querySelector(".af-overlay"));
        t.check("Not yet -> PATCH {applied:false}; modal closes; toast points to the Applications page",
            [confirmCall(env.api)[0].body, await pending(env.page), (await toast(env.page).innerText()).includes("Applications page")], [{ applied: false }, null, true]);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm() });
        await applyAndLeave(env);
        await env.page.keyboard.press("Escape");
        await env.page.waitForFunction(() => !document.querySelector(".af-overlay"));
        t.check("Escape counts as Not yet", confirmCall(env.api)[0].body, { applied: false });
        await env.context.close();
    }
    {
        let attempts = 0;
        const env = await openJobs({ apiSetup: a => { a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK()); a.on("PATCH", /confirm-external$/, () => ++attempts === 1 ? { status: 500, json: { message: "Database is down" } } : { json: { applicationId: 9001, status: "Applied Externally" } }); } });
        await applyAndLeave(env);
        await env.page.getByRole("button", { name: "Yes, I applied" }).click();
        await env.page.waitForSelector(".af-error:not([hidden])");
        t.check("if saving fails the modal stays, shows why, and can be retried",
            [await env.page.locator(".af-error").innerText(), await env.page.getByRole("button", { name: "Yes, I applied" }).isEnabled(), (await pending(env.page)).length], ["Database is down", true, 1]);
        await env.page.getByRole("button", { name: "Yes, I applied" }).click();
        await env.page.waitForFunction(() => !document.querySelector(".af-overlay"));
        t.check("...and the retry works", [attempts, await pending(env.page)], [2, null]);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm(409, { message: "This application isn't waiting for confirmation." }) });
        await applyAndLeave(env);
        await env.page.getByRole("button", { name: "Yes, I applied" }).click();
        await env.page.waitForFunction(() => !document.querySelector(".af-overlay"));
        t.check("409 (already confirmed elsewhere): modal just closes and stops asking", await pending(env.page), null);
        await env.context.close();
    }
    {
        // They switch to the job site while the API call is still running.
        const env = await openJobs({ apiSetup: a => { a.on("POST", /^\/jobs\/501\/apply$/, () => ({ delay: 800, ...EXTERNAL_OK() })); a.on("PATCH", /confirm-external$/, () => ({ json: {} })); } });
        const tabPromise = nextTab(env.context, 3000);
        await env.page.locator(applyBtn(501)).click();
        await tabPromise;
        await leave(env.page);
        await env.page.waitForSelector(".af-toast");
        await comeBack(env.page);
        t.check("leaving BEFORE the API answered still counts - the prompt appears on return", await env.page.locator(".af-overlay").count(), 1);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm() });
        const tabPromise = nextTab(env.context, 3000);
        await env.page.locator(applyBtn(501)).click();
        await tabPromise;
        await env.page.waitForSelector(".af-toast");
        // A real headless tab open may or may not fire blur; make the "never left" case explicit.
        await env.page.evaluate(key => localStorage.setItem(key, JSON.stringify(JSON.parse(localStorage.getItem(key)).map(p => ({ ...p, left: false })))), PENDING);
        await comeBack(env.page);
        await sleep(300);
        t.check("no prompt if they never left JobLink", await env.page.locator(".af-overlay").count(), 0);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: a => a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK(9001, { status: "Applied Externally" })) });
        await applyAndLeave(env);
        await sleep(300);
        t.check("clicking Apply on a job they already confirmed doesn't ask again", [await env.page.locator(".af-overlay").count(), await pending(env.page)], [0, null]);
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm() });
        await applyAndLeave(env);
        await env.page.waitForSelector(".af-overlay");
        await env.page.reload();
        await env.page.waitForSelector(".af-overlay");
        t.check("closing/reloading JobLink doesn't lose the question - it's asked again on load", await modalText(env.page), "Did you finish applying? Did you finish applying for Fullstack Developer on LinkedIn? Not yet Yes, I applied");
        await env.context.close();
    }
    {
        const env = await openJobs({ jobs: [EXT, NOPUB], apiSetup: a => {
            a.on("POST", /^\/jobs\/501\/apply$/, () => EXTERNAL_OK(9001));
            a.on("POST", /^\/jobs\/503\/apply$/, () => EXTERNAL_OK(9002, { publisher: "Indeed", redirectUrl: "https://www.indeed.com/viewjob?jk=1" }));
            a.on("PATCH", /confirm-external$/, () => ({ json: {} }));
        } });
        const first = nextTab(env.context, 3000); await env.page.locator(applyBtn(501)).click(); await first;
        const second = nextTab(env.context, 3000); await env.page.locator(applyBtn(503)).click(); await second;
        await env.page.waitForFunction(() => document.querySelectorAll(".af-toast").length >= 2);
        await leave(env.page); await comeBack(env.page);
        await env.page.getByRole("button", { name: "Yes, I applied" }).click();
        await env.page.waitForFunction(() => document.querySelector(".af-modal")?.textContent.includes("No Publisher Job"));
        t.check("two jobs waiting: they're asked one after the other", await modalText(env.page), "Did you finish applying? Did you finish applying for No Publisher Job on Indeed? Not yet Yes, I applied");
        await env.context.close();
    }
    {
        const env = await openJobs({ apiSetup: withConfirm() });
        await applyAndLeave(env);
        await env.page.waitForSelector(".af-overlay");
        await env.page.evaluate(() => { const hidden = v => Object.defineProperty(document, "hidden", { value: v, configurable: true }); hidden(true); document.dispatchEvent(new Event("visibilitychange")); hidden(false); });
        t.check("the tab becoming hidden/visible (visibilitychange) also drives the prompt", await env.page.locator(".af-overlay").count(), 1);
        await env.context.close();
    }

    // ================= logged out =================
    t.section("no login token");
    for (const pageName of ["Jobs.html", "dashboard.html"]) {
        const session = await loggedInPage(browser, server.baseUrl, { token: null });   // a saved user from before tokens existed
        const api = await mockApi(session.context);
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/${pageName}`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check(`${pageName}: an old session without a token is sent to log in, and no API call is made`,
            [session.page.url().endsWith("/LOGIN/login.html"), api.calls.length], [true, 0]);
        await session.context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

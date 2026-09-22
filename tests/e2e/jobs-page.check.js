// The Jobs page: search + filters sent to GET /api/Recommendations/search (server-side scoring
// and filtering - see the .NET RecommendationsDbTests for the filter/merge logic itself), the
// ?q= hand-off, out-of-order responses, error display, and the "Posted on JobLink" badge for
// employer-posted jobs. Apply is covered by apply-flow.check.js. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob } = require("./helpers");

const matched = (score, detailed = false) => ({ score, band: { level: score >= 75 ? "excellent" : score >= 50 ? "good" : score >= 25 ? "fair" : "low", label: "x" }, detailed });

const job = (id, extra) => searchJob(id, { job_title: `Job J${id}`, employer_name: `Company J${id}`, job_publisher: null, joblink_match: matched(60), ...extra });

const internalJob = (id, extra) => ({
    job_id: `internal-${id}`, joblink_job_id: id, joblink_source: "Internal",
    job_title: `Internal Job J${id}`, employer_name: `Employer ${id}`,
    job_description: "Posted directly by an employer.", job_employment_type: "Full-time", job_employment_types: ["FULLTIME"],
    job_is_remote: false, job_city: "Manila", joblink_work_setup: "onsite",
    job_min_salary: 30000, job_max_salary: 40000, job_salary_currency: "PHP", job_salary_period: "MONTH",
    joblink_match: matched(70), ...extra,
});

const FIXTURE = [
    job(1, { job_is_remote: true, job_city: "Manila", job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" }),
    job(2, { job_employment_type: "Part-time", job_employment_types: ["PARTTIME"], job_city: "Makati" }),
    job(3, { job_employment_type: "Contractor", job_employment_types: ["CONTRACTOR"], job_description: "A hybrid setup.", job_city: "Cebu" }),
    job(4, { job_city: "Cebu", job_min_salary: 15000, job_max_salary: 20000, job_salary_period: "MONTH" }),
    job(5, { job_country: "US", job_city: "Austin", job_min_salary: 120000, job_max_salary: 150000, job_salary_period: "YEAR" }),
];

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Jobs page");

    async function openJobs({ url = "Jobs.html", respond = () => ({ json: { status: "OK", query: "jobs philippines", page: 1, skillCount: 0, hasPreferences: false, detailed: false, data: FIXTURE } }), token = "test-token", waitForCards = true } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { token });
        const api = await mockApi(session.context);
        const queries = [];
        api.on("GET", /^\/Recommendations\/search$/, call => { queries.push(call.query); return respond(call, queries.length); });
        api.on("GET", /^\/JobSearch\/details$/, () => ({ json: { status: "OK", data: [{ job_description: "FULL DETAIL TEXT" }] } }));
        api.on("GET", /^\/JobSearch\/salary$/, () => ({ json: { status: "OK", data: [] } }));
        const errors = [];
        session.page.on("pageerror", e => errors.push(String(e)));
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/${url}`);
        if (waitForCards) await session.page.waitForSelector(".job-card");
        return { ...session, api, queries, errors };
    }
    const shownIds = page => page.$$eval(".view-details-btn", b => b.map(x => x.dataset.jobId));
    const ids = (...n) => n.map(i => `jsearch-${i}`);

    t.section("page basics");
    {
        const { context, page, queries, api, errors } = await openJobs();
        t.check("default search on load", queries, [{ q: "jobs philippines", page: "1" }]);
        t.check("all 5 jobs listed, best-first order preserved from the server", await shownIds(page), ids(1, 2, 3, 4, 5));
        t.check("result text", await page.locator("#jobResultText").innerText(), "5 jobs found");
        t.check("sidebar highlights Jobs", (await page.locator(".nav-links li.active a").innerText()).trim(), "Jobs");
        t.check("title + navbar name", [await page.locator(".welcome-section h1").innerText(), await page.locator("#userName").innerText()], ["Find Jobs", "Maria"]);
        t.check("all filter fields present", await Promise.all(["workSetup", "locationInput", "minSalary", "maxSalary", "jobType", "minScore", "applyFilters", "resetFilters"].map(id => page.locator("#" + id).count())), [1, 1, 1, 1, 1, 1, 1, 1]);
        t.check("every API call the page makes carries the login token", [api.calls.length > 0, api.calls.every(c => c.headers.authorization === "Bearer test-token")], [true, true]);
        t.check("no page errors", errors, []);
        t.check("every card shows its suitability score", await page.locator(".score-ring").count(), 5);

        t.section("filters are sent to the server as their own query params");
        const apply = async () => { await page.click("#applyFilters"); await page.waitForFunction(() => !document.querySelector(".loading-jobs")); };
        const reset = async () => { await page.click("#resetFilters"); await page.waitForFunction(() => !document.querySelector(".loading-jobs")); };
        const set = async f => { for (const [id, v] of Object.entries(f)) { if (["workSetup", "jobType", "minScore"].includes(id)) await page.selectOption("#" + id, v); else await page.fill("#" + id, v); } };

        await set({ workSetup: "remote" }); await apply();
        t.check("work setup", queries.at(-1), { q: "jobs philippines", page: "1", workSetup: "remote" });
        await reset();

        await set({ locationInput: "Cebu" }); await apply();
        t.check("location", queries.at(-1), { q: "jobs philippines", page: "1", location: "Cebu" });
        await reset();

        await set({ minSalary: "20000", maxSalary: "60000" }); await apply();
        t.check("salary range", queries.at(-1), { q: "jobs philippines", page: "1", minSalary: "20000", maxSalary: "60000" });
        await reset();

        await set({ jobType: "FULLTIME" }); await apply();
        t.check("job type", queries.at(-1), { q: "jobs philippines", page: "1", jobType: "FULLTIME" });
        await reset();

        await set({ minScore: "50" }); await apply();
        t.check("minimum suitability score", queries.at(-1), { q: "jobs philippines", page: "1", minScore: "50" });
        await reset();

        await set({ workSetup: "onsite", jobType: "FULLTIME", locationInput: "Cebu", minScore: "25" });
        await page.fill("#searchInput", "nurse"); await page.press("#searchInput", "Enter");
        await page.waitForFunction(() => !document.querySelector(".loading-jobs"));
        t.check("keyword and every filter combine into one request", queries.at(-1),
            { q: "nurse", page: "1", workSetup: "onsite", location: "Cebu", jobType: "FULLTIME", minScore: "25" });

        await reset();
        t.check("reset restores the default search and clears every field",
            [queries.at(-1), await page.inputValue("#searchInput"), await page.inputValue("#locationInput"), await page.inputValue("#jobType"), await page.inputValue("#minScore")],
            [{ q: "jobs philippines", page: "1" }, "", "", "", ""]);

        await page.locator('.view-details-btn[data-job-id="jsearch-2"]').click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "FULL DETAIL TEXT");
        t.check("View Details opens the popup for that job", [await page.locator("#popupTitle").innerText(), await page.locator("#popupCompany").innerText(), await page.locator("#popupJobType").innerText()], ["Job J2", "Company J2", "Part-time"]);
        await page.click("#closePopupBtn");
        t.check("popup closes", await page.locator("#jobPopup.show").count(), 0);
        await context.close();
    }

    t.section("internal (employer-posted) jobs");
    {
        const { context, page } = await openJobs({
            respond: () => ({ json: { status: "OK", query: "jobs philippines", page: 1, skillCount: 0, hasPreferences: false, detailed: false, data: [internalJob(6), job(1)] } }),
        });
        t.check("internal job is listed first, with a Posted on JobLink badge", [await shownIds(page), await page.locator(".job-card").first().locator(".joblink-badge").count(), await page.locator(".job-card").nth(1).locator(".joblink-badge").count()], [["internal-6", "jsearch-1"], 1, 0]);
        t.check("internal job's Apply button says just Apply, not Apply on a publisher", (await page.locator(".job-card").first().locator(".apply-job-btn").innerText()).trim(), "Apply");
        await context.close();
    }

    t.section("?q= hand-off from the dashboard search box");
    {
        const { context, page, queries } = await openJobs({ url: `Jobs.html?q=${encodeURIComponent("chef & baker")}` });
        t.check("search box is pre-filled and the keyword is what's searched", [await page.inputValue("#searchInput"), queries], ["chef & baker", [{ q: "chef & baker", page: "1" }]]);
        await context.close();
    }

    t.section("newest search wins");
    {
        const { context, page } = await openJobs({ respond: (call, n) => call.query.q === "jobs philippines"
            ? { delay: 1500, json: { status: "OK", query: "jobs philippines", page: 1, skillCount: 0, hasPreferences: false, detailed: false, data: [job(90)] } }
            : { json: { status: "OK", query: call.query.q, page: 1, skillCount: 0, hasPreferences: false, detailed: false, data: [job(91)] } }, waitForCards: false });
        await page.fill("#searchInput", "quick"); await page.press("#searchInput", "Enter");
        await page.waitForSelector('[data-job-id="jsearch-91"]');
        await page.waitForTimeout(2200);
        t.check("a slow older response doesn't overwrite the newer one", await shownIds(page), ids(91));
        await context.close();
    }

    t.section("errors and login");
    {
        const { context, page } = await openJobs({ respond: () => ({ status: 503, json: { message: "Key missing <img src=x onerror=window.__x=1>" } }), waitForCards: false });
        await page.waitForSelector(".no-jobs");
        t.check("error text shown, HTML escaped",
            [(await page.locator(".no-jobs").innerText()).includes("Key missing <img"), await page.locator(".no-jobs img").count(), await page.evaluate(() => window.__x)], [true, 0, undefined]);
        t.check("result text says it failed", await page.locator("#jobResultText").innerText(), "Couldn't load jobs.");
        await context.close();
    }
    {
        const session = await loggedInPage(browser, server.baseUrl, { user: null, token: null });
        const api = await mockApi(session.context);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Jobs.html`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("a logged-out visitor is sent to login and no API quota is spent", [session.page.url().endsWith("/LOGIN/login.html"), api.calls.length], [true, 0]);
        await session.context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

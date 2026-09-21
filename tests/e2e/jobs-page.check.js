// The Jobs page: search, every filter, the search text sent to the backend, ?q= hand-off,
// out-of-order responses, error display. Apply is covered by apply-flow.check.js. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob } = require("./helpers");

const job = (id, extra) => searchJob(id, { job_title: `Job J${id}`, employer_name: `Company J${id}`, job_publisher: null, ...extra });
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

    async function openJobs({ url = "Jobs.html", respond = () => ({ json: { status: "OK", data: FIXTURE } }), token = "test-token", waitForCards = true } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { token });
        const api = await mockApi(session.context);
        const queries = [];
        api.on("GET", /^\/JobSearch\/search$/, call => { queries.push(call.query.query); return respond(call, queries.length); });
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
        t.check("default search on load", queries, ["jobs philippines"]);
        t.check("all 5 jobs listed", await shownIds(page), ids(1, 2, 3, 4, 5));
        t.check("result text", await page.locator("#jobResultText").innerText(), "5 jobs found");
        t.check("sidebar highlights Jobs", (await page.locator(".nav-links li.active a").innerText()).trim(), "Jobs");
        t.check("title + navbar name", [await page.locator(".welcome-section h1").innerText(), await page.locator("#userName").innerText()], ["Find Jobs", "Maria"]);
        t.check("all filter fields present", await Promise.all(["workSetup", "locationInput", "minSalary", "maxSalary", "jobType", "applyFilters", "resetFilters"].map(id => page.locator("#" + id).count())), [1, 1, 1, 1, 1, 1, 1]);
        t.check("search results need no login token from the page", api.callsTo("GET", /search/)[0].headers.authorization, undefined);
        t.check("no page errors", errors, []);

        t.section("filters (on top of the search results)");
        const apply = async () => { await page.click("#applyFilters"); await page.waitForFunction(() => !document.querySelector(".loading-jobs")); };
        const reset = async () => { await page.click("#resetFilters"); await page.waitForFunction(() => !document.querySelector(".loading-jobs")); };
        const set = async f => { for (const [id, v] of Object.entries(f)) { if (["workSetup", "jobType"].includes(id)) await page.selectOption("#" + id, v); else await page.fill("#" + id, v); } };
        const expectFilter = async (name, f, expected) => { await set(f); await apply(); t.check(name, await shownIds(page), ids(...expected)); await reset(); };

        await expectFilter("FULLTIME -> 1,4,5", { jobType: "FULLTIME" }, [1, 4, 5]);
        await expectFilter("PARTTIME -> 2", { jobType: "PARTTIME" }, [2]);
        await expectFilter("CONTRACTOR -> 3", { jobType: "CONTRACTOR" }, [3]);
        await expectFilter("location 'cebu' (any case) -> 3,4", { locationInput: "cebu" }, [3, 4]);
        await expectFilter("remote -> 1", { workSetup: "remote" }, [1]);
        await expectFilter("hybrid -> 3 (description says hybrid)", { workSetup: "hybrid" }, [3]);
        await expectFilter("onsite -> everything not remote", { workSetup: "onsite" }, [2, 3, 4, 5]);
        await expectFilter("min 30000 drops job 4 (pays up to 20k); unlisted and USD jobs pass", { minSalary: "30000" }, [1, 2, 3, 5]);
        await expectFilter("max 30000 drops job 1 (pays from 40k)", { maxSalary: "30000" }, [2, 3, 4, 5]);
        await expectFilter("range 16k-45k keeps overlapping jobs", { minSalary: "16000", maxSalary: "45000" }, [1, 2, 3, 4, 5]);
        await expectFilter("filters combine (FULLTIME + cebu -> 4)", { jobType: "FULLTIME", locationInput: "cebu" }, [4]);
        await set({ locationInput: "nowhere" }); await apply();
        t.check("nothing matches -> empty state", [await page.locator(".no-jobs h3").innerText(), await page.locator("#jobResultText").innerText()], ["No matching jobs found", "0 jobs found"]);
        await reset();

        t.section("the search text sent to the backend");
        queries.length = 0;
        await set({ workSetup: "onsite", jobType: "FULLTIME", locationInput: "Cebu" });
        await page.fill("#searchInput", "nurse"); await page.press("#searchInput", "Enter");
        await page.waitForFunction(() => !document.querySelector(".loading-jobs"));
        t.check("keyword + setup + type + location", queries.at(-1), "nurse onsite fulltime in Cebu");
        await reset();
        t.check("reset restores the default search and clears the fields",
            [queries.at(-1), await page.inputValue("#searchInput"), await page.inputValue("#locationInput"), await page.inputValue("#jobType")], ["jobs philippines", "", "", ""]);
        await set({ workSetup: "remote" }); await apply();
        t.check("remote adds remote wording and defaults to philippines", queries.at(-1), "jobs remote work from home philippines");

        await page.click("#resetFilters");
        await page.waitForFunction(() => !document.querySelector(".loading-jobs"));
        await page.locator('.view-details-btn[data-job-id="jsearch-2"]').click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "FULL DETAIL TEXT");
        t.check("View Details opens the popup for that job", [await page.locator("#popupTitle").innerText(), await page.locator("#popupCompany").innerText(), await page.locator("#popupJobType").innerText()], ["Job J2", "Company J2", "Part-time"]);
        await page.click("#closePopupBtn");
        t.check("popup closes", await page.locator("#jobPopup.show").count(), 0);
        await context.close();
    }

    t.section("?q= hand-off from the dashboard search box");
    {
        const { context, page, queries } = await openJobs({ url: `Jobs.html?q=${encodeURIComponent("chef & baker")}` });
        t.check("search box is pre-filled and the keyword is what's searched", [await page.inputValue("#searchInput"), queries], ["chef & baker", ["chef & baker philippines"]]);
        await context.close();
    }

    t.section("newest search wins");
    {
        const { context, page } = await openJobs({ respond: (call, n) => call.query.query === "jobs philippines"
            ? { delay: 1500, json: { status: "OK", data: [job(90)] } } : { json: { status: "OK", data: [job(91)] } }, waitForCards: false });
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

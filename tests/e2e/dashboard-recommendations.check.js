// The dashboard's Recommended Jobs: the resume-based suitability score (exact numbers worked
// out by hand), every setup/empty/error state, and that external jobs still get a score
// and a working Apply. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob, nextTab } = require("./helpers");

const job = (id, extra) => searchJob(id, { job_title: `Job J${id}`, employer_name: `Company J${id}`, job_publisher: null, job_apply_link: null, ...extra });
const FIXTURE = [
    job(3, { job_title: "Chef", job_description: "Lots of cooking.", job_city: "Manila", job_state: null, job_min_salary: 20000, job_max_salary: 25000, job_salary_period: "MONTH" }),
    job(2, { job_title: "Data Analyst", job_description: "Uses SQL daily.", job_city: "Cebu", job_state: null }),
    job(1, { job_title: "React SQL Developer", job_description: "We use React and SQL every day.", job_city: "Makati", job_state: null,
             job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH", job_publisher: "LinkedIn" }),
];

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Dashboard recommendations");

    async function openDashboard(o = {}) {
        const opts = {
            resumes: [{ resumeId: 5 }],
            catalog: [{ skillId: 1, skillName: "React" }, { skillId: 2, skillName: "SQL" }],
            links: [{ resumeId: 5, skillId: 1 }, { resumeId: 5, skillId: 2 }],
            experience: [
                { position: "Intern", startDate: "2020-01-01T00:00:00", endDate: "2020-06-01T00:00:00" },
                { position: "Frontend Developer", startDate: "2021-01-01T00:00:00", endDate: null },
            ],
            prefs: { preferredLocation: "Makati", workArrangement: null, minSalary: 30000, maxSalary: 60000 },
            jobs: FIXTURE, searchStatus: 200, resumeStatus: 200, ...o,
        };
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        const queries = [];
        api.on("GET", /^\/Resume\/by-user\/\d+$/, () => opts.resumeStatus === 200 ? { json: opts.resumes } : { status: opts.resumeStatus, json: { message: "boom" } });
        api.on("GET", /^\/Skills$/, () => ({ json: opts.catalog }));
        api.on("GET", /^\/ResumeSkills\/by-resume\/\d+$/, () => ({ json: opts.links }));
        api.on("GET", /^\/Experience\/by-resume\/\d+$/, () => ({ json: opts.experience }));
        api.on("GET", /^\/JobPreference\/by-user\/\d+$/, () => opts.prefs ? { json: opts.prefs } : { status: 404 });
        api.on("GET", /^\/JobSearch\/search$/, call => { queries.push(call.query.query); return opts.searchStatus === 200 ? { json: { status: "OK", data: opts.jobs } } : { status: opts.searchStatus, json: { message: "Key <b>missing</b>" } }; });
        api.on("GET", /^\/JobSearch\/details$/, () => ({ json: { status: "OK", data: [{ job_description: "FULL DETAIL" }] } }));
        api.on("GET", /^\/JobSearch\/salary$/, () => ({ json: { status: "OK", data: [] } }));
        const errors = [];
        session.page.on("pageerror", e => errors.push(String(e)));
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/dashboard.html`);
        return { ...session, api, queries, errors };
    }
    const settle = page => page.waitForFunction(() => !document.querySelector(".loading-jobs"), null, { timeout: 30000 });
    const cards = page => page.$$eval(".recommended-card", cs => cs.map(c => ({
        id: c.querySelector(".view-details-btn").dataset.jobId,
        score: Number(c.querySelector(".score-ring span").textContent.replace("%", "")),
        pctVar: c.querySelector(".score-ring").style.getPropertyValue("--pct"),
        label: c.querySelector(".match-label").textContent.trim(),
        level: c.querySelector(".score-ring").className.replace("score-ring level-", ""),
        rows: [...c.querySelectorAll(".match-row")].map(r => ({ name: r.querySelector(".match-name").textContent.trim(), muted: r.classList.contains("muted"), pct: r.querySelector(".match-pct")?.textContent.trim() ?? null, note: r.querySelector(".match-note").textContent.trim() })),
        chips: [...c.querySelectorAll(".skill-chip")].map(x => x.textContent.trim()),
        meta: c.querySelector(".job-meta").innerText.replace(/\s+/g, " ").trim(),
    })));

    t.section("the page");
    {
        const { context, page, errors } = await openDashboard();
        await settle(page);
        t.check("the job filter is gone", await Promise.all(["workSetup", "locationInput", "minSalary", "maxSalary", "jobType", "applyFilters", "resetFilters"].map(id => page.locator("#" + id).count())), [0, 0, 0, 0, 0, 0, 0]);
        t.check("titled Recommended Jobs, greets by first name",
            [await page.locator(".job-section-header h2").innerText(), await page.locator("#welcomeText").innerText()], ["Recommended Jobs", "Welcome, Maria!"]);
        t.check("sidebar: Dashboard active, Jobs links to the Jobs page",
            [(await page.locator(".nav-links li.active a").innerText()).trim(), await page.locator('.nav-links a:has-text("Jobs")').getAttribute("href")], ["Dashboard", "Jobs.html"]);
        t.check("no page errors", errors, []);
        await context.close();
    }

    t.section("scores (worked out by hand)");
    {
        const { context, page, queries } = await openDashboard();
        await settle(page);
        const cs = await cards(page);
        t.check("query = current role + preferred place", queries, ["Frontend Developer jobs in Makati"]);
        t.check("sorted best first", cs.map(c => c.id), ["jsearch-1", "jsearch-2", "jsearch-3"]);
        t.check("scores", cs.map(c => c.score), [100, 38, 17]);
        t.check("ring uses the score", cs.map(c => c.pctVar), ["100", "38", "17"]);
        t.check("labels + colour levels", [cs.map(c => c.label), cs.map(c => c.level)], [["Excellent match", "Fair match", "Low match"], ["excellent", "fair", "low"]]);
        t.check("job 1 breakdown", cs[0].rows, [
            { name: "Skills", muted: false, pct: "100%", note: "Mentions 2 of your 2 skills" },
            { name: "Location", muted: false, pct: "100%", note: "In your preferred area" },
            { name: "Salary", muted: false, pct: "100%", note: "Meets your salary range" }]);
        t.check("job 2: unlisted salary is left out, not counted as 0", cs[1].rows, [
            { name: "Skills", muted: false, pct: "50%", note: "Mentions 1 of your 2 skills" },
            { name: "Location", muted: false, pct: "0%", note: "Outside your preferred area" },
            { name: "Salary", muted: true, pct: null, note: "Salary not listed" }]);
        t.check("job 3: pay under the minimum", cs[2].rows[2], { name: "Salary", muted: false, pct: "83%", note: "Pays up to ₱25,000/month - under your ₱30,000 minimum" });
        t.check("matched-skill chips", cs.map(c => c.chips), [["React", "SQL"], ["SQL"], []]);
        t.check("Job Matches counts jobs scoring 50+", await page.locator("#jobCount").innerText(), "1");
        await context.close();
    }

    t.section("external jobs keep their score, and Apply works from the card");
    {
        const { context, page, api } = await openDashboard({ apiSetup: null });
        api.on("POST", /^\/jobs\/1\/apply$/, () => ({ json: { type: "external", applicationId: 9001, redirectUrl: "https://www.linkedin.com/jobs/view/1", publisher: "LinkedIn", status: "Redirected" } }));
        await settle(page);
        const top = page.locator(".recommended-card").first();
        t.check("an external recommendation still shows its score ring, label and breakdown",
            [await top.locator(".score-ring").count(), (await top.locator(".match-label").innerText()).trim(), await top.locator(".match-row").count()], [1, "Excellent match", 3]);
        t.check("...with the external button label and note",
            [(await top.locator(".apply-job-btn").innerText()).replace(/\s+/g, " ").trim(), (await top.locator(".external-note").innerText()).replace(/\s+/g, " ").trim()],
            ["Apply on LinkedIn", "This job is posted on LinkedIn. You'll finish your application there."]);
        t.check("...and nothing about priority applications or employer tools", await top.locator(':text-matches("priority", "i")').count(), 0);
        const tabPromise = nextTab(context, 3000);
        await top.locator(".apply-job-btn").click();
        const tab = await tabPromise;
        await tab.waitForURL("https://www.linkedin.com/jobs/view/1", { timeout: 5000 });
        const call = api.callsTo("POST", /apply$/)[0];
        t.check("Apply on a recommended card records the click and opens the posting",
            [call.path, call.headers.authorization, tab.url()], ["/jobs/1/apply", "Bearer test-token", "https://www.linkedin.com/jobs/view/1"]);
        await context.close();
    }

    t.section("setup states");
    {
        const { context, page, queries } = await openDashboard({ resumes: [] });
        await page.waitForSelector(".no-jobs");
        t.check("no resume: asks for skills, links to the Resume Builder, makes NO job search (quota protected)",
            [await page.locator(".no-jobs h3").innerText(), await page.locator(".no-jobs .notice-btn").getAttribute("href"), queries, await page.locator("#jobCount").innerText()],
            ["We need your skills to find matches", "ResumeBuilder.html", [], "0"]);
        await context.close();
    }
    {
        const { context, page, queries } = await openDashboard({ links: [] });
        await page.waitForSelector(".no-jobs");
        t.check("a resume with no skills: same prompt, no search", [await page.locator(".no-jobs h3").innerText(), queries], ["We need your skills to find matches", []]);
        await context.close();
    }
    {
        const { context, page, queries } = await openDashboard({ prefs: null });
        await settle(page);
        const cs = await cards(page);
        t.check("no preferences: a notice links to Profile preferences, searches Philippines-wide, scores on skills only",
            [await page.locator(".recommend-notice .notice-btn").getAttribute("href"), queries, cs.map(c => c.score)], ["Profile.html#preferences", ["Frontend Developer jobs in Philippines"], [100, 50, 0]]);
        t.check("...and the rows explain what's missing", cs[0].rows.slice(1).map(r => [r.muted, r.note]), [[true, "No location preference set"], [true, "No salary preference set"]]);
        await context.close();
    }
    {
        const { context, page, queries } = await openDashboard({ prefs: { preferredLocation: "Cebu, Davao", workArrangement: "remote", minSalary: null, maxSalary: null } });
        await settle(page);
        t.check("saved preferences: no notice; remote wording and the first listed place go in the query", [await page.locator(".recommend-notice").count(), queries], [0, ["Frontend Developer remote jobs in Cebu"]]);
        await context.close();
    }
    {
        const { context, page, queries } = await openDashboard({ experience: [] });
        await settle(page);
        t.check("no work history: the query uses the top skills", queries, ["React SQL jobs in Makati"]);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ jobs: [] });
        await page.waitForSelector(".no-jobs");
        t.check("nothing found", [await page.locator(".no-jobs h3").innerText(), await page.locator(".no-jobs .notice-btn").getAttribute("href")], ['No jobs found for "Frontend Developer jobs in Makati"', "Jobs.html"]);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ searchStatus: 503 });
        await page.waitForSelector(".no-jobs");
        t.check("a search failure shows the message, escaped",
            [(await page.locator(".no-jobs").innerText()).includes("Key <b>missing</b>"), await page.locator(".no-jobs b").count(), await page.locator("#jobResultText").innerText()], [true, 0, "Couldn't load recommendations."]);
        await context.close();
    }
    {
        const { context, page, queries } = await openDashboard({ resumeStatus: 500 });
        await page.waitForSelector(".no-jobs");
        t.check("a resume load failure is reported, no search made", [(await page.locator(".no-jobs").innerText()).includes("Couldn't load your resume (500)"), queries], [true, []]);
        await context.close();
    }

    t.section("interactions");
    {
        const { context, page } = await openDashboard();
        await settle(page);
        await page.locator('.view-details-btn[data-job-id="jsearch-2"]').click();
        await page.waitForFunction(() => document.getElementById("popupDescription").textContent === "FULL DETAIL");
        t.check("View Details opens the popup for that job", [await page.locator("#popupTitle").innerText(), await page.locator("#popupCompany").innerText()], ["Data Analyst", "Company J2"]);
        await page.click("#closePopupBtn");
        await page.fill("#searchInput", "sous chef & baker");
        await page.press("#searchInput", "Enter");
        await page.waitForURL("**/Jobs.html?q=*");
        t.check("the navbar search hands over to the Jobs page", [new URL(page.url()).pathname.endsWith("/Jobs.html"), new URL(page.url()).searchParams.get("q")], [true, "sous chef & baker"]);
        await context.close();
    }
    {
        const session = await loggedInPage(browser, server.baseUrl, { user: null, token: null });
        const api = await mockApi(session.context);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/dashboard.html`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("logged out: sent to login with zero API calls", [session.page.url().endsWith("/LOGIN/login.html"), api.calls.length], [true, 0]);
        await session.context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

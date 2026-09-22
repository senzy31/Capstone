// The dashboard's Recommended Jobs. The score is worked out by the server (GET /api/Recommendations): the
// page only shows what it is sent. A Free plan is sent each job's overall score and band and gets an
// invitation where the breakdown would be; a Premium plan is also sent how each part scored and which skills
// matched. Every setup, empty and error state, and external jobs keep a working Apply. API faked - the
// scoring itself is tested in the .NET tests (against the browser's old answers) and in backend.check.js.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, searchJob, nextTab } = require("./helpers");

const QUERY = "Frontend Developer jobs in Makati";

const band = score => score >= 75 ? { level: "excellent", label: "Excellent match" } : score >= 50 ? { level: "good", label: "Good match" }
    : score >= 25 ? { level: "fair", label: "Fair match" } : { level: "low", label: "Low match" };

// What the server sends per plan for the hand-worked fixture (100 / 38 / 17). The parts only exist for Premium.
const PARTS = {
    1: {
        skills: { score: 100, matched: ["React", "SQL"], total: 2, note: "Mentions 2 of your 2 skills" },
        location: { score: 100, note: "In your preferred area" },
        salary: { score: 100, note: "Meets your salary range" },
    },
    2: {
        skills: { score: 50, matched: ["SQL"], total: 2, note: "Mentions 1 of your 2 skills" },
        location: { score: 0, note: "Outside your preferred area" },
        salary: { score: null, note: "Salary not listed" },
    },
    3: {
        skills: { score: 0, matched: [], total: 2, note: "Mentions none of your 2 skills" },
        location: { score: 0, note: "Outside your preferred area" },
        salary: { score: 83, note: "Pays up to ₱25,000/month - under your ₱30,000 minimum" },
    },
};
const SCORES = { 1: 100, 2: 38, 3: 17 };

const match = (id, detailed) => detailed
    ? { detailed: true, score: SCORES[id], band: band(SCORES[id]), ...PARTS[id] }
    : { detailed: false, score: SCORES[id], band: band(SCORES[id]) };

const job = (id, extra, detailed) => ({
    ...searchJob(id, { job_title: `Job J${id}`, employer_name: `Company J${id}`, job_publisher: null, job_apply_link: null, ...extra }),
    joblink_match: match(id, detailed),
});

// Best first, as the server sends them.
const fixture = detailed => [
    job(1, { job_title: "React SQL Developer", job_publisher: "LinkedIn", job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" }, detailed),
    job(2, { job_title: "Data Analyst", job_city: "Cebu" }, detailed),
    job(3, { job_title: "Chef", job_city: "Manila", job_min_salary: 20000, job_max_salary: 25000, job_salary_period: "MONTH" }, detailed),
];

const envelope = (detailed, extra = {}) => ({ status: "OK", query: QUERY, page: 1, skillCount: 2, hasPreferences: true, detailed, data: fixture(detailed), ...extra });

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Dashboard recommendations");

    // `body` is what GET /Recommendations answers (a function of the call, or an object); `status` overrides the HTTP status.
    async function openDashboard({ premium = false, body, status = 200, abort = false, plan } = {}) {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        const errors = [];
        api.on("GET", /^\/Recommendations$/, () => abort ? { abort: true } : { status, json: typeof body === "function" ? body() : (body ?? envelope(premium)) });
        api.on("GET", /^\/Subscription$/, () => plan === null ? { status: 500, json: {} } : ({ json: { plan: premium ? "Premium" : "Free", isPremium: premium, features: { showAds: !premium }, plans: [], limits: {}, usage: {} } }));
        api.on("GET", /^\/JobSearch\/details$/, () => ({ json: { status: "OK", data: [{ job_description: "FULL DETAIL" }] } }));
        api.on("GET", /^\/JobSearch\/salary$/, () => ({ json: { status: "OK", data: [] } }));
        session.page.on("pageerror", e => errors.push(String(e)));
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/dashboard.html`);
        return { ...session, api, errors };
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
        locked: c.querySelectorAll(".match-locked").length,
        lockedLink: c.querySelector(".match-locked a")?.getAttribute("href") ?? null,
        text: c.innerText.replace(/\s+/g, " "),
        meta: c.querySelector(".job-meta").innerText.replace(/\s+/g, " ").trim(),
    })));
    const recommendationCalls = api => api.callsTo("GET", /^\/Recommendations$/);

    t.section("the page");
    {
        const { context, page, errors, api } = await openDashboard();
        await settle(page);
        t.check("the job filter is gone", await Promise.all(["workSetup", "locationInput", "minSalary", "maxSalary", "jobType", "applyFilters", "resetFilters"].map(id => page.locator("#" + id).count())), [0, 0, 0, 0, 0, 0, 0]);
        t.check("titled Recommended Jobs, greets by first name",
            [await page.locator(".job-section-header h2").innerText(), await page.locator("#welcomeText").innerText()], ["Recommended Jobs", "Welcome, Maria!"]);
        t.check("sidebar: Dashboard active, Jobs links to the Jobs page",
            [(await page.locator(".nav-links li.active a").innerText()).trim(), await page.locator('.nav-links a:has-text("Jobs")').getAttribute("href")], ["Dashboard", "Jobs.html"]);
        t.check("ONE request builds the whole list, with the login token and nothing but the page number - no user id, skills or plan",
            [recommendationCalls(api).map(c => [c.headers.authorization, c.query]), api.calls.filter(c => c.method === "GET" && c.path !== "/Subscription" && !c.path.startsWith("/Profile/by-user/") && !c.path.startsWith("/Notification")).length], [[["Bearer test-token", { page: "1" }]], 1]);
        t.check("the page no longer loads the resume, skills, preferences or runs a job search itself",
            api.calls.filter(c => /^\/(Resume|Skills|ResumeSkills|Experience|JobPreference|JobSearch)/.test(c.path)).length, 0);
        t.check("says how many jobs were scored against how many skills, and what was searched", await page.locator("#jobResultText").innerText(), `3 jobs scored against your 2 resume skills · searched "${QUERY}"`);
        t.check("no script errors; nothing was called that we didn't fake", [errors, api.unmocked], [[], []]);
        await context.close();
    }

    t.section("a Free plan sees the overall score and band - and an invitation, not the breakdown");
    {
        const { context, page } = await openDashboard();
        await settle(page);
        const cs = await cards(page);
        t.check("jobs are shown in the order the server sent them (best first)", cs.map(c => c.id), ["jsearch-1", "jsearch-2", "jsearch-3"]);
        t.check("scores as the server computed them", cs.map(c => c.score), [100, 38, 17]);
        t.check("ring uses the score", cs.map(c => c.pctVar), ["100", "38", "17"]);
        t.check("labels + colour levels", [cs.map(c => c.label), cs.map(c => c.level)], [["Excellent match", "Fair match", "Low match"], ["excellent", "fair", "low"]]);
        t.check("Job Matches counts jobs scoring 50+", await page.locator("#jobCount").innerText(), "1");
        t.check("no breakdown rows and no matched-skill chips are on any card", [cs.map(c => c.rows.length), cs.map(c => c.chips.length)], [[0, 0, 0], [0, 0, 0]]);
        t.check("each card invites an upgrade instead, linking to the Plans page", [cs.map(c => c.locked), cs.map(c => c.lockedLink)], [[1, 1, 1], ["Plans.html", "Plans.html", "Plans.html"]]);
        t.check("...in words that promise the breakdown, and say nothing of the resume", cs.map(c => [c.text.includes("See how skills, location and salary each scored"), /Mentions|preferred area|salary range|React|SQL/.test(c.text.replace("React SQL Developer", ""))]), [[true, false], [true, false], [true, false]]);
        await context.close();
    }

    t.section("a page never shows a breakdown the server did not mark detailed");
    {
        // A server that (wrongly) sent parts with a Free view: the page still draws none of it.
        const leaky = { ...envelope(false), data: fixture(false).map(j => ({ ...j, joblink_match: { ...j.joblink_match, skills: PARTS[1].skills, location: PARTS[1].location, salary: PARTS[1].salary } })) };
        const { context, page } = await openDashboard({ body: leaky });
        await settle(page);
        const cs = await cards(page);
        t.check("only detailed: true draws rows and chips", [cs.map(c => c.rows.length), cs.map(c => c.chips.length), cs.map(c => c.locked)], [[0, 0, 0], [0, 0, 0], [1, 1, 1]]);
        await context.close();
    }

    t.section("a Premium plan also sees how each part scored (all from the server, nothing worked out here)");
    {
        const { context, page } = await openDashboard({ premium: true });
        await settle(page);
        const cs = await cards(page);
        t.check("the same order, scores, labels and levels as a Free plan", [cs.map(c => c.id), cs.map(c => c.score), cs.map(c => c.label), cs.map(c => c.level)],
            [["jsearch-1", "jsearch-2", "jsearch-3"], [100, 38, 17], ["Excellent match", "Fair match", "Low match"], ["excellent", "fair", "low"]]);
        t.check("no invitation to upgrade", cs.map(c => c.locked), [0, 0, 0]);
        t.check("job 1 breakdown", cs[0].rows, [
            { name: "Skills", muted: false, pct: "100%", note: "Mentions 2 of your 2 skills" },
            { name: "Location", muted: false, pct: "100%", note: "In your preferred area" },
            { name: "Salary", muted: false, pct: "100%", note: "Meets your salary range" }]);
        t.check("job 2: a part left out of the score is muted and says why, not counted as 0", cs[1].rows, [
            { name: "Skills", muted: false, pct: "50%", note: "Mentions 1 of your 2 skills" },
            { name: "Location", muted: false, pct: "0%", note: "Outside your preferred area" },
            { name: "Salary", muted: true, pct: null, note: "Salary not listed" }]);
        t.check("job 3: pay under the minimum", cs[2].rows[2], { name: "Salary", muted: false, pct: "83%", note: "Pays up to ₱25,000/month - under your ₱30,000 minimum" });
        t.check("matched-skill chips", cs.map(c => c.chips), [["React", "SQL"], ["SQL"], []]);
        t.check("the bars are as wide as their scores", await page.$$eval(".recommended-card:first-child .match-fill", fs => fs.map(f => f.style.width)), ["100%", "100%", "100%"]);
        await context.close();
    }
    {
        const many = { ...envelope(true), data: [{ ...job(1, {}, true), joblink_match: { ...match(1, true), skills: { score: 100, matched: ["A", "B", "C", "D", "E", "F", "G", "H"], total: 8, note: "Mentions 8 of your 8 skills" } } }] };
        const { context, page } = await openDashboard({ premium: true, body: many });
        await settle(page);
        const chips = (await cards(page))[0].chips;
        t.check("a long list of matched skills shows six and the rest as a count", chips, ["A", "B", "C", "D", "E", "F", "+2 more"]);
        await context.close();
    }

    t.section("nothing from the server is trusted as HTML");
    {
        const evil = { ...envelope(true), query: "<b>q</b>", data: [{ ...job(1, { job_title: "<i>Title</i>" }, true), joblink_match: { ...match(1, true), band: { level: "excellent", label: "<u>Label</u>" }, skills: { score: 100, matched: ["<img src=x onerror=window.__pwned=1>"], total: 1, note: "<b>note</b>" }, location: { score: null, note: "<script>window.__pwned=2</script>" } } }] };
        const { context, page, errors } = await openDashboard({ premium: true, body: evil });
        await settle(page);
        t.check("titles, labels, chips, notes and the query are all shown as text", [
            await page.locator(".recommended-card i:not(.fa-solid):not(.fa-regular), .recommended-card u, .recommended-card b, .recommended-card img, .recommended-card script").count(),
            (await page.locator(".match-label").innerText()).trim(),
            (await page.locator("#jobResultText").innerText()).includes('searched "<b>q</b>"'),
            await page.evaluate(() => window.__pwned === undefined), errors,
        ], [0, "<u>Label</u>", true, true, []]);
        await context.close();
    }

    t.section("external jobs keep their score, and Apply works from the card");
    {
        const { context, page, api } = await openDashboard({ premium: true });
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
        const { context, page, api } = await openDashboard({ body: { status: "OK", query: null, page: 1, skillCount: 0, hasPreferences: true, detailed: false, data: [] } });
        await page.waitForSelector(".no-jobs");
        t.check("no skills: asks for them, links to the Resume Builder, shows no preference notice, and the job count stays 0",
            [await page.locator(".no-jobs h3").innerText(), await page.locator(".no-jobs .notice-btn").getAttribute("href"), await page.locator(".recommend-notice").count(), await page.locator("#jobCount").innerText(), await page.locator("#jobResultText").innerText()],
            ["We need your skills to find matches", "ResumeBuilder.html", 0, "0", "Add your skills to get recommendations."]);
        t.check("...and only the one request was made (the server made no job search)", recommendationCalls(api).length, 1);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ body: envelope(false, { hasPreferences: false }) });
        await settle(page);
        t.check("no preferences: a notice links to Profile preferences, and the jobs still show",
            [await page.locator(".recommend-notice .notice-btn").getAttribute("href"), await page.locator(".recommended-card").count()], ["Profile.html#preferences", 3]);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ body: envelope(false, { hasPreferences: true }) });
        await settle(page);
        t.check("saved preferences: no notice", await page.locator(".recommend-notice").count(), 0);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ body: envelope(false, { skillCount: 1, query: "React jobs in Philippines" }) });
        await settle(page);
        t.check("one skill is worded in the singular, with the query the server used", await page.locator("#jobResultText").innerText(), '3 jobs scored against your 1 resume skill · searched "React jobs in Philippines"');
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ body: envelope(false, { data: [] }) });
        await page.waitForSelector(".no-jobs");
        t.check("nothing found", [await page.locator(".no-jobs h3").innerText(), await page.locator(".no-jobs .notice-btn").getAttribute("href"), await page.locator("#jobCount").innerText()], ['No jobs found for "Frontend Developer jobs in Makati"', "Jobs.html", "0"]);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ status: 503, body: { message: "Key <b>missing</b>" } });
        await page.waitForSelector(".no-jobs");
        t.check("a failure shows the server's message, escaped",
            [(await page.locator(".no-jobs").innerText()).includes("Key <b>missing</b>"), await page.locator(".no-jobs b").count(), await page.locator("#jobResultText").innerText()], [true, 0, "Couldn't load recommendations."]);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ status: 429, body: { message: "The monthly job search limit has been reached. Please try again later." } });
        await page.waitForSelector(".no-jobs");
        t.check("out of search allowance: the message is shown", (await page.locator(".no-jobs").innerText()).includes("The monthly job search limit has been reached."), true);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ status: 500, body: () => "<html>oops</html>" });
        await page.waitForSelector(".no-jobs");
        t.check("an error with no message says the status", (await page.locator(".no-jobs").innerText()).includes("API Error: 500"), true);
        await context.close();
    }
    {
        const { context, page } = await openDashboard({ abort: true });
        await page.waitForSelector(".no-jobs");
        t.check("an unreachable server is reported, not left loading", [await page.locator("#jobResultText").innerText(), await page.locator(".loading-jobs").count()], ["Couldn't load recommendations.", 0]);
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
        const { context, page } = await openDashboard();
        await settle(page);
        await page.click('.match-locked a >> nth=0');
        await page.waitForURL("**/DASHBOARD/Plans.html");
        t.check("the upgrade invitation opens the Plans page", page.url().endsWith("/DASHBOARD/Plans.html"), true);
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

    // ----- the bell (shared with every page - see Navbar.js) --------------------------------------

    t.section("the bell");
    {
        const NOTES = [
            { notificationId: 501, message: "Your Premium plan expires in 3 days.", isRead: false, createdAt: new Date().toISOString(), type: "PremiumExpiringSoon", link: "/DASHBOARD/Plans.html" },
            { notificationId: 500, message: "Older, already read.", isRead: true, createdAt: new Date(Date.now() - 86400000).toISOString(), type: "NewApplication", link: null },
        ];
        const { context, page, api } = await openDashboard();
        // openDashboard() already navigated (and mountBell() already made its first, default-
        // mocked unread-count request) before these overrides exist - force one more check now
        // that they do, rather than waiting out the real 30s poll.
        api.on("GET", /^\/Notification\/unread-count$/, () => ({ json: { count: 3 } }));
        api.on("GET", /^\/Notification$/, () => ({ json: { data: NOTES, page: 1, pageSize: 20, totalCount: 2, unreadCount: 1 } }));
        api.on("PATCH", /^\/Notification\/501\/read$/, () => ({ json: { ...NOTES[0], isRead: true } }));
        api.on("PATCH", /^\/Notification\/read-all$/, () => ({ json: { updated: 1 } }));
        await settle(page);
        await page.evaluate(() => Navbar.refreshUnreadCount());

        await page.waitForFunction(() => document.getElementById("navBellBadge")?.hidden === false);
        t.check("the badge shows the unread count", await page.locator("#navBellBadge").innerText(), "3");

        t.check("closed by default, with the right aria state", [await page.locator("#navBellDropdown").isHidden(), await page.getAttribute("#navBellBtn", "aria-expanded")], [true, "false"]);

        await page.click("#navBellBtn");
        await page.waitForFunction(() => document.querySelectorAll(".nav-bell-item").length === 2);
        t.check("opens on click, shows both notifications newest first, unread marked",
            [await page.getAttribute("#navBellBtn", "aria-expanded"), await page.$$eval(".nav-bell-item", els => els.map(e => e.classList.contains("unread")))],
            ["true", [true, false]]);

        await page.click("body", { position: { x: 5, y: 5 } });
        t.check("clicking outside closes it", await page.locator("#navBellDropdown").isHidden(), true);

        await page.click("#navBellBtn");
        await page.keyboard.press("Escape");
        t.check("Escape closes it too", await page.locator("#navBellDropdown").isHidden(), true);

        await page.click("#navBellBtn");
        await page.click('.nav-bell-item[data-id="501"]');
        await page.waitForURL("**/DASHBOARD/Plans.html");
        t.check("clicking an item marks it read and follows its link",
            [api.callsTo("PATCH", /^\/Notification\/501\/read$/).length, page.url().endsWith("/DASHBOARD/Plans.html")], [1, true]);
        await context.close();
    }
    {
        const { context, page, api } = await openDashboard();
        api.on("GET", /^\/Notification\/unread-count$/, () => ({ json: { count: 0 } }));
        await settle(page);
        await page.waitForTimeout(300);
        t.check("no unread notifications: the badge stays hidden", await page.locator("#navBellBadge").isHidden(), true);

        api.on("GET", /^\/Notification$/, () => ({ json: { data: [], page: 1, pageSize: 20, totalCount: 0, unreadCount: 0 } }));
        let markedAll = false;
        api.on("PATCH", /^\/Notification\/read-all$/, () => { markedAll = true; return { json: { updated: 0 } }; });
        await page.click("#navBellBtn");
        await page.waitForFunction(() => document.querySelector(".nav-bell-empty") !== null);
        t.check("an empty list says so", await page.locator(".nav-bell-empty").innerText(), "No notifications yet.");
        await page.click("#navMarkAllReadBtn");
        await page.waitForResponse(response => /\/Notification\/read-all$/.test(new URL(response.url()).pathname) && response.request().method() === "PATCH");
        t.check("mark all as read calls the endpoint", markedAll, true);
        await context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

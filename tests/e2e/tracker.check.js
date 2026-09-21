// The Applications page (tracker): the three kinds of application, sorting,
// "Mark as applied", and that every request carries the login token. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, sleep } = require("./helpers");

const app = (id, extra) => ({ applicationId: id, userId: 7, jobId: 10 + id, resumeId: null, status: "Applied", isDeleted: false,
    applicationType: "External", appliedAt: null, redirectedAt: null, confirmedAt: null, ...extra });

const APPLICATIONS = [
    app(1, { status: "Applied", appliedAt: "2026-09-01T00:00:00" }),                                                      // logged by hand
    app(2, { status: "Redirected", redirectedAt: "2026-09-20T10:00:00" }),                                                 // sent to LinkedIn, not confirmed
    app(3, { status: "Applied Externally", appliedAt: "2026-09-18T09:00:00", redirectedAt: "2026-09-18T08:00:00", confirmedAt: "2026-09-18T09:00:00" }),
    app(4, { status: "Submitted", applicationType: "Internal", appliedAt: "2026-09-19T12:00:00" }),                         // employer job
    app(5, { status: "Redirected", redirectedAt: "2026-09-10T00:00:00" }),                                                 // older redirect
    app(6, { status: "Under Review" }),                                                                                    // logged by hand, no dates at all
];

const LISTINGS = {
    11: { jobId: 11, title: "Logged Job", company: "Manual Co", location: "Davao", publisher: null },
    12: { jobId: 12, title: "Redirect Job", company: "Globex", location: "Cebu", publisher: "LinkedIn" },
    13: { jobId: 13, title: "Confirmed Job", company: "Initech", location: "Manila", publisher: "Indeed" },
    14: { jobId: 14, title: "Employer Job", company: "Acme", location: "Makati", publisher: null },
    15: { jobId: 15, title: "Older Redirect", company: "Hooli", location: "Pasig", publisher: "Glassdoor" },
    16: { jobId: 16, title: "No Dates", company: "Pied Piper", location: "Taguig", publisher: null },
};

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Application tracker");

    async function openTracker({ applications = APPLICATIONS, apiSetup, dialogs = [] } = {}) {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        let rows = applications.map(a => ({ ...a }));
        api.on("GET", /^\/Application\/by-user\/7$/, () => ({ json: rows }));
        api.on("GET", /^\/Joblisting\/(\d+)$/, (call, m) => ({ json: LISTINGS[m[1]] || null }));
        api.on("PATCH", /^\/applications\/(\d+)\/confirm-external$/, (call, m) => {
            const row = rows.find(r => r.applicationId === Number(m[1]));
            if (call.body?.applied && row) { row.status = "Applied Externally"; row.appliedAt = row.confirmedAt = "2026-09-21T09:00:00"; }
            return { json: { applicationId: Number(m[1]), status: row?.status } };
        });
        apiSetup?.(api, rows);
        session.page.on("dialog", d => { dialogs.push(d.message()); d.accept(); });
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Application.html`);
        await session.page.waitForSelector(".application-card");
        return { ...session, api, rows, dialogs };
    }

    const cards = page => page.$$eval(".application-card", list => list.map(c => ({
        title: c.querySelector(".job-details span")?.textContent.trim(),
        details: c.querySelector(".job-details").innerText.replace(/\s+/g, " ").trim(),
        badge: c.querySelector(".status-badge").textContent.trim(),
        badgeClass: [...c.querySelector(".status-badge").classList].find(x => x.startsWith("status-") && x !== "status-badge"),
        select: !!c.querySelector(".status-select"),
        mark: !!c.querySelector(".mark-applied-btn"),
        withdraw: !!c.querySelector(".withdraw-btn"),
    })));

    // ================= what each kind of application shows =================
    t.section("rows");
    {
        const { context, page, api } = await openTracker();
        const list = await cards(page);

        t.check("newest first by applied date, falling back to the redirect date; undated last",
            list.map(c => c.title), ["Redirect Job", "Employer Job", "Confirmed Job", "Older Redirect", "Logged Job", "No Dates"]);
        t.check("badges", list.map(c => [c.badge, c.badgeClass]), [
            ["Redirected", "status-redirected"], ["Submitted", "status-submitted"], ["Applied Externally", "status-applied-externally"],
            ["Redirected", "status-redirected"], ["Applied", "status-applied"], ["Under Review", "status-under-review"]]);
        t.check("'Mark as applied' appears only while Redirected", list.map(c => c.mark), [true, false, false, true, false, false]);
        t.check("the status dropdown appears only for applications logged by hand", list.map(c => c.select), [false, false, false, false, true, true]);
        t.check("every row can still be withdrawn", list.every(c => c.withdraw), true);
        t.check("redirect rows say where they went", [list[0].details, list[2].details, list[3].details],
            ["Redirect Job • Cebu • via LinkedIn", "Confirmed Job • Manila • via Indeed", "Older Redirect • Pasig • via Glassdoor"]);
        t.check("rows logged by hand or by employers don't get a 'via'", [list[1].details, list[4].details], ["Employer Job • Makati", "Logged Job • Davao"]);
        t.check("the list request carries the login token", api.callsTo("GET", /by-user/)[0].headers.authorization, "Bearer test-token");
        t.check("no leftover 'Resume' lookup (the server attaches the resume now)", api.calls.some(c => /Resume/.test(c.path)), false);
        t.check("nothing unexpected was called", api.unmocked, []);
        await context.close();
    }

    // ================= mark as applied =================
    t.section("mark as applied");
    {
        const { context, page, api } = await openTracker();
        await page.locator('.application-card:has-text("Redirect Job") .mark-applied-btn').click();
        await page.waitForFunction(() => [...document.querySelectorAll(".application-card")].some(c => c.textContent.includes("Redirect Job") && c.querySelector(".status-badge").textContent.trim() === "Applied Externally"));
        const call = api.callsTo("PATCH", /confirm-external$/)[0];
        t.check("PATCH /applications/2/confirm-external {applied:true} with the token",
            [call.path, call.body, call.headers.authorization], ["/applications/2/confirm-external", { applied: true }, "Bearer test-token"]);
        const list = await cards(page);
        t.check("the row flips to Applied Externally and loses its button", [list.find(c => c.title === "Redirect Job").badge, list.filter(c => c.mark).length], ["Applied Externally", 1]);
        t.check("it now sorts by the date it was confirmed (still newest)", list[0].title, "Redirect Job");
        await context.close();
    }
    {
        // Confirmed elsewhere (409) - just refresh, no scary error.
        const { context, page, dialogs } = await openTracker({ apiSetup: a => a.on("PATCH", /confirm-external$/, () => ({ status: 409, json: { message: "This application isn't waiting for confirmation." } })) });
        await page.locator('.application-card:has-text("Older Redirect") .mark-applied-btn').click();
        await sleep(400);
        t.check("409 (already confirmed elsewhere): no error dialog", dialogs, []);
        await context.close();
    }
    {
        const { context, page, dialogs } = await openTracker({ apiSetup: a => a.on("PATCH", /confirm-external$/, () => ({ status: 500, json: { message: "Database is down" } })) });
        await page.locator('.application-card:has-text("Older Redirect") .mark-applied-btn').click();
        await page.waitForFunction(() => true);
        await sleep(400);
        t.check("a real failure is reported and the button comes back", [dialogs, await page.locator('.application-card:has-text("Older Redirect") .mark-applied-btn').isEnabled()], [["Database is down"], true]);
        await context.close();
    }

    // ================= hand-logged applications keep working =================
    t.section("status changes, withdraw");
    {
        const { context, page, api } = await openTracker({ apiSetup: a => {
            a.on("PUT", /^\/Application$/, () => ({ json: true }));
            a.on("DELETE", /^\/Application$/, () => ({ json: true }));
        } });
        await page.locator('.application-card:has-text("Logged Job") .status-select').selectOption("Interview");
        await sleep(300);
        const put = api.callsTo("PUT", /^\/Application$/)[0];
        t.check("changing a status sends only the id and the new status, with the token", [put.body, put.headers.authorization], [{ applicationId: 1, status: "Interview" }, "Bearer test-token"]);
        t.check("...and the dropdown shows it", await page.locator('.application-card:has-text("Logged Job") .badge, .application-card:has-text("Logged Job") .status-badge').first().innerText(), "Interview");

        await page.locator('.application-card:has-text("Employer Job") .withdraw-btn').click();
        await page.waitForFunction(() => ![...document.querySelectorAll(".application-card")].some(c => c.textContent.includes("Employer Job")));
        const del = api.callsTo("DELETE", /^\/Application$/)[0];
        t.check("withdrawing sends DELETE ?id=4 with the token and removes the row", [del.query, del.headers.authorization, (await cards(page)).length], [{ id: "4" }, "Bearer test-token", 5]);
        await context.close();
    }
    {
        const { context, page, dialogs } = await openTracker({ apiSetup: a => a.on("PUT", /^\/Application$/, () => ({ status: 409, json: { message: "This application's status is managed by JobLink and can't be changed here." } })) });
        await page.locator('.application-card:has-text("Logged Job") .status-select').selectOption("Offer");
        await sleep(400);
        t.check("if the server refuses a status change, its message is shown", dialogs, ["This application's status is managed by JobLink and can't be changed here."]);
        await context.close();
    }

    // ================= "Log Application" =================
    t.section("log an application by hand");
    {
        const { context, page, api } = await openTracker({ apiSetup: a => {
            a.on("POST", /^\/Joblisting$/, () => ({ json: { jobId: 900, title: "Typed Job", company: "Typed Co", location: "Iloilo", source: "External", sourceApi: "manual" } }));
            a.on("POST", /^\/Application$/, () => ({ json: true }));
        } });
        await page.click("#logApplicationBtn");
        await page.fill("#logCompany", "Typed Co");
        await page.fill("#logPosition", "Typed Job");
        await page.fill("#logLocation", "Iloilo");
        await page.selectOption("#logStatus", "Interview");
        await page.fill("#logDate", "2026-08-30");
        await page.click("#submitLogApplication");
        await page.waitForFunction(() => !document.getElementById("logApplicationOverlay").classList.contains("show"));

        const job = api.callsTo("POST", /^\/Joblisting$/)[0], created = api.callsTo("POST", /^\/Application$/)[0];
        t.check("the job goes up with only what was typed, plus the token", [job.body, job.headers.authorization], [{ title: "Typed Job", company: "Typed Co", location: "Iloilo" }, "Bearer test-token"]);
        t.check("the application uses the id the server returned - no user/resume/apply fields are sent",
            [created.body.jobId, created.body.status, created.body.appliedAt.startsWith("2026-08-30"), Object.keys(created.body).sort()], [900, "Interview", true, ["appliedAt", "jobId", "status"]]);
        t.check("no more fetching every job listing to find the new one", api.callsTo("GET", /^\/Joblisting$/).length, 0);
        await context.close();
    }
    {
        const { context, page, dialogs } = await openTracker({ apiSetup: a => a.on("POST", /^\/Joblisting$/, () => ({ status: 400, json: { message: "A job title and company are required." } })) });
        await page.click("#logApplicationBtn");
        await page.fill("#logCompany", "Typed Co");
        await page.fill("#logPosition", "Typed Job");
        await page.click("#submitLogApplication");
        await sleep(400);
        t.check("a server error while logging shows the server's message", dialogs, ["A job title and company are required."]);
        await context.close();
    }

    // ================= the prompt works here too =================
    t.section("return prompt on this page");
    {
        const { context, page, api } = await openTracker();
        await page.evaluate(() => localStorage.setItem("joblink.pendingApply", JSON.stringify([{ applicationId: 2, jobTitle: "Redirect Job", publisher: "LinkedIn", left: true }])));
        await page.reload();
        await page.waitForSelector(".af-overlay");
        await page.getByRole("button", { name: "Yes, I applied" }).click();
        await page.waitForFunction(() => !document.querySelector(".af-overlay"));
        await page.waitForFunction(() => [...document.querySelectorAll(".application-card")].some(c => c.textContent.includes("Redirect Job") && c.querySelector(".status-badge").textContent.trim() === "Applied Externally"));
        t.check("answering Yes refreshes the list to show Applied Externally", api.callsTo("PATCH", /confirm-external$/)[0].body, { applied: true });
        await context.close();
    }
    {
        // Confirming from the tracker button clears the pending question so it isn't asked again.
        const { context, page } = await openTracker();
        await page.evaluate(() => localStorage.setItem("joblink.pendingApply", JSON.stringify([{ applicationId: 2, jobTitle: "Redirect Job", publisher: "LinkedIn", left: false }])));
        await page.locator('.application-card:has-text("Redirect Job") .mark-applied-btn').click();
        await page.waitForFunction(() => !document.querySelector(".mark-applied-btn:not([disabled])") || true);
        await sleep(400);
        t.check("using 'Mark as applied' clears the pending question", await page.evaluate(() => localStorage.getItem("joblink.pendingApply")), null);
        await context.close();
    }

    // ================= no token =================
    t.section("no login token");
    {
        const session = await loggedInPage(browser, server.baseUrl, { token: null });
        const api = await mockApi(session.context);
        api.on("GET", /Application/, () => ({ json: [] }));
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Application.html`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("a session without a token is sent to log in", [session.page.url().endsWith("/LOGIN/login.html"), api.calls.filter(c => c.method !== "GET").length], [true, 0]);
        await session.context.close();
    }
    {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        api.on("GET", /^\/Application\/by-user\/7$/, () => ({ status: 401, json: { message: "no" } }));
        session.page.on("dialog", d => d.accept());
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Application.html`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("a 401 from the API (expired token) logs the user out", await session.page.evaluate(() => [localStorage.getItem("token"), localStorage.getItem("user")]), [null, null]);
        await session.context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

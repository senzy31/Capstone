// The whole thing in a real browser against the REAL backend and database: log in through
// the login page, search jobs (one live JSearch call), click "Apply on {publisher}", answer
// "Did you finish applying?", then see it in the Applications page.
//
// This is the one check that proves the browser's cross-origin rules (CORS) accept the login
// token header and the PATCH request.
//   - needs: the backend running, SQL Server LocalDB and `sqlcmd` on the PATH
//   - makes ONE live JSearch call; the jobs a search imports are left in Job_Listings (that is
//     what a normal search does) - the test user and their applications are removed
const { chromium } = require("playwright");
const { execSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { startStaticServer, createChecker, sleep, nextTab } = require("./helpers");

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";   // the dev HTTPS certificate is self-signed

const API = "https://localhost:7142/api";
const STAMP = Date.now();
const TMP = path.join(os.tmpdir(), `joblink-fullstack-${STAMP}.sql`);
const QUERY = "accountant";   // different from backend.check.js so their search caches don't overlap

function sql(query) {
    fs.writeFileSync(TMP, "SET NOCOUNT ON;\n" + query);
    return execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d Joblinkv2 -C -I -b -h -1 -W -s "|" -i "${TMP}"`, { encoding: "utf8" })
        .split(/\r?\n/).map(l => l.trim()).filter(Boolean);
}

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Full stack (real browser + real backend + real database)");
    const email = `e2e.fullstack.${STAMP}@example.com`, password = "E2ePassw0rd!";
    let userId = null;

    try {
        const created = await fetch(`${API}/User`, { method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullName: "E2E Fullstack", email, passwordHash: password, role: "user" }) });
        if (!created.ok) throw new Error("could not create the test user (is the backend running?)");
        userId = Number(sql(`SELECT user_id FROM Users WHERE email = '${email}';`)[0]);

        const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
        // Whatever job site we're sent to: don't touch the real internet, just answer.
        await context.route(url => url.protocol === "https:" && !/^(localhost|cdnjs\.cloudflare\.com|fonts\.(googleapis|gstatic)\.com)$/.test(url.hostname),
            route => route.fulfill({ status: 200, contentType: "text/html", body: "<title>job site</title>" }));
        const page = await context.newPage();
        const problems = [];
        page.on("pageerror", e => problems.push("pageerror: " + e));
        page.on("console", m => { if (m.type() === "error" && !/favicon|Failed to load resource: the server responded with a status of (404|410)/.test(m.text())) problems.push("console: " + m.text()); });
        page.on("dialog", d => d.accept());

        t.section("log in through the login page");
        await page.goto(`${server.baseUrl}/LOGIN/login.html`);
        await page.fill("#loginEmail", email);
        await page.fill("#loginPassword", password);
        await page.click("#loginBtn");
        await page.waitForURL("**/DASHBOARD/dashboard.html", { timeout: 15000 });
        const token = await page.evaluate(() => localStorage.getItem("token"));
        t.check("the real backend issued a token and the dashboard opened", [typeof token, token.split(".").length], ["string", 3]);

        t.section("search, then Apply on the first job");
        await page.goto(`${server.baseUrl}/DASHBOARD/Jobs.html?q=${QUERY}`);
        await page.waitForSelector(".job-card", { timeout: 60000 });
        const firstCard = page.locator(".job-card").first();
        const label = (await firstCard.locator(".apply-job-btn").innerText()).replace(/\s+/g, " ").trim();
        const note = (await firstCard.locator(".external-note").innerText()).replace(/\s+/g, " ").trim();
        const title = (await firstCard.locator(".job-title").innerText()).trim();
        const publisher = label.replace(/^Apply on /, "");
        t.check("real search results: external button + note", [label.startsWith("Apply on "), publisher.length > 0, note.includes(`posted on ${publisher}`)], [true, true, true]);

        const tabPromise = nextTab(context, 15000);
        await firstCard.locator(".apply-job-btn").click();
        const tab = await tabPromise;
        t.check("a tab opened", tab !== null, true);
        await tab.waitForURL(u => u.protocol === "https:", { timeout: 15000 });
        const redirectUrl = tab.url();
        t.check("the tab went to an https job-site URL and can't reach back into JobLink", [redirectUrl.startsWith("https://"), await tab.evaluate(() => window.opener)], [true, null]);

        const row = sql(`SELECT a.application_id, a.status, a.application_type, CASE WHEN a.redirected_at IS NULL THEN 'null' ELSE 'set' END, ISNULL(CONVERT(varchar, a.applied_at), 'NULL'), j.title
                         FROM Applications a JOIN Job_Listings j ON j.job_id = a.job_id WHERE a.user_id = ${userId};`);
        const [applicationId, status, type, redirectedAt, appliedAt] = row[0].split("|");
        t.check("the database now has one Redirected External application (redirected_at set, applied_at empty)", [row.length, status, type, redirectedAt, appliedAt], [1, "Redirected", "External", "set", "NULL"]);

        t.section("come back and answer the question");
        await page.evaluate(() => { window.dispatchEvent(new Event("blur")); window.dispatchEvent(new Event("focus")); });
        await page.waitForSelector(".af-overlay");
        t.check("the modal names the job and the site", (await page.locator(".af-modal p").first().innerText()).replace(/\s+/g, " ").includes(`Did you finish applying for ${title} on`), true);
        await page.getByRole("button", { name: "Yes, I applied" }).click();
        await page.waitForFunction(() => !document.querySelector(".af-overlay"), null, { timeout: 15000 });
        const confirmed = sql(`SELECT status, CASE WHEN confirmed_at IS NULL THEN 'null' ELSE 'set' END, CASE WHEN applied_at IS NULL THEN 'null' ELSE 'set' END FROM Applications WHERE application_id = ${applicationId};`)[0];
        t.check("Yes reached the real database: Applied Externally, confirmed_at and applied_at set", confirmed, "Applied Externally|set|set");

        t.section("clicking Apply again does not duplicate");
        const tab2Promise = nextTab(context, 15000);
        await firstCard.locator(".apply-job-btn").click();
        const tab2 = await tab2Promise;
        await tab2?.waitForURL(u => u.protocol === "https:", { timeout: 15000 });
        await sleep(500);
        t.check("still one application, still Applied Externally, and no new question", [Number(sql(`SELECT COUNT(*) FROM Applications WHERE user_id = ${userId};`)[0]),
            sql(`SELECT status FROM Applications WHERE user_id = ${userId};`)[0], await page.locator(".af-overlay").count()], [1, "Applied Externally", 0]);

        t.section("the Applications page");
        await page.goto(`${server.baseUrl}/DASHBOARD/Application.html`);
        await page.waitForSelector(".application-card", { timeout: 15000 });
        const card = page.locator(".application-card").first();
        t.check("shows it as Applied Externally, 'via' the site, with no Mark-as-applied button and no dropdown",
            [(await card.locator(".status-badge").innerText()).trim(), (await card.locator(".job-details").innerText()).replace(/\s+/g, " ").includes(`via ${publisher}`), await card.locator(".mark-applied-btn").count(), await card.locator(".status-select").count()],
            ["Applied Externally", true, 0, 0]);

        t.section("no browser errors (this is where a CORS problem would show up)");
        t.check("no page errors or console errors", problems, []);
        await context.close();
    } finally {
        try {
            if (userId) sql(`DELETE FROM Applications WHERE user_id = ${userId}; DELETE FROM Notifications WHERE user_id = ${userId}; DELETE FROM Users WHERE user_id = ${userId};`);
            console.log("\ncleanup done (test user and their applications removed)");
        } catch (e) { console.log("CLEANUP FAILED - remove", email, "by hand:", e.message); }
        try { fs.unlinkSync(TMP); } catch {}
        await browser.close();
        await server.close();
    }

    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

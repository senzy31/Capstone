// The whole thing in a real browser against the REAL backend and database: log in through
// the login page, search jobs (one live JSearch call), click "Apply on {publisher}", answer
// "Did you finish applying?", then see it in the Applications page.
//
// This is the one check that proves the browser's cross-origin rules (CORS) accept the login
// token header and the PATCH request.
//   - needs: the backend running, SQL Server LocalDB and `sqlcmd` on the PATH
//   - makes ONE live JSearch call; the jobs a search imports are left in Job_Listings (that is
//     what a normal search does) - the test user and everything the pages created for them are removed
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
            body: JSON.stringify({ fullName: "E2E Fullstack", email, password, role: "user" }) });
        if (!created.ok) throw new Error("could not create the test user (is the backend running?)");
        userId = Number(sql(`SELECT user_id FROM Users WHERE email = '${email}';`)[0]);

        const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
        // Whatever job site we're sent to: don't touch the real internet, just answer.
        await context.route(url => url.protocol === "https:" && !/^(localhost|cdnjs\.cloudflare\.com|fonts\.(googleapis|gstatic)\.com)$/.test(url.hostname),
            route => route.fulfill({ status: 200, contentType: "text/html", body: "<title>job site</title>" }));
        const page = await context.newPage();
        const problems = [];
        page.on("pageerror", e => problems.push("pageerror: " + e));
        page.on("console", m => { if (m.type() === "error" && !/favicon|Failed to load resource: the server responded with a status of (400|403|404|410)/.test(m.text())) problems.push("console: " + m.text()); });
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

        t.section("the Profile page: change the name, then the email through the password prompt");
        await page.goto(`${server.baseUrl}/DASHBOARD/Profile.html`);
        await page.waitForFunction(() => document.getElementById("fullNameDisplay")?.textContent.includes("E2E Fullstack"), null, { timeout: 15000 });
        await page.click("#editProfileBtn");
        await page.fill("#fullNameInput", "E2E Renamed");
        await page.click("#saveBtn");
        await page.getByText("Profile updated successfully!").waitFor({ timeout: 15000 });
        t.check("a name change saves for real, with no password dialog", [sql(`SELECT full_name FROM Users WHERE user_id = ${userId};`)[0], await page.locator(".ac-overlay").count()], ["E2E Renamed", 0]);

        const newEmail = `e2e.fullstack2.${STAMP}@example.com`;
        await page.getByText("Profile updated successfully!").waitFor({ state: "detached", timeout: 15000 });
        await page.click("#editProfileBtn");
        await page.fill("#emailInput", newEmail);
        await page.click("#saveBtn");
        await page.locator(".ac-overlay").waitFor({ timeout: 15000 });
        await page.fill(".ac-input", "not-my-password");
        await page.keyboard.press("Enter");
        await page.waitForSelector(".ac-error:not([hidden])", { timeout: 15000 });
        t.check("the real server refuses a wrong password, the dialog asks again, and you stay logged in",
            [(await page.locator(".ac-error").innerText()).trim(), sql(`SELECT email FROM Users WHERE user_id = ${userId};`)[0], page.url().includes("Profile.html"), await page.evaluate(() => Boolean(localStorage.getItem("token")))],
            ["That password isn't correct.", email, true, true]);
        await page.fill(".ac-input", password);
        await page.keyboard.press("Enter");
        for (let i = 0; i < 50 && sql(`SELECT email FROM Users WHERE user_id = ${userId};`)[0] !== newEmail; i++) await sleep(200);
        t.check("the right password changes the login email in the database", sql(`SELECT email FROM Users WHERE user_id = ${userId};`)[0], newEmail);
        const relogin = await fetch(`${API}/User/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: newEmail, password }) });
        t.check("...and the new email logs in", relogin.status, 200);

        t.section("the Resume Builder: a month date, deleting an entry, and skills - all three were broken or open before");
        const inMyResumes = `resume_id IN (SELECT resume_id FROM Resumes WHERE user_id = ${userId})`;
        const experienceRows = () => sql(`SELECT position, ISNULL(CONVERT(varchar(10), start_date, 23), 'NULL'), is_deleted FROM Experience WHERE ${inMyResumes} ORDER BY experience_id;`);
        const waitFor = async (read, done) => { for (let i = 0; i < 60 && !done(read()); i++) await sleep(200); return read(); };

        await page.goto(`${server.baseUrl}/DASHBOARD/ResumeBuilder.html`);
        await page.waitForFunction(() => document.getElementById("addExperienceBtn") && !document.getElementById("addExperienceBtn").disabled, null, { timeout: 20000 });
        await page.click("#addExperienceBtn");
        await page.waitForSelector('#experienceList [data-field="position"]', { timeout: 15000 });
        await page.fill('#experienceList [data-field="position"]', "QA Engineer");
        await page.fill('#experienceList [data-field="companyName"]', "Acme");
        await page.fill('#experienceList [data-field="startDate"]', "2021-05");
        t.check("the text and the month date really saved (a month like 2021-05 used to be refused)",
            await waitFor(experienceRows, rows => rows[0]?.startsWith("QA Engineer|2021-05-01")), ["QA Engineer|2021-05-01|0"]);

        await page.reload();
        await page.waitForSelector('#experienceList [data-field="startDate"]', { timeout: 15000 });
        t.check("...and both come back after a reload", [await page.inputValue('#experienceList [data-field="position"]'), await page.inputValue('#experienceList [data-field="startDate"]')], ["QA Engineer", "2021-05"]);

        await page.click(".remove-entry-btn");
        t.check("removing the entry works (it returned a 500 before) and it stays gone",
            [await waitFor(experienceRows, rows => rows[0]?.endsWith("|1")), await page.locator(".empty-entry-hint").first().isVisible()], [["QA Engineer|2021-05-01|1"], true]);

        const skillName = `E2E Skill ${STAMP}`;
        const skillLinks = live => sql(`SELECT COUNT(*) FROM Resume_Skills rs JOIN Skills s ON s.skill_id = rs.skill_id WHERE s.skill_name = '${skillName}' AND rs.is_deleted = ${live ? 0 : 1} AND rs.${inMyResumes};`)[0];
        await page.fill("#skillInput", skillName);
        await page.click("#addSkillBtn");
        t.check("a new skill is created and put on the resume", await waitFor(() => skillLinks(true), n => n === "1"), "1");
        await page.click(".remove-skill");
        t.check("removing it keeps the row but marks it deleted", [await waitFor(() => skillLinks(true), n => n === "0"), skillLinks(false)], ["0", "1"]);
        await page.fill("#skillInput", skillName);
        await page.click("#addSkillBtn");
        t.check("adding the same skill again brings it back instead of failing on the table's key", [await waitFor(() => skillLinks(true), n => n === "1"), skillLinks(false)], ["1", "0"]);

        t.section("no browser errors (this is where a CORS problem would show up)");
        t.check("no page errors or console errors", problems, []);
        await context.close();
    } finally {
        try {
            if (userId) sql(`DELETE FROM Applications WHERE user_id = ${userId}; DELETE FROM Notifications WHERE user_id = ${userId}; DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id = ${userId}); DELETE FROM Education WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id = ${userId}); DELETE FROM Experience WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id = ${userId}); DELETE FROM Resumes WHERE user_id = ${userId}; DELETE FROM Skills WHERE skill_name = 'E2E Skill ${STAMP}'; DELETE FROM Profiles WHERE user_id = ${userId}; DELETE FROM Job_Preferences WHERE user_id = ${userId}; DELETE FROM Users WHERE user_id = ${userId};`);
            console.log("\ncleanup done (test user and everything the pages created for them removed)");
        } catch (e) { console.log("CLEANUP FAILED - remove", email, "by hand:", e.message); }
        try { fs.unlinkSync(TMP); } catch {}
        await browser.close();
        await server.close();
    }

    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

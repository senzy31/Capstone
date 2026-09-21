// Your account details on the Profile page and in the Resume Builder. Only the name and
// email are ever sent to PUT /api/User (never a whole record), the login token goes with
// every call, and changing the email asks for the current password. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, sleep } = require("./helpers");

const ACCOUNT = { userId: 7, fullName: "Maria Santos", email: "maria@example.com", role: "user", companyName: null, createdAt: "2026-01-01T00:00:00" };
const PASSWORD = "Sup3rSecret!";

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Account details (Profile + Resume Builder)");

    // ----- the Profile page -----------------------------------------------------

    async function profile(putUser) {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        api.on("GET", /^\/User\/7$/, () => ({ json: ACCOUNT }));
        api.on("GET", /^\/Profile\/by-user\/7$/, () => ({ status: 404 }));
        api.on("GET", /^\/JobPreference\/by-user\/7$/, () => ({ status: 404 }));
        api.on("POST", /^\/Profile$/, () => ({ json: true }));
        api.on("PUT", /^\/Profile$/, () => ({ json: true }));
        if (putUser) api.on("PUT", /^\/User$/, putUser);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Profile.html`);
        await session.page.waitForFunction(() => document.getElementById("fullNameDisplay")?.textContent.includes("Maria"));
        return { ...session, api };
    }

    const editAndSave = async (page, { name, email }) => {
        await page.click("#editProfileBtn");
        if (name !== undefined) await page.fill("#fullNameInput", name);
        if (email !== undefined) await page.fill("#emailInput", email);
        await page.click("#saveBtn");
    };

    const putUserBodies = api => api.callsTo("PUT", /^\/User$/).map(c => c.body);
    const savedEmail = page => page.evaluate(() => JSON.parse(localStorage.getItem("user")).email);

    t.section("Profile: reading and saving just your name");
    {
        const { context, page, api } = await profile(() => ({ json: { message: "User updated successfully", user: { ...ACCOUNT, fullName: "Maria S. Santos" } } }));
        t.check("the account is read with the login token", api.callsTo("GET", /^\/User\/7$/)[0].headers.authorization, "Bearer test-token");
        await editAndSave(page, { name: "Maria S. Santos" });
        await page.getByText("Profile updated successfully!").waitFor();
        const call = api.callsTo("PUT", /^\/User$/)[0];
        t.check("PUT /User carries only the name and email - no role, hash, id or flags", call.body, { fullName: "Maria S. Santos", email: "maria@example.com" });
        t.check("...with the login token", call.headers.authorization, "Bearer test-token");
        t.check("no password dialog for a name change", await page.locator(".ac-overlay").count(), 0);
        t.check("the rest of the profile still saved", api.callsTo("POST", /^\/Profile$/).length + api.callsTo("PUT", /^\/Profile$/).length, 1);
        t.check("every API call the page made (account, profile, preferences) carried the login token", api.calls.every(c => c.headers.authorization === "Bearer test-token"), true);
        t.check("nothing else was called that we didn't fake", api.unmocked, []);
        await context.close();
    }

    t.section("Profile: changing the email asks for the current password");
    {
        const { context, page, api } = await profile(call => {
            if (!call.body.currentPassword) return { status: 400, json: { message: "Enter your current password to change your email.", code: "password_required" } };
            if (call.body.currentPassword !== PASSWORD) return { status: 403, json: { message: "That password isn't correct.", code: "wrong_password" } };
            return { json: { message: "User updated successfully", user: { ...ACCOUNT, email: call.body.email } } };
        });

        await editAndSave(page, { email: "maria.new@example.com" });
        await page.locator(".ac-overlay").waitFor();
        t.check("a dialog asks for the password (masked, focused)", [
            await page.locator(".ac-input").getAttribute("type"),
            await page.evaluate(() => document.activeElement?.classList.contains("ac-input")),
            (await page.locator(".ac-modal").innerText()).includes("current password"),
            await page.locator(".ac-error").isHidden()
        ], ["password", true, true, true]);

        await page.fill(".ac-input", "wrong");
        await page.keyboard.press("Enter");
        await page.waitForSelector(".ac-error:not([hidden])");
        t.check("a wrong password asks again and says why (and does not log you out)", [
            (await page.locator(".ac-error").innerText()).trim(),
            page.url().endsWith("/DASHBOARD/Profile.html"),
            await page.evaluate(() => localStorage.getItem("token"))
        ], ["That password isn't correct.", true, "test-token"]);

        await page.fill(".ac-input", PASSWORD);
        await page.click(".ac-btn-primary");
        await page.getByText("Profile updated successfully!").waitFor();

        t.check("the three attempts: none, wrong, right - each with the new email",
            putUserBodies(api).map(b => [b.email, b.currentPassword ?? null]),
            [["maria.new@example.com", null], ["maria.new@example.com", "wrong"], ["maria.new@example.com", PASSWORD]]);
        t.check("the dialog is gone and the saved login shows the new email", [await page.locator(".ac-overlay").count(), await savedEmail(page)], [0, "maria.new@example.com"]);
        await context.close();
    }

    t.section("Profile: cancelling the password dialog");
    for (const how of ["Cancel button", "Escape key"]) {
        const { context, page, api } = await profile(() => ({ status: 400, json: { message: "Enter your current password to change your email.", code: "password_required" } }));
        await editAndSave(page, { email: "maria.new@example.com" });
        await page.locator(".ac-overlay").waitFor();
        if (how === "Cancel button") await page.click("[data-cancel]"); else await page.keyboard.press("Escape");
        await page.getByText("Nothing was changed - your email stays the same.").waitFor();
        t.check(`${how}: nothing is saved anywhere and the email is unchanged`, [
            await page.locator(".ac-overlay").count(),
            api.callsTo("PUT", /^\/Profile$/).length + api.callsTo("POST", /^\/Profile$/).length,
            await savedEmail(page)
        ], [0, 0, "maria@example.com"]);
        await context.close();
    }

    t.section("Profile: the server refuses");
    {
        const { context, page } = await profile(() => ({ status: 409, json: { message: "That email is already in use.", code: "email_taken" } }));
        await editAndSave(page, { email: "taken@example.com" });
        await page.getByText("That email is already in use.").waitFor();
        t.check("an email that's taken says so - no password dialog", await page.locator(".ac-overlay").count(), 0);
        await context.close();
    }
    {
        const { context, page } = await profile(() => ({ status: 401, json: {} }));
        await editAndSave(page, { name: "Someone Else" });
        await page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("an expired login sends you to the login page and clears the session", await page.evaluate(() => [localStorage.getItem("token"), localStorage.getItem("user")]), [null, null]);
        await context.close();
    }
    {
        const { context, page } = await profile(() => ({ abort: true }));
        await editAndSave(page, { name: "Someone Else" });
        await page.getByText("Couldn't save to the server. Check that the API is running and try again.").waitFor();
        t.check("an unreachable server keeps the generic message", true, true);
        await context.close();
    }

    t.section("Profile: a page opened without a login token");
    {
        const session = await loggedInPage(browser, server.baseUrl, { token: null });
        const api = await mockApi(session.context);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/Profile.html`);
        await session.page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("goes to login and never asks the API for the account", api.callsTo("GET", /^\/User\//).length, 0);
        await session.context.close();
    }

    // ----- the Resume Builder ---------------------------------------------------

    async function builder() {
        const session = await loggedInPage(browser, server.baseUrl);
        const api = await mockApi(session.context);
        api.on("GET", /^\/User\/7$/, () => ({ json: ACCOUNT }));
        api.on("GET", /^\/Profile\/by-user\/7$/, () => ({ status: 404 }));
        api.on("GET", /^\/Resume\/by-user\/7$/, () => ({ json: [{ resumeId: 5, userId: 7, title: "My Resume", aiGeneratedContent: "" }] }));
        api.on("GET", /^\/(Experience|Education)\/by-resume\/5$/, () => ({ json: [] }));
        api.on("GET", /^\/Skills$/, () => ({ json: [] }));
        api.on("GET", /^\/ResumeSkills\/by-resume\/5$/, () => ({ json: [] }));
        api.on("POST", /^\/Profile$/, () => ({ json: true }));
        api.on("PUT", /^\/Resume$/, () => ({ json: true }));
        api.on("PUT", /^\/User$/, call => ({ json: { message: "User updated successfully", user: { ...ACCOUNT, fullName: call.body.fullName || ACCOUNT.fullName } } }));
        await session.page.goto(`${server.baseUrl}/DASHBOARD/ResumeBuilder.html`);
        await session.page.waitForFunction(() => document.getElementById("fullName")?.value === "Maria Santos");
        return { ...session, api };
    }

    const waitForCalls = async (api, method, pattern, count, ms = 4000) => {
        const until = Date.now() + ms;
        while (Date.now() < until && api.callsTo(method, pattern).length < count) await sleep(100);
    };

    t.section("Resume Builder: autosave never touches your login email");
    {
        const { context, page, api } = await builder();
        t.check("the account is read with the login token", api.callsTo("GET", /^\/User\/7$/)[0].headers.authorization, "Bearer test-token");
        t.check("the email box starts as the account email", await page.inputValue("#email"), "maria@example.com");

        await page.fill("#email", "resume.contact@example.com");
        await waitForCalls(api, "PUT", /^\/User$/, 1);
        const body = api.callsTo("PUT", /^\/User$/)[0]?.body;
        t.check("autosave sent the name only - no email, so no password is ever needed", [body && Object.keys(body), await page.locator(".ac-overlay").count()], [["fullName"], 0]);

        await waitForCalls(api, "PUT", /^\/Resume$/, 1);
        const extras = await page.evaluate(() => JSON.parse(localStorage.getItem("joblinkResumeExtras_7")));
        t.check("the email typed on the resume is kept with the resume", extras.resumeEmail, "resume.contact@example.com");

        await page.reload();
        await page.waitForFunction(() => document.getElementById("fullName")?.value === "Maria Santos");
        t.check("...and is still there after a reload", await page.inputValue("#email"), "resume.contact@example.com");

        const before = api.callsTo("PUT", /^\/Resume$/).length;
        await page.fill("#email", "maria@example.com");
        await waitForCalls(api, "PUT", /^\/Resume$/, before + 1);
        t.check("typing the account email back drops the override, so the resume follows the account again",
            (await page.evaluate(() => JSON.parse(localStorage.getItem("joblinkResumeExtras_7")))).resumeEmail, "");
        t.check("the saved login snapshot keeps the account email", await savedEmail(page), "maria@example.com");
        t.check("every API call the builder made (account, profile, resume, entries, skills) carried the login token", [api.calls.length > 6, api.calls.every(c => c.headers.authorization === "Bearer test-token")], [true, true]);
        t.check("nothing was called that we didn't fake", api.unmocked, []);
        await context.close();
    }

    t.section("Resume Builder: a blank name is not sent to your account");
    {
        const { context, page, api } = await builder();
        await page.fill("#fullName", "");
        await waitForCalls(api, "PUT", /^\/Resume$/, 1);
        t.check("the resume still saves, but no PUT /User goes out with an empty name", [api.callsTo("PUT", /^\/Resume$/).length >= 1, api.callsTo("PUT", /^\/User$/).length], [true, 0]);
        await context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

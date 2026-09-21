// Login, signup and logout keep a login TOKEN now, and an old saved session without one
// must not bounce between the login page and the dashboard. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi, sleep } = require("./helpers");

const TOKEN = "header.payload.signature";
const LOGIN_OK = (role = "user") => ({ json: { message: "Login successful", token: TOKEN, user: { userId: 7, fullName: "Maria Santos", email: "maria@example.com", role, companyName: null } } });

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Login / session");

    async function open(path, { user, token, apiSetup } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { user, token });
        const api = await mockApi(session.context);
        apiSetup?.(api);
        const dialogs = [];
        session.page.on("dialog", d => { dialogs.push(d.message()); d.accept(); });
        await session.page.goto(`${server.baseUrl}${path}`);
        return { ...session, api, dialogs };
    }
    const stored = page => page.evaluate(() => ({ token: localStorage.getItem("token"), user: localStorage.getItem("user") }));
    const EXPIRED = "Your session expired. Please log in again.";

    t.section("old saved sessions (no token)");
    {
        const { context, page, dialogs } = await open("/LOGIN/login.html", { token: null });
        await sleep(600);
        t.check("login page: the stale session is dropped, the user is told once, and stays on the login page",
            [await stored(page), dialogs, page.url().endsWith("/LOGIN/login.html")], [{ token: null, user: null }, [EXPIRED], true]);
        await page.reload();
        await sleep(400);
        t.check("...and isn't told again after a reload", dialogs.length, 1);
        await context.close();
    }
    {
        const { context, page, dialogs } = await open("/DASHBOARD/dashboard.html", { token: null });
        await page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        await sleep(1200);
        t.check("dashboard -> login and it STAYS there (no redirect loop), with one explanation",
            [page.url().endsWith("/LOGIN/login.html"), dialogs], [true, [EXPIRED]]);
        await context.close();
    }
    {
        const { context, page, dialogs } = await open("/LOGIN/signup.html", { token: null });
        await sleep(500);
        t.check("signup page: drops the stale session quietly (no alert)", [await stored(page), dialogs], [{ token: null, user: null }, []]);
        await context.close();
    }

    t.section("valid sessions");
    {
        const { context, page } = await open("/LOGIN/login.html");
        await page.waitForURL("**/DASHBOARD/dashboard.html", { timeout: 8000 });
        t.check("a job seeker who is already logged in skips the login page", page.url().endsWith("/DASHBOARD/dashboard.html"), true);
        await context.close();
    }
    {
        const { context, page } = await open("/LOGIN/login.html", { user: { userId: 9, fullName: "Ric", email: "r@x.com", role: "employer" } });
        await page.waitForURL("**/Employer%20Dashboard/dashboard.html", { timeout: 8000 });
        t.check("an employer goes to the employer dashboard", decodeURIComponent(page.url()).endsWith("/Employer Dashboard/dashboard.html"), true);
        await context.close();
    }
    {
        const { context, page } = await open("/LOGIN/login.html", { user: null, token: "orphan-token" });
        await sleep(500);
        t.check("a token with no saved user doesn't redirect", page.url().endsWith("/LOGIN/login.html"), true);
        await context.close();
    }

    t.section("logging in stores the token");
    {
        const { context, page, api } = await open("/LOGIN/login.html", { user: null, token: null, apiSetup: a => a.on("POST", /^\/User\/login$/, () => LOGIN_OK()) });
        await page.fill("#loginEmail", "maria@example.com");
        await page.fill("#loginPassword", "Secret123!");
        await page.click("#loginBtn");
        await page.waitForURL("**/DASHBOARD/dashboard.html", { timeout: 8000 });
        const saved = await stored(page);
        t.check("the token and the user are saved, and a job seeker lands on the dashboard",
            [saved.token, JSON.parse(saved.user).fullName, api.callsTo("POST", /login$/)[0].body], [TOKEN, "Maria Santos", { email: "maria@example.com", password: "Secret123!" }]);
        await context.close();
    }
    {
        const { context, page, dialogs } = await open("/LOGIN/login.html", { user: null, token: null, apiSetup: a => a.on("POST", /^\/User\/login$/, () => ({ status: 401, text: "Invalid password" })) });
        await page.fill("#loginEmail", "maria@example.com");
        await page.fill("#loginPassword", "wrong");
        await page.click("#loginBtn");
        await page.waitForFunction(() => document.getElementById("loginBtn").textContent.trim() === "Login");
        t.check("a wrong password stores nothing and says why", [await stored(page), dialogs.includes("Invalid password"), page.url().endsWith("/LOGIN/login.html")], [{ token: null, user: null }, true, true]);
        await context.close();
    }
    {
        const { context, page } = await open("/LOGIN/login.html", { user: null, token: null, apiSetup: a => a.on("POST", /^\/User\/login$/, () => LOGIN_OK("employer")) });
        await page.fill("#loginEmail", "ric@example.com");
        await page.fill("#loginPassword", "Secret123!");
        await page.click("#loginBtn");
        await page.waitForURL("**/Employer%20Dashboard/dashboard.html", { timeout: 8000 });
        t.check("an employer login is stored the same way and goes to the employer dashboard", (await stored(page)).token, TOKEN);
        await context.close();
    }

    t.section("signing up logs you straight in with a token");
    {
        const { context, page, api } = await open("/LOGIN/signup.html", { user: null, token: null, apiSetup: a => {
            a.on("POST", /^\/User$/, () => ({ json: { message: "User registered successfully" } }));
            a.on("POST", /^\/User\/login$/, () => LOGIN_OK());
        } });
        await page.fill("#signupName", "Maria Santos");
        await page.fill("#signupEmail", "maria@example.com");
        await page.fill("#signupPass", "Secret123!");
        await page.fill("#signupConfirm", "Secret123!");
        await page.click("#agreeTerms");
        await page.click("#signupSubmit");
        await page.waitForURL("**/DASHBOARD/dashboard.html", { timeout: 8000 });
        t.check("after signup the token is saved and the dashboard opens", [(await stored(page)).token, api.callsTo("POST", /^\/User$/).length], [TOKEN, 1]);
        await context.close();
    }

    t.section("logging out");
    {
        const { context, page } = await open("/DASHBOARD/dashboard.html", { apiSetup: a => {
            a.on("GET", /^\/Resume\/by-user\/\d+$/, () => ({ json: [] }));
            a.on("GET", /^\/Skills$/, () => ({ json: [] }));
            a.on("GET", /^\/JobPreference\/by-user\/\d+$/, () => ({ status: 404 }));
        } });
        await page.click("#logoutBtn");
        await page.click("#confirmLogout");
        await page.waitForURL("**/LOGIN/login.html", { timeout: 8000 });
        t.check("logout removes both the user and the token", await stored(page), { token: null, user: null });
        await context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

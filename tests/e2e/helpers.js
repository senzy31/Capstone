// Shared by the browser checks:
//   startStaticServer()  serves the repo's front-end folders (nothing to start by hand)
//   createChecker()      tiny pass/fail reporter
//   loggedInPage()       a page that already has a saved login (user + token)
//   mockApi()            fakes the JobLink API (https://localhost:7142/api) - no backend needed
const http = require("http");
const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");

const CONTENT_TYPES = {
    ".html": "text/html; charset=utf-8", ".js": "application/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8", ".json": "application/json", ".png": "image/png",
    ".svg": "image/svg+xml", ".ico": "image/x-icon", ".jpg": "image/jpeg",
};

// Serves the repository over http://127.0.0.1:<free port>.
function startStaticServer() {
    const server = http.createServer((request, response) => {
        const requested = decodeURIComponent(new URL(request.url, "http://x").pathname);
        const file = path.normalize(path.join(REPO_ROOT, requested));

        // Never serve anything outside the repo.
        if (!file.startsWith(REPO_ROOT) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
            response.writeHead(404);
            return response.end("not found");
        }

        response.writeHead(200, { "Content-Type": CONTENT_TYPES[path.extname(file)] || "application/octet-stream", "Cache-Control": "no-store" });
        fs.createReadStream(file).pipe(response);
    });

    return new Promise(resolve => {
        server.listen(0, "127.0.0.1", () => resolve({
            baseUrl: `http://127.0.0.1:${server.address().port}`,
            close: () => new Promise(done => server.close(done)),
        }));
    });
}

// check(name, actual, expected) compares by value; done() prints the totals and sets the exit code.
function createChecker(title) {
    let passed = 0, failed = 0;

    console.log(`\n=== ${title} ===`);

    return {
        section: name => console.log(`\n${name}`),
        check(name, actual, expected) {
            const a = JSON.stringify(actual), e = JSON.stringify(expected);

            if (a === e) { passed++; console.log("  ok   ", name); }
            else { failed++; console.log("  FAIL ", name, "\n         expected:", e, "\n         actual:  ", a); }
        },
        done() {
            console.log(`\n${title}: ${passed} passed, ${failed} failed`);
            if (failed) process.exitCode = 1;
            return failed === 0;
        },
    };
}

const DEFAULT_USER = { userId: 7, fullName: "Maria Santos", email: "maria@example.com", role: "user" };

// A page that is already logged in. The saved login is written from a neutral page
// first, so the pages under test find it the way they would after a real login.
async function loggedInPage(browser, baseUrl, { user = DEFAULT_USER, token = "test-token", init } = {}) {
    const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });

    // Pretend the job sites people are sent to exist (the sandbox has no need to reach them).
    await context.route(/^https:\/\/([a-z0-9-]+\.)*(linkedin\.com|indeed\.com|careers\.acme\.example|jobs\.acme\.example)\//,
        route => route.fulfill({ status: 200, contentType: "text/html", body: `<title>job site</title><p>${route.request().url()}</p>` }));

    const page = await context.newPage();

    if (init) await page.addInitScript(init);

    await page.goto(`${baseUrl}/LOGIN/terms.html`);

    await page.evaluate(({ user, token }) => {
        if (user) localStorage.setItem("user", JSON.stringify(user));
        if (token) localStorage.setItem("token", token);
    }, { user, token });

    return { context, page };
}

const CORS = {
    "access-control-allow-origin": "*",
    "access-control-allow-headers": "authorization, content-type",
    "access-control-allow-methods": "GET, POST, PUT, PATCH, DELETE, OPTIONS",
    // Matches the real backend's one CORS policy (Program.cs): Content-Disposition otherwise isn't
    // a header fetch() exposes to a cross-origin caller, so a page could never read a download's name.
    "access-control-expose-headers": "Content-Disposition",
};

// Fakes https://localhost:7142/api/**. Register handlers with api.on(method, /path-regex/, handler);
// the handler gets (call, regexMatch) and returns { status = 200, json | text | file, headers, delay = 0 }.
// `file` is for a binary download: { contentType, body: Buffer, headers } (headers merges in, so a
// route can set Content-Disposition). Later registrations win. Every request is recorded in
// api.calls; anything with no handler is 404 and listed in api.unmocked so a test can assert
// nothing unexpected was called.
async function mockApi(context) {
    const handlers = [];
    const api = {
        calls: [],
        unmocked: [],
        on(method, pattern, handler) {
            handlers.unshift({ method, pattern, handler });
            return api;
        },
        callsTo: (method, pattern) => api.calls.filter(c => c.method === method && pattern.test(c.path)),
    };

    await context.route(/^https:\/\/localhost:7142\/api\//, async route => {
        const request = route.request();
        const url = new URL(request.url());
        const path = url.pathname.replace(/^\/api/, "");

        if (request.method() === "OPTIONS") {
            return route.fulfill({ status: 204, headers: CORS });
        }

        let body = null;
        try { body = request.postDataJSON(); } catch { body = request.postData() || null; }

        const call = { method: request.method(), path, query: Object.fromEntries(url.searchParams), headers: request.headers(), body };
        api.calls.push(call);

        const match = handlers.map(h => h.method === call.method && h.pattern.exec(path) ? { h, m: h.pattern.exec(path) } : null).find(Boolean);

        if (!match) {
            api.unmocked.push(`${call.method} ${path}`);
            return route.fulfill({ status: 404, headers: CORS, contentType: "application/json", body: JSON.stringify({ message: "unmocked" }) });
        }

        const result = typeof match.h.handler === "function" ? match.h.handler(call, match.m) : match.h.handler;
        const { status = 200, json, text, file, headers = {}, delay = 0, abort = false } = result || {};

        if (delay) await new Promise(resolve => setTimeout(resolve, delay));

        if (abort) return route.abort();

        if (file) return route.fulfill({ status, headers: { ...CORS, ...file.headers, ...headers }, contentType: file.contentType, body: file.body });

        if (text !== undefined) return route.fulfill({ status, headers: { ...CORS, ...headers }, contentType: "text/plain", body: text });

        return route.fulfill({ status, headers: { ...CORS, ...headers }, contentType: "application/json", body: json === undefined ? "" : JSON.stringify(json) });
    });

    return api;
}

// A job the way the JobLink search returns it (JSearch job + the joblink_* tags).
function searchJob(id, extra = {}) {
    return {
        job_id: `jsearch-${id}`, joblink_job_id: id, joblink_source: "External",
        job_title: `Job ${id}`, employer_name: `Company ${id}`, job_publisher: "LinkedIn",
        job_description: "A plain description.", job_employment_type: "Full-time", job_employment_types: ["FULLTIME"],
        job_is_remote: false, job_city: "Makati", job_state: "Metro Manila", job_country: "PH",
        job_apply_link: `https://www.linkedin.com/jobs/view/${id}`, job_apply_is_direct: false,
        job_min_salary: null, job_max_salary: null, job_salary_period: null, ...extra,
    };
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// Waits for the next new tab, or resolves null if none opens within `ms`.
function nextTab(context, ms = 1500) {
    return context.waitForEvent("page", { timeout: ms }).catch(() => null);
}

module.exports = { startStaticServer, createChecker, loggedInPage, mockApi, searchJob, sleep, nextTab, REPO_ROOT, DEFAULT_USER };

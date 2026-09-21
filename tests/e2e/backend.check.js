// End-to-end check of login tokens, the apply flow and the locked-down endpoints against the
// REAL backend (https://localhost:7142) and the local Joblinkv2 database.
//
//   - needs: the backend running, SQL Server LocalDB, and `sqlcmd` on the PATH
//   - makes ONE live JSearch call (the import check), so it spends a little RapidAPI quota
//   - creates its own users and jobs and deletes exactly what it created when it finishes -
//     except the listings the JSearch call imports: a normal search keeps them, and the backend
//     caches their ids, so deleting them would break the next run (and a second run reuses the cache)
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";   // the dev HTTPS certificate is self-signed
const { execSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { createChecker } = require("./helpers");

const API = "https://localhost:7142/api";
const STAMP = Date.now();
const TMP = path.join(os.tmpdir(), `joblink-backend-check-${STAMP}.sql`);

const t = createChecker("Backend (real API + database)");
const check = t.check;

function sql(query) {
    fs.writeFileSync(TMP, "SET NOCOUNT ON;\n" + query);
    return execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d Joblinkv2 -C -I -b -h -1 -W -s "|" -i "${TMP}"`, { encoding: "utf8" })
        .split(/\r?\n/).map(l => l.trim()).filter(Boolean);
}
const scalar = q => sql(q)[0];

async function call(method, url, { token, body } = {}) {
    const res = await fetch(API + url, {
        method,
        headers: { ...(body !== undefined ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    let json = null; const text = await res.text();
    try { json = text ? JSON.parse(text) : null; } catch { json = text; }
    return { status: res.status, json, headers: res.headers };
}

async function makeUser(label, role) {
    const email = `e2e.${label}.${STAMP}@example.com`, password = "E2ePassw0rd!";
    const created = await call("POST", "/User", { body: { fullName: `E2E ${label}`, email, password, role, companyName: role === "employer" ? "E2E Co" : null } });
    if (created.status !== 200) throw new Error("signup failed: " + JSON.stringify(created));
    const login = await call("POST", "/User/login", { body: { email, password } });
    return { email, password, login, token: login.json.token, id: login.json.user.userId };
}

(async () => {
    const startMaxApp = Number(scalar("SELECT ISNULL(MAX(application_id),0) FROM Applications;"));
    const startNotifications = Number(scalar("SELECT ISNULL(MAX(notification_id),0) FROM Notifications;"));
    let users = [];
    const createdJobIds = new Set();

    try {
        // ================= login issues tokens =================
        console.log("login + tokens");
        const A = await makeUser("a", "user");
        const B = await makeUser("b", "user");
        const E = await makeUser("emp", "employer");
        users = [A, B, E];
        check("login returns a token", [typeof A.token, A.token.split(".").length], ["string", 3]);
        check("login response still has the same user fields", Object.keys(A.login.json.user).sort(), ["companyName", "email", "fullName", "role", "userId"]);
        check("employer login role", E.login.json.user.role, "employer");
        const wrong = await call("POST", "/User/login", { body: { email: A.email, password: "nope" } });
        check("wrong password is still 401 and has no token", [wrong.status, wrong.json?.token], [401, undefined]);
        const payload = JSON.parse(Buffer.from(A.token.split(".")[1], "base64url").toString());
        check("token holds only sub + role (+ standard exp/iss/aud)", Object.keys(payload).sort(), ["aud", "exp", "iss", "nbf", "role", "sub"]);

        // ================= accounts =================
        console.log("\naccounts (real SQL): what /api/User used to allow");
        const userRow = id => sql(`SELECT full_name, email, role, ISNULL(company_name,'NULL'), CONVERT(varchar(23), created_at, 121), is_deleted, password_hash FROM Users WHERE user_id = ${id};`)[0].split("|");
        const bHashBefore = userRow(B.id)[6], aHashBefore = userRow(A.id)[6], aCreatedBefore = userRow(A.id)[4];

        check("there is no list of users (405, with or without a login)", [(await call("GET", "/User")).status, (await call("GET", "/User", { token: A.token })).status], [405, 405]);
        check("reading an account needs a login", (await call("GET", `/User/${A.id}`)).status, 401);
        const own = await call("GET", `/User/${A.id}`, { token: A.token });
        check("your own account: 200 with these fields and no password hash", [own.status, Object.keys(own.json).sort(), JSON.stringify(own.json).includes("$2")], [200, ["companyName", "createdAt", "email", "fullName", "role", "userId"], false]);
        const theirs = await call("GET", `/User/${B.id}`, { token: A.token });
        check("someone else's account -> 403 and their email is not in the answer", [theirs.status, JSON.stringify(theirs.json).includes(B.email)], [403, false]);
        check("DELETE /User/{id} no longer exists (405) and the user is still there", [(await call("DELETE", `/User/${B.id}`, { token: A.token })).status, (await call("DELETE", `/User/${B.id}`)).status, userRow(B.id)[5]], [405, 405, "0"]);
        check("changing an account needs a login", (await call("PUT", "/User", { body: { fullName: "Anon" } })).status, 401);

        // the attack: A names B's id and every column an attacker would like to set
        const attack = await call("PUT", "/User", { token: A.token, body: {
            userId: B.id, fullName: "Mallory Was Here", email: A.email, role: "admin", passwordHash: "pwn", password: "pwn",
            isDeleted: true, createdAt: "2000-01-01T00:00:00", plan: "premium", premiumUntil: "2099-12-31T00:00:00", companyName: "Sneaky Corp" } });
        check("the attack request itself is accepted (unknown fields are ignored)", attack.status, 200);
        const aAfter = userRow(A.id), bAfter = userRow(B.id);
        check("A: only the name changed - role, hash, deleted flag, created date and company are untouched",
            [aAfter[0], aAfter[2], aAfter[6] === aHashBefore, aAfter[5], aAfter[4] === aCreatedBefore, aAfter[3]], ["Mallory Was Here", "user", true, "0", true, "NULL"]);
        check("B (named in the body): not touched at all", [bAfter[0], bAfter[2], bAfter[6] === bHashBefore, bAfter[5]], ["E2E b", "user", true, "0"]);
        check("B can still log in; 'pwn' works for nobody", [(await call("POST", "/User/login", { body: { email: B.email, password: B.password } })).status,
            (await call("POST", "/User/login", { body: { email: B.email, password: "pwn" } })).status, (await call("POST", "/User/login", { body: { email: A.email, password: "pwn" } })).status], [200, 401, 401]);

        // changing the email needs the current password
        const newEmail = `e2e.a2.${STAMP}@example.com`;
        const noPw = await call("PUT", "/User", { token: A.token, body: { email: newEmail } });
        check("new email without the password -> 400 password_required, nothing changed", [noPw.status, noPw.json.code, userRow(A.id)[1]], [400, "password_required", A.email]);
        const badPw = await call("PUT", "/User", { token: A.token, body: { email: newEmail, currentPassword: "guess" } });
        check("wrong password -> 403 wrong_password (never 401), nothing changed", [badPw.status, badPw.json.code, userRow(A.id)[1]], [403, "wrong_password", A.email]);
        const takenTry = await call("PUT", "/User", { token: A.token, body: { email: B.email.toUpperCase(), currentPassword: A.password } });
        check("an email someone else has (any capitals) -> 409 email_taken", [takenTry.status, takenTry.json.code, userRow(A.id)[1]], [409, "email_taken", A.email]);
        const goodPw = await call("PUT", "/User", { token: A.token, body: { email: newEmail, currentPassword: A.password } });
        check("right password -> 200 and the new email is saved", [goodPw.status, goodPw.json.user.email, userRow(A.id)[1]], [200, newEmail, newEmail]);
        check("you now log in with the new email, not the old one", [(await call("POST", "/User/login", { body: { email: newEmail, password: A.password } })).status, (await call("POST", "/User/login", { body: { email: A.email, password: A.password } })).status], [200, 401]);
        A.email = newEmail;

        // signup
        const signup = body => call("POST", "/User", { body: { fullName: "E2E Signup", password: "E2ePassw0rd!", ...body } });
        const s = e => `e2e.${e}.${STAMP}@example.com`;
        const adminTry = await signup({ email: s("admin"), role: "admin" });
        check("signup as 'admin' is refused with a plain-text 400", [adminTry.status, typeof adminTry.json, Number(scalar(`SELECT COUNT(*) FROM Users WHERE email = '${s("admin")}';`))], [400, "string", 0]);
        const dupTry = await signup({ email: B.email.toUpperCase() });
        check("signing up with a taken email (any capitals) -> 409 plain text", [dupTry.status, typeof dupTry.json, /already exists/.test(dupTry.json)], [409, "string", true]);
        check("a short password and an employer without a company are 400", [(await signup({ email: s("short"), password: "short" })).status, (await signup({ email: s("nocompany"), role: "employer" })).status], [400, 400]);
        const extra = await signup({ email: s("extra"), passwordHash: "plain", userId: 1, isDeleted: true, createdAt: "2000-01-01", plan: "premium", premiumUntil: "2099-01-01", companyName: "Sneaky" });
        const xr = sql(`SELECT role, ISNULL(company_name,'NULL'), is_deleted, LEFT(password_hash, 4), DATEDIFF(minute, created_at, GETDATE()), user_id FROM Users WHERE email = '${s("extra")}';`)[0].split("|");
        check("signup ignores every field a client should not choose (role user, live, hashed, created now, no company)", [extra.status, xr[0], xr[1], xr[2], xr[3], Number(xr[4]) >= 0 && Number(xr[4]) <= 5, Number(xr[5]) !== 1], [200, "user", "NULL", "0", "$2a$", true, true]);
        check("...and that new user can log in with the password they chose", (await call("POST", "/User/login", { body: { email: s("extra"), password: "E2ePassw0rd!" } })).status, 200);

        // login
        const unknown = await call("POST", "/User/login", { body: { email: s("nobody"), password: "E2ePassw0rd!" } });
        const wrongPw = await call("POST", "/User/login", { body: { email: B.email, password: "not-it" } });
        check("unknown email and wrong password give the same 401 and the same text", [unknown.status, wrongPw.status, unknown.json === wrongPw.json, unknown.json], [401, 401, true, "Invalid email or password"]);
        check("an email full of SQL is just an unknown email", (await call("POST", "/User/login", { body: { email: "' OR 1=1 --", password: "x" } })).status, 401);
        check("login ignores the capitals in the email", (await call("POST", "/User/login", { body: { email: B.email.toUpperCase(), password: B.password } })).status, 200);
        sql(`INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted) VALUES ('E2E legacy', '${s("legacy")}', 'legacy-pass-1', 'user', GETDATE(), 0);`);
        const legacy = await call("POST", "/User/login", { body: { email: s("legacy"), password: "legacy-pass-1" } });
        const legacyHash = scalar(`SELECT LEFT(password_hash, 4) FROM Users WHERE email = '${s("legacy")}';`);
        check("an old plain-text account logs in once and is rehashed on the spot", [legacy.status, legacyHash, (await call("POST", "/User/login", { body: { email: s("legacy"), password: "legacy-pass-1" } })).status], [200, "$2a$", 200]);

        // ================= seed jobs =================
        const ids = {};
        const values = [];
        for (let i = 1; i <= 25; i++)
            values.push(`('e2e-int-${i}-${STAMP}', 'E2E Internal Job ${i}', 'Acme', 'Manila', 'employer', 0, 'Internal', ${E.id}, NULL, 0, NULL, NULL, 0)`);
        const opts = j => `N'${JSON.stringify(j).replace(/'/g, "''")}'`;
        const ext = (key, url, direct, options, pub) =>
            `('e2e-ext-${key}-${STAMP}', 'E2E External ${key}', 'Globex', 'Cebu', 'jsearch', 0, 'External', NULL, ${url ? `N'${url}'` : "NULL"}, ${direct ? 1 : 0}, ${pub ? `N'${pub}'` : "NULL"}, ${options ? opts(options) : "NULL"}, 0)`;
        const optIndeed = { publisher: "Indeed", apply_link: "https://www.indeed.com/viewjob?jk=1", is_direct: false };
        const optCareers = { publisher: "Acme Careers", apply_link: "https://careers.acme.example/job/2", is_direct: true };
        values.push(
            ext("a", "https://jobs.acme.example/apply/a", true, [optIndeed, optCareers], "LinkedIn"),
            ext("b", "https://www.linkedin.com/jobs/view/b", false, [optIndeed, optCareers], "LinkedIn"),
            ext("c", "https://www.linkedin.com/jobs/view/c", false, [optIndeed], "LinkedIn"),
            ext("d", null, false, [optIndeed, { publisher: "Glassdoor", apply_link: "https://www.glassdoor.com/2", is_direct: false }], "LinkedIn"),
            ext("expired", "https://www.linkedin.com/jobs/expired/9", true, [{ publisher: "X", apply_link: "http://insecure.example/1", is_direct: true }], "LinkedIn"),
            ext("race", "https://www.linkedin.com/jobs/view/race", false, null, "LinkedIn"),
            ext("confirm", "https://www.linkedin.com/jobs/view/confirm", false, null, "Indeed"),
        );
        sql(`INSERT INTO Job_Listings (external_job_id, title, company, location, source_api, is_deleted, source, employer_id, apply_url, apply_is_direct, publisher, apply_options, is_expired)
             VALUES ${values.join(",\n")};`);
        const rows = sql(`SELECT external_job_id, job_id FROM Job_Listings WHERE external_job_id LIKE 'e2e-%-${STAMP}';`);
        for (const r of rows) { const [k, id] = r.split("|"); ids[k.replace(`-${STAMP}`, "")] = Number(id); }
        Object.values(ids).forEach(id => createdJobIds.add(id));
        const internalIds = Array.from({ length: 25 }, (_, i) => ids[`e2e-int-${i + 1}`]);

        // ================= internal apply =================
        console.log("\ninternal apply (real SQL)");
        const first = await call("POST", `/jobs/${internalIds[0]}/apply`, { token: A.token });
        check("internal apply -> 200 {type,applicationId,status,alreadyApplied}", [first.status, first.json.type, first.json.status, first.json.alreadyApplied, Number.isInteger(first.json.applicationId)], [200, "internal", "Submitted", false, true]);
        const row = sql(`SELECT user_id, job_id, status, application_type, redirected_at, confirmed_at, CASE WHEN applied_at IS NULL THEN 'null' ELSE 'set' END, is_deleted FROM Applications WHERE application_id = ${first.json.applicationId};`)[0].split("|");
        check("row saved as Internal/Submitted with applied_at set", [Number(row[0]), Number(row[1]), row[2], row[3], row[4], row[5], row[6], row[7]], [A.id, internalIds[0], "Submitted", "Internal", "NULL", "NULL", "set", "0"]);
        const notes = sql(`SELECT user_id, CAST(message AS nvarchar(400)) FROM Notifications WHERE notification_id > ${startNotifications};`);
        check("employer was notified (name + job title)", [notes.length, notes[0].startsWith(`${E.id}|`), notes[0].includes("Mallory Was Here"), notes[0].includes("E2E Internal Job 1")], [1, true, true, true]);
        const again = await call("POST", `/jobs/${internalIds[0]}/apply`, { token: A.token });
        check("applying again -> alreadyApplied with the same id", [again.status, again.json.alreadyApplied, again.json.applicationId === first.json.applicationId], [200, true, true]);
        check("still one row, still one notification", [Number(scalar(`SELECT COUNT(*) FROM Applications WHERE user_id=${A.id} AND job_id=${internalIds[0]};`)), Number(scalar(`SELECT COUNT(*) FROM Notifications WHERE notification_id > ${startNotifications};`))], [1, 1]);
        const emp = await call("POST", `/jobs/${internalIds[1]}/apply`, { token: E.token });
        check("an employer token cannot apply (403)", emp.status, 403);
        check("unknown job -> 404", (await call("POST", `/jobs/99999999/apply`, { token: A.token })).status, 404);

        // ================= the atomic rate limit =================
        console.log("\nrate limit under 25 PARALLEL requests");
        const burst = await Promise.all(internalIds.map(id => call("POST", `/jobs/${id}/apply`, { token: B.token })));
        const ok = burst.filter(r => r.status === 200), limited = burst.filter(r => r.status === 429);
        check("exactly 20 succeed and 5 get 429", [ok.length, limited.length], [20, 5]);
        check("the database holds exactly 20 internal applications for that user", Number(scalar(`SELECT COUNT(*) FROM Applications WHERE user_id=${B.id} AND application_type='Internal';`)), 20);
        const l = limited[0];
        check("429 body + Retry-After header", [typeof l.json.message, l.json.message.includes("20 applications"), Number(l.headers.get("retry-after")) > 80000, l.json.retryAfterSeconds === Number(l.headers.get("retry-after"))], ["string", true, true, true]);
        const okId = internalIds[burst.findIndex(r => r.status === 200)];
        const dup = await call("POST", `/jobs/${okId}/apply`, { token: B.token });
        check("at the limit, applying to a job you already applied to is alreadyApplied (not 429)", [dup.status, dup.json.alreadyApplied], [200, true]);
        const extAtLimit = await call("POST", `/jobs/${ids["e2e-ext-c"]}/apply`, { token: B.token });
        check("at the limit, an external redirect still works", [extAtLimit.status, extAtLimit.json.type], [200, "external"]);
        const w = await call("DELETE", `/Application?id=${ok[0].json.applicationId}`, { token: B.token });
        const afterWithdraw = await call("POST", `/jobs/${internalIds[burst.findIndex(r => r.status === 429)]}/apply`, { token: B.token });
        check("withdrawing an application does not free a slot", [w.status, afterWithdraw.status], [200, 429]);
        check("user A (1 application) is unaffected by B's limit", (await call("POST", `/jobs/${internalIds[2]}/apply`, { token: A.token })).status, 200);

        // ================= external apply =================
        console.log("\nexternal apply (link chosen from real SQL rows)");
        const link = async key => (await call("POST", `/jobs/${ids["e2e-ext-" + key]}/apply`, { token: A.token }));
        const a = await link("a"), b = await link("b"), c = await link("c"), d = await link("d");
        check("(a) direct apply_url", [a.json.redirectUrl, a.json.publisher], ["https://jobs.acme.example/apply/a", "LinkedIn"]);
        check("(b) first direct option beats a non-direct apply_url", [b.json.redirectUrl, b.json.publisher], ["https://careers.acme.example/job/2", "Acme Careers"]);
        check("(c) apply_url when no option is direct", [c.json.redirectUrl, c.json.publisher], ["https://www.linkedin.com/jobs/view/c", "LinkedIn"]);
        check("(d) first option when there is no apply_url", [d.json.redirectUrl, d.json.publisher], ["https://www.indeed.com/viewjob?jk=1", "Indeed"]);
        check("response shape", [a.status, a.json.type, a.json.status, Number.isInteger(a.json.applicationId)], [200, "external", "Redirected", true]);
        const erow = sql(`SELECT status, application_type, CASE WHEN redirected_at IS NULL THEN 'null' ELSE 'set' END, applied_at, confirmed_at FROM Applications WHERE application_id = ${a.json.applicationId};`)[0].split("|");
        check("row saved as External/Redirected, redirected_at set, applied_at null", erow, ["Redirected", "External", "set", "NULL", "NULL"]);
        const exp = await call("POST", `/jobs/${ids["e2e-ext-expired"]}/apply`, { token: A.token });
        check("expired/insecure links only -> 410 with a message", [exp.status, exp.json.expired, typeof exp.json.message], [410, true, "string"]);
        check("...and the listing is flagged expired in the database", scalar(`SELECT is_expired FROM Job_Listings WHERE job_id=${ids["e2e-ext-expired"]};`), "1");
        check("...and no application row was created for it", Number(scalar(`SELECT COUNT(*) FROM Applications WHERE job_id=${ids["e2e-ext-expired"]};`)), 0);
        check("external applies notify nobody (notifications == internal applications)", Number(scalar(`SELECT COUNT(*) FROM Notifications WHERE notification_id > ${startNotifications};`)), Number(scalar(`SELECT COUNT(*) FROM Applications WHERE application_id > ${startMaxApp} AND application_type='Internal';`)));

        // ================= duplicate click, in parallel =================
        console.log("\n10 parallel clicks on the same external job");
        const race = await Promise.all(Array.from({ length: 10 }, () => call("POST", `/jobs/${ids["e2e-ext-race"]}/apply`, { token: A.token })));
        check("all 10 succeed", race.map(r => r.status), Array(10).fill(200));
        check("all 10 return the same application", new Set(race.map(r => r.json.applicationId)).size, 1);
        check("only ONE row exists (unique index + reuse)", Number(scalar(`SELECT COUNT(*) FROM Applications WHERE user_id=${A.id} AND job_id=${ids["e2e-ext-race"]} AND is_deleted=0;`)), 1);

        // ================= confirm =================
        console.log("\nconfirm-external");
        const cj = await call("POST", `/jobs/${ids["e2e-ext-confirm"]}/apply`, { token: A.token });
        const cid = cj.json.applicationId;
        check("someone else cannot confirm (403)", (await call("PATCH", `/applications/${cid}/confirm-external`, { token: B.token, body: { applied: true } })).status, 403);
        check("missing body -> 400", (await call("PATCH", `/applications/${cid}/confirm-external`, { token: A.token })).status, 400);
        const no = await call("PATCH", `/applications/${cid}/confirm-external`, { token: A.token, body: { applied: false } });
        check("applied:false leaves it Redirected", [no.status, no.json.status, scalar(`SELECT status FROM Applications WHERE application_id=${cid};`)], [200, "Redirected", "Redirected"]);
        const yes = await call("PATCH", `/applications/${cid}/confirm-external`, { token: A.token, body: { applied: true } });
        const crow = sql(`SELECT status, CASE WHEN confirmed_at IS NULL THEN 'null' ELSE 'set' END, CASE WHEN applied_at IS NULL THEN 'null' ELSE 'set' END FROM Applications WHERE application_id=${cid};`)[0].split("|");
        check("applied:true -> Applied Externally, confirmed_at + applied_at set", [yes.status, yes.json.status, crow], [200, "Applied Externally", ["Applied Externally", "set", "set"]]);
        check("confirming twice -> 409", (await call("PATCH", `/applications/${cid}/confirm-external`, { token: A.token, body: { applied: true } })).status, 409);
        check("clicking Apply again keeps the confirmed status", (await call("POST", `/jobs/${ids["e2e-ext-confirm"]}/apply`, { token: A.token })).json.status, "Applied Externally");
        check("internal application can't be confirmed (409)", (await call("PATCH", `/applications/${first.json.applicationId}/confirm-external`, { token: A.token, body: { applied: true } })).status, 409);

        // ================= tracker + lockdown (real SQL) =================
        console.log("\ntracker flow + lockdown");
        const mj = await call("POST", "/Joblisting", { token: A.token, body: { title: "E2E Manual Job", company: "Manual Co", location: "Davao", source: "Internal", employerId: E.id, sourceApi: "jsearch", applyUrl: "https://evil.example/phish", applyIsDirect: true, publisher: "LinkedIn", applyOptions: "[{\"apply_link\":\"https://evil.example\",\"is_direct\":true}]", isExpired: true } });
        createdJobIds.add(mj.json.jobId);
        const mrow = sql(`SELECT source_api, source, ISNULL(CAST(employer_id AS varchar),'NULL'), ISNULL(apply_url,'NULL'), apply_is_direct, ISNULL(publisher,'NULL'), ISNULL(apply_options,'NULL'), is_expired, LEFT(external_job_id,7) FROM Job_Listings WHERE job_id=${mj.json.jobId};`)[0].split("|");
        check("hand-added job: server forces manual/External and clears apply data", [mj.status, mrow], [200, ["manual", "External", "NULL", "NULL", "0", "NULL", "NULL", "0", "manual-"]]);
        const mapp = await call("POST", "/Application", { token: A.token, body: { userId: B.id, jobId: mj.json.jobId, status: "Interview", applicationType: "Internal", resumeId: 999999 } });
        const marow = sql(`SELECT user_id, application_type, status FROM Applications WHERE job_id=${mj.json.jobId};`)[0].split("|");
        check("hand-logged application belongs to the token user, is External", [mapp.status, Number(marow[0]), marow[1], marow[2]], [200, A.id, "External", "Interview"]);
        check("forging a Submitted status via POST -> 400", (await call("POST", "/Application", { token: A.token, body: { jobId: ids["e2e-int-3"], status: "Submitted" } })).status, 400);
        check("logging against a real (internal) job via POST -> 400", (await call("POST", "/Application", { token: A.token, body: { jobId: ids["e2e-int-3"], status: "Applied" } })).status, 400);
        const mine = await call("GET", `/Application/by-user/${A.id}`, { token: A.token });
        check("tracker list works and carries the new fields", [mine.status, mine.json.length >= 5, "applicationType" in mine.json[0], "redirectedAt" in mine.json[0], "confirmedAt" in mine.json[0]], [200, true, true, true, true]);
        check("by-user for another user -> 403", (await call("GET", `/Application/by-user/${B.id}`, { token: A.token })).status, 403);
        check("GET /Application returns only my own", (await call("GET", "/Application", { token: A.token })).json.every(x => x.userId === A.id), true);
        const mid = (await call("GET", "/Application", { token: A.token })).json.find(x => x.jobId === mj.json.jobId).applicationId;
        check("PUT status on a manual application works", [(await call("PUT", "/Application", { token: A.token, body: { applicationId: mid, status: "Offer", userId: B.id, jobId: 1, applicationType: "Internal" } })).status, sql(`SELECT status, user_id, job_id, application_type FROM Applications WHERE application_id=${mid};`)[0]], [200, `Offer|${A.id}|${mj.json.jobId}|External`]);
        check("PUT cannot touch another user's application (403)", (await call("PUT", "/Application", { token: B.token, body: { applicationId: mid, status: "Rejected" } })).status, 403);
        check("PUT cannot change an internal application (409)", (await call("PUT", "/Application", { token: A.token, body: { applicationId: first.json.applicationId, status: "Offer" } })).status, 409);
        check("PUT cannot change a redirect application (409)", (await call("PUT", "/Application", { token: A.token, body: { applicationId: a.json.applicationId, status: "Offer" } })).status, 409);
        check("DELETE another user's application -> 403", (await call("DELETE", `/Application?id=${mid}`, { token: B.token })).status, 403);
        check("PUT / DELETE Joblisting are closed (403) and anonymous is 401", [(await call("PUT", "/Joblisting", { token: A.token, body: { jobId: ids["e2e-ext-a"], applyUrl: "https://evil.example" } })).status, (await call("DELETE", `/Joblisting?id=${ids["e2e-ext-a"]}`, { token: A.token })).status, (await call("PUT", "/Joblisting", { body: {} })).status], [403, 403, 401]);
        check("...and the apply_url is untouched", scalar(`SELECT apply_url FROM Job_Listings WHERE job_id=${ids["e2e-ext-a"]};`), "https://jobs.acme.example/apply/a");
        check("anonymous Application access is 401", [(await call("GET", "/Application")).status, (await call("POST", "/Application", { body: {} })).status], [401, 401]);
        check("DELETE own application works (soft delete)", [(await call("DELETE", `/Application?id=${mid}`, { token: A.token })).status, scalar(`SELECT is_deleted FROM Applications WHERE application_id=${mid};`)], [200, "1"]);
        check("a tracker-logged job can't be applied to (no link -> 410)", (await call("POST", `/jobs/${mj.json.jobId}/apply`, { token: A.token })).status, 410);

        // ================= JSearch import (real API) =================
        console.log("\nJSearch import (live API call)");
        const q1 = "software developer jobs in Makati";
        const s1 = await call("GET", `/JobSearch/search?query=${encodeURIComponent(q1)}&page=1`);
        if (s1.status !== 200) throw new Error("search failed " + JSON.stringify(s1.json));
        const jobs1 = s1.json.data;
        // NOT added to createdJobIds: a normal search keeps what it imports, and the backend caches
        // these ids for 15 minutes - deleting the rows would leave that cache pointing at nothing.
        check("every result is tagged with joblink_job_id + joblink_source", [jobs1.length > 0, jobs1.every(j => Number.isInteger(j.joblink_job_id)), jobs1.every(j => j.joblink_source === "External")], [true, true, true]);
        check("original JSearch fields are all still there", ["job_id", "job_title", "employer_name", "job_apply_link", "apply_options", "job_publisher"].every(k => k in jobs1[0]), true);
        const j0 = jobs1[0];
        const irow = sql(`SELECT source_api, source, ISNULL(CAST(employer_id AS varchar),'NULL'), external_job_id, apply_url, apply_is_direct, publisher, ISJSON(apply_options), latitude, longitude, is_expired FROM Job_Listings WHERE job_id=${j0.joblink_job_id};`)[0].split("|");
        check("imported row: source_api/source/employer", [irow[0], irow[1], irow[2]], ["jsearch", "External", "NULL"]);
        check("imported row: job_id -> external_job_id (~400 chars stored in full)", [irow[3] === j0.job_id, irow[3].length > 100], [true, true]);
        check("imported row: job_apply_link -> apply_url, job_publisher -> publisher, direct flag", [irow[4] === j0.job_apply_link, irow[6] === j0.job_publisher, irow[5] === (j0.job_apply_is_direct ? "1" : "0")], [true, true, true]);
        check("imported row: apply_options stored as valid JSON", irow[7], "1");
        check("imported row: coordinates", [Number(irow[8]).toFixed(3), Number(irow[9]).toFixed(3)], [Number(j0.job_latitude).toFixed(3), Number(j0.job_longitude).toFixed(3)]);
        const importedIds = jobs1.map(j => j.joblink_job_id).join(",");
        check("one saved row per imported job", [new Set(jobs1.map(j => j.joblink_job_id)).size, Number(scalar(`SELECT COUNT(*) FROM Job_Listings WHERE job_id IN (${importedIds});`))], [jobs1.length, jobs1.length]);

        // the imported job works end to end through Apply
        const real = await call("POST", `/jobs/${j0.joblink_job_id}/apply`, { token: A.token });
        check("applying to a real imported job returns a usable https link", [real.status, real.json.type, real.json.redirectUrl?.startsWith("https://")], [200, "external", true]);
        console.log("    ->", real.json.publisher, real.json.redirectUrl?.slice(0, 80));

        // (Re-importing the same job updates its row - covered by SqlApplyStoreDbTests.)
        check("search still works with no token (public)", s1.status, 200);

    } finally {
        // ================= cleanup: only what this run created =================
        const uids = users.map(u => u.id).filter(Boolean).join(",") || "-1";
        const jids = [...createdJobIds].join(",") || "-1";
        try {
            sql(`
              DELETE FROM Applications WHERE user_id IN (${uids}) OR job_id IN (${jids});
              DELETE FROM Notifications WHERE user_id IN (${uids});
              DELETE FROM Job_Listings WHERE job_id IN (${jids});
              DELETE FROM Users WHERE user_id IN (${uids}) OR email LIKE 'e2e.%.${STAMP}@example.com';`);
            console.log("\ncleanup done (removed this run's users, jobs, applications and notifications)");
        } catch (e) { console.log("CLEANUP FAILED - remove rows for e2e.*@example.com by hand:", e.message); }
        try { fs.unlinkSync(TMP); } catch {}
    }

    t.done();
})().catch(err => { console.error("SCRIPT FAILED:", err.stack || err.message); process.exit(1); });

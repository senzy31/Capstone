// Runs the checks one after another and prints a summary.
//   node run-all.js             the checks that need nothing running (fake API)
//   node run-all.js --backend   also the ones that use the real backend + database (JSearch faked;
//                               JOBLINK_LIVE_JSEARCH=1 uses your running backend and the real JSearch)
const { spawnSync } = require("child_process");
const path = require("path");

const withBackend = process.argv.includes("--backend");

const suites = [
    ["scoring golden file (matches the frozen reference)", "unit/golden.check.js"],
    ["Jobs page", "e2e/jobs-page.check.js"],
    ["dashboard recommendations", "e2e/dashboard-recommendations.check.js"],
    ["apply flow", "e2e/apply-flow.check.js"],
    ["application tracker", "e2e/tracker.check.js"],
    ["login / session", "e2e/login-session.check.js"],
    ["account details", "e2e/account.check.js"],
    ["plans, ads and the upgrade prompt", "e2e/plans.check.js"],
    ["resume builder: templates and downloads", "e2e/resume-builder.check.js"],
    ...(withBackend ? [
        ["backend (real API + database)", "e2e/backend.check.js"],
        ["full stack (real browser + backend)", "e2e/full-stack.check.js"],
    ] : []),
];

const results = suites.map(([name, file]) => {
    const started = Date.now();
    const run = spawnSync(process.execPath, [path.join(__dirname, file)], { stdio: "inherit", cwd: __dirname });
    return { name, ok: run.status === 0, seconds: ((Date.now() - started) / 1000).toFixed(1) };
});

console.log("\n==================== summary ====================");
results.forEach(r => console.log(`${r.ok ? "PASS" : "FAIL"}  ${r.name}  (${r.seconds}s)`));

if (!withBackend) console.log("\n(add --backend to also run the checks that use the real backend and database)");

process.exit(results.every(r => r.ok) ? 0 : 1);

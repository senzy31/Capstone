// Gives a "real backend" check the backend it talks to, without spending the JSearch allowance.
//
//   const backend = await startBackend({ stamp });
//   backend.mode    "fake"  the check started its own backend, pointed at a local fake JSearch (default)
//                   "live"  JOBLINK_LIVE_JSEARCH=1: uses the backend you already have running, which
//                           calls the real JSearch (each check then spends a little quota)
//   backend.fake    the FakeJSearch (null in live mode) - see fakeJSearch.js
//   await backend.stop()
//
// Fake mode: the backend is built, then started with RapidApi:BaseUrl pointing at the fake and a
// throwaway RapidApi:Key, so even a mistake could not reach RapidAPI with your key. It needs port
// 7142 free (the pages have that address built in), so stop a backend you have running first - or
// set JOBLINK_LIVE_JSEARCH=1 to use it and accept the real calls.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";   // the dev HTTPS certificate is self-signed

const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const net = require("net");
const os = require("os");
const path = require("path");
const { FakeJSearch } = require("./fakeJSearch");
const { REPO_ROOT } = require("./helpers");

const API_ORIGIN = "https://localhost:7142";
const PROJECT = path.join(REPO_ROOT, "JobLinkv2", "Joblink", "Joblink.csproj");
const LIVE = process.env.JOBLINK_LIVE_JSEARCH === "1";

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// Any answer at all (a 401 is fine) means a backend is there.
async function isUp() {
    try {
        await fetch(`${API_ORIGIN}/api/Subscription`);
        return true;
    } catch {
        return false;
    }
}

function portInUse(port) {
    const tryHost = host => new Promise(resolve => {
        const socket = net.connect({ port, host });
        socket.once("connect", () => { socket.destroy(); resolve(true); });
        socket.once("error", () => resolve(false));
    });

    return Promise.all([tryHost("127.0.0.1"), tryHost("::1")]).then(results => results.some(Boolean));
}

function killTree(child) {
    if (!child || child.exitCode !== null) return;

    if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
    else try { process.kill(-child.pid, "SIGKILL"); } catch { /* already gone */ }
}

function tail(file, lines = 25) {
    try { return fs.readFileSync(file, "utf8").split(/\r?\n/).slice(-lines).join("\n"); } catch { return "(no log)"; }
}

async function startBackend({ stamp = Date.now() } = {}) {
    if (LIVE) {
        if (!(await isUp())) {
            throw new Error(`JOBLINK_LIVE_JSEARCH=1 uses the backend you have running, but nothing answers on ${API_ORIGIN}. Start it (or unset the variable to let this check start its own with JSearch faked).`);
        }

        console.log("\nJSearch: LIVE (JOBLINK_LIVE_JSEARCH=1) - this run makes real JSearch calls and spends RapidAPI quota");

        return { mode: "live", fake: null, stop: async () => {} };
    }

    if (await portInUse(7142)) {
        throw new Error(
            "Something is already listening on port 7142 - probably your own backend, which talks to the real JSearch.\n" +
            "This check starts its own backend with JSearch faked so it can't spend your RapidAPI quota, and it needs that port.\n" +
            "  - Stop your backend and run this again, or\n" +
            "  - set JOBLINK_LIVE_JSEARCH=1 to use the backend that is running and make real JSearch calls.");
    }

    const fake = await new FakeJSearch({ stamp }).start();
    const logFile = path.join(os.tmpdir(), `joblink-backend-${stamp}.log`);
    let child = null;

    const stop = async () => {
        killTree(child);
        await fake.stop();
        try { fs.unlinkSync(logFile); } catch { /* keep nothing */ }
    };

    process.once("exit", () => killTree(child));

    try {
        // Build first so what runs is the code in front of you (and fail here, not later, if a
        // backend of yours is holding the files).
        const build = spawnSync("dotnet", ["build", PROJECT, "-nologo", "-v", "q"], { encoding: "utf8" });

        if (build.status !== 0) {
            throw new Error(`dotnet build failed:\n${(build.stdout || "") + (build.stderr || "")}`.slice(-2500));
        }

        const log = fs.openSync(logFile, "w");

        child = spawn("dotnet", ["run", "--no-build", "--launch-profile", "https", "--project", PROJECT], {
            stdio: ["ignore", log, log],
            windowsHide: true,
            detached: process.platform !== "win32",
            env: {
                ...process.env,
                ASPNETCORE_ENVIRONMENT: "Development",
                RapidApi__BaseUrl: fake.baseUrl,
                RapidApi__Key: "fake-key-never-sent-to-rapidapi",
            },
        });

        for (let waited = 0; !(await isUp()); waited += 500) {
            if (child.exitCode !== null) throw new Error(`The backend exited while starting (code ${child.exitCode}). Last of its log:\n${tail(logFile)}`);
            if (waited > 90000) throw new Error(`The backend did not answer on ${API_ORIGIN} within 90 seconds. Last of its log:\n${tail(logFile)}`);
            await sleep(500);
        }
    } catch (error) {
        await stop();
        throw error;
    }

    console.log(`\nJSearch: FAKE at ${fake.baseUrl} - this run makes no RapidAPI calls (set JOBLINK_LIVE_JSEARCH=1 to use your running backend and the real service)`);

    return { mode: "fake", fake, stop };
}

module.exports = { startBackend, isLive: LIVE, API_ORIGIN };

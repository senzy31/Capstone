# JobLink front-end tests

Browser (Playwright) checks for the job seeker pages, plus one scoring unit check. The .NET
tests live separately in `JobLinkv2/Joblink.Tests` (see the last section).

## Setup (once)

```
cd tests
npm install
npx playwright install chromium
```

## Run

| Command | What runs | Needs |
| --- | --- | --- |
| `npm test` | every check below except the two "real" ones | nothing running - the API is faked and the pages are served by a tiny built-in server |
| `npm run test:backend` | all of the above **plus** `backend.check.js` and `full-stack.check.js`, each against its own backend with **JSearch faked** (no RapidAPI quota) | SQL Server LocalDB, `sqlcmd` on the PATH, .NET SDK, and port `7142` **free** (stop your own backend first) |
| `JOBLINK_LIVE_JSEARCH=1 npm run test:backend` | the same, but against the backend you already have running and the **real** JSearch | your backend running on `https://localhost:7142`; spends RapidAPI quota (one call per real check) |
| `node e2e/apply-flow.check.js` | one file | same as above for that file |

| File | Covers |
| --- | --- |
| `unit/suitability.check.js` | the recommended-jobs score, in Node (no browser) |
| `unit/golden.check.js` | `golden/suitability.golden.json` (612 job + profile cases and the exact score, band, sub-scores and notes the browser scorer gave for each) is what `golden/reference/` (a frozen copy of the browser scorer) produces. The .NET tests check the C# scorer against this file. Regenerate on purpose only: `node golden/generate.js` |
| `e2e/jobs-page.check.js` | Jobs page search and filters |
| `e2e/dashboard-recommendations.check.js` | dashboard scores, empty/setup states, external jobs keep their score |
| `e2e/apply-flow.check.js` | Apply button labels, new tab, redirect, failures, "Did you finish applying?" |
| `e2e/tracker.check.js` | Applications page: badges, sorting, Mark as applied, logging by hand |
| `e2e/login-session.check.js` | login token, old sessions without one, signup, logout |
| `e2e/account.check.js` | Profile and Resume Builder: only name + email are sent, the password prompt for an email change, the resume email kept apart from the login email |
| `e2e/plans.check.js` | Plans page (prices, the simulated checkout and the demo-only note, cancel, every state and error), the Plans link on every page, the placeholder ads (Free sees two, Premium and an unreadable plan see none, nothing loaded from another site) and the upgrade prompt on a 403 |
| `e2e/backend.check.js` | real API + database: tokens, accounts (what `/api/User` used to allow), profile / resumes / entries / skills / preferences, and notifications / saved jobs / matches / the skills list (one user against another), the endpoints that spend money (search, AI), Free/Premium plans (simulated checkout, expiry, the plan read from the database on every request), Priority Application (stored at apply time, snapshot, external never priority, same 20/day limit), apply flow, rate limit, lockdown, JSearch import |
| `e2e/full-stack.check.js` | real browser + real backend: login page, Profile (name + email with the password prompt), Resume Builder (month date, delete an entry, skills), apply to a confirmed application, the Free ad, the real 403 upgrade prompt, Activate Premium (Demo) and Cancel |

The two "real" checks create their own test users and jobs and delete them afterwards.

**JSearch is faked by default.** The check builds the backend, starts it on port 7142 with
`RapidApi__BaseUrl` pointing at a small fake JSearch server (`e2e/fakeJSearch.js`) and a throwaway
`RapidApi__Key`, runs, and stops it - so the real call path (HTTP, JSON, import into `Job_Listings`,
the 15 minute cache) still runs, but nothing reaches RapidAPI and the listings it imports are removed
at the end. If something is already listening on 7142 (probably your own backend, which uses your real
key) the check stops with a message instead of running. Set `JOBLINK_LIVE_JSEARCH=1` to use that backend and
the real JSearch instead; each real check then makes one call, and the jobs a real search imports are kept
(that is what a normal search does, and the backend caches their ids).

On Windows PowerShell: `$env:JOBLINK_LIVE_JSEARCH = "1"; npm run test:backend`.

## The .NET tests

```
cd JobLinkv2
dotnet test Joblink.Tests            # fast: fake store, no database
JOBLINK_TEST_DB=1 dotnet test Joblink.Tests    # also the 107 tests that use the local Joblinkv2 database
```

On Windows PowerShell: `$env:JOBLINK_TEST_DB = "1"; dotnet test Joblink.Tests`.

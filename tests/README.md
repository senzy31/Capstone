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
| `npm run test:backend` | all of the above **plus** `backend.check.js` and `full-stack.check.js` | backend running on `https://localhost:7142`, SQL Server LocalDB, `sqlcmd` on the PATH |
| `node e2e/apply-flow.check.js` | one file | same as above for that file |

| File | Covers |
| --- | --- |
| `unit/suitability.check.js` | the recommended-jobs score, in Node (no browser) |
| `e2e/jobs-page.check.js` | Jobs page search and filters |
| `e2e/dashboard-recommendations.check.js` | dashboard scores, empty/setup states, external jobs keep their score |
| `e2e/apply-flow.check.js` | Apply button labels, new tab, redirect, failures, "Did you finish applying?" |
| `e2e/tracker.check.js` | Applications page: badges, sorting, Mark as applied, logging by hand |
| `e2e/login-session.check.js` | login token, old sessions without one, signup, logout |
| `e2e/account.check.js` | Profile and Resume Builder: only name + email are sent, the password prompt for an email change, the resume email kept apart from the login email |
| `e2e/backend.check.js` | real API + database: tokens, accounts (what `/api/User` used to allow), profile / resumes / entries / skills / preferences (one user against another), apply flow, rate limit, lockdown, JSearch import |
| `e2e/full-stack.check.js` | real browser + real backend: login page, Profile (name + email with the password prompt), Resume Builder (month date, delete an entry, skills), apply to a confirmed application |

The two "real" checks create their own test users and jobs and delete them afterwards (the listings
a JSearch search imports are the exception: a normal search keeps them, and the backend caches their ids).
`backend.check.js` makes one live JSearch call and `full-stack.check.js` another, so each spends a
little of the RapidAPI quota. `full-stack.check.js` leaves the jobs a search imports (that is what a
normal search does).

## The .NET tests

```
cd JobLinkv2
dotnet test Joblink.Tests            # fast: fake store, no database
JOBLINK_TEST_DB=1 dotnet test Joblink.Tests    # also the 53 tests that use the local Joblinkv2 database
```

On Windows PowerShell: `$env:JOBLINK_TEST_DB = "1"; dotnet test Joblink.Tests`.

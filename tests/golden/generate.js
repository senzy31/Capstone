// Builds suitability.golden.json: many (job, profile) inputs and what the FROZEN browser scorer
// (reference/Suitability.js + reference/JobHelpers.js) answers for each. The C# port
// (JobLinkv2/Services/Matching) is tested against this file, case by case, for the exact same
// score, band, sub-scores and notes.
//
//   node tests/golden/generate.js          rewrite suitability.golden.json
//   node tests/golden/generate.js --check  exit 1 if the file on disk is not what this would write
//                                          (used by tests/unit/golden.check.js)
//
// Only regenerate on purpose - the scoring rules changed and docs/scoring.md is being updated too.
// The random cases come from a seeded generator, so a re-run writes the same file.
const vm = require("vm");
const fs = require("fs");
const path = require("path");

const HERE = __dirname;
const OUT = path.join(HERE, "suitability.golden.json");

// ---------- the reference scorer, in its own scope, like a page loads it ----------

function loadReference() {
    const context = vm.createContext({ console, Intl });

    // The notes format pesos with toLocaleString(); pin it so the file doesn't depend on this machine's locale.
    vm.runInContext('Number.prototype.toLocaleString = function () { return new Intl.NumberFormat("en-US").format(this); };', context);

    for (const file of ["JobHelpers.js", "Suitability.js"]) {
        vm.runInContext(fs.readFileSync(path.join(HERE, "reference", file), "utf8"), context, { filename: file });
    }

    return (job, profile) => {
        context.__job = job;
        context.__profile = profile;
        // Through JSON, so the result is plain data (undefined properties dropped, Infinity never appears in output).
        return JSON.parse(vm.runInContext("JSON.stringify(scoreJob(__job, __profile))", context));
    };
}

// ---------- hand-written cases ----------

const cases = [];
const add = (name, job, profile) => cases.push({ name, job, profile });

const job = extra => ({ job_title: "t", job_description: "", job_city: "Makati", job_state: "Metro Manila", job_country: "PH", ...extra });
const pay = extra => ({ job_country: "PH", ...extra });
const prefs30to60 = { minSalary: 30000, maxSalary: 60000 };
const allPrefs = { preferredLocation: "Makati", workArrangement: "onsite", minSalary: 30000, maxSalary: 70000 };

// -- skills (the assertions of the old tests/unit/suitability.check.js, kept as cases) --
const fullStack = { job_title: "Full Stack Developer", job_description: "We use React, Node.js, MySQL, JavaScript and C# daily. Knowledge of CI/CD." };
add("skills: whole words only (Java != JavaScript, SQL != MySQL)", fullStack, { skills: ["React", "Node.js", "SQL", "Java", "C#", "Python"], preferences: null });
add("skills: case-insensitive", { job_title: "REACT dev", job_description: "" }, { skills: ["react"], preferences: null });
add("skills: punctuation next to a skill", { job_title: "", job_description: "Skills: (SQL), C++/Java." }, { skills: ["SQL", "C++", "Java"], preferences: null });
add("skills: regex characters in a skill are literal", { job_title: "", job_description: "x" }, { skills: ["C++", "(weird[", "a.b*"], preferences: null });
add("skills: 2 skills, both mentioned", { job_title: "react sql", job_description: "" }, { skills: ["React", "SQL"], preferences: null });
add("skills: 10 skills, 5 mentioned (target caps at 5)", { job_title: "a b c d e", job_description: "" }, { skills: "abcdefghij".split(""), preferences: null });
add("skills: 10 skills, 2 mentioned", { job_title: "a b", job_description: "" }, { skills: "abcdefghij".split(""), preferences: null });
add("skills: none listed, nothing scored", { job_title: "x" }, { skills: [], preferences: null });
add("skills: no skills key at all", { job_title: "x" }, { preferences: null });
add("skills: 1 of 3 mentioned", { job_title: "react", job_description: "" }, { skills: ["React", "Vue", "Svelte"], preferences: null });
add("skills: 2 of 3 mentioned", { job_title: "react vue", job_description: "" }, { skills: ["React", "Vue", "Svelte"], preferences: null });
add("skills: 4 of 7 mentioned (over the target of 5 is capped at 100)", { job_title: "a b c d", job_description: "" }, { skills: "abcdefg".split(""), preferences: null });
add("skills: 3 of 4 mentioned", { job_title: "a b c", job_description: "" }, { skills: "abcd".split(""), preferences: null });
add("skills: duplicates each count (same spelling)", { job_title: "react", job_description: "" }, { skills: ["React", "React", "SQL"], preferences: null });
add("skills: same skill in two spellings both match", { job_title: "react", job_description: "" }, { skills: ["React", "react"], preferences: null });
add("skills: a blank skill (never produced by the profile loader) matches at any boundary", { job_title: "", job_description: "" }, { skills: ["", "React"], preferences: null });
add("skills: only the title has the skill", { job_title: "SQL Developer" }, { skills: ["SQL"], preferences: null });
add("skills: only the description has the skill", { job_title: "Developer", job_description: "Strong SQL" }, { skills: ["SQL"], preferences: null });
add("skills: skill at the very start and end of the text", { job_title: "sql", job_description: "react" }, { skills: ["sql", "react"], preferences: null });
add("skills: skill glued to letters or digits is not a mention", { job_title: "sqlite react2 xnode", job_description: "" }, { skills: ["SQL", "React", "Node"], preferences: null });
add("skills: skill next to accented letters and symbols is a mention", { job_title: "é sql ñ · react ™ node.js/", job_description: "" }, { skills: ["SQL", "React", "Node.js"], preferences: null });
add("skills: skill with a hyphen or slash next to it", { job_title: "react-native, sql/nosql, docker-compose", job_description: "" }, { skills: ["React", "SQL", "Docker", "NoSQL"], preferences: null });
add("skills: a skill that is a substring inside another word twice, then standalone", { job_title: "aaa a-a", job_description: "" }, { skills: ["a"], preferences: null });
add("skills: multi-word skill", { job_title: "Project Management office", job_description: "" }, { skills: ["Project Management", "Office"], preferences: null });
add("skills: skill with surrounding whitespace is compared as typed", { job_title: "react", job_description: "" }, { skills: [" React "], preferences: null });
add("skills: no title or description at all", {}, { skills: ["React"], preferences: null });
add("skills: null title and description", { job_title: null, job_description: null }, { skills: ["React"], preferences: null });

// -- location --
add("location: preferred area matches the city", job({}), { skills: [], preferences: { preferredLocation: "Makati, Cebu" } });
add("location: preferred area matches the state", job({}), { skills: [], preferences: { preferredLocation: "metro manila" } });
add("location: outside the preferred area", job({}), { skills: [], preferences: { preferredLocation: "Cebu" } });
add("location: remote job counts for the place only if they want remote", job({ job_is_remote: true, job_city: "Davao" }), { skills: [], preferences: { preferredLocation: "Cebu", workArrangement: "remote" } });
add("location: remote job, place preferred, no arrangement", job({ job_is_remote: true, job_city: "Davao" }), { skills: [], preferences: { preferredLocation: "Cebu" } });
add("location: arrangement match (hybrid)", job({ job_description: "hybrid setup" }), { skills: [], preferences: { workArrangement: "hybrid" } });
add("location: arrangement mismatch", job({}), { skills: [], preferences: { workArrangement: "remote" } });
add("location: place + arrangement averaged (100 and 0)", job({}), { skills: [], preferences: { preferredLocation: "Makati", workArrangement: "remote" } });
add("location: place fails, arrangement passes", job({}), { skills: [], preferences: { preferredLocation: "Cebu", workArrangement: "onsite" } });
add("location: nothing set", job({}), { skills: [], preferences: {} });
add("location: preferences null", job({}), { skills: ["x"], preferences: null });
add("location: places with spaces, blanks and case", job({ job_city: "Quezon City" }), { skills: [], preferences: { preferredLocation: " , CEBU , quezon city ,, " } });
add("location: only commas and spaces is no preference", job({}), { skills: ["x"], preferences: { preferredLocation: " , ,  " } });
add("location: a short place matches inside a longer word", job({ job_city: "Cebu City" }), { skills: [], preferences: { preferredLocation: "u c" } });
add("location: Metro Manila preferred, job says only Manila", job({ job_city: "Manila", job_state: null }), { skills: [], preferences: { preferredLocation: "Metro Manila" } });
add("location: match through the location text field", { job_title: "t", job_description: "", job_location: "Iloilo City, Western Visayas" }, { skills: [], preferences: { preferredLocation: "iloilo" } });
add("location: match through the country", job({ job_country: "PH", job_city: null, job_state: null }), { skills: [], preferences: { preferredLocation: "ph" } });
add("location: work from home in the description is remote", job({ job_description: "Work from home allowed" }), { skills: [], preferences: { workArrangement: "remote" } });
add("location: WFH in the description is remote", job({ job_description: "WFH setup" }), { skills: [], preferences: { workArrangement: 'remote' } });
add("location: 'no remote work' still reads as remote", job({ job_description: "There is no remote work." }), { skills: [], preferences: { workArrangement: "remote" } });
add("location: remote wins over hybrid in one description", job({ job_description: "hybrid or remote" }), { skills: [], preferences: { workArrangement: "hybrid" } });
add("location: the title does not decide the work setup", job({ job_title: "Remote Developer", job_description: "" }), { skills: [], preferences: { workArrangement: "onsite" } });
add("location: is_remote must be exactly true", job({ job_is_remote: "true" }), { skills: [], preferences: { workArrangement: "remote" } });
add("location: is_remote false, description silent", job({ job_is_remote: false }), { skills: [], preferences: { workArrangement: "onsite" } });
add("location: an unknown arrangement value never matches", job({}), { skills: [], preferences: { workArrangement: "onsite " } });

// -- salary --
add("salary: in range", pay({ job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" }), { skills: [], preferences: prefs30to60 });
add("salary: yearly is converted (20k-25k a month, minimum 30k)", pay({ job_min_salary: 240000, job_max_salary: 300000, job_salary_period: "YEAR" }), { skills: [], preferences: prefs30to60 });
add("salary: higher pay is never penalised", pay({ job_min_salary: 150000, job_max_salary: 200000, job_salary_period: "MONTH" }), { skills: [], preferences: prefs30to60 });
add("salary: open-ended 'starting at' pay", pay({ job_min_salary: 50000 }), { skills: [], preferences: prefs30to60 });
add("salary: open-ended pay below the minimum still meets it", pay({ job_min_salary: 5000 }), { skills: [], preferences: prefs30to60 });
add("salary: hourly is converted", pay({ job_min_salary: 200, job_max_salary: 200, job_salary_period: "HOUR" }), { skills: [], preferences: prefs30to60 });
add("salary: hourly too low", pay({ job_min_salary: 100, job_max_salary: 120, job_salary_period: "HOUR" }), { skills: [], preferences: prefs30to60 });
add("salary: daily is converted", pay({ job_min_salary: 800, job_max_salary: 1000, job_salary_period: "DAY" }), { skills: [], preferences: prefs30to60 });
add("salary: weekly is converted", pay({ job_min_salary: 5000, job_max_salary: 6000, job_salary_period: "WEEK" }), { skills: [], preferences: prefs30to60 });
add("salary: period in lower case", pay({ job_min_salary: 240000, job_max_salary: 300000, job_salary_period: "year" }), { skills: [], preferences: prefs30to60 });
add("salary: no listed salary", pay({}), { skills: [], preferences: prefs30to60 });
add("salary: a USD listing is not compared", { job_country: "US", job_min_salary: 100000, job_max_salary: 120000, job_salary_period: "YEAR" }, { skills: [], preferences: prefs30to60 });
add("salary: no salary preference", pay({ job_min_salary: 40000, job_max_salary: 50000 }), { skills: [], preferences: {} });
add("salary: an unknown period", pay({ job_min_salary: 40000, job_salary_period: "DECADE" }), { skills: [], preferences: prefs30to60 });
add("salary: only a maximum preference, everything known passes", pay({ job_min_salary: 10000, job_max_salary: 12000 }), { skills: [], preferences: { maxSalary: 60000 } });
add("salary: only a minimum preference", pay({ job_min_salary: 10000, job_max_salary: 12000 }), { skills: [], preferences: { minSalary: 30000 } });
add("salary: currency field wins over the country", { job_country: "US", job_salary_currency: "PHP", job_min_salary: 20000, job_max_salary: 25000, job_salary_period: "MONTH" }, { skills: [], preferences: prefs30to60 });
add("salary: currency in lower case is not PHP", { job_country: "PH", job_salary_currency: "php", job_min_salary: 20000, job_max_salary: 25000 }, { skills: [], preferences: prefs30to60 });
add("salary: an empty currency falls back to the country", { job_country: "PH", job_salary_currency: "", job_min_salary: 20000, job_max_salary: 25000 }, { skills: [], preferences: prefs30to60 });
add("salary: an unknown country and no currency", { job_country: "SG", job_min_salary: 20000, job_max_salary: 25000 }, { skills: [], preferences: prefs30to60 });
add("salary: no country and no currency", { job_min_salary: 20000, job_max_salary: 25000 }, { skills: [], preferences: prefs30to60 });
add("salary: an empty period means month", pay({ job_min_salary: 20000, job_max_salary: 25000, job_salary_period: "" }), { skills: [], preferences: prefs30to60 });
add("salary: a null period means month", pay({ job_min_salary: 20000, job_max_salary: 25000, job_salary_period: null }), { skills: [], preferences: prefs30to60 });
add("salary: salary as numeric strings", pay({ job_min_salary: "20000", job_max_salary: "25000" }), { skills: [], preferences: prefs30to60 });
add("salary: salary strings with spaces and an exponent", pay({ job_min_salary: " 20000 ", job_max_salary: "2.5e4" }), { skills: [], preferences: prefs30to60 });
add("salary: unreadable salary strings count as not given", pay({ job_min_salary: "abc", job_max_salary: "" }), { skills: [], preferences: prefs30to60 });
add("salary: a zero maximum is not given (open-ended)", pay({ job_min_salary: 20000, job_max_salary: 0 }), { skills: [], preferences: prefs30to60 });
add("salary: only a zero minimum", pay({ job_min_salary: 0, job_max_salary: 25000 }), { skills: [], preferences: prefs30to60 });
add("salary: both zero is no salary", pay({ job_min_salary: 0, job_max_salary: 0 }), { skills: [], preferences: prefs30to60 });
add("salary: a maximum at exactly the minimum", pay({ job_max_salary: 30000 }), { skills: [], preferences: prefs30to60 });
add("salary: a maximum one peso under the minimum", pay({ job_max_salary: 29999 }), { skills: [], preferences: prefs30to60 });
add("salary: a result of exactly 62.5 rounds up", pay({ job_max_salary: 12500 }), { skills: [], preferences: { minSalary: 20000 } });
add("salary: a result of 61.725 rounds to 62", pay({ job_max_salary: 12345 }), { skills: [], preferences: { minSalary: 20000 } });
add("salary: preferences as numeric strings", pay({ job_max_salary: 24000 }), { skills: [], preferences: { minSalary: "30000", maxSalary: "60000" } });
add("salary: a fractional preference shows rounded in the note", pay({ job_max_salary: 24000 }), { skills: [], preferences: { minSalary: 30000.5 } });
add("salary: a big number is grouped in the note", pay({ job_max_salary: 1234567 }), { skills: [], preferences: { minSalary: 2000000 } });
add("salary: null preferences values", pay({ job_max_salary: 24000 }), { skills: [], preferences: { minSalary: null, maxSalary: null } });
add("salary: a fractional monthly maximum is rounded in the note", pay({ job_max_salary: 100000, job_salary_period: "YEAR" }), { skills: [], preferences: { minSalary: 30000 } });
add("salary: an hourly maximum that is a fraction of a peso a month", pay({ job_max_salary: 155.5, job_salary_period: "HOUR" }), { skills: [], preferences: { minSalary: 30000 } });

// -- overall --
const jobGood = { job_title: "React Developer", job_description: "React SQL Node.js Docker AWS", job_city: "Makati", job_state: "Metro Manila", job_country: "PH", job_min_salary: 40000, job_max_salary: 60000, job_salary_period: "MONTH" };
add("overall: perfect on everything", jobGood, { skills: ["React", "SQL", "Node.js", "Docker", "AWS"], preferences: allPrefs });
add("overall: skills only, no preferences", jobGood, { skills: ["React", "Go", "Rust", "PHP", "Ruby"], preferences: null });
add("overall: re-weighting, skills 20 + location 100", jobGood, { skills: ["React", "Go", "Rust", "PHP", "Ruby"], preferences: { preferredLocation: "Makati" } });
add("overall: zero skills and perfect location and pay caps at 40", jobGood, { skills: ["Go", "Rust"], preferences: allPrefs });
add("overall: a job with no description does not fail", { job_title: "x" }, { skills: ["a"], preferences: {} });
add("overall: band edge, exactly 75", { job_title: "a b c", job_description: "" }, { skills: "abcd".split(""), preferences: null });
add("overall: band edge, exactly 50", { job_title: "a b", job_description: "" }, { skills: "ab".split("").concat("cd".split("")).slice(0, 4), preferences: null });
add("overall: band edge, exactly 25", { job_title: "a", job_description: "" }, { skills: "abcd".split(""), preferences: null });
add("overall: nothing scored at all", { job_title: "x" }, { skills: [], preferences: {} });
add("overall: weights re-scaled without salary", jobGood, { skills: ["React", "SQL", "Go"], preferences: { preferredLocation: "Cebu", workArrangement: "onsite" } });
add("overall: a .5 result rounds up (skills 50, location 100 -> 62.5)", { job_title: "a", job_description: "", job_city: "Makati" }, { skills: ["a", "b"], preferences: { preferredLocation: "Makati" } });
add("overall: 58.25 rounds down", { job_title: "Developer", job_description: "Git.", job_city: "Cebu", job_country: "PH", job_min_salary: 240000, job_max_salary: 300000, job_salary_period: "YEAR" }, { skills: ["Git", "Java"], preferences: { minSalary: 30000 } });

// -- the documented examples (docs/scoring.md) --
const docSkills = ["React", "SQL", "Node.js", "Docker", "AWS", "Git", "Python"];
const docPrefs = { preferredLocation: "Makati, Cebu", workArrangement: "onsite", minSalary: 30000, maxSalary: 60000 };
add("doc example A: strong match", { job_title: "Full Stack Developer", job_description: "React, SQL and Docker every day.", job_city: "Makati", job_state: "Metro Manila", job_country: "PH", job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" }, { skills: docSkills, preferences: docPrefs });
add("doc example B: pay below the minimum", { job_title: "Junior Developer", job_description: "React and SQL.", job_city: "Makati", job_country: "PH", job_min_salary: 18000, job_max_salary: 24000, job_salary_period: "MONTH" }, { skills: docSkills, preferences: docPrefs });
add("doc example C: no salary listed, remote job", { job_title: "Backend Engineer", job_description: "Node.js and AWS. Work from home.", job_city: "Davao", job_country: "PH", job_is_remote: true }, { skills: docSkills, preferences: docPrefs });
add("doc example D: skills only", { job_title: "Data Analyst", job_description: "SQL and Python reporting.", job_city: "Manila", job_country: "PH" }, { skills: docSkills, preferences: null });
add("doc example E: yearly pay", { job_title: "Developer", job_description: "Git.", job_city: "Cebu", job_country: "PH", job_min_salary: 240000, job_max_salary: 300000, job_salary_period: "YEAR" }, { skills: ["Git", "Java"], preferences: { minSalary: 30000 } });
add("doc example F: USD listing", { job_title: "Developer", job_description: "React", job_city: "Austin", job_country: "US", job_min_salary: 100000, job_max_salary: 120000, job_salary_period: "YEAR" }, { skills: ["React"], preferences: { minSalary: 30000, preferredLocation: "Austin" } });

// -- the dashboard's hand-worked fixture (tests/e2e/dashboard-recommendations.check.js): 100 / 38 / 17 --
const dashSearch = (extra) => ({ job_publisher: null, job_apply_link: null, job_employment_type: "Full-time", job_is_remote: false, job_city: "Makati", job_state: "Metro Manila", job_country: "PH", ...extra });
const dashProfile = { skills: ["React", "SQL"], preferences: { preferredLocation: "Makati", workArrangement: null, minSalary: 30000, maxSalary: 60000 } };
add("dashboard fixture: React SQL Developer", dashSearch({ job_title: "React SQL Developer", job_description: "We use React and SQL every day.", job_city: "Makati", job_state: null, job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" }), dashProfile);
add("dashboard fixture: Data Analyst", dashSearch({ job_title: "Data Analyst", job_description: "Uses SQL daily.", job_city: "Cebu", job_state: null }), dashProfile);
add("dashboard fixture: Chef", dashSearch({ job_title: "Chef", job_description: "Lots of cooking.", job_city: "Manila", job_state: null, job_min_salary: 20000, job_max_salary: 25000, job_salary_period: "MONTH" }), dashProfile);

// ---------- random cases ----------

// A small deterministic generator (mulberry32) so the file is the same every time.
function makeRandom(seed) {
    let a = seed >>> 0;
    const next = () => {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
    const int = (lo, hi) => lo + Math.floor(next() * (hi - lo + 1));
    const pick = list => list[int(0, list.length - 1)];
    const sample = (list, n) => { const copy = [...list]; const out = []; while (out.length < n && copy.length) out.push(copy.splice(int(0, copy.length - 1), 1)[0]); return out; };
    const chance = p => next() < p;
    return { next, int, pick, sample, chance };
}

const SKILLS = ["React", "SQL", "Node.js", "Docker", "AWS", "Java", "JavaScript", "C#", "C++", "Python", "Go", "Git", "MySQL", "Excel", "QuickBooks", "Photoshop",
    "Communication", "Project Management", ".NET", "R", "Rust", "PHP", "Ruby", "Kotlin", "HTML", "CSS", "Linux", "Figma", "Tableau", "Customer Service"];
const FRAGMENTS = ["We use React and SQL daily.", "Experience with Node.js, Docker and AWS required.", "Knowledge of MySQL or JavaScript.", "Java/C#/C++ a plus.", "(Python) preferred.",
    "Remote-first culture.", "This is a hybrid role.", "Work from home options.", "WFH allowed twice a week.", "No remote work.", "Promoted from within.", "Familiar with Git-based workflows.",
    "Excel, QuickBooks and communication skills.", "Ünïcode café skills: SQL·React.", "SQLite and reactive programming.", "C#.NET stack.", "Go-getter attitude.", "R&D team.",
    "React-Native mobile apps.", "Node.js/Express services.", "docker-compose and Linux.", "On-site in Makati.", "Project Management (PMP) certificate.", "HTML5, CSS3 and Figma.",
    "Tableau dashboards.", "Kotlin or Rust are nice to have.", "PHP,Ruby,Go.", "Customer Service experience.", "javascript, TYPESCRIPT and react.", "Photoshop or figma."];
const TITLES = ["React Developer", "Java Developer", "Accountant", "SQL Analyst", "Remote Support Engineer", "Hybrid Product Manager", "QA Engineer", "Data Engineer",
    "Customer Service Representative", ".NET Developer", "Graphic Designer", "Full Stack Developer (Remote)", ""];
const CITIES = ["Makati", "Cebu City", "Davao", "Manila", "Quezon City", "Taguig", "Pasig", "Iloilo", "Baguio", "", null];
const STATES = ["Metro Manila", "Cebu", "Davao del Sur", "NCR", "Central Luzon", "", null];
const COUNTRIES = ["PH", "PH", "PH", "US", "SG", "GB", null];
const LOCATIONS = ["Makati, Metro Manila, Philippines", "Remote", "Cebu City, Cebu", "", null, "Bonifacio Global City, Taguig"];
const PLACE_PREFS = ["Makati", "makati, cebu", " Cebu , , Davao ", "metro manila", "PH", "a", "", "   ", null, "Manila", "Taguig,Pasig", "Quezon City", "Remote", "Iloilo, Baguio", ",,"];
const ARRANGEMENTS = [null, null, "", "onsite", "remote", "hybrid"];
const PERIODS = [null, "", "YEAR", "MONTH", "WEEK", "DAY", "HOUR", "month", "year", "DECADE", "Month"];
const CURRENCIES = [undefined, undefined, "PHP", "USD", "php", ""];

function randomCase(random, index) {
    const { int, pick, sample, chance } = random;

    const fragments = sample(FRAGMENTS, int(0, 5));
    const jobRecord = {
        job_title: pick(TITLES),
        job_description: fragments.join(" "),
    };

    if (chance(0.9)) jobRecord.job_city = pick(CITIES);
    if (chance(0.8)) jobRecord.job_state = pick(STATES);
    if (chance(0.9)) jobRecord.job_country = pick(COUNTRIES);
    if (chance(0.5)) jobRecord.job_location = pick(LOCATIONS);
    if (chance(0.3)) jobRecord.job_is_remote = chance(0.6);

    if (chance(0.85)) {
        const kind = int(0, 9);
        const min = pick([5, 100, 150, 500, 800, 5000, 12000, 18000, 20000, 25000, 30000, 40000, 55000, 80000, 240000, 360000]);
        const max = min + pick([0, 100, 1000, 5000, 10000, 40000, 120000]);

        if (kind < 6) { jobRecord.job_min_salary = min; jobRecord.job_max_salary = max; }
        else if (kind === 6) { jobRecord.job_min_salary = min; }
        else if (kind === 7) { jobRecord.job_max_salary = max; }
        else if (kind === 8) { jobRecord.job_min_salary = String(min); jobRecord.job_max_salary = max; }
        else { jobRecord.job_min_salary = null; jobRecord.job_max_salary = null; }

        const period = pick(PERIODS);
        if (period !== undefined) jobRecord.job_salary_period = period;
        const currency = pick(CURRENCIES);
        if (currency !== undefined) jobRecord.job_salary_currency = currency;

        // Most listings in this app are Philippine ones with a usable currency and period; keep the odd ones as the minority.
        if (chance(0.7)) {
            jobRecord.job_country = "PH";
            if (chance(0.7)) delete jobRecord.job_salary_currency; else jobRecord.job_salary_currency = "PHP";
            jobRecord.job_salary_period = pick(["YEAR", "MONTH", "MONTH", "WEEK", "DAY", "HOUR"]);
        }
    }

    const skills = [];
    const count = int(0, 12);
    for (let i = 0; i < count; i++) {
        const skill = pick(SKILLS);
        skills.push(chance(0.1) ? skill.toLowerCase() : skill);
    }

    // Often let the job mention some of the job seeker's own skills, in different surroundings, so the
    // cases cover every match ratio and every kind of word boundary - not just "no match".
    if (chance(0.65) && skills.length) {
        const separators = [", ", " and ", "/", " (", "-", " ", "; ", ".\n", " + "];
        const mentioned = sample(skills, int(1, Math.min(7, skills.length)));
        jobRecord.job_description += " Requires " + mentioned.map(skill => (chance(0.5) ? skill : skill.toUpperCase()) + pick(separators)).join("") + "daily.";
    }

    let preferences = null;

    if (!chance(0.15)) {
        preferences = {};
        if (chance(0.7)) preferences.preferredLocation = pick(PLACE_PREFS);
        if (chance(0.5)) preferences.workArrangement = pick(ARRANGEMENTS);
        if (chance(0.75)) preferences.minSalary = pick([0, null, 10000, 20000, 30000, 30000.5, 45000, 80000]);
        if (chance(0.4)) preferences.maxSalary = pick([0, null, 40000, 60000, 100000]);
    }

    return { name: `random ${String(index).padStart(3, "0")}`, job: jobRecord, profile: { skills, preferences } };
}

const random = makeRandom(20260922);
for (let i = 1; i <= 500; i++) cases.push(randomCase(random, i));

// ---------- write ----------

function build() {
    const score = loadReference();

    const withExpected = cases.map(({ name, job: jobRecord, profile }) => ({ name, job: jobRecord, profile, expected: score(jobRecord, profile) }));

    const text = "{\n" +
        `"about": "What the frozen browser scorer (tests/golden/reference) answers for each case. Generated by tests/golden/generate.js - do not edit by hand.",\n` +
        `"count": ${withExpected.length},\n` +
        '"cases": [\n' + withExpected.map(c => JSON.stringify(c)).join(",\n") + "\n]\n}\n";

    return { text, withExpected };
}

const { text, withExpected } = build();

if (process.argv.includes("--check")) {
    const onDisk = fs.existsSync(OUT) ? fs.readFileSync(OUT, "utf8").replace(/\r\n/g, "\n") : null;
    console.log(onDisk === text ? `golden file is current (${withExpected.length} cases)` : "golden file is OUT OF DATE - run: node tests/golden/generate.js");
    process.exit(onDisk === text ? 0 : 1);
}

fs.writeFileSync(OUT, text);

const bands = withExpected.reduce((h, c) => (h[c.expected.band.level] = (h[c.expected.band.level] || 0) + 1, h), {});
const notScored = ["skills", "location", "salary"].map(p => `${p}: ${withExpected.filter(c => c.expected[p] === null).length} not scored`);
console.log(`wrote ${withExpected.length} cases to ${path.relative(process.cwd(), OUT)}\nbands: ${JSON.stringify(bands)}\n${notScored.join(", ")}`);

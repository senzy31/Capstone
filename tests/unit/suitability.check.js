// Scoring rules from DASHBOARD/Suitability.js, run in Node - no browser needed.
const vm = require("vm");
const fs = require("fs");
const path = require("path");
const DIR = path.join(__dirname, "..", "..", "DASHBOARD") + path.sep;

// Load the two browser scripts into one shared global scope, like the page does.
const ctx = vm.createContext({ console });
for (const file of ["JobsShared.js", "Suitability.js"]) {
    vm.runInContext(fs.readFileSync(DIR + file, "utf8"), ctx, { filename: file });
}
const run = code => vm.runInContext(code, ctx);

let passed = 0, failed = 0;
function check(name, actual, expected) {
    const a = JSON.stringify(actual), e = JSON.stringify(expected);
    if (a === e) { passed++; console.log("  ok   ", name); }
    else { failed++; console.log("  FAIL ", name, "\n         expected:", e, "\n         actual:  ", a); }
}

// ---------- skills ----------
console.log("skills");
ctx.job1 = { job_title: "Full Stack Developer", job_description: "We use React, Node.js, MySQL, JavaScript and C# daily. Knowledge of CI/CD." };
check("whole-word matching (Java!=JavaScript, SQL!=MySQL)",
    run(`scoreSkills(job1, ["React","Node.js","SQL","Java","C#","Python"]).matched`),
    ["React", "Node.js", "C#"]);
check("case-insensitive", run(`scoreSkills({job_title:"REACT dev", job_description:""}, ["react"]).score`), 100);
check("punctuation next to skill is fine", run(`scoreSkills({job_title:"", job_description:"Skills: (SQL), C++/Java."}, ["SQL","C++","Java"]).matched.length`), 3);
check("regex chars in skill don't break", run(`scoreSkills({job_title:"", job_description:"x"}, ["C++","(weird[","a.b*"]).matched`), []);
check("2 skills, both matched -> 100", run(`scoreSkills({job_title:"react sql", job_description:""}, ["React","SQL"]).score`), 100);
check("10 skills, 5 matched -> 100 (target caps at 5)", run(`scoreSkills({job_title:"a b c d e", job_description:""}, ["a","b","c","d","e","f","g","h","i","j"]).score`), 100);
check("10 skills, 2 matched -> 40", run(`scoreSkills({job_title:"a b", job_description:""}, ["a","b","c","d","e","f","g","h","i","j"]).score`), 40);
check("no skills -> null (not scored)", run(`scoreSkills({job_title:"x"}, [])`), null);

// ---------- location ----------
console.log("location");
const mk = extra => JSON.stringify({ job_title: "t", job_description: "", job_city: "Makati", job_state: "Metro Manila", job_country: "PH", ...extra });
check("preferred area matches city", run(`scoreLocation(${mk({})}, {preferredLocation:"Makati, Cebu"}).score`), 100);
check("preferred area matches state", run(`scoreLocation(${mk({})}, {preferredLocation:"metro manila"}).score`), 100);
check("outside preferred area -> 0", run(`scoreLocation(${mk({})}, {preferredLocation:"Cebu"}).score`), 0);
check("remote job satisfies place only if they want remote",
    [run(`scoreLocation(${mk({ job_is_remote: true, job_city: "Davao" })}, {preferredLocation:"Cebu", workArrangement:"remote"}).score`),
     run(`scoreLocation(${mk({ job_is_remote: true, job_city: "Davao" })}, {preferredLocation:"Cebu"}).score`)],
    [100, 0]);
check("arrangement match", run(`scoreLocation(${mk({ job_description: "hybrid setup" })}, {workArrangement:"hybrid"}).score`), 100);
check("arrangement mismatch note", run(`scoreLocation(${mk({})}, {workArrangement:"remote"}).note`), "On-site (you prefer remote)");
check("place + arrangement averaged (100 & 0 -> 50)", run(`scoreLocation(${mk({})}, {preferredLocation:"Makati", workArrangement:"remote"}).score`), 50);
check("nothing set -> null", run(`scoreLocation(${mk({})}, {})`), null);

// ---------- salary ----------
console.log("salary");
const pay = extra => JSON.stringify({ job_country: "PH", ...extra });
const prefs = `{minSalary:30000, maxSalary:60000}`;
check("in range", run(`scoreSalary(${pay({ job_min_salary: 40000, job_max_salary: 50000, job_salary_period: "MONTH" })}, ${prefs}).score`), 100);
check("yearly is converted (240k-300k/yr = 20k-25k/mo, min 30k) -> 83",
    run(`scoreSalary(${pay({ job_min_salary: 240000, job_max_salary: 300000, job_salary_period: "YEAR" })}, ${prefs}).score`), 83);
check("higher pay never penalised", run(`scoreSalary(${pay({ job_min_salary: 150000, job_max_salary: 200000, job_salary_period: "MONTH" })}, ${prefs}).score`), 100);
check("open-ended 'Starting at' pay", run(`scoreSalary(${pay({ job_min_salary: 50000 })}, ${prefs}).score`), 100);
check("hourly converted (200/hr ~ 34.6k/mo) -> 100", run(`scoreSalary(${pay({ job_min_salary: 200, job_max_salary: 200, job_salary_period: "HOUR" })}, ${prefs}).score`), 100);
check("no listed salary -> null", run(`scoreSalary(${pay({})}, ${prefs})`), null);
check("USD listing not compared -> null", run(`scoreSalary({job_country:"US", job_min_salary:100000, job_max_salary:120000, job_salary_period:"YEAR"}, ${prefs})`), null);
check("no salary preference -> null", run(`scoreSalary(${pay({ job_min_salary: 40000, job_max_salary: 50000 })}, {})`), null);
check("unknown period -> null", run(`scoreSalary(${pay({ job_min_salary: 40000, job_salary_period: "DECADE" })}, ${prefs})`), null);
check("only a max preference -> everything known passes", run(`scoreSalary(${pay({ job_min_salary: 10000, job_max_salary: 12000 })}, {maxSalary:60000}).score`), 100);

// ---------- overall ----------
console.log("overall");
const jobGood = JSON.stringify({ job_title: "React Developer", job_description: "React SQL Node.js Docker AWS", job_city: "Makati", job_state: "Metro Manila", job_country: "PH", job_min_salary: 40000, job_max_salary: 60000, job_salary_period: "MONTH" });
const allPrefs = `{preferredLocation:"Makati", workArrangement:"onsite", minSalary:30000, maxSalary:70000}`;
check("perfect on everything -> 100", run(`scoreJob(${jobGood}, {skills:["React","SQL","Node.js","Docker","AWS"], preferences:${allPrefs}}).score`), 100);
check("skills only (no prefs) -> skill score alone", run(`scoreJob(${jobGood}, {skills:["React","Go","Rust","PHP","Ruby"], preferences:null}).score`), 20);
check("re-weighting: skills 20 + location 100 -> (20*60+100*20)/80 = 40",
    run(`scoreJob(${jobGood}, {skills:["React","Go","Rust","PHP","Ruby"], preferences:{preferredLocation:"Makati"}}).score`), 40);
check("zero skills + perfect location & pay -> 40 (capped below 'Good')",
    run(`scoreJob(${jobGood}, {skills:["Go","Rust"], preferences:${allPrefs}}).score`), 40);
check("band for that is Fair", run(`scoreJob(${jobGood}, {skills:["Go","Rust"], preferences:${allPrefs}}).band.level`), "fair");
check("bands", [100, 75, 74, 50, 49, 25, 24, 0].map(s => run(`getSuitabilityBand(${s}).level`)),
    ["excellent", "excellent", "good", "good", "fair", "fair", "low", "low"]);
check("job with no description doesn't throw", run(`scoreJob({job_title:"x"}, {skills:["a"], preferences:{}}).score`), 0);

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);

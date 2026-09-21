// FROZEN REFERENCE - do not edit.
//
// The job helpers (work setup, currency, monthly salary) that DASHBOARD/JobsShared.js provided to the browser's suitability score,
// copied verbatim from that file as it was at commit dad9e84, together with the scorer itself
// (Suitability.js in this folder, a verbatim copy of DASHBOARD/Suitability.js at the same commit).
// Together they are the implementation the C# port (JobLinkv2/Services/Matching) must agree with:
// generate.js runs them to produce ../suitability.golden.json, and the .NET tests check the port
// against that file. See docs/scoring.md for the rules in words.
//
// (JobsShared.js itself stays in DASHBOARD/ - the pages still use these helpers to show a work-setup
// badge and a salary - so it may change; this copy must not.)

function getWorkSetup(job) {

    const description = (job.job_description || "").toLowerCase();

    if (
        job.job_is_remote === true ||
        description.includes("remote") ||
        description.includes("work from home") ||
        description.includes("wfh")
    ) {
        return "Remote / WFH";
    }

    if (description.includes("hybrid")) {
        return "Hybrid";
    }

    return "On-site";

}


// JSearch search results carry no currency field, so fall back to the job's
// country instead of labelling every salary as PHP.
function getJobCurrency(job) {

    return job.job_salary_currency ||
        { PH: "PHP", US: "USD" }[job.job_country] ||
        "";

}


// Salaries in the app (filters, preferences) are monthly PHP, but JSearch
// reports whatever period/currency the listing used.
const MONTHLY_FACTOR = {
    YEAR: 1 / 12,
    MONTH: 1,
    WEEK: 52 / 12,
    DAY: (5 * 52) / 12,
    HOUR: (40 * 52) / 12
};


// The job's pay as a { min, max } monthly-PHP range, or null when the
// listing has no salary or it isn't in PHP (so it can't be compared).
// A missing bound means open-ended ("Starting at X" -> max is Infinity).
function getMonthlySalaryRange(job) {

    const min = Number(job.job_min_salary) || 0;
    const max = Number(job.job_max_salary) || 0;

    if (!min && !max) {
        return null;
    }

    if (getJobCurrency(job) !== "PHP") {
        return null;
    }

    // PH listings without a stated period are quoted per month.
    const factor = MONTHLY_FACTOR[String(job.job_salary_period || "MONTH").toUpperCase()];

    if (!factor) {
        return null;
    }

    return {
        min: min ? min * factor : 0,
        max: max ? max * factor : Infinity
    };

}

// ======================================================
// SUITABILITY SCORE
// How well a job suits a jobseeker, 0-100, from three parts:
//
//   skills    60%  - the skills on their resume that the job mentions
//   location  20%  - preferred location and work arrangement
//   salary    20%  - the job's pay vs. their preferred monthly range
//
// A part with nothing to compare (no preference saved, or the listing
// shows no PHP salary) is left out and the others are re-weighted, so a
// missing detail never drags a score down - and never inflates it.
// Skills carry the most weight so a job that mentions none of your
// skills can't reach "Good" on location and pay alone.
//
// Needs JobsShared.js (getWorkSetup, getMonthlySalaryRange, ...).
// ======================================================

const SUITABILITY_WEIGHTS = {
    skills: 60,
    location: 20,
    salary: 20
};

// A job mentioning this many of your skills - or all of them, if you list
// fewer - counts as a full skills match. Job ads rarely name every skill
// a candidate has, so matching all of a long list is not expected.
const SKILL_MATCH_TARGET = 5;


// ------------------------------------------------------
// SKILLS
// ------------------------------------------------------

function escapeRegExp(text) {

    return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

}


// Whole-skill match: "Java" must not match "JavaScript", "SQL" not "MySQL".
function textMentionsSkill(text, skill) {

    return new RegExp(`(?<![a-z0-9])${escapeRegExp(skill.toLowerCase())}(?![a-z0-9])`).test(text);

}


function scoreSkills(job, skills) {

    if (skills.length === 0) {
        return null;
    }

    const text = `${job.job_title || ""} ${job.job_description || ""}`.toLowerCase();

    const matched = skills.filter(skill => textMentionsSkill(text, skill));

    const target = Math.min(skills.length, SKILL_MATCH_TARGET);

    return {
        score: Math.min(100, Math.round((matched.length / target) * 100)),
        matched,
        total: skills.length
    };

}


// ------------------------------------------------------
// LOCATION + WORK ARRANGEMENT
// ------------------------------------------------------

const WORK_SETUP_KEYS = {
    "Remote / WFH": "remote",
    "Hybrid": "hybrid",
    "On-site": "onsite"
};

const WORK_SETUP_LABELS = {
    remote: "remote",
    hybrid: "hybrid",
    onsite: "on-site"
};


function scoreLocation(job, preferences) {

    const places = (preferences.preferredLocation || "")
        .split(",")
        .map(place => place.trim().toLowerCase())
        .filter(Boolean);

    const arrangement = preferences.workArrangement || "";

    const scores = [];
    const notes = [];

    const jobSetup = WORK_SETUP_KEYS[getWorkSetup(job)];


    if (places.length > 0) {

        const jobPlace =
            `${job.job_city || ""} ${job.job_state || ""} ${job.job_country || ""} ${job.job_location || ""}`
                .toLowerCase();

        const inPlace = places.some(place => jobPlace.includes(place));

        // A remote job can be done from the preferred area too - but only
        // count that if they said they want remote work.
        const fits = inPlace || (arrangement === "remote" && jobSetup === "remote");

        scores.push(fits ? 100 : 0);

        notes.push(fits ? "In your preferred area" : "Outside your preferred area");

    }


    if (arrangement) {

        const fits = jobSetup === arrangement;

        scores.push(fits ? 100 : 0);

        notes.push(
            fits
                ? `${capitalize(WORK_SETUP_LABELS[arrangement])} as you prefer`
                : `${capitalize(WORK_SETUP_LABELS[jobSetup])} (you prefer ${WORK_SETUP_LABELS[arrangement]})`
        );

    }


    if (scores.length === 0) {
        return null;
    }

    return {
        score: Math.round(scores.reduce((sum, value) => sum + value, 0) / scores.length),
        note: notes.join(" · ")
    };

}


function capitalize(text) {

    return text.charAt(0).toUpperCase() + text.slice(1);

}


// ------------------------------------------------------
// SALARY
// ------------------------------------------------------

function formatPeso(amount) {

    return `₱${Math.round(amount).toLocaleString()}`;

}


// Pay above what they asked for is never a problem - only pay that falls
// short of their minimum counts against a job.
function scoreSalary(job, preferences) {

    const wantMin = Number(preferences.minSalary) || 0;
    const wantMax = Number(preferences.maxSalary) || 0;

    if (!wantMin && !wantMax) {
        return null;
    }

    const pay = getMonthlySalaryRange(job);

    if (!pay) {
        return null;
    }

    if (wantMin && pay.max < wantMin) {

        return {
            score: Math.round((pay.max / wantMin) * 100),
            note: `Pays up to ${formatPeso(pay.max)}/month - under your ${formatPeso(wantMin)} minimum`
        };

    }

    return {
        score: 100,
        note: "Meets your salary range"
    };

}


// ------------------------------------------------------
// OVERALL
// ------------------------------------------------------

function getSuitabilityBand(score) {

    if (score >= 75) {
        return { level: "excellent", label: "Excellent match" };
    }

    if (score >= 50) {
        return { level: "good", label: "Good match" };
    }

    if (score >= 25) {
        return { level: "fair", label: "Fair match" };
    }

    return { level: "low", label: "Low match" };

}


// profile = { skills: ["React", "SQL", ...], preferences: {...} | null }
// preferences uses the JobPreference API's field names.
function scoreJob(job, profile) {

    const preferences = profile.preferences || {};

    const parts = {
        skills: scoreSkills(job, profile.skills || []),
        location: scoreLocation(job, preferences),
        salary: scoreSalary(job, preferences)
    };

    let weighted = 0;
    let totalWeight = 0;

    for (const [name, part] of Object.entries(parts)) {

        if (part) {

            weighted += part.score * SUITABILITY_WEIGHTS[name];

            totalWeight += SUITABILITY_WEIGHTS[name];

        }

    }

    const score = totalWeight > 0 ? Math.round(weighted / totalWeight) : 0;

    return {
        score,
        band: getSuitabilityBand(score),
        ...parts
    };

}

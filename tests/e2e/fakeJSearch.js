// A stand-in for the JSearch API (RapidAPI) so the live checks never spend the real allowance.
// The backend is started with RapidApi:BaseUrl pointing here (see liveBackend.js), so the whole
// real path still runs - the HTTP call, the JSON parsing, the import into Job_Listings, the cache -
// only the far end is fake.
//
//   const fake = new FakeJSearch({ stamp });
//   await fake.start();            // fake.baseUrl is where the backend should send its calls
//   fake.calls                     // every request the backend made: { path, query }
//   fake.on("/search-v2", call => ({ status: 200, json: {...} }))   // take over a path
//   fake.jobs("software developer jobs in Makati")                  // what the default search returns
//   fake.stop();
//
// Default answers look like the real ones (search-v2 nests the list under data.jobs; ids are ~400
// characters; apply_options carry publishers and a direct flag), so the import checks mean the
// same thing they do against the real service.
const http = require("http");

const PUBLISHERS = [
    { name: "LinkedIn", host: "www.linkedin.com" },
    { name: "Indeed", host: "ph.indeed.com" },
    { name: "Glassdoor", host: "www.glassdoor.com" },
    { name: "JobStreet", host: "ph.jobstreet.com" },
    { name: "Kalibrr", host: "www.kalibrr.com" },
];

const TITLES = ["Software Developer", "Backend Engineer", "Frontend Developer", "Data Analyst", "QA Engineer",
    "DevOps Engineer", "Accountant", "Project Manager", "Support Engineer", "Full Stack Developer"];

const slug = text => String(text).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 40);

class FakeJSearch {
    constructor({ stamp = Date.now() } = {}) {
        this.stamp = stamp;
        this.calls = [];
        this.handlers = [];
        this.server = null;
        this.baseUrl = null;
    }

    // Ids are unique per run (so a run's rows can be recognised and removed) and ~400 characters
    // long, like JSearch's own.
    idFor(query, index) {
        const head = `e2e-fake-${this.stamp}-${slug(query)}-${index}-`;
        return head + "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=".repeat(12).slice(0, Math.max(0, 400 - head.length));
    }

    // The ten jobs a search answers with unless a check has set something else up.
    jobs(query, count = 10) {
        return Array.from({ length: count }, (_, i) => {
            const publisher = PUBLISHERS[i % PUBLISHERS.length];
            const link = `https://${publisher.host}/jobs/view/${this.stamp}-${slug(query)}-${i}`;

            return {
                job_id: this.idFor(query, i),
                employer_name: `Fake Company ${i + 1}`,
                job_publisher: publisher.name,
                job_employment_type: "FULLTIME",
                job_employment_types: ["FULLTIME"],
                job_title: TITLES[i % TITLES.length],
                job_apply_link: link,
                job_apply_is_direct: i % 3 === 0,
                apply_options: [
                    { publisher: publisher.name, apply_link: link, is_direct: i % 3 === 0 },
                    ...(i % 2 === 0 ? [{ publisher: "Fake Careers", apply_link: `https://careers.fake.example/job/${this.stamp}-${i}`, is_direct: true }] : []),
                ],
                job_description: `Fake listing ${i + 1} for "${query}". Work with React, SQL and Git in a small team.`,
                job_is_remote: false,
                job_posted_at_datetime_utc: "2026-09-15T00:00:00.000Z",
                job_location: "Makati, Metro Manila, Philippines",
                job_city: "Makati",
                job_state: "Metro Manila",
                job_country: "PH",
                job_latitude: 14.5547 + i / 1000,
                job_longitude: 121.0244 + i / 1000,
                job_min_salary: null,
                job_max_salary: null,
                job_salary_period: null,
            };
        });
    }

    // Take over one upstream path. `pattern` is a string (exact path) or a RegExp; `handler` gets
    // { path, query } and returns { status = 200, json } (or a plain object, sent as the body).
    on(pattern, handler) {
        this.handlers.unshift({ pattern, handler });
        return this;
    }

    // Forget every path a check took over: back to the default answers.
    reset() {
        this.handlers = [];
    }

    // Answer every search with these jobs (an array, or a function of the query text).
    searchReturns(jobsOrFn) {
        return this.on("/search-v2", call => ({ json: this.searchBody(typeof jobsOrFn === "function" ? jobsOrFn(call.query.query) : jobsOrFn) }));
    }

    searchBody(jobs) {
        return { status: "OK", request_id: "fake", parameters: {}, data: { jobs } };
    }

    callsTo(path) {
        return this.calls.filter(c => c.path === path);
    }

    respond(call) {
        const custom = this.handlers.find(h => typeof h.pattern === "string" ? h.pattern === call.path : h.pattern.test(call.path));

        if (custom) {
            const result = custom.handler(call);
            return result && (result.json !== undefined || result.status !== undefined) ? result : { json: result };
        }

        if (call.path === "/search-v2") {
            return { json: this.searchBody(this.jobs(call.query.query || "jobs")) };
        }

        if (call.path === "/job-details") {
            const [job] = this.jobs("details");
            return { json: { status: "OK", data: [{ ...job, job_description: "FAKE FULL DESCRIPTION" }] } };
        }

        if (call.path === "/estimated-salary") {
            return { json: { status: "OK", data: [{ job_title: call.query.job_title, location: call.query.location, min_salary: 20000, max_salary: 40000, median_salary: 30000, salary_period: "MONTH", salary_currency: "PHP" }] } };
        }

        return { status: 404, json: { message: "not faked" } };
    }

    start() {
        this.server = http.createServer((request, response) => {
            const url = new URL(request.url, "http://fake");
            const call = { path: url.pathname, query: Object.fromEntries(url.searchParams), headers: request.headers };
            this.calls.push(call);

            const { status = 200, json } = this.respond(call);

            response.writeHead(status, { "Content-Type": "application/json" });
            response.end(JSON.stringify(json ?? {}));
        });

        return new Promise(resolve => {
            this.server.listen(0, "127.0.0.1", () => {
                this.baseUrl = `http://127.0.0.1:${this.server.address().port}`;
                resolve(this);
            });
        });
    }

    stop() {
        return new Promise(done => this.server ? this.server.close(done) : done());
    }
}

module.exports = { FakeJSearch };

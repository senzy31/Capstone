// The golden file the C# scorer is tested against (tests/golden/suitability.golden.json) must be exactly what
// the frozen browser scorer (tests/golden/reference) produces - so it can't drift from the reference, and the
// reference can't be edited without the file (and so the C# tests) noticing.
const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const { createChecker } = require("../e2e/helpers");

const GOLDEN = path.join(__dirname, "..", "golden");
const t = createChecker("Scoring golden file");

t.section("the file matches the frozen reference");
const run = spawnSync(process.execPath, [path.join(GOLDEN, "generate.js"), "--check"], { encoding: "utf8" });
t.check("regenerating gives the same file", [run.status, run.stdout.trim().replace(/\d+ cases/, "N cases")], [0, "golden file is current (N cases)"]);

t.section("it covers what the port must get right");
const file = JSON.parse(fs.readFileSync(path.join(GOLDEN, "suitability.golden.json"), "utf8"));
const expected = file.cases.map(c => c.expected);
t.check("has a few hundred cases and the count is right", [file.cases.length >= 500, file.count === file.cases.length], [true, true]);
t.check("case names are unique", new Set(file.cases.map(c => c.name)).size, file.cases.length);
t.check("every band appears", [...new Set(expected.map(e => e.band.level))].sort(), ["excellent", "fair", "good", "low"]);
t.check("each part is sometimes scored and sometimes left out", ["skills", "location", "salary"].map(p => [expected.some(e => e[p]), expected.some(e => e[p] === null)]), [[true, true], [true, true], [true, true]]);
t.check("some cases score all three parts", expected.filter(e => e.skills && e.location && e.salary).length >= 50, true);
t.check("some salaries fall short of the minimum", expected.filter(e => e.salary && e.salary.score < 100).length >= 30, true);
t.check("scores are whole numbers from 0 to 100 - except the one case that pins what nonsense pay (a negative maximum) does",
    [expected.every(e => Number.isInteger(e.score)), file.cases.filter(c => c.expected.score < 0 || c.expected.score > 100).map(c => c.name)],
    [true, ["salary: a negative maximum scores below zero"]]);

t.done();
process.exit(process.exitCode || 0);

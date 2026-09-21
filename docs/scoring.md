# JobLink suitability score - the exact rules

This is what the code does today, written down so it can be checked against the research paper.
Every rule below was read from `DASHBOARD/Suitability.js` and the helpers it uses in
`DASHBOARD/JobsShared.js` (as of commit `dad9e84`), and the worked examples were produced by running
that code. Where the code does something a reader might not expect, it is listed in
[Things to know](#things-to-know-when-describing-it) rather than smoothed over.

## In one paragraph

A job gets one **suitability score**, a whole number from 0 to 100, for one job seeker. It is made of
up to three **parts**: **skills** (weight 60), **location** (weight 20) and **salary** (weight 20). Each
part is scored 0-100 on its own. A part that has nothing to compare (the job seeker saved no
preference, or the job lists no usable salary) is **left out** and the remaining weights are
re-scaled, so a missing detail neither lowers nor raises the score. The result is rounded to a whole
number and mapped to one of four **bands**: Excellent, Good, Fair or Low.

## Constants

| Name | Value | Meaning |
| --- | --- | --- |
| Skills weight | 60 | share of the score that comes from skills |
| Location weight | 20 | share that comes from place and work arrangement |
| Salary weight | 20 | share that comes from pay |
| Skill match target | 5 | mentioning this many of your skills (or all of them, if you have fewer) is a full skills match |
| Band cut-offs | 75 / 50 / 25 | lowest score for Excellent / Good / Fair |

## Rounding

Every "round" below is **round half up**: 0.5 goes up (12.5 becomes 13, 58.5 becomes 59). Nothing is
ever rounded before the last step of its own part, and the overall score is rounded once, from the
unrounded weighted average of the **already rounded** part scores.

## Inputs

**About the job** (the fields JSearch returns for each listing):
title, description, city, state, country, location text, "is remote" flag, minimum and maximum
salary, salary period, salary currency.

**About the job seeker** (their own data, read from the database):

- *Skills* - the skill names on their **first** resume (lowest resume id, not deleted). Names are
  trimmed, blank names are dropped, and exact duplicates (same spelling) are counted once. Order is the
  order of the skill ids.
- *Preferences* - preferred location (free text, may list several places separated by commas), work
  arrangement (`onsite`, `remote` or `hybrid`, or none), minimum salary and maximum salary (monthly, in
  Philippine pesos, either may be empty).

## Part 1 - Skills (weight 60)

1. If the job seeker has no skills the skills part is not scored. (The dashboard then does not score
   at all; it asks them to add skills first.)
2. The text searched is the job **title**, a space, and the job **description**, all lower-cased.
3. A skill is **mentioned** if its lower-cased text occurs in that text as a *whole term*: the character
   just before it and the character just after it must each be either the edge of the text or something
   that is **not** an ASCII letter `a-z` or digit `0-9`. So `Java` does not match `JavaScript`, `SQL`
   does not match `MySQL`, but `C#` matches in `"C#, C++"` and `Node.js` matches in `"node.js"`. Letters
   with accents, punctuation and spaces all count as boundaries.
4. `matched` = the job seeker's skills (in their own spelling and order) that are mentioned.
5. `target = min(number of skills, 5)`.
6. **Skills score = min(100, round(matched / target × 100))**.

So a job seeker with 2 skills needs both mentioned for 100; one with 10 skills needs 5 mentioned for
100 and gets 40 for 2 mentioned (2/5).

## Part 2 - Location and work arrangement (weight 20)

Two independent checks, each worth 100 or 0. Only the checks that apply are used, and the part's score
is their **average, rounded** (so it is 100, 50 or 0).

**The job's work setup** is worked out from the job, not stored: it is **Remote** if the "is remote"
flag is exactly `true` *or* the lower-cased description contains `remote`, `work from home` or `wfh`;
otherwise **Hybrid** if the description contains `hybrid`; otherwise **On-site**. (Substring
matching, description only - the title is not looked at.)

**Place check** - applies only if the job seeker listed at least one place.

- Places = the preferred location split at commas, trimmed, lower-cased, blanks dropped.
- The job's place text = city, state, country and location text joined with spaces, lower-cased.
- The check passes (100) if **any** place appears **anywhere inside** the job's place text
  (a plain substring test), **or** if the job seeker's arrangement is `remote` and the job's work setup
  is Remote. Otherwise it fails (0).

**Arrangement check** - applies only if the job seeker set an arrangement. It passes (100) when the
job's work setup equals it (`onsite` = On-site, `remote` = Remote, `hybrid` = Hybrid), else fails (0).

If neither check applies the location part is not scored.

**Location score = round(average of the checks that apply)**.

## Part 3 - Salary (weight 20)

**The job seeker's side.** Their minimum and maximum, read as numbers (empty or unreadable = 0). If
**both** are 0 the salary part is not scored.

**The job's side** - the pay is turned into a monthly range in pesos, or found unusable (then the
salary part is not scored):

1. The job's minimum and maximum, read as numbers (empty, `null`, unreadable or 0 = "not given"). If
   neither is given, unusable.
2. Currency = the listing's currency field if it is not empty; otherwise by country (`PH` -> `PHP`,
   `US` -> `USD`); otherwise unknown. **Only `PHP` is compared**; any other currency is unusable.
3. Period = the listing's period, or `MONTH` if it has none. Multiply by the factor for that period:

   | Period | Factor to reach monthly |
   | --- | --- |
   | `YEAR` | 1 / 12 |
   | `MONTH` | 1 |
   | `WEEK` | 52 / 12 |
   | `DAY` | (5 × 52) / 12 |
   | `HOUR` | (40 × 52) / 12 |

   Any other period is unusable.
4. Monthly minimum = minimum × factor (0 if not given). Monthly maximum = maximum × factor, or
   **unlimited** if not given ("Starting at X" pay is open-ended).

**The score.** Only the **top** of the job's range is compared with the job seeker's **minimum**:

- If the job seeker has a minimum and the job's monthly maximum is **below** it:
  **salary score = round(monthly maximum / preferred minimum × 100)**.
- Otherwise **100** ("meets your range").

Pay above what they asked for is never a problem, the job seeker's **maximum is never used** (it only
decides whether the part is scored), and the job's own minimum is not used.

## Overall score and bands

```
parts    = the parts that were scored, each with a score 0-100
weight   = 60 (skills), 20 (location), 20 (salary)

score    = round( Σ (part score × weight)  /  Σ weight )      over the scored parts
score    = 0 if no part was scored
```

| Score | Band (internal name) | Label shown |
| --- | --- | --- |
| 75-100 | `excellent` | Excellent match |
| 50-74 | `good` | Good match |
| 25-49 | `fair` | Fair match |
| 0-24 | `low` | Low match |

A job that mentions none of your skills can score at most **40** (perfect location and pay, 40 =
(0×60 + 100×20 + 100×20) / 100), which is why it can never reach "Good" on location and pay alone.
With skills as the only scored part the overall score **is** the skills score.

## What decides which jobs are scored, and in what order

These are dashboard rules around the score, not part of it:

- **Which jobs.** One JSearch search (first page, about ten listings) built from the job seeker's data:
  `"<focus>[ remote] jobs in <place>"`, where *focus* is their latest job title from the experience on
  the first resume (a job with no end date, otherwise the one that ended most recently; the later start
  wins a tie), or their
  first three skills if they have no titled experience; ` remote` is added when their arrangement is
  `remote`; *place* is the first place in their preferred location, or `Philippines` if none.
- **Order.** Highest score first; for equal scores, the job that mentions **more** of their skills
  first; otherwise the search's own order.
- **"Job Matches" counter** = the number of listed jobs scoring **50 or more**.

## What the score does not use

Years of experience, education, the job's employment type, company, posting date, the job's
coordinates (JobLink stores latitude/longitude for listings but **no distance is ever calculated**),
and whether a skill is a "core" one. There are **no distance cut-offs** - location is a text match,
described above.

## Free and Premium

The score is **identical** for both plans. What differs is how much of it is shown:

| | Free | Premium |
| --- | --- | --- |
| Overall score (%) and band | yes | yes |
| Skills / location / salary sub-scores and their notes | no | yes |
| Which of your skills the job mentions | no | yes |

The API sends a Free user only the overall score and the band; the sub-scores are never in the
response.

## Worked examples

Job seeker: skills React, SQL, Node.js, Docker, AWS, Git, Python (7); prefers Makati or Cebu, on-site,
₱30,000-₱60,000 a month. Skills target = min(7, 5) = 5.

| | Job | Skills | Location | Salary | Overall |
| --- | --- | --- | --- | --- | --- |
| A | Makati, on-site, ₱40,000-50,000/month, mentions React, SQL, Docker | 3/5 = **60** | place 100, arrangement 100 -> **100** | max 50,000 ≥ 30,000 -> **100** | (60×60 + 100×20 + 100×20) / 100 = **76**, Excellent |
| B | Makati, on-site, ₱18,000-24,000/month, mentions React, SQL | 2/5 = **40** | **100** | 24,000 / 30,000 × 100 = **80** | (40×60 + 100×20 + 80×20) / 100 = **60**, Good |
| C | Davao, remote (work from home), no salary listed, mentions Node.js, AWS | 2/5 = **40** | place: Davao is not Makati/Cebu and they did not ask for remote -> 0; arrangement: Remote ≠ on-site -> 0 -> **0** | not scored | (40×60 + 0×20) / 80 = **30**, Fair |
| D | Manila, mentions SQL, Python; no preferences saved | **40** | not scored | not scored | **40**, Fair |

Job seeker with 2 skills (Git, Java) and only a minimum salary of ₱30,000; job mentions Git, pays
₱240,000-300,000 **per year**:

| | Skills | Location | Salary | Overall |
| --- | --- | --- | --- | --- |
| E | 1/2 = **50** | not scored | 300,000 / 12 = 25,000 < 30,000 -> 25,000 / 30,000 × 100 = 83.3 -> **83** | (50×60 + 83×20) / 80 = 58.25 -> **58**, Good |

A US-dollar listing is never compared: a job seeker who prefers the job's city and mentions its one
skill scores 100 on skills and 100 on location; the salary is left out, so the score is 100.

## Things to know when describing it

1. **Location is text, not distance.** `Cebu` matches "Cebu City" because it is a substring; `Manila`
   matches "Metro Manila"; but `Metro Manila` does not match a job whose place text says only
   "Manila". A very short place such as `a` would match almost anything.
2. **"Remote" is detected by substring in the description.** A description that says "no remote work"
   still contains `remote`, so the job is treated as Remote. The title is not checked.
3. **A remote job satisfies the place check only if the job seeker asked for remote.** A remote job in
   another city otherwise fails the place check.
4. **The salary check only looks at the top of the job's pay against the job seeker's minimum.** A job
   paying ₱200,000 to a job seeker whose *maximum* is ₱60,000 still scores 100, and a job that says
   "Starting at ₱10,000" is treated as meeting any minimum (its top is unlimited).
5. **The skills cap of 5.** A résumé with 20 skills gets a full skills score for 5 mentions.
6. **Matching is on the search result's text.** The description JSearch returns in the search list is
   what is searched; a skill only in a part of the description that was not returned is not seen.
7. **Case and boundaries.** Matching ignores case. Only ASCII letters and digits make a word
   boundary, so `SQL` next to `é` still matches.
8. **Whole-number arithmetic.** Part scores are rounded before they are weighted; the overall is
   rounded once at the end (half up), so a result such as 58.25 becomes 58.

## How this differs from the Python service

`JobLink-AI/app/services/matching_service.py` computes a different number and is **not** this score:
`round(0.5 × skills% + 0.5 × experience%)`, where skills% is the share of a built-in list of skill
terms found in the job text that the résumé also has (100 if the job names none), and experience% is
the share of the résumé's experience entries that mention a keyword from the job. It has no location
or salary, no weights of 60/20/20 and no bands, and it also asks an AI for improvement notes. Nothing
in the app calls it yet.

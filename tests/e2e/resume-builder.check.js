// The Resume Builder's template picker and Download PDF/DOCX buttons
// (GET /api/Resume/{id}/export?format=&template=), built by JobLink-AI (Python) through the .NET
// gateway. What each plan is ALLOWED to download is decided by the server and tested in the .NET
// suite (ResumeExportEndpointTests) and the live check (backend.check.js); this checks what the
// page sends, how it handles a real file coming back, and the states around it. API faked.
const { chromium } = require("playwright");
const { startStaticServer, createChecker, loggedInPage, mockApi } = require("./helpers");

const ACCOUNT = { userId: 7, fullName: "Maria Santos", email: "maria@example.com", role: "user" };
const PDF_BYTES = Buffer.from("%PDF-1.4\n%fake pdf for the check\n");
const DOCX_BYTES = Buffer.from("PK\x03\x04fake docx for the check");

(async () => {
    const server = await startStaticServer();
    const browser = await chromium.launch();
    const t = createChecker("Resume Builder: templates and downloads");

    async function builder({ resume = { resumeId: 5, userId: 7, title: "My Resume", templateType: "reverse_chronological", aiGeneratedContent: "" }, exportHandler } = {}) {
        const session = await loggedInPage(browser, server.baseUrl, { user: ACCOUNT });
        const api = await mockApi(session.context);
        api.on("GET", /^\/User\/7$/, () => ({ json: ACCOUNT }));
        api.on("GET", /^\/Profile\/by-user\/7$/, () => ({ status: 404 }));
        api.on("GET", /^\/Resume\/by-user\/7$/, () => ({ json: [resume] }));
        api.on("GET", /^\/(Experience|Education)\/by-resume\/5$/, () => ({ json: [] }));
        api.on("GET", /^\/Skills$/, () => ({ json: [] }));
        api.on("GET", /^\/ResumeSkills\/by-resume\/5$/, () => ({ json: [] }));
        api.on("POST", /^\/Profile$/, () => ({ json: true }));
        api.on("PUT", /^\/User$/, call => ({ json: { message: "User updated successfully", user: { ...ACCOUNT, fullName: call.body.fullName || ACCOUNT.fullName } } }));
        api.on("PUT", /^\/Resume$/, call => { resume = { ...resume, ...call.body }; return { json: true }; });
        if (exportHandler) api.on("GET", /^\/Resume\/5\/export$/, exportHandler);
        await session.page.goto(`${server.baseUrl}/DASHBOARD/ResumeBuilder.html`);
        await session.page.waitForFunction(() => document.getElementById("fullName")?.value === "Maria Santos");
        return { ...session, api, resumeRef: () => resume };
    }

    const chosen = page => page.locator('input[name="template"]:checked').getAttribute("value");
    const exportCalls = api => api.callsTo("GET", /^\/Resume\/5\/export$/);

    t.section("the picker");
    {
        const { context, page, api } = await builder();
        t.check("no printBtn left over from window.print()", await page.locator("#printBtn").count(), 0);
        t.check("Reverse Chronological is chosen by default", await chosen(page), "reverse_chronological");
        t.check("ATS is clearly marked Premium", (await page.locator('.template-option:has(input[value="ats"]) .premium-badge').innerText()).trim(), "Premium");

        await page.click('.template-option:has(input[value="harvard"])');
        await page.waitForFunction(() => document.querySelector('input[name="template"]:checked')?.value === "harvard");
        await page.waitForFunction(count => document.querySelectorAll("body").length > 0 && true, null); // no-op settle
        await page.waitForTimeout(200);
        t.check("choosing a template saves it to the resume (PUT /Resume) and marks the card chosen",
            [api.callsTo("PUT", /^\/Resume$/).some(c => c.body.templateType === "harvard"), await page.locator('.template-option:has(input:checked)').getAttribute("class")],
            [true, "template-option"]);

        await context.close();
    }

    t.section("a saved template is picked up when the page loads");
    {
        const { context, page } = await builder({ resume: { resumeId: 5, userId: 7, title: "My Resume", templateType: "functional", aiGeneratedContent: "" } });
        t.check("Functional is pre-selected", await chosen(page), "functional");
        await context.close();
    }

    t.section("an unrecognised or missing saved template falls back to Reverse Chronological");
    for (const templateType of [undefined, null, "", "standard", "made-up"]) {
        const { context, page } = await builder({ resume: { resumeId: 5, userId: 7, title: "My Resume", templateType, aiGeneratedContent: "" } });
        t.check(`templateType ${JSON.stringify(templateType)}`, await chosen(page), "reverse_chronological");
        await context.close();
    }

    t.section("downloading a PDF");
    {
        const { context, page, api } = await builder({
            exportHandler: () => ({ file: { contentType: "application/pdf", body: PDF_BYTES, headers: { "Content-Disposition": 'attachment; filename="Maria_Santos_Resume.pdf"' } } }),
        });

        const downloadPromise = page.waitForEvent("download");
        await page.click("#downloadPdfBtn");
        const download = await downloadPromise;

        t.check("the request carries the chosen template, the format, and the login token",
            [exportCalls(api)[0].query, exportCalls(api)[0].headers.authorization], [{ format: "pdf", template: "reverse_chronological" }, "Bearer test-token"]);
        t.check("the browser saves the server's own filename", download.suggestedFilename(), "Maria_Santos_Resume.pdf");
        t.check("...and it's the real bytes the server sent", (await require("fs").promises.readFile(await download.path())).toString(), PDF_BYTES.toString());
        t.check("the button is usable again afterwards", [await page.locator("#downloadPdfBtn").isEnabled(), (await page.locator("#downloadPdfBtn").innerText()).includes("Download PDF")], [true, true]);
        t.check("a save toast confirms it", await page.locator("#saveStatus").innerText(), "Downloaded!");
        await context.close();
    }

    t.section("downloading a DOCX asks for docx, not pdf");
    {
        const { context, page, api } = await builder({
            exportHandler: (call) => ({ file: { contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document", body: DOCX_BYTES, headers: { "Content-Disposition": 'attachment; filename="resume.docx"' } } }),
        });

        const downloadPromise = page.waitForEvent("download");
        await page.click("#downloadDocxBtn");
        await downloadPromise;

        t.check("format=docx", exportCalls(api)[0].query.format, "docx");
        await context.close();
    }

    t.section("a pending edit is saved before the download is requested");
    {
        const { context, page, api } = await builder({
            exportHandler: () => ({ file: { contentType: "application/pdf", body: PDF_BYTES, headers: { "Content-Disposition": 'attachment; filename="r.pdf"' } } }),
        });

        await page.fill("#summary", "Freshly typed, not yet autosaved.");

        const downloadPromise = page.waitForEvent("download");
        await page.click("#downloadPdfBtn");   // clicked well inside the 800ms autosave debounce
        await downloadPromise;

        const putIndex = api.calls.findIndex(c => c.method === "PUT" && c.path === "/Resume" && c.body.aiGeneratedContent === "Freshly typed, not yet autosaved.");
        const exportIndex = api.calls.findIndex(c => c.method === "GET" && c.path === "/Resume/5/export");
        t.check("the edit reached the server, and reached it before the export was requested", [putIndex >= 0, putIndex < exportIndex], [true, true]);
        await context.close();
    }

    t.section("the ATS template on a Free account");
    {
        const { context, page, api } = await builder({
            exportHandler: () => ({ status: 403, json: { message: "The ATS-friendly template is part of Premium. Upgrade to Premium to use it.", code: "upgrade_required", upgradeRequired: true, feature: "advancedTemplates" } }),
        });

        await page.click('.template-option:has(input[value="ats"])');
        await page.click("#downloadPdfBtn");

        await page.locator(".ac-upgrade").waitFor({ timeout: 5000 });
        t.check("the server's 403 opens the same upgrade dialog every locked feature uses",
            [(await page.locator(".ac-upgrade h3").innerText()).trim(), (await page.locator(".ac-upgrade .ac-message").innerText()).trim(), await page.locator(".ac-upgrade a.ac-btn-primary").getAttribute("href")],
            ["Upgrade to Premium", "The ATS-friendly template is part of Premium. Upgrade to Premium to use it.", "Plans.html"]);
        t.check("...and the toast says the same thing, in case the dialog is missed", await page.locator("#saveStatus").innerText(), "The ATS-friendly template is part of Premium. Upgrade to Premium to use it.");
        t.check("nothing was downloaded", await page.evaluate(() => document.querySelectorAll("a[download]").length), 0);
        await context.close();
    }

    t.section("the ATS template on a Premium account");
    {
        const { context, page, api } = await builder({
            exportHandler: call => ({
                file: { contentType: "application/pdf", body: PDF_BYTES, headers: { "Content-Disposition": 'attachment; filename="Maria_Santos_Resume.pdf"' } },
            }),
        });

        await page.click('.template-option:has(input[value="ats"])');

        const downloadPromise = page.waitForEvent("download");
        await page.click("#downloadPdfBtn");
        await downloadPromise;

        t.check("template=ats reached the server and it answered 200 (as it would for a real Premium account)", exportCalls(api)[0].query.template, "ats");
        t.check("no upgrade dialog for a successful download", await page.locator(".ac-upgrade").count(), 0);
        await context.close();
    }

    t.section("errors");
    {
        const { context, page } = await builder({ exportHandler: () => ({ status: 503, json: { message: "The resume document service is not available right now." } }) });
        await page.click("#downloadPdfBtn");
        await page.waitForFunction(() => document.getElementById("saveStatus").textContent.includes("not available"));
        t.check("the server's message reaches the toast, and the button recovers", [await page.locator("#saveStatus").innerText(), await page.locator("#downloadPdfBtn").isEnabled()], ["The resume document service is not available right now.", true]);
        await context.close();
    }
    {
        const { context, page } = await builder({ exportHandler: () => ({ abort: true }) });
        await page.click("#downloadPdfBtn");
        await page.waitForFunction(() => document.getElementById("saveStatus").classList.contains("show"));
        t.check("an unreachable server still leaves the page usable", await page.locator("#downloadPdfBtn").isEnabled(), true);
        await context.close();
    }

    t.section("both buttons are disabled together while one download is in flight");
    {
        const { context, page } = await builder({
            exportHandler: () => ({ delay: 300, file: { contentType: "application/pdf", body: PDF_BYTES, headers: { "Content-Disposition": 'attachment; filename="r.pdf"' } } }),
        });

        const downloadPromise = page.waitForEvent("download");
        await page.click("#downloadPdfBtn");
        t.check("both buttons are disabled while it's in flight", [await page.locator("#downloadPdfBtn").isDisabled(), await page.locator("#downloadDocxBtn").isDisabled()], [true, true]);
        await downloadPromise;
        t.check("both are enabled again once it finishes", [await page.locator("#downloadPdfBtn").isEnabled(), await page.locator("#downloadDocxBtn").isEnabled()], [true, true]);
        await context.close();
    }

    await browser.close();
    await server.close();
    t.done();
})().catch(error => { console.error("SCRIPT FAILED:", error); process.exit(1); });

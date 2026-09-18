// ================= PASSWORD TOGGLE =================

const togglePassword = document.getElementById("togglePassword");

if (togglePassword) {

    togglePassword.addEventListener("click", () => {

        const password =
            document.getElementById("loginPassword");

        if (password.type === "password") {

            password.type = "text";

            togglePassword.classList.remove("fa-eye");
            togglePassword.classList.add("fa-eye-slash");

        } else {

            password.type = "password";

            togglePassword.classList.remove("fa-eye-slash");
            togglePassword.classList.add("fa-eye");

        }

    });

}

// ================= AUTO REDIRECT IF LOGGED IN =================

document.addEventListener("DOMContentLoaded", () => {

    const currentPage =
        window.location.pathname.toLowerCase();

    const userData =
        localStorage.getItem("user");

    if (userData &&
        (
            currentPage.includes("login") ||
            currentPage.includes("signup")
        )) {

        try {
            const user = JSON.parse(userData);
            const redirectPage =
                user?.role === "employer"
                    ? "../Employer Dashboard/dashboard.html"
                    : "../DASHBOARD/dashboard.html";

            window.location.replace(redirectPage);
            return;
        }
        catch {
            localStorage.removeItem("user");
        }
    }

});

// ================= LOGIN =================

document.getElementById("loginForm")?.addEventListener("submit", async (e) => {

    e.preventDefault();

    const loginBtn =
        document.getElementById("loginBtn");

    const email =
        document.getElementById("loginEmail").value.trim();

    const password =
        document.getElementById("loginPassword").value;

    loginBtn.disabled = true;
    loginBtn.innerHTML = "Logging In...";

    try {

        const response = await fetch(
            "https://localhost:7142/api/User/login",
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    email,
                    password
                })
            }
        );

        if (response.ok) {

            const result =
                await response.json();

            localStorage.setItem(
                "user",
                JSON.stringify(result.user)
            );

            if (result.token) {

                localStorage.setItem(
                    "token",
                    result.token
                );

            }

            loginBtn.innerHTML =
                "Login Successful ✓";

            // Same login form for every role - route by what the account
            // actually is, same as the "already logged in" redirect above.
            const destination =
                result.user?.role === "employer"
                    ? "../Employer Dashboard/dashboard.html"
                    : "../DASHBOARD/dashboard.html";

            setTimeout(() => {

                window.location.replace(destination);

            }, 1000);

            return;

        }

        const error =
            await response.text();

        alert(error || "Invalid email or password");

    }
    catch (error) {

        console.error(error);

        alert(
            "Cannot connect to server.\n\nMake sure your ASP.NET API is running."
        );

    }
    finally {

        loginBtn.disabled = false;
        loginBtn.innerHTML = "Login";

    }

});

// ================= ROLE SELECTOR (Jobseeker / Employer) =================
// Minimal signup, LinkedIn/Indeed-style: pick a role up front, only ask for
// what actually gets saved (name, email, password, + company name for
// employers). Detailed profile info (skills, experience, education) lives
// in the Profile / Resume Builder pages after account creation, same as
// how LinkedIn and Indeed separate "create an account" from "build a
// profile" instead of front-loading everything into one long form.

const roleUserBtn = document.getElementById("roleUserBtn");
const roleEmployerBtn = document.getElementById("roleEmployerBtn");
const accountRoleInput = document.getElementById("accountRole");
const companyNameGroup = document.getElementById("companyNameGroup");
const companyNameInput = document.getElementById("companyName");
const signupSubmit = document.getElementById("signupSubmit");
const heroTitle = document.getElementById("heroTitle");
const heroSubtitle = document.getElementById("heroSubtitle");

const ROLE_COPY = {
    user: {
        title: "Find Your Next Opportunity",
        subtitle: "Join thousands of job seekers and let JobLink's AI connect you with opportunities that match your skills.",
        submitLabel: "Create Account",
    },
    employer: {
        title: "Find Your Next Great Hire",
        subtitle: "Post jobs, manage applicants, and connect with candidates matched to your open roles.",
        submitLabel: "Create Employer Account",
    },
};

function setSignupRole(role) {
    accountRoleInput.value = role;

    roleUserBtn?.classList.toggle("active", role === "user");
    roleEmployerBtn?.classList.toggle("active", role === "employer");

    if (companyNameGroup) {
        companyNameGroup.hidden = role !== "employer";
    }

    const copy = ROLE_COPY[role];

    if (copy) {
        if (heroTitle) heroTitle.textContent = copy.title;
        if (heroSubtitle) heroSubtitle.textContent = copy.subtitle;
        if (signupSubmit) signupSubmit.textContent = copy.submitLabel;
    }
}

roleUserBtn?.addEventListener("click", () => setSignupRole("user"));
roleEmployerBtn?.addEventListener("click", () => setSignupRole("employer"));

// The Terms/Privacy links sit inside the <label> that wraps #agreeTerms, so
// clicking them would also toggle the checkbox (default label behavior).
// Stop that bubbling so opening a policy page never silently checks/unchecks
// the box.
document.querySelectorAll(".terms-row a").forEach(link => {
    link.addEventListener("click", (e) => e.stopPropagation());
});

if (roleUserBtn && roleEmployerBtn) {
    setSignupRole("user");
}


// ================= PASSWORD STRENGTH METER =================

const strengthEl = document.getElementById("passwordStrength");
const strengthFill = document.getElementById("strengthFill");
const strengthLabel = document.getElementById("strengthLabel");

function scorePasswordStrength(password) {
    if (!password) return 0;

    // Any non-empty password is at least "Weak" (level 1) - the extra
    // checks below only add on top of that floor, so a short password
    // shows a visible red bar instead of no bar at all.
    let score = 1;

    if (password.length >= 8) score++;
    if (password.length >= 12) score++;
    if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score++;
    if (/\d/.test(password)) score++;
    if (/[^A-Za-z0-9]/.test(password)) score++;

    return Math.min(score, 4);
}

const STRENGTH_LEVELS = [
    { label: "", color: "transparent", width: "0%" },
    { label: "Weak", color: "#e04b4b", width: "25%" },
    { label: "Fair", color: "#e0a23b", width: "50%" },
    { label: "Good", color: "#4c9be0", width: "75%" },
    { label: "Strong", color: "#3fb56f", width: "100%" },
];

document.getElementById("signupPass")?.addEventListener("input", (e) => {
    const value = e.target.value;

    if (!strengthEl) return;

    if (!value) {
        strengthEl.hidden = true;
        return;
    }

    strengthEl.hidden = false;

    const level = STRENGTH_LEVELS[scorePasswordStrength(value)];

    strengthFill.style.width = level.width;
    strengthFill.style.backgroundColor = level.color;
    strengthLabel.textContent = level.label;
    strengthLabel.style.color = level.color;
});


// ================= SIGNUP =================

document.getElementById("signupForm")?.addEventListener("submit", async (e) => {

    e.preventDefault();

    const signupBtn =
        document.getElementById("signupSubmit");

    const fullName =
        document.getElementById("signupName").value.trim();

    const email =
        document.getElementById("signupEmail").value.trim();

    const password =
        document.getElementById("signupPass").value;

    const confirmPassword =
        document.getElementById("signupConfirm").value;

    const accountRole =
        accountRoleInput?.value || "user";

    const isEmployer = accountRole === "employer";
    const companyName = companyNameInput?.value.trim() || "";

    const agreeTerms =
        document.getElementById("agreeTerms")?.checked;

    if (!fullName) {
        alert("Please enter your full name");
        return;
    }

    if (!email) {
        alert("Please enter your email address");
        return;
    }

    if (password.length < 8) {
        alert("Password must be at least 8 characters.");
        return;
    }

    if (password !== confirmPassword) {
        alert("Passwords do not match");
        return;
    }

    if (isEmployer && !companyName) {
        alert("Please enter your company name.");
        return;
    }

    if (!agreeTerms) {
        alert("Please agree to the Terms of Service and Privacy Policy to continue.");
        return;
    }

    signupBtn.disabled = true;
    signupBtn.innerHTML = "Creating Account...";

    const payload = {
        fullName: fullName,
        email: email,
        passwordHash: password,
        role: accountRole,
        companyName: isEmployer ? companyName : null,
    };

    try {

        const response = await fetch(
            "https://localhost:7142/api/User",
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            }
        );

        if (response.ok) {

            signupBtn.innerHTML =
                "Account Created ✓ Signing you in...";

            // Straight into the account, no separate login step -
            // matches how LinkedIn/Indeed drop you right into the app
            // after signup instead of asking you to re-type what you
            // just typed.
            const loginResponse = await fetch(
                "https://localhost:7142/api/User/login",
                {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ email, password })
                }
            );

            if (loginResponse.ok) {

                const loginResult = await loginResponse.json();

                localStorage.setItem("user", JSON.stringify(loginResult.user));

                if (loginResult.token) {
                    localStorage.setItem("token", loginResult.token);
                }

                const destination =
                    loginResult.user?.role === "employer"
                        ? "../Employer Dashboard/dashboard.html"
                        : "../DASHBOARD/dashboard.html";

                window.location.replace(destination);

                return;

            }

            // Account was created but auto-login failed for some reason -
            // fall back to sending them to the login page manually rather
            // than leaving them stuck on a broken redirect.
            window.location.href = "login.html";

            return;

        }

        const error =
            await response.text();

        alert(error);

    }
    catch (error) {

        console.error(error);

        alert(
            "Cannot connect to server.\n\nMake sure your ASP.NET API is running."
        );

    }
    finally {

        signupBtn.disabled = false;

        const copy = ROLE_COPY[accountRoleInput?.value || "user"];
        signupBtn.innerHTML = copy ? copy.submitLabel : "Create Account";

    }

});

// ================= LOGOUT =================

function logout() {

    localStorage.removeItem("user");
    localStorage.removeItem("token");

    window.location.href =
        "../LOGIN/login.html";

}
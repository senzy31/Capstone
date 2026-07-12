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

            if (result.user?.role === "employer") {
                alert("Employer accounts must use the Employer Login page.");
                loginBtn.disabled = false;
                loginBtn.innerHTML = "Login";
                return;
            }

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

            setTimeout(() => {

                window.location.replace(
                    "../DASHBOARD/dashboard.html"
                );

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

// ================= SIGNUP MODE SELECTOR =================

const employerOnlyBtn = document.getElementById("employerOnlyBtn");
const employerLoginForm = document.getElementById("employerLoginForm");
const employerSignUpBtn = document.getElementById("employerSignUpBtn");
const accountRoleInput = document.getElementById("accountRole");
const accountTypeIndicator = document.getElementById("accountTypeIndicator");
const employerFields = document.getElementById("employerFields");
const candidateFields = document.getElementById("candidateFields");
const candidateExtra = document.getElementById("candidateExtra");
const emailInput = document.getElementById("signupEmail");
const emailHint = document.getElementById("emailHint");
const signupSubmit = document.getElementById("signupSubmit");

let isEmployerMode = false;

function updateSignupMode(mode) {
    isEmployerMode = mode;
    accountRoleInput.value = mode ? "employer" : "user";
    accountTypeIndicator.textContent = mode ? "Account type: Employer" : "Account type: User";
    employerFields.style.display = mode ? "block" : "none";
    candidateFields.style.display = mode ? "none" : "block";
    candidateExtra.style.display = mode ? "none" : "block";
    emailHint.style.display = mode ? "block" : "none";
    emailInput.placeholder = mode ? "Company Email Address (company domain only)" : "Email Address";
    employerSignUpBtn.textContent = mode ? "Switch to User Signup" : "Switch to Employer Signup";
    signupSubmit.textContent = mode ? "Create Employer Account" : "Create Account";
    employerSignUpBtn.classList.toggle("active", mode);
}

if (employerSignUpBtn && accountRoleInput && accountTypeIndicator && employerFields && candidateFields && candidateExtra && signupSubmit) {
    employerSignUpBtn.addEventListener("click", () => {
        updateSignupMode(!isEmployerMode);
    });

    updateSignupMode(false);
}

if (employerOnlyBtn) {
    employerOnlyBtn.addEventListener("click", () => {
        window.location.href = "employeelogin.html";
    });
}

if (employerLoginForm) {
    employerLoginForm.addEventListener("submit", async (e) => {
        e.preventDefault();

        const loginBtn =
            document.getElementById("employerLoginBtn");

        const email =
            document.getElementById("employerEmail").value.trim();

        const password =
            document.getElementById("employerPassword").value;

        loginBtn.disabled = true;
        loginBtn.innerHTML = "Signing In...";

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
                const result = await response.json();

                if (result.user?.role !== "employer") {
                    alert("This page is for employer accounts only.");
                    loginBtn.disabled = false;
                    loginBtn.innerHTML = "Employer Login";
                    return;
                }

                localStorage.setItem(
                    "user",
                    JSON.stringify(result.user)
                );
                if (result.token) {
                    localStorage.setItem("token", result.token);
                }

                loginBtn.innerHTML = "Login Successful ✓";

                setTimeout(() => {
                    window.location.replace("../Employer Dashboard/dashboard.html");
                }, 1000);
                return;
            }

            const error = await response.text();
            alert(error || "Invalid email or password");
        }
        catch (error) {
            console.error(error);
            alert("Cannot connect to server.\n\nMake sure your ASP.NET API is running.");
        }
        finally {
            loginBtn.disabled = false;
            loginBtn.innerHTML = "Employer Login";
        }
    });
}

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
        document.getElementById("accountRole").value || "user";

    const isEmployer = accountRole === "employer";
    const companyName =
        document.getElementById("companyName").value.trim();
    const companyWebsite =
        document.getElementById("companyWebsite").value.trim();
    const companyEmailDomain =
        document.getElementById("companyEmailDomain").value.trim();
    const companyIndustry =
        document.getElementById("companyIndustry").value.trim();
    const companySize =
        document.getElementById("companySize").value.trim();
    const companyPhone =
        document.getElementById("companyPhone").value.trim();

    if (!fullName) {
        alert("Please enter your full name");
        return;
    }

    if (!email) {
        alert("Please enter your email address");
        return;
    }

    if (password !== confirmPassword) {
        alert("Passwords do not match");
        return;
    }

    if (isEmployer && !companyName) {
        alert("Please enter your company name for employer signup.");
        return;
    }

    if (isEmployer && !companyEmailDomain) {
        alert("Please enter your company email domain, for example: company.com");
        return;
    }

    if (isEmployer) {
        const emailParts = email.split("@");
        if (emailParts.length !== 2 || emailParts[1].toLowerCase() !== companyEmailDomain.toLowerCase()) {
            alert("Employer email must use your company domain: " + companyEmailDomain);
            return;
        }
    }

    signupBtn.disabled = true;
    signupBtn.innerHTML = "Creating Account...";

    const payload = {
        fullName: fullName,
        email: email,
        passwordHash: password,
        role: accountRole
    };

    if (isEmployer) {
        payload.companyName = companyName;
        payload.companyWebsite = companyWebsite;
        payload.companyIndustry = companyIndustry;
        payload.companySize = companySize;
        payload.companyPhone = companyPhone;
    } else {
        payload.jobTitle = document.getElementById("jobTitle").value.trim();
        payload.education = document.getElementById("education").value;
        payload.experience = document.getElementById("experience").value;
        payload.workSetup = document.getElementById("workSetup").value;
        payload.technicalSkills = Array.from(document.querySelectorAll(".skills-grid input:checked")).map(el => el.value);
        payload.achievements = document.getElementById("achievements").value.trim();
        payload.salaryRange = document.getElementById("salaryRange").value;
    }

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
                "Account Created ✓";

            setTimeout(() => {

                window.location.href =
                    "login.html";

            }, 1000);

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
        signupBtn.innerHTML = "Sign Up";

    }

});

// ================= LOGOUT =================

function logout() {

    localStorage.removeItem("user");
    localStorage.removeItem("token");

    window.location.href =
        "../LOGIN/login.html";

}
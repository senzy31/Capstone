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

    const user =
        localStorage.getItem("user");

    if (
        user &&
        (
            currentPage.includes("login") ||
            currentPage.includes("signup")
        )
    ) {
        console.log("User already logged in");
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

// ================= SIGNUP =================

document.getElementById("signupForm")?.addEventListener("submit", async (e) => {

    e.preventDefault();

    const signupBtn =
        document.querySelector(".btn");

    const fullName =
        document.getElementById("signupName").value.trim();

    const email =
        document.getElementById("signupEmail").value.trim();

    const password =
        document.getElementById("signupPass").value;

    const confirmPassword =
        document.getElementById("signupConfirm").value;

    if (!fullName) {

        alert("Please enter your full name");
        return;

    }

    if (password !== confirmPassword) {

        alert("Passwords do not match");
        return;

    }

    signupBtn.disabled = true;
    signupBtn.innerHTML = "Creating Account...";

    try {

        const response = await fetch(
            "https://localhost:7142/api/User",
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    fullName: fullName,
                    email: email,
                    passwordHash: password,
                    role: "user"
                })
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
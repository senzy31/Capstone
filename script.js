const signupForm = document.getElementById("signupForm");
const loginForm = document.getElementById("loginForm");

// SIGN UP
if (signupForm) {
  signupForm.addEventListener("submit", async (e) => {
    e.preventDefault();

    const name = document.getElementById("signupName").value.trim();
    const email = document.getElementById("signupEmail").value.trim();
    const password = document.getElementById("signupPass").value;
    const confirmPassword = document.getElementById("signupConfirm").value;

    if (password !== confirmPassword) {
      alert("Passwords do not match.");
      return;
    }

    const { data, error } = await signUpUser(name, email, password);

    if (error) {
      alert("Signup failed: " + error.message);
      return;
    }

    alert("Account created successfully! You can now log in.");
    window.location.href = "login.html";
  });
}

// LOGIN
if (loginForm) {
  loginForm.addEventListener("submit", async (e) => {
    e.preventDefault();

    const email = document.getElementById("loginEmail").value.trim();
    const password = document.getElementById("loginPassword").value;

    const { data, error } = await loginUser(email, password);

    if (error) {
      alert("Login failed: " + error.message);
      return;
    }

    if (!data.user) {
      alert("No account found for this email.");
      return;
    }

    alert("Login successful!");
    window.location.href = "index.html";
  });
}
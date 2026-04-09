// ================= LOGIN =================
document.getElementById("loginForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();

  const email = document.getElementById("loginEmail").value;
  const password = document.getElementById("loginPassword").value;

  try {
    const response = await fetch("https://localhost:7142/api/User/login", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ email, password })
    });

    if (response.ok) {
      const result = await response.json();

      // ✅ Save user info
      localStorage.setItem("user", JSON.stringify(result.user));

      // ✅ Save token (if your backend returns one)
      if (result.token) {
        localStorage.setItem("token", result.token);
      }

      alert("Login successful!");

      // ✅ Redirect to dashboard
      window.location.replace("../DASHBOARD/dashboard.html");
      return;
    }

    // ❌ Failed login
    alert("Invalid email or password");

  } catch (error) {
    console.error(error);
    alert("Server error. Make sure your backend is running.");
  }
});
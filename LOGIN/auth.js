const SUPABASE_URL = "https://levveflpiytbwxnkclzz.supabase.co";
const SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImxldnZlZmxwaXl0Ynd4bmtjbHp6Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQxOTQ2NjEsImV4cCI6MjA4OTc3MDY2MX0.I9xl80gIXa2oMusWt63Q-xhqBiDs39WD52B57WypdXw";

const supabaseClient = supabase.createClient(SUPABASE_URL, SUPABASE_ANON_KEY);


// SIGNUP
document.getElementById("signupForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();

  const name = document.getElementById("signupName").value;
  const email = document.getElementById("signupEmail").value;
  const password = document.getElementById("signupPass").value;
  const confirm = document.getElementById("signupConfirm").value;

  if (password !== confirm) {
    alert("Passwords do not match!");
    return;
  }

  const { error } = await supabaseClient.auth.signUp({
    email,
    password,
    options: { data: { full_name: name } }
  });

  if (error) {
    alert(error.message);
  } else {
    alert("Signup successful!");
    window.location.href = "login.html";
  }
});


// LOGIN
document.getElementById("loginForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();

  const email = document.getElementById("loginEmail").value;
  const password = document.getElementById("loginPassword").value;

  const { data, error } = await supabaseClient.auth.signInWithPassword({
    email,
    password
  });

  if (error) {
    alert("Invalid email or password.");
    return;
  }

  if (!data.user) {
    alert("Account not found.");
    return;
  }

  alert("Login successful!");
  window.location.href = "../DASHBOARD/dashboard.html";
});
const SUPABASE_URL = "https://levveflpiytbwxnkclzz.supabase.co";
const SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImxldnZlZmxwaXl0Ynd4bmtjbHp6Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQxOTQ2NjEsImV4cCI6MjA4OTc3MDY2MX0.I9xl80gIXa2oMusWt63Q-xhqBiDs39WD52B57WypdXw";

const supabaseClient = supabase.createClient(SUPABASE_URL, SUPABASE_ANON_KEY);

async function signUpUser(name, email, password) {
  const { data, error } = await supabaseClient.auth.signUp({
    email,
    password,
    options: {
      data: {
        full_name: name
      }
    }
  });

  return { data, error };
}

async function loginUser(email, password) {
  const { data, error } = await supabaseClient.auth.signInWithPassword({
    email,
    password
  });

  return { data, error };
}
document.querySelectorAll("a[href^='#']").forEach(anchor => {
    anchor.addEventListener("click", function(e) {
        e.preventDefault();

        document.querySelector(this.getAttribute("href"))
            .scrollIntoView({
                behavior: "smooth"
            });
    });
});

window.addEventListener("scroll", () => {

    const navbar = document.querySelector(".navbar");

    if(window.scrollY > 50){
        navbar.style.background = "#ffffff";
        navbar.style.boxShadow = "0 5px 15px rgba(0,0,0,.1)";
    }
    else{
        navbar.style.boxShadow = "0 2px 10px rgba(0,0,0,.08)";
    }
});


// ================= LOGIN BUTTON =================

document.getElementById("loginBtn")
?.addEventListener("click", () => {

    window.location.href =
        "../LOGIN/login.html";

});


// ================= REGISTER BUTTON =================

document.getElementById("registerBtn")
?.addEventListener("click", () => {

    window.location.href =
        "../LOGIN/signup.html";

});
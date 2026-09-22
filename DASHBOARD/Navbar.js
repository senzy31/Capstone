// ======================================================
// NAVBAR AVATAR
// The circular avatar in the top-right of every job seeker page: the user's own profile photo,
// or their initials if they haven't set one. Shared so every page shows and updates it the same
// way - including right after an upload, with no logout needed (see Profile.html/Profile.js).
//
// Needs three elements already on the page:
//   #navAvatarLink       the whole clickable avatar (an <a href="Profile.html">)
//   #navAvatarImg        an <img>, hidden until there is a photo to show
//   #navAvatarInitials   the fallback, shown until there is a photo
// ======================================================
const Navbar = (() => {
    "use strict";

    if (!window.ApiClient) {
        throw new Error("Navbar.js needs ApiClient.js loaded first.");
    }

    // Remembered so a page can call render(name) on its own - after the name changes, say -
    // without having to carry the last-looked-up photo around itself.
    let lastPhotoUrl = null;

    function initials(name) {

        const trimmed = (name || "").trim();

        if (!trimmed) {
            return "U";
        }

        const parts = trimmed.split(/\s+/);

        return (parts.length >= 2
            ? parts[0].charAt(0) + parts[parts.length - 1].charAt(0)
            : parts[0].charAt(0)
        ).toUpperCase();

    }

    // Shows a photo if given one, otherwise the initials fallback. Safe to call as soon as the
    // page knows the user's name, before their photo (if any) has loaded. photoUrl defaults to
    // whatever was last shown, so a page can call render(name) on its own after the name
    // changes without needing to track the photo itself - pass null explicitly to clear it.
    function render(fullName, photoUrl = lastPhotoUrl) {

        lastPhotoUrl = photoUrl;

        const img = document.getElementById("navAvatarImg");
        const fallback = document.getElementById("navAvatarInitials");

        if (photoUrl) {

            if (img) {
                img.src = photoUrl;
                img.alt = "Your profile photo";
                img.hidden = false;
            }

            if (fallback) {
                fallback.hidden = true;
            }

        } else {

            if (img) {
                img.hidden = true;
                img.removeAttribute("src");
            }

            if (fallback) {
                fallback.hidden = false;
                fallback.textContent = initials(fullName);
            }

        }

    }

    // Loads the current photo (if any) and renders the avatar. Call once per page, after the
    // user is known to be logged in - a failed lookup just leaves the initials showing, never
    // blocks the page.
    async function mount(userId, fullName) {

        render(fullName, null);

        try {

            const response = await ApiClient.authFetch(
                `${ApiClient.API}/Profile/by-user/${userId}`, { noUpgradePrompt: true }
            );

            if (!response.ok) {
                return;
            }

            const profile = await response.json();

            render(fullName, profile.photoUrl || null);

        } catch {
            // Initials are already shown.
        }

    }

    return { mount, render, initials };

})();

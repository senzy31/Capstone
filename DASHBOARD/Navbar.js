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

    // ======================================================
    // NOTIFICATIONS (the bell)
    //
    // Needs, already on the page:
    //   #navBellBtn          the bell button (aria-haspopup, aria-expanded managed here)
    //   #navBellBadge        the unread-count badge, hidden when there are none
    //   #navBellDropdown     the dropdown panel, hidden until opened
    //   #navBellList         where the latest notifications are drawn
    //   #navMarkAllReadBtn   optional - "mark all as read"
    // "View all" is a plain link in the dropdown's own markup - nothing here needs its href.
    // ======================================================

    const escapeText = (value) => String(value ?? "")
        .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;").replace(/'/g, "&#039;");

    function timeAgo(iso) {

        const then = new Date(iso).getTime();

        if (Number.isNaN(then)) {
            return "";
        }

        const minutes = Math.floor(Math.max(0, Date.now() - then) / 60000);

        if (minutes < 1) return "just now";
        if (minutes < 60) return `${minutes}m ago`;

        const hours = Math.floor(minutes / 60);

        if (hours < 24) return `${hours}h ago`;

        const days = Math.floor(hours / 24);

        return days < 7 ? `${days}d ago` : new Date(iso).toLocaleDateString();

    }

    function renderBadge(count) {

        const badge = document.getElementById("navBellBadge");

        if (!badge) {
            return;
        }

        if (count > 0) {
            badge.textContent = count > 99 ? "99+" : String(count);
            badge.hidden = false;
        } else {
            badge.hidden = true;
        }

    }

    async function refreshUnreadCount() {

        try {

            const response = await ApiClient.authFetch(`${ApiClient.API}/Notification/unread-count`, { noUpgradePrompt: true });

            if (!response.ok) {
                return;
            }

            renderBadge((await response.json()).count);

        } catch {
            // Leaves the badge as it was.
        }

    }

    function renderDropdownList(items) {

        const list = document.getElementById("navBellList");

        if (!list) {
            return;
        }

        if (items.length === 0) {
            list.innerHTML = `<p class="nav-bell-empty">No notifications yet.</p>`;
            return;
        }

        list.innerHTML = items.map(n => `
            <button type="button" class="nav-bell-item${n.isRead ? "" : " unread"}" data-id="${n.notificationId}" data-link="${escapeText(n.link || "")}">
                <span class="nav-bell-dot" aria-hidden="true"></span>
                <span class="nav-bell-message">${escapeText(n.message)}</span>
                <span class="nav-bell-time">${escapeText(timeAgo(n.createdAt))}</span>
            </button>
        `).join("");

    }

    async function loadDropdown() {

        try {

            const response = await ApiClient.authFetch(`${ApiClient.API}/Notification?page=1`, { noUpgradePrompt: true });

            if (!response.ok) {
                return;
            }

            const body = await response.json();

            renderDropdownList(body.data.slice(0, 8));
            renderBadge(body.unreadCount);

        } catch {
            // Leaves the dropdown as it was.
        }

    }

    let dropdownOpen = false;

    function closeDropdown() {

        const dropdown = document.getElementById("navBellDropdown");

        if (!dropdown) {
            return;
        }

        dropdown.hidden = true;
        dropdownOpen = false;
        document.getElementById("navBellBtn")?.setAttribute("aria-expanded", "false");

    }

    function openDropdown() {

        const dropdown = document.getElementById("navBellDropdown");

        if (!dropdown) {
            return;
        }

        dropdown.hidden = false;
        dropdownOpen = true;
        document.getElementById("navBellBtn")?.setAttribute("aria-expanded", "true");

        loadDropdown();

    }

    // Sets up the bell: click to open/close, click outside or Escape to close, "mark all as
    // read", clicking an item marks it read and follows its link, and polls the unread count
    // every 30s (paused while the tab is hidden). Call once per page, after login is confirmed.
    function mountBell() {

        const button = document.getElementById("navBellBtn");
        const dropdown = document.getElementById("navBellDropdown");
        const markAllBtn = document.getElementById("navMarkAllReadBtn");
        const list = document.getElementById("navBellList");

        if (!button || !dropdown) {
            return;
        }

        button.addEventListener("click", (event) => {
            event.stopPropagation();
            dropdownOpen ? closeDropdown() : openDropdown();
        });

        document.addEventListener("click", (event) => {
            if (dropdownOpen && !dropdown.contains(event.target) && event.target !== button) {
                closeDropdown();
            }
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && dropdownOpen) {
                closeDropdown();
            }
        });

        markAllBtn?.addEventListener("click", async () => {

            try {
                await ApiClient.authFetch(`${ApiClient.API}/Notification/read-all`, { method: "PATCH", noUpgradePrompt: true });
            } catch {
                // Best effort.
            }

            await loadDropdown();

        });

        list?.addEventListener("click", async (event) => {

            const item = event.target.closest(".nav-bell-item");

            if (!item) {
                return;
            }

            const id = item.dataset.id;
            const link = item.dataset.link;

            item.classList.remove("unread");

            try {
                await ApiClient.authFetch(`${ApiClient.API}/Notification/${id}/read`, { method: "PATCH", noUpgradePrompt: true });
            } catch {
                // Best effort - still navigate below.
            }

            refreshUnreadCount();

            if (link) {
                window.location.href = link;
            }

        });

        refreshUnreadCount();

        setInterval(() => {
            if (!document.hidden) {
                refreshUnreadCount();
            }
        }, 30000);

        document.addEventListener("visibilitychange", () => {
            if (!document.hidden) {
                refreshUnreadCount();
            }
        });

    }

    return { mount, render, initials, mountBell, refreshUnreadCount, timeAgo, escapeText };

})();

// ======================================================
// NOTIFICATIONS PAGE (View all)
// Reached from the bell on any page, job seeker or employer - so this page doesn't assume
// either one's own sidebar. Auth is a plain "are you logged in at all" check, the same as
// Profile.html, just without its role-specific parts.
// ======================================================

document.addEventListener("DOMContentLoaded", () => {

    const user = requireLogin();

    if (!user) {
        return;
    }

    Navbar.mount(user.userId, user.fullName);
    Navbar.mountBell();

    setupListeners();

    loadPage(1);

});


function requireLogin() {

    const loginPage = "../LOGIN/login.html";

    try {

        const user = JSON.parse(localStorage.getItem("user"));

        const userId = user?.userId || user?.user_id;

        if (!userId) {
            throw new Error("No saved session");
        }

        if (!localStorage.getItem("token")) {

            localStorage.removeItem("user");

            sessionStorage.setItem("joblink.sessionExpired", "1");

            throw new Error("No login token");

        }

        return { ...user, userId, fullName: user.fullName || user.full_name || "User" };

    } catch {

        window.location.href = loginPage;

        return null;

    }

}


let currentPage = 1;
let totalCount = 0;
const PAGE_SIZE = 20;

function escapeHtml(value) {

    if (value === null || value === undefined) {
        return "";
    }

    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");

}

function setupListeners() {

    document.getElementById("markAllReadBtn").addEventListener("click", async () => {

        try {
            await ApiClient.authFetch(`${ApiClient.API}/Notification/read-all`, { method: "PATCH", noUpgradePrompt: true });
        } catch {
            // Best effort.
        }

        loadPage(currentPage);
        Navbar.refreshUnreadCount();

    });

    document.getElementById("prevPageBtn").addEventListener("click", () => {
        if (currentPage > 1) {
            loadPage(currentPage - 1);
        }
    });

    document.getElementById("nextPageBtn").addEventListener("click", () => {
        if (currentPage * PAGE_SIZE < totalCount) {
            loadPage(currentPage + 1);
        }
    });

}

async function loadPage(page) {

    const list = document.getElementById("notificationsList");

    list.innerHTML = `<p class="notifications-loading">Loading...</p>`;

    try {

        const response = await ApiClient.authFetch(`${ApiClient.API}/Notification?page=${page}`, { noUpgradePrompt: true });

        if (!response.ok) {
            throw new Error(`Couldn't load your notifications (${response.status}).`);
        }

        const body = await response.json();

        currentPage = body.page;
        totalCount = body.totalCount;

        render(body.data);
        updatePager();

    } catch (error) {

        console.error("Error loading notifications:", error);

        list.innerHTML = `
            <p class="notifications-empty">
                <i class="fa-solid fa-circle-exclamation"></i>
                Unable to load notifications. ${escapeHtml(error.message)}
            </p>
        `;

    }

}

function render(items) {

    const list = document.getElementById("notificationsList");

    if (items.length === 0) {
        list.innerHTML = `<p class="notifications-empty">No notifications yet.</p>`;
        return;
    }

    list.innerHTML = items.map(n => `
        <button type="button" class="notification-row${n.isRead ? "" : " unread"}" data-id="${n.notificationId}" data-link="${escapeHtml(n.link || "")}">
            <span class="dot" aria-hidden="true"></span>
            <span class="notification-body">
                <p class="notification-message">${escapeHtml(n.message)}</p>
                <span class="notification-time">${escapeHtml(Navbar.timeAgo(n.createdAt))}</span>
            </span>
        </button>
    `).join("");

    list.querySelectorAll(".notification-row").forEach(row => {

        row.addEventListener("click", async () => {

            const id = row.dataset.id;
            const link = row.dataset.link;

            row.classList.remove("unread");

            try {
                await ApiClient.authFetch(`${ApiClient.API}/Notification/${id}/read`, { method: "PATCH", noUpgradePrompt: true });
            } catch {
                // Best effort - still navigate below.
            }

            Navbar.refreshUnreadCount();

            if (link) {
                window.location.href = link;
            }

        });

    });

}

function updatePager() {

    document.getElementById("pageStatus").textContent = `Page ${currentPage} of ${Math.max(1, Math.ceil(totalCount / PAGE_SIZE))}`;
    document.getElementById("prevPageBtn").disabled = currentPage <= 1;
    document.getElementById("nextPageBtn").disabled = currentPage * PAGE_SIZE >= totalCount;

}

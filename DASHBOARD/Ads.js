// ======================================================
// ADS - the two placeholder ad slots on the dashboard (Free plan only)
//
// These are static placeholders: no ad network, no external script, no image or frame from
// another site - just a labelled box. Premium members never see them.
//
// Whether to show them comes from the server (GET /api/subscription -> features.showAds).
// If the plan can't be read the slots stay hidden: better no ad than an ad for a Premium member.
//
//   Ads.mount()                 puts the card under the sidebar menu
//   Ads.placeInList(container)  puts one card between the job cards; call it after every render
// ======================================================
const Ads = (function () {
    "use strict";

    const SIDEBAR_SLOT = "ad-sidebar";
    const LIST_SLOT = "ad-list";

    // Where in the job list the card goes: after this many cards (fewer when the list is short,
    // and never at the very end - an ad sits *between* listings).
    const AFTER_CARDS = 3;


    function shouldShow() {

        if (typeof ApiClient === "undefined") {
            return Promise.resolve(false);
        }

        return ApiClient.getPlan()
            .then(plan => Boolean(plan && plan.features && plan.features.showAds === true))
            .catch(() => false);

    }


    function buildCard(slotId, className) {

        const card = document.createElement("aside");

        card.className = `ad-card ${className}`;
        card.id = slotId;
        card.setAttribute("aria-label", "Advertisement");

        card.innerHTML = `
            <span class="ad-label">Advertisement</span>
            <div class="ad-body">
                <i class="fa-solid fa-rectangle-ad" aria-hidden="true"></i>
                <p class="ad-title">Your ad could be here</p>
                <p class="ad-text">Placeholder ad space.</p>
            </div>
            <a class="ad-remove" href="Plans.html">
                <i class="fa-solid fa-crown"></i>
                Go Premium to remove ads
            </a>
        `;

        return card;

    }


    async function mount() {

        const sidebar = document.querySelector(".sidebar");

        if (!sidebar || document.getElementById(SIDEBAR_SLOT)) {
            return;
        }

        if (!(await shouldShow())) {
            return;
        }

        if (!document.getElementById(SIDEBAR_SLOT)) {
            sidebar.appendChild(buildCard(SIDEBAR_SLOT, "ad-sidebar-card"));
        }

    }


    async function placeInList(container) {

        if (!container) {
            return;
        }

        const show = await shouldShow();

        // The list may have been redrawn while we waited for the plan.
        const cards = Array.from(container.children).filter(node => node.classList.contains("job-card"));

        container.querySelectorAll(`#${LIST_SLOT}`).forEach(node => node.remove());

        if (!show || cards.length < 2) {
            return;
        }

        const after = cards[Math.min(AFTER_CARDS, cards.length - 1) - 1];

        after.insertAdjacentElement("afterend", buildCard(LIST_SLOT, "ad-list-card"));

    }


    return { mount, placeInList };

})();

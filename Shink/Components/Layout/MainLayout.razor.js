const NAV_TOGGLE_SELECTOR = "[data-nav-menu-toggle]";
const NAV_TOGGLE_LABEL_SELECTOR = "[data-nav-menu-toggle-label]";
const OPEN_CLASS = "is-open";
const OPEN_LABEL = "Maak navigasie toe";
const CLOSED_LABEL = "Maak navigasie oop";

function wireNavToggle(toggleButton) {
    if (!(toggleButton instanceof HTMLElement) || toggleButton.dataset.navMenuWired === "true") {
        return;
    }

    const navId = toggleButton.getAttribute("aria-controls");
    if (!navId) {
        return;
    }

    const navMenu = document.getElementById(navId);
    if (!(navMenu instanceof HTMLElement)) {
        return;
    }

    const navControls = toggleButton.closest(".nav-controls");
    const srLabel = toggleButton.querySelector(NAV_TOGGLE_LABEL_SELECTOR);

    const setMenuState = (isOpen) => {
        navMenu.classList.toggle(OPEN_CLASS, isOpen);
        toggleButton.setAttribute("aria-expanded", isOpen ? "true" : "false");

        const label = isOpen ? OPEN_LABEL : CLOSED_LABEL;
        toggleButton.setAttribute("aria-label", label);
        toggleButton.setAttribute("title", label);

        if (srLabel instanceof HTMLElement) {
            srLabel.textContent = label;
        }
    };

    const isOpen = () => navMenu.classList.contains(OPEN_CLASS);

    setMenuState(false);

    toggleButton.addEventListener("click", (event) => {
        event.preventDefault();
        setMenuState(!isOpen());
    });

    navMenu.querySelectorAll("a").forEach((link) => {
        link.addEventListener("click", () => {
            setMenuState(false);
        });
    });

    document.addEventListener("click", (event) => {
        if (!isOpen()) {
            return;
        }

        const target = event.target;
        if (target instanceof Node && navControls instanceof HTMLElement && navControls.contains(target)) {
            return;
        }

        setMenuState(false);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            setMenuState(false);
        }
    });

    document.addEventListener("enhancedload", () => {
        setMenuState(false);
    });

    toggleButton.dataset.navMenuWired = "true";
}

function initializeNavMenus() {
    document.querySelectorAll(NAV_TOGGLE_SELECTOR).forEach((toggleButton) => {
        wireNavToggle(toggleButton);
    });
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeNavMenus, { once: true });
} else {
    initializeNavMenus();
}

document.addEventListener("enhancedload", initializeNavMenus);

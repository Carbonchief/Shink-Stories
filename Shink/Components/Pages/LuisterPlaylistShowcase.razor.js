const SCROLL_TRIGGER_SELECTOR = "[data-scroll-target]";

function handleScrollTriggerClick(event) {
    if (!(event.currentTarget instanceof HTMLElement)) {
        return;
    }

    const targetId = event.currentTarget.getAttribute("data-scroll-target");
    if (!targetId) {
        return;
    }

    const target = document.getElementById(targetId);
    if (!(target instanceof HTMLElement)) {
        return;
    }

    event.preventDefault();

    const prefersReducedMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const scrollBehavior = prefersReducedMotion ? "auto" : "smooth";
    const top = target.getBoundingClientRect().top + window.pageYOffset;

    window.requestAnimationFrame(() => {
        window.scrollTo({ top, behavior: scrollBehavior });
    });

    if (history && typeof history.replaceState === "function") {
        const url = new URL(window.location.href);
        url.hash = `#${targetId}`;
        history.replaceState(null, "", url.toString());
    }
}

function wireScrollTrigger(trigger) {
    if (!(trigger instanceof HTMLElement) || trigger.dataset.scrollWired === "true") {
        return;
    }

    trigger.addEventListener("click", handleScrollTriggerClick);
    trigger.dataset.scrollWired = "true";
}

export function initializePlaylistShowcaseScroll() {
    document.querySelectorAll(SCROLL_TRIGGER_SELECTOR).forEach((trigger) => {
        wireScrollTrigger(trigger);
    });
}

export function disposePlaylistShowcaseScroll() {
    document.querySelectorAll(SCROLL_TRIGGER_SELECTOR).forEach((trigger) => {
        if (!(trigger instanceof HTMLElement) || trigger.dataset.scrollWired !== "true") {
            return;
        }

        trigger.removeEventListener("click", handleScrollTriggerClick);
        delete trigger.dataset.scrollWired;
    });
}

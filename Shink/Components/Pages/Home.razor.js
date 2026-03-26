export function scrollCarouselByPage(carouselElement, direction) {
    if (!(carouselElement instanceof HTMLElement)) {
        return;
    }

    const normalizedDirection = direction >= 0 ? 1 : -1;
    const scrollAmount = Math.max(220, Math.round(carouselElement.clientWidth * 0.85));

    carouselElement.scrollBy({
        left: normalizedDirection * scrollAmount,
        behavior: "smooth"
    });
}

let homeStoryLoadingWired = false;
let homeRevealObserver = null;

function clearStoryNavigationLoadingState() {
    const activeCards = document.querySelectorAll(".story-carousel-item.is-navigating");
    for (let index = 0; index < activeCards.length; index += 1) {
        activeCards[index].classList.remove("is-navigating");
    }
}

export function initializeHomeStoryLoading() {
    if (homeStoryLoadingWired) {
        return;
    }

    document.addEventListener("click", (event) => {
        if (event.defaultPrevented ||
            event.button !== 0 ||
            event.metaKey ||
            event.ctrlKey ||
            event.shiftKey ||
            event.altKey) {
            return;
        }

        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const storyLink = target.closest(".story-carousel-link[href]");
        if (!(storyLink instanceof HTMLAnchorElement)) {
            return;
        }

        const href = storyLink.getAttribute("href") || "";
        if (!href.startsWith("/gratis/") && !href.startsWith("/luister/")) {
            return;
        }

        const card = storyLink.closest(".story-carousel-item");
        if (!(card instanceof HTMLElement)) {
            return;
        }

        clearStoryNavigationLoadingState();
        card.classList.add("is-navigating");
    });

    document.addEventListener("enhancedload", clearStoryNavigationLoadingState);
    window.addEventListener("pageshow", clearStoryNavigationLoadingState);
    homeStoryLoadingWired = true;
}

function disconnectHomeRevealObserver() {
    if (homeRevealObserver instanceof IntersectionObserver) {
        homeRevealObserver.disconnect();
    }

    homeRevealObserver = null;
}

function getHomeRevealElements() {
    return Array.from(document.querySelectorAll(".home-scroll-reveal"))
        .filter((element) => element instanceof HTMLElement);
}

function isNearViewport(element) {
    const bounds = element.getBoundingClientRect();
    return bounds.top <= window.innerHeight * 0.9 && bounds.bottom >= 0;
}

export function initializeHomeAnimations() {
    disconnectHomeRevealObserver();

    const revealElements = getHomeRevealElements();
    if (revealElements.length === 0) {
        return;
    }

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        for (const element of revealElements) {
            element.classList.remove("home-motion-ready");
            element.classList.add("is-visible");
        }

        return;
    }

    const deferredElements = [];
    for (const element of revealElements) {
        if (isNearViewport(element)) {
            element.classList.remove("home-motion-ready");
            element.classList.add("is-visible");
            continue;
        }

        element.classList.remove("is-visible");
        element.classList.add("home-motion-ready");
        deferredElements.push(element);
    }

    if (deferredElements.length === 0) {
        return;
    }

    homeRevealObserver = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting || !(entry.target instanceof HTMLElement)) {
                continue;
            }

            entry.target.classList.add("is-visible");
            homeRevealObserver?.unobserve(entry.target);
        }
    }, {
        threshold: 0.18,
        rootMargin: "0px 0px -10% 0px"
    });

    for (const element of deferredElements) {
        homeRevealObserver.observe(element);
    }
}

export function disposeHomeAnimations() {
    disconnectHomeRevealObserver();

    for (const element of getHomeRevealElements()) {
        element.classList.remove("home-motion-ready");
        element.classList.remove("is-visible");
    }
}

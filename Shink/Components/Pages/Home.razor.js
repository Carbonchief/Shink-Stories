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

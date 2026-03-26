const STORY_CAROUSEL_SELECTOR = ".stories-carousel";
const STORY_CAROUSEL_DRAGGING_CLASS = "is-dragging";
const STORY_CAROUSEL_DRAG_THRESHOLD_PX = 8;
const STORY_CAROUSEL_CLICK_SUPPRESSION_MS = 350;

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

function wireStoryCarouselDrag(carouselElement) {
    if (!(carouselElement instanceof HTMLElement) || carouselElement.dataset.carouselDragWired === "true") {
        return;
    }

    const dragState = {
        pointerId: null,
        startX: 0,
        startY: 0,
        startScrollLeft: 0,
        isPointerDown: false,
        isDragging: false,
        suppressClickUntil: 0
    };

    const resetDragState = () => {
        if (dragState.pointerId !== null && carouselElement.hasPointerCapture(dragState.pointerId)) {
            try {
                carouselElement.releasePointerCapture(dragState.pointerId);
            } catch {
                // Ignore capture release failures during teardown.
            }
        }

        dragState.pointerId = null;
        dragState.isPointerDown = false;
        dragState.isDragging = false;
        carouselElement.classList.remove(STORY_CAROUSEL_DRAGGING_CLASS);
    };

    carouselElement.addEventListener("pointerdown", (event) => {
        if (!event.isPrimary || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }

        dragState.pointerId = event.pointerId;
        dragState.startX = event.clientX;
        dragState.startY = event.clientY;
        dragState.startScrollLeft = carouselElement.scrollLeft;
        dragState.isPointerDown = true;
        dragState.isDragging = false;

        if (event.pointerType === "mouse") {
            carouselElement.setPointerCapture(event.pointerId);
        }
    }, { passive: true });

    carouselElement.addEventListener("pointermove", (event) => {
        if (!dragState.isPointerDown || event.pointerId !== dragState.pointerId) {
            return;
        }

        const deltaX = event.clientX - dragState.startX;
        const deltaY = event.clientY - dragState.startY;

        if (!dragState.isDragging) {
            if (Math.abs(deltaX) < STORY_CAROUSEL_DRAG_THRESHOLD_PX &&
                Math.abs(deltaY) < STORY_CAROUSEL_DRAG_THRESHOLD_PX) {
                return;
            }

            if (Math.abs(deltaY) > Math.abs(deltaX)) {
                resetDragState();
                return;
            }

            dragState.isDragging = true;
            carouselElement.classList.add(STORY_CAROUSEL_DRAGGING_CLASS);
        }

        event.preventDefault();
        carouselElement.scrollLeft = dragState.startScrollLeft - deltaX;
    }, { passive: false });

    const finishDrag = (event) => {
        if (!dragState.isPointerDown || event.pointerId !== dragState.pointerId) {
            return;
        }

        const draggedFarEnough = dragState.isDragging &&
            Math.abs(event.clientX - dragState.startX) >= STORY_CAROUSEL_DRAG_THRESHOLD_PX;

        resetDragState();

        if (draggedFarEnough) {
            dragState.suppressClickUntil = Date.now() + STORY_CAROUSEL_CLICK_SUPPRESSION_MS;
        }
    };

    carouselElement.addEventListener("pointerup", finishDrag);
    carouselElement.addEventListener("pointercancel", finishDrag);
    carouselElement.addEventListener("lostpointercapture", resetDragState);

    carouselElement.addEventListener("click", (event) => {
        if (Date.now() > dragState.suppressClickUntil) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
    }, true);

    carouselElement.addEventListener("dragstart", (event) => {
        event.preventDefault();
    });

    carouselElement.dataset.carouselDragWired = "true";
}

function wireStoryCarousels(root = document) {
    const carousels = root.querySelectorAll(STORY_CAROUSEL_SELECTOR);
    for (let index = 0; index < carousels.length; index += 1) {
        wireStoryCarouselDrag(carousels[index]);
    }
}

function clearStoryNavigationLoadingState() {
    const activeCards = document.querySelectorAll(".story-carousel-item.is-navigating");
    for (let index = 0; index < activeCards.length; index += 1) {
        activeCards[index].classList.remove("is-navigating");
    }
}

export function initializeHomeStoryLoading() {
    wireStoryCarousels();

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

    document.addEventListener("enhancedload", () => {
        clearStoryNavigationLoadingState();
        wireStoryCarousels();
    });

    window.addEventListener("pageshow", () => {
        clearStoryNavigationLoadingState();
        wireStoryCarousels();
    });
    homeStoryLoadingWired = true;
}

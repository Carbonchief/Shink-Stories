const STORY_CAROUSEL_SELECTOR = ".stories-carousel";
const STORY_CAROUSEL_DRAGGING_CLASS = "is-dragging";
const STORY_CAROUSEL_DRAG_THRESHOLD_PX = 8;
const STORY_CAROUSEL_CLICK_SUPPRESSION_MS = 350;
const STORY_CAROUSEL_MOUSE_POINTER_ID = -1;

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
let homeReviewCarouselWired = false;
let homeSocialProofObserver = null;

function wireCarouselDragScrolling() {
    const carousels = document.querySelectorAll(".stories-carousel");
    for (const carousel of carousels) {
        if (!(carousel instanceof HTMLElement) || carousel.dataset.dragScrollWired === "true") {
            continue;
        }

        carousel.dataset.dragScrollWired = "true";
        let activePointerId = null;
        let startX = 0;
        let startScrollLeft = 0;
        let pendingScrollLeft = 0;
        let animationFrameId = 0;
        let pointerDown = false;
        let isDragging = false;
        let suppressClick = false;

        const flushScrollPosition = () => {
            animationFrameId = 0;
            carousel.scrollLeft = pendingScrollLeft;
        };

        const queueScrollPosition = (nextScrollLeft) => {
            pendingScrollLeft = nextScrollLeft;

            if (animationFrameId !== 0) {
                return;
            }

            animationFrameId = window.requestAnimationFrame(flushScrollPosition);
        };

        const releaseDragState = () => {
            if (animationFrameId !== 0) {
                window.cancelAnimationFrame(animationFrameId);
                animationFrameId = 0;
            }

            if (activePointerId !== null && carousel.hasPointerCapture(activePointerId)) {
                carousel.releasePointerCapture(activePointerId);
            }

            activePointerId = null;
            pointerDown = false;
            isDragging = false;
            carousel.classList.remove("is-dragging");
            document.body.style.userSelect = "";
        };

        carousel.addEventListener("pointerdown", (event) => {
            if (event.button !== 0 || event.pointerType === "touch") {
                return;
            }

            event.preventDefault();
            activePointerId = event.pointerId;
            pointerDown = true;
            startX = event.clientX;
            startScrollLeft = carousel.scrollLeft;
            pendingScrollLeft = startScrollLeft;
            isDragging = false;
            suppressClick = false;
        });

        carousel.addEventListener("pointermove", (event) => {
            if (!pointerDown || activePointerId !== event.pointerId) {
                return;
            }

            const deltaX = event.clientX - startX;
            if (!isDragging && Math.abs(deltaX) < 6) {
                return;
            }

            if (!isDragging) {
                isDragging = true;
                suppressClick = true;
                carousel.classList.add("is-dragging");
                document.body.style.userSelect = "none";

                if (!carousel.hasPointerCapture(event.pointerId)) {
                    carousel.setPointerCapture(event.pointerId);
                }
            }

            queueScrollPosition(startScrollLeft - deltaX);
            event.preventDefault();
        });

        carousel.addEventListener("pointerup", (event) => {
            if (!pointerDown || activePointerId !== event.pointerId) {
                return;
            }

            const wasDragging = isDragging;
            releaseDragState();

            if (wasDragging) {
                window.setTimeout(() => {
                    suppressClick = false;
                }, 0);
            }
        });

        carousel.addEventListener("pointercancel", releaseDragState);
        carousel.addEventListener("lostpointercapture", releaseDragState);

        carousel.addEventListener("click", (event) => {
            if (!suppressClick) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            suppressClick = false;
        }, true);

        carousel.addEventListener("dragstart", (event) => {
            event.preventDefault();
        });
    }
}

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

    const tryCapturePointer = (pointerId) => {
        if (pointerId === null || carouselElement.hasPointerCapture(pointerId)) {
            return;
        }

        try {
            carouselElement.setPointerCapture(pointerId);
        } catch {
            // Ignore pointer capture failures on browsers that reject touch capture here.
        }
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
        if (event.pointerType === "touch" || event.pointerType === "mouse") {
            return;
        }

        event.preventDefault();
        dragState.pointerId = event.pointerId;
        dragState.startX = event.clientX;
        dragState.startY = event.clientY;
        dragState.startScrollLeft = carouselElement.scrollLeft;
        dragState.isPointerDown = true;
        dragState.isDragging = false;
    });

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
            tryCapturePointer(event.pointerId);
        }

        event.preventDefault();
        carouselElement.scrollLeft = dragState.startScrollLeft - deltaX;
    }, { passive: false });

    const finishDrag = (clientX) => {
        if (!dragState.isPointerDown) {
            return;
        }

        const draggedFarEnough = dragState.isDragging &&
            Math.abs(clientX - dragState.startX) >= STORY_CAROUSEL_DRAG_THRESHOLD_PX;

        resetDragState();

        if (draggedFarEnough) {
            dragState.suppressClickUntil = Date.now() + STORY_CAROUSEL_CLICK_SUPPRESSION_MS;
        }
    };

    carouselElement.addEventListener("pointerup", (event) => {
        if (event.pointerId !== dragState.pointerId) {
            return;
        }

        finishDrag(event.clientX);
    });
    carouselElement.addEventListener("pointercancel", (event) => {
        if (event.pointerId !== dragState.pointerId) {
            return;
        }

        finishDrag(event.clientX);
    });
    carouselElement.addEventListener("lostpointercapture", resetDragState);

    carouselElement.addEventListener("mousedown", (event) => {
        if (event.button !== 0) {
            return;
        }

        event.preventDefault();
        dragState.pointerId = STORY_CAROUSEL_MOUSE_POINTER_ID;
        dragState.startX = event.clientX;
        dragState.startY = event.clientY;
        dragState.startScrollLeft = carouselElement.scrollLeft;
        dragState.isPointerDown = true;
        dragState.isDragging = false;
    });

    window.addEventListener("mousemove", (event) => {
        if (!dragState.isPointerDown || dragState.pointerId !== STORY_CAROUSEL_MOUSE_POINTER_ID) {
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
    });

    window.addEventListener("mouseup", (event) => {
        if (!dragState.isPointerDown || dragState.pointerId !== STORY_CAROUSEL_MOUSE_POINTER_ID || event.button !== 0) {
            return;
        }

        finishDrag(event.clientX);
    });
    window.addEventListener("blur", resetDragState);

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
    wireReviewCarousel();

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
        wireReviewCarousel();
    });

    window.addEventListener("pageshow", () => {
        clearStoryNavigationLoadingState();
        wireStoryCarousels();
        wireReviewCarousel();
    });
    homeStoryLoadingWired = true;
}

function scrollReviewCarousel(direction) {
    const carousel = document.querySelector("[data-review-carousel]");
    if (!(carousel instanceof HTMLElement)) {
        return;
    }

    const firstCard = carousel.querySelector(".home-review-card");
    const cardWidth = firstCard instanceof HTMLElement ? firstCard.getBoundingClientRect().width : carousel.clientWidth;
    const styles = window.getComputedStyle(carousel);
    const gap = Number.parseFloat(styles.columnGap || styles.gap || "0") || 0;
    const scrollAmount = Math.max(240, Math.round(cardWidth + gap));

    carousel.scrollBy({
        left: direction * scrollAmount,
        behavior: "smooth"
    });
}

function updateReviewCarouselControls() {
    const carousel = document.querySelector("[data-review-carousel]");
    const prevButton = document.querySelector("[data-review-carousel-prev]");
    const nextButton = document.querySelector("[data-review-carousel-next]");

    if (!(carousel instanceof HTMLElement) ||
        !(prevButton instanceof HTMLButtonElement) ||
        !(nextButton instanceof HTMLButtonElement)) {
        return;
    }

    const maxScrollLeft = Math.max(0, carousel.scrollWidth - carousel.clientWidth);
    const hasOverflow = maxScrollLeft > 4;

    prevButton.disabled = !hasOverflow || carousel.scrollLeft <= 4;
    nextButton.disabled = !hasOverflow || carousel.scrollLeft >= maxScrollLeft - 4;
}

function wireReviewCarousel() {
    const carousel = document.querySelector("[data-review-carousel]");
    if (!(carousel instanceof HTMLElement)) {
        return;
    }

    if (!homeReviewCarouselWired) {
        document.addEventListener("click", (event) => {
            const target = event.target;
            if (!(target instanceof Element)) {
                return;
            }

            if (target.closest("[data-review-carousel-prev]")) {
                scrollReviewCarousel(-1);
            }

            if (target.closest("[data-review-carousel-next]")) {
                scrollReviewCarousel(1);
            }
        });

        homeReviewCarouselWired = true;
    }

    wireStoryCarouselDrag(carousel);
    updateReviewCarouselControls();

    if (carousel.dataset.reviewCarouselStateWired === "true") {
        return;
    }

    const syncControls = () => updateReviewCarouselControls();
    carousel.addEventListener("scroll", syncControls, { passive: true });
    window.addEventListener("resize", syncControls);
    carousel.dataset.reviewCarouselStateWired = "true";
}

function disconnectHomeRevealObserver() {
    if (homeRevealObserver instanceof IntersectionObserver) {
        homeRevealObserver.disconnect();
    }

    homeRevealObserver = null;
}

function disconnectHomeSocialProofObserver() {
    if (homeSocialProofObserver instanceof IntersectionObserver) {
        homeSocialProofObserver.disconnect();
    }

    homeSocialProofObserver = null;
}

function getSocialProofCounters() {
    return Array.from(document.querySelectorAll("[data-count-up]"))
        .filter((element) => element instanceof HTMLElement);
}

function setSocialProofCounterValue(counter, value) {
    const suffix = counter.dataset.countSuffix || "";
    counter.textContent = `${Math.round(value)}${suffix}`;
}

function animateSocialProofCounter(counter) {
    if (!(counter instanceof HTMLElement) || counter.dataset.countStarted === "true") {
        return;
    }

    const target = Number.parseInt(counter.dataset.countTo || "0", 10);
    if (!Number.isFinite(target) || target <= 0) {
        return;
    }

    counter.dataset.countStarted = "true";

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        setSocialProofCounterValue(counter, target);
        return;
    }

    const duration = 4200;
    const startTime = window.performance.now();

    const step = (timestamp) => {
        const progress = Math.min(1, (timestamp - startTime) / duration);
        const easedProgress = 1 - Math.pow(1 - progress, 3);
        setSocialProofCounterValue(counter, target * easedProgress);

        if (progress < 1) {
            window.requestAnimationFrame(step);
        }
    };

    window.requestAnimationFrame(step);
}

function initializeSocialProofCounters() {
    disconnectHomeSocialProofObserver();

    const counters = getSocialProofCounters();
    if (counters.length === 0) {
        return;
    }

    for (const counter of counters) {
        if (counter.dataset.countStarted !== "true") {
            setSocialProofCounterValue(counter, 0);
        }
    }

    if (!("IntersectionObserver" in window)) {
        for (const counter of counters) {
            animateSocialProofCounter(counter);
        }

        return;
    }

    homeSocialProofObserver = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting || !(entry.target instanceof HTMLElement)) {
                continue;
            }

            animateSocialProofCounter(entry.target);
            if (homeSocialProofObserver instanceof IntersectionObserver) {
                homeSocialProofObserver.unobserve(entry.target);
            }
        }
    }, {
        threshold: 0.45,
        rootMargin: "0px 0px -8% 0px"
    });

    for (const counter of counters) {
        if (counter.dataset.countStarted !== "true") {
            homeSocialProofObserver.observe(counter);
        }
    }
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
    initializeSocialProofCounters();

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
            if (homeRevealObserver instanceof IntersectionObserver) {
                homeRevealObserver.unobserve(entry.target);
            }
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
    disconnectHomeSocialProofObserver();

    for (const element of getHomeRevealElements()) {
        element.classList.remove("home-motion-ready");
        element.classList.remove("is-visible");
    }
}

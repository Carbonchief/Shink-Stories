import { initializeHomeStoryLoading } from "/Components/Pages/Home.razor.js";

const PEEK_MASCOT_SELECTOR = "[data-luister-peek-mascot]";
const PEEK_VISIBLE_CLASS = "is-peeking";
const PEEK_POSITIONING_CLASS = "is-positioning";
const PEEK_POSITION_CLASSES = [
    "peek-side-left",
    "peek-side-right",
    "peek-side-top",
    "peek-side-bottom"
];
const PEEK_POSITION_ALIASES = new Map([
    ["left", "peek-side-left"],
    ["right", "peek-side-right"],
    ["top", "peek-side-top"],
    ["bottom", "peek-side-bottom"],
    ["peek-top-left", "peek-side-top"],
    ["peek-top-right", "peek-side-top"],
    ["peek-bottom-left", "peek-side-bottom"],
    ["peek-bottom-right", "peek-side-bottom"]
]);
const FIVE_MINUTES_MS = 5 * 60 * 1000;
const MAX_PEEKS_PER_WINDOW = 2;
const PEEK_VISIBLE_MS = 3600;
const PEEK_TRANSITION_BUFFER_MS = 850;
const PEEK_HIDE_WIGGLE_MS = 940;
const PEEK_CLICK_JUMP_MS = 560;
const INITIAL_DELAY_MIN_MS = 22000;
const INITIAL_DELAY_MAX_MS = 58000;
const NEXT_DELAY_MIN_MS = 78000;
const NEXT_DELAY_MAX_MS = 178000;

let peekTimerId = 0;
let hideTimerId = 0;
let peekMascotElement = null;
let recentPeekTimes = [];
let lastSideClass = "";
let activePeekAnimation = null;

export function initializeLuisterPage() {
    initializeHomeStoryLoading();
    initializeLuisterPeekMascot();
    window.schinkForceLuisterPeek = forceLuisterPeekMascot;
}

export function disposeLuisterPage() {
    clearLuisterPeekTimers();

    if (peekMascotElement instanceof HTMLElement) {
        peekMascotElement.classList.remove(PEEK_VISIBLE_CLASS, PEEK_POSITIONING_CLASS, ...PEEK_POSITION_CLASSES);
        peekMascotElement.removeAttribute("style");
        peekMascotElement.removeEventListener("click", handlePeekMascotClick);
    }

    peekMascotElement = null;
    recentPeekTimes = [];
    lastSideClass = "";
    cancelActivePeekAnimation();

    if (window.schinkForceLuisterPeek === forceLuisterPeekMascot) {
        delete window.schinkForceLuisterPeek;
    }
}

export function forceLuisterPeekMascot(positionClass = "") {
    initializeLuisterPeekMascot();

    if (!(peekMascotElement instanceof HTMLElement)) {
        return false;
    }

    clearLuisterPeekTimers();
    showPeekMascot({ force: true, sideClass: normalizeRequestedSideClass(positionClass) });
    return true;
}

function initializeLuisterPeekMascot() {
    const element = document.querySelector(PEEK_MASCOT_SELECTOR);
    if (!(element instanceof HTMLImageElement)) {
        disposeLuisterPage();
        return;
    }

    if (window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches) {
        disposeLuisterPage();
        return;
    }

    if (peekMascotElement === element && (peekTimerId !== 0 || hideTimerId !== 0)) {
        return;
    }

    clearLuisterPeekTimers();
    peekMascotElement = element;
    peekMascotElement.addEventListener("click", handlePeekMascotClick);
    scheduleNextPeek(randomBetween(INITIAL_DELAY_MIN_MS, INITIAL_DELAY_MAX_MS));
}

function clearLuisterPeekTimers() {
    if (peekTimerId !== 0) {
        window.clearTimeout(peekTimerId);
        peekTimerId = 0;
    }

    if (hideTimerId !== 0) {
        window.clearTimeout(hideTimerId);
        hideTimerId = 0;
    }
}

function scheduleNextPeek(delayMs) {
    if (!(peekMascotElement instanceof HTMLElement)) {
        return;
    }

    if (peekTimerId !== 0) {
        window.clearTimeout(peekTimerId);
    }

    peekTimerId = window.setTimeout(showPeekMascot, Math.max(1000, delayMs));
}

function showPeekMascot(options = {}) {
    peekTimerId = 0;

    if (!(peekMascotElement instanceof HTMLElement) || !document.body.contains(peekMascotElement)) {
        disposeLuisterPage();
        return;
    }

    cancelActivePeekAnimation();
    const isForced = options.force === true;
    const now = Date.now();
    recentPeekTimes = recentPeekTimes.filter((timestamp) => now - timestamp < FIVE_MINUTES_MS);

    if (!isForced && recentPeekTimes.length >= MAX_PEEKS_PER_WINDOW) {
        const nextAllowedAt = recentPeekTimes[0] + FIVE_MINUTES_MS;
        scheduleNextPeek(nextAllowedAt - now + randomBetween(6000, 24000));
        return;
    }

    const nextSideClass = PEEK_POSITION_CLASSES.includes(options.sideClass)
        ? options.sideClass
        : chooseNextSideClass();
    const nextEdgePosition = buildEdgePosition(nextSideClass);
    peekMascotElement.classList.add(PEEK_POSITIONING_CLASS);
    peekMascotElement.classList.remove(PEEK_VISIBLE_CLASS, ...PEEK_POSITION_CLASSES);
    peekMascotElement.classList.add(nextSideClass);
    peekMascotElement.style.setProperty("--peek-x", nextEdgePosition.x);
    peekMascotElement.style.setProperty("--peek-y", nextEdgePosition.y);
    lastSideClass = nextSideClass;

    if (!isForced) {
        recentPeekTimes.push(now);
    }

    // Commit the hidden position before enabling the slide-out transition.
    void peekMascotElement.offsetWidth;
    peekMascotElement.classList.remove(PEEK_POSITIONING_CLASS);

    window.requestAnimationFrame(() => {
        if (peekMascotElement instanceof HTMLElement) {
            peekMascotElement.classList.add(PEEK_VISIBLE_CLASS);
        }
    });

    hideTimerId = window.setTimeout(() => {
        hideTimerId = 0;
        animatePeekAway({ jump: false });
    }, PEEK_VISIBLE_MS);
}

function handlePeekMascotClick(event) {
    if (!(peekMascotElement instanceof HTMLElement) ||
        !peekMascotElement.classList.contains(PEEK_VISIBLE_CLASS)) {
        return;
    }

    event.preventDefault();
    clearLuisterPeekTimers();
    animatePeekAway({ jump: true });
}

function animatePeekAway(options = {}) {
    if (!(peekMascotElement instanceof HTMLElement)) {
        return;
    }

    const element = peekMascotElement;
    const computedStyle = window.getComputedStyle(element);
    const hiddenTransform = readPeekTransform(computedStyle, "--peek-hidden-transform", "translateX(-110%)");
    const visibleTransform = readPeekTransform(computedStyle, "--peek-visible-transform", hiddenTransform);
    const wiggleTransform = readPeekTransform(computedStyle, "--peek-wiggle-transform", visibleTransform);
    const jumpTransform = readPeekTransform(computedStyle, "--peek-jump-transform", wiggleTransform);
    const isJump = options.jump === true;

    cancelActivePeekAnimation();

    if (typeof element.animate !== "function") {
        element.classList.remove(PEEK_VISIBLE_CLASS);
        scheduleNextPeek(PEEK_TRANSITION_BUFFER_MS + randomBetween(NEXT_DELAY_MIN_MS, NEXT_DELAY_MAX_MS));
        return;
    }

    activePeekAnimation = element.animate(
        isJump
            ? [
                { transform: visibleTransform, opacity: 1, offset: 0 },
                { transform: jumpTransform, opacity: 1, offset: 0.34 },
                { transform: hiddenTransform, opacity: 0, offset: 1 }
            ]
            : [
                { transform: visibleTransform, opacity: 1, offset: 0 },
                { transform: wiggleTransform, opacity: 1, offset: 0.2 },
                { transform: visibleTransform, opacity: 1, offset: 0.42 },
                { transform: hiddenTransform, opacity: 0, offset: 1 }
            ],
        {
            duration: isJump ? PEEK_CLICK_JUMP_MS : PEEK_HIDE_WIGGLE_MS,
            easing: isJump ? "cubic-bezier(0.22, 1, 0.36, 1)" : "cubic-bezier(0.34, 1.56, 0.64, 1)",
            fill: "forwards"
        });

    activePeekAnimation.onfinish = () => {
        const completedAnimation = activePeekAnimation;
        if (activePeekAnimation) {
            activePeekAnimation = null;
        }

        if (element === peekMascotElement) {
            element.classList.add(PEEK_POSITIONING_CLASS);
            element.classList.remove(PEEK_VISIBLE_CLASS);
            completedAnimation?.cancel();
            void element.offsetWidth;
            element.classList.remove(PEEK_POSITIONING_CLASS);
            scheduleNextPeek(PEEK_TRANSITION_BUFFER_MS + randomBetween(NEXT_DELAY_MIN_MS, NEXT_DELAY_MAX_MS));
            return;
        }

        completedAnimation?.cancel();
    };

    activePeekAnimation.oncancel = () => {
        if (activePeekAnimation) {
            activePeekAnimation = null;
        }
    };
}

function readPeekTransform(computedStyle, propertyName, fallback) {
    const transform = computedStyle.getPropertyValue(propertyName).trim();
    return transform.length > 0 ? transform : fallback;
}

function cancelActivePeekAnimation() {
    if (activePeekAnimation === null) {
        return;
    }

    activePeekAnimation.cancel();
    activePeekAnimation = null;
}

function chooseNextSideClass() {
    const candidates = PEEK_POSITION_CLASSES.filter((positionClass) => positionClass !== lastSideClass);
    return candidates[Math.floor(Math.random() * candidates.length)] ?? PEEK_POSITION_CLASSES[0];
}

function normalizeRequestedSideClass(value) {
    if (typeof value !== "string") {
        return "";
    }

    const normalizedValue = value.trim();
    if (PEEK_POSITION_CLASSES.includes(normalizedValue)) {
        return normalizedValue;
    }

    return PEEK_POSITION_ALIASES.get(normalizedValue.toLowerCase()) ?? "";
}

function buildEdgePosition(sideClass) {
    if (sideClass === "peek-side-top" || sideClass === "peek-side-bottom") {
        return {
            x: `${randomBetween(18, 82)}vw`,
            y: "50svh"
        };
    }

    return {
        x: "50vw",
        y: `${randomBetween(22, 78)}svh`
    };
}

function randomBetween(minimumInclusive, maximumInclusive) {
    return Math.floor(Math.random() * (maximumInclusive - minimumInclusive + 1)) + minimumInclusive;
}

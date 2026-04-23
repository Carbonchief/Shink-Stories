const STORY_AUDIO_SELECTOR = ".story-audio";
const STORY_COVER_SELECTOR = ".story-cover";
const STORY_PAGE_SELECTOR = ".story-player-page";
const STORY_PLAYER_CONTENT_SELECTOR = ".story-player-content";
const DEFAULT_IMAGE_PATH = "/branding/schink-logo-text.png";
const ARTWORK_SIZES = ["96x96", "128x128", "192x192", "256x256", "384x384", "512x512"];
const STORY_PROGRESS_PREFIX = "schink:story-progress:";
const PROGRESS_SAVE_INTERVAL_MS = 4000;
const AUDIO_VOLUME_KEY = "schink:audio-volume";
const CUSTOM_PLAYER_SELECTOR = ".story-custom-player";
const PLAY_TOGGLE_SELECTOR = ".story-play-toggle";
const PLAY_ICON_SELECTOR = ".story-play-icon";
const STORY_PREV_BUTTON_SELECTOR = ".story-story-prev";
const STORY_NEXT_BUTTON_SELECTOR = ".story-story-next";
const STORY_NAV_PREV_SELECTOR = ".story-nav-prev";
const STORY_NAV_NEXT_SELECTOR = ".story-nav-next";
const PROGRESS_SLIDER_SELECTOR = ".story-progress-slider";
const CURRENT_TIME_SELECTOR = ".story-time-current";
const TOTAL_TIME_SELECTOR = ".story-time-total";
const VOLUME_SLIDER_SELECTOR = ".story-volume-slider";
const MUTE_TOGGLE_SELECTOR = ".story-mute-toggle";
const MUTE_ICON_SELECTOR = ".story-mute-icon";
const SPEED_TOGGLE_SELECTOR = ".story-speed-toggle";
const SPEED_LABEL_SELECTOR = ".story-speed-label";
const SHARE_TOGGLE_SELECTOR = ".story-share-toggle";
const SPEED_STEPS = [1, 1.25, 1.5, 0.8];
const STORY_TRACKING_ENDPOINT_PREFIX = "/api/stories/";
const NOTIFICATION_ENDPOINT = "/api/notifications?limit=10";
const NOTIFICATION_REFRESH_EVENT = "schink:notifications-refresh";
const CHARACTER_UNLOCK_NOTIFICATION_TYPE = "character_unlock";
const CHARACTER_UNLOCK_POPUP_STORAGE_KEY = "schink:pending-character-unlock-popup";
const LISTEN_FLUSH_THRESHOLD_SECONDS = 12;
const LISTEN_MAX_DELTA_SECONDS = 30;
const LISTEN_MAX_EVENT_SECONDS = 600;
const LISTEN_MIN_EVENT_SECONDS = 0.2;
const STORY_CAROUSEL_SELECTOR = ".story-carousel";
const STORY_CAROUSEL_DRAGGING_CLASS = "is-dragging";
const STORY_CAROUSEL_DRAG_THRESHOLD_PX = 8;
const STORY_CAROUSEL_CLICK_SUPPRESSION_MS = 350;
const STORY_CAROUSEL_MOUSE_POINTER_ID = -1;
const PENDING_FULLSCREEN_INTENT_KEY = "schink:pending-story-fullscreen-intent";
const FULLSCREEN_CHROME_HIDDEN_CLASS = "fullscreen-controls-hidden";
const FULLSCREEN_IDLE_TIMEOUT_MS = 2200;

const boundAudios = new WeakSet();
const fullscreenBindings = new WeakMap();
const playerCapabilityCache = new WeakMap();
const storyTrackingStateCache = new WeakMap();
const lastBoundAudioSource = new WeakMap();
const trackActionDotNetRefs = new WeakMap();
let suppressFullscreenExitCallbacks = 0;

function waitForNextAnimationFrame() {
    return new Promise((resolve) => {
        window.requestAnimationFrame(() => resolve());
    });
}

function setPendingStoryPlayerFullscreenIntent(isEnabled) {
    try {
        if (isEnabled) {
            window.sessionStorage.setItem(PENDING_FULLSCREEN_INTENT_KEY, "1");
            return;
        }

        window.sessionStorage.removeItem(PENDING_FULLSCREEN_INTENT_KEY);
    } catch {
        // Ignore session storage availability errors.
    }
}

function hasPendingStoryPlayerFullscreenIntent() {
    try {
        return window.sessionStorage.getItem(PENDING_FULLSCREEN_INTENT_KEY) === "1";
    } catch {
        return false;
    }
}

function shouldCarryFullscreenIntentForward() {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    return storyPage instanceof HTMLElement && storyPage.classList.contains("manual-fullscreen-focus");
}

function getActiveFullscreenElement() {
    return document.fullscreenElement
        ?? document.webkitFullscreenElement
        ?? null;
}

function getFullscreenRequestMethod(element) {
    if (!(element instanceof HTMLElement)) {
        return null;
    }

    if (typeof element.requestFullscreen === "function") {
        return "requestFullscreen";
    }

    if (typeof element.webkitRequestFullscreen === "function") {
        return "webkitRequestFullscreen";
    }

    return null;
}

function getFullscreenExitMethod() {
    if (typeof document.exitFullscreen === "function") {
        return "exitFullscreen";
    }

    if (typeof document.webkitExitFullscreen === "function") {
        return "webkitExitFullscreen";
    }

    if (typeof document.webkitCancelFullScreen === "function") {
        return "webkitCancelFullScreen";
    }

    return null;
}

function shouldUseNativeStoryPlayerFullscreen() {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (!(storyPage instanceof HTMLElement)) {
        return false;
    }

    const content = storyPage.querySelector(STORY_PLAYER_CONTENT_SELECTOR);
    const requestMethod = getFullscreenRequestMethod(content);
    if (!requestMethod) {
        return false;
    }

    const userAgent = navigator.userAgent || "";
    const isIPhoneOrIPod = /iPhone|iPod/i.test(userAgent);

    // Keep the CSS immersive fallback on iPhone/iPod where page-level fullscreen
    // support is still unreliable, but allow iPad/native Safari fullscreen when supported.
    return !isIPhoneOrIPod;
}

async function requestNativeFullscreen(element) {
    const requestMethod = getFullscreenRequestMethod(element);
    if (!requestMethod) {
        return false;
    }

    try {
        if (requestMethod === "requestFullscreen") {
            await element.requestFullscreen({ navigationUI: "hide" });
        } else {
            const result = element[requestMethod]();
            if (result && typeof result.then === "function") {
                await result;
            }
        }

        return true;
    } catch {
        if (requestMethod !== "requestFullscreen") {
            return false;
        }

        try {
            await element.requestFullscreen();
            return true;
        } catch {
            return false;
        }
    }
}

async function exitNativeFullscreen() {
    const exitMethod = getFullscreenExitMethod();
    if (!exitMethod) {
        return false;
    }

    try {
        const result = document[exitMethod]();
        if (result && typeof result.then === "function") {
            await result;
        }

        return true;
    } catch {
        return false;
    }
}

async function syncNativeStoryPlayerFullscreen(content, isEnabled) {
    if (!(content instanceof HTMLElement) || !shouldUseNativeStoryPlayerFullscreen()) {
        return;
    }

    const fullscreenElement = getActiveFullscreenElement();

    if (isEnabled) {
        try {
            if (fullscreenElement && fullscreenElement !== content) {
                suppressFullscreenExitCallbacks += 1;
                await exitNativeFullscreen();
                await waitForNextAnimationFrame();
            }

            if (!getActiveFullscreenElement()) {
                await requestNativeFullscreen(content);
            }
        } finally {
            if (suppressFullscreenExitCallbacks > 0) {
                suppressFullscreenExitCallbacks -= 1;
            }
        }

        return;
    }

    if (fullscreenElement) {
        await exitNativeFullscreen();
    }
}

function setFontAwesomeIcon(iconElement, iconClass) {
    if (!(iconElement instanceof HTMLElement)) {
        return;
    }

    iconElement.classList.add("fa-solid");

    const variantClasses = [
        "fa-play",
        "fa-pause",
        "fa-volume-high",
        "fa-volume-xmark"
    ];

    variantClasses.forEach((cssClass) => {
        if (cssClass !== iconClass) {
            iconElement.classList.remove(cssClass);
        }
    });

    iconElement.classList.add(iconClass);
}

function toAbsoluteUrl(url) {
    if (!url) {
        return new URL(DEFAULT_IMAGE_PATH, window.location.origin).toString();
    }

    try {
        return new URL(url, window.location.origin).toString();
    } catch {
        return new URL(DEFAULT_IMAGE_PATH, window.location.origin).toString();
    }
}

function createArtwork(imageUrl, imageType) {
    return ARTWORK_SIZES.map((size) => ({
        src: imageUrl,
        sizes: size,
        type: imageType || "image/jpeg"
    }));
}

function inferImageType(imageUrl, fallbackType) {
    if (fallbackType) {
        return fallbackType;
    }

    const cleaned = imageUrl.split("?")[0].toLowerCase();
    if (cleaned.endsWith(".png")) {
        return "image/png";
    }

    if (cleaned.endsWith(".webp")) {
        return "image/webp";
    }

    return "image/jpeg";
}

function getStoryCoverImage(audioElement) {
    const container = audioElement.closest(".story-player-content");
    if (!container) {
        return null;
    }

    const cover = container.querySelector(STORY_COVER_SELECTOR);
    return cover instanceof HTMLImageElement ? cover : null;
}

function resolveArtworkUrl(audioElement) {
    const coverImage = getStoryCoverImage(audioElement);
    if (coverImage) {
        const renderedSource = coverImage.currentSrc || coverImage.src;
        if (renderedSource) {
            return toAbsoluteUrl(renderedSource);
        }
    }

    return toAbsoluteUrl(audioElement.dataset.storyImage);
}

function updateMediaMetadata(audioElement) {
    if (!("mediaSession" in navigator)) {
        return;
    }

    const title = audioElement.dataset.storyTitle ?? "Schink Stories";
    const artist = audioElement.dataset.storyArtist ?? "Schink Stories";
    const imageUrl = resolveArtworkUrl(audioElement);
    const imageType = inferImageType(imageUrl, audioElement.dataset.storyImageType);

    try {
        navigator.mediaSession.metadata = new MediaMetadata({
            title,
            artist,
            album: "Schink Stories",
            artwork: createArtwork(imageUrl, imageType)
        });
    } catch {
        // Browser rejected metadata payload.
    }

    navigator.mediaSession.playbackState = audioElement.paused ? "paused" : "playing";
}

function updatePositionState(audioElement) {
    if (!("mediaSession" in navigator) || typeof navigator.mediaSession.setPositionState !== "function") {
        return;
    }

    if (!Number.isFinite(audioElement.duration) || audioElement.duration <= 0) {
        return;
    }

    try {
        navigator.mediaSession.setPositionState({
            duration: audioElement.duration,
            playbackRate: audioElement.playbackRate,
            position: audioElement.currentTime
        });
    } catch {
        // Ignore position update errors.
    }
}

function persistAudioState(audioElement, eventType, useKeepalive) {
    const trackingState = storyTrackingStateCache.get(audioElement);
    if (trackingState) {
        captureListenDelta(trackingState);
        flushStoryListen(audioElement, trackingState, eventType, true, useKeepalive);
        stopListenTimer(trackingState);
    }

    saveStoryProgress(audioElement);
    saveVolumeState(audioElement);
    audioElement.pause();
}

async function tryInvokeTrackAction(audioElement, methodName) {
    const dotNetRef = trackActionDotNetRefs.get(audioElement);
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== "function") {
        return false;
    }

    try {
        await dotNetRef.invokeMethodAsync(methodName);
        return true;
    } catch {
        return false;
    }
}

function bindActionHandlers(audioElement, resolveTrackNavigation) {
    const requestPreviousTrack = async () => {
        if (await tryInvokeTrackAction(audioElement, "HandleJsPreviousTrackRequest")) {
            return;
        }

        const navigation = typeof resolveTrackNavigation === "function" ? resolveTrackNavigation() : null;
        if (navigation?.previous instanceof HTMLAnchorElement) {
            setPendingStoryPlayerFullscreenIntent(shouldCarryFullscreenIntentForward());
            navigation.previous.click();
        }
    };

    const requestNextTrack = async () => {
        if (await tryInvokeTrackAction(audioElement, "HandleJsNextTrackRequest")) {
            return;
        }

        const navigation = typeof resolveTrackNavigation === "function" ? resolveTrackNavigation() : null;
        if (navigation?.next instanceof HTMLAnchorElement) {
            setPendingStoryPlayerFullscreenIntent(shouldCarryFullscreenIntentForward());
            navigation.next.click();
        }
    };

    if (!("mediaSession" in navigator)) {
        return {
            requestPreviousTrack,
            requestNextTrack
        };
    }

    const setHandler = (action, handler) => {
        try {
            navigator.mediaSession.setActionHandler(action, handler);
        } catch {
            // Ignore unsupported media session actions.
        }
    };

    setHandler("play", async () => {
        await audioElement.play();
    });

    setHandler("pause", () => {
        audioElement.pause();
    });

    setHandler("seekbackward", (event) => {
        const offset = event.seekOffset ?? 10;
        audioElement.currentTime = Math.max(audioElement.currentTime - offset, 0);
    });

    setHandler("seekforward", (event) => {
        const offset = event.seekOffset ?? 10;
        const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : Number.MAX_VALUE;
        audioElement.currentTime = Math.min(audioElement.currentTime + offset, duration);
    });

    setHandler("seekto", (event) => {
        if (typeof event.seekTime === "number") {
            audioElement.currentTime = event.seekTime;
        }
    });

    setHandler("previoustrack", requestPreviousTrack);
    setHandler("nexttrack", requestNextTrack);

    return {
        requestPreviousTrack,
        requestNextTrack
    };
}

function detectPlayerCapabilities(audioElement) {
    const cached = playerCapabilityCache.get(audioElement);
    if (cached) {
        return cached;
    }

    let canControlVolume = true;
    const originalVolume = Number.isFinite(audioElement.volume) ? audioElement.volume : 1;
    const probeVolume = originalVolume >= 0.5 ? 0.25 : 0.75;

    try {
        audioElement.volume = probeVolume;
        canControlVolume = Math.abs(audioElement.volume - probeVolume) < 0.01;
    } catch {
        canControlVolume = false;
    } finally {
        try {
            audioElement.volume = originalVolume;
        } catch {
            // Ignore volume reset failures.
        }
    }

    let canChangePlaybackRate = true;
    const originalRate = Number.isFinite(audioElement.playbackRate) ? audioElement.playbackRate : 1;
    const probeRate = Math.abs(originalRate - 1.25) < 0.001 ? 1.5 : 1.25;

    try {
        audioElement.playbackRate = probeRate;
        canChangePlaybackRate = Math.abs(audioElement.playbackRate - probeRate) < 0.001;
    } catch {
        canChangePlaybackRate = false;
    } finally {
        try {
            audioElement.playbackRate = originalRate;
        } catch {
            // Ignore playback-rate reset failures.
        }
    }

    const capabilities = { canControlVolume, canChangePlaybackRate };
    playerCapabilityCache.set(audioElement, capabilities);
    return capabilities;
}

function getProgressKey(audioElement) {
    const slug = (audioElement.dataset.storySlug || "").trim().toLowerCase();
    return slug ? `${STORY_PROGRESS_PREFIX}${slug}` : null;
}

function loadStoryProgress(audioElement) {
    const key = getProgressKey(audioElement);
    if (!key) {
        return null;
    }

    try {
        const raw = window.localStorage.getItem(key);
        if (!raw) {
            return null;
        }

        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.position !== "number") {
            return null;
        }

        return parsed.position;
    } catch {
        return null;
    }
}

function saveStoryProgress(audioElement) {
    const key = getProgressKey(audioElement);
    if (!key) {
        return;
    }

    if (!Number.isFinite(audioElement.currentTime) || audioElement.currentTime <= 0) {
        return;
    }

    const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : null;
    const payload = {
        position: audioElement.currentTime,
        duration,
        savedAtUtc: Date.now()
    };

    try {
        window.localStorage.setItem(key, JSON.stringify(payload));
    } catch {
        // Ignore storage quota and privacy-mode errors.
    }
}

function clearStoryProgress(audioElement) {
    const key = getProgressKey(audioElement);
    if (!key) {
        return;
    }

    try {
        window.localStorage.removeItem(key);
    } catch {
        // Ignore storage errors.
    }
}

function restoreStoryProgress(audioElement) {
    const savedPosition = loadStoryProgress(audioElement);
    if (typeof savedPosition !== "number" || !Number.isFinite(savedPosition)) {
        return;
    }

    const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : null;
    if (duration !== null) {
        // Ignore resume values that are effectively at the end.
        if (savedPosition >= duration - 2.5) {
            clearStoryProgress(audioElement);
            return;
        }

        audioElement.currentTime = Math.max(0, Math.min(savedPosition, duration - 0.5));
        return;
    }

    audioElement.currentTime = Math.max(0, savedPosition);
}

function loadSavedVolumeState() {
    try {
        const raw = window.localStorage.getItem(AUDIO_VOLUME_KEY);
        if (!raw) {
            return null;
        }

        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.volume !== "number" || typeof parsed.muted !== "boolean") {
            return null;
        }

        return parsed;
    } catch {
        return null;
    }
}

function saveVolumeState(audioElement) {
    const volume = Number.isFinite(audioElement.volume)
        ? Math.max(0, Math.min(1, audioElement.volume))
        : 1;

    const payload = {
        volume,
        muted: Boolean(audioElement.muted)
    };

    try {
        window.localStorage.setItem(AUDIO_VOLUME_KEY, JSON.stringify(payload));
    } catch {
        // Ignore storage quota and privacy-mode errors.
    }
}

function restoreVolumeState(audioElement) {
    const saved = loadSavedVolumeState();
    if (!saved) {
        return;
    }

    audioElement.volume = Math.max(0, Math.min(1, saved.volume));
    audioElement.muted = saved.muted;
}

function formatTime(seconds) {
    if (!Number.isFinite(seconds) || seconds < 0) {
        return "0:00";
    }

    const whole = Math.floor(seconds);
    const mins = Math.floor(whole / 60);
    const secs = whole % 60;
    return `${mins}:${String(secs).padStart(2, "0")}`;
}

async function copyTextToClipboard(text) {
    if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Fall through to legacy copy method.
        }
    }

    const tempInput = document.createElement("textarea");
    tempInput.value = text;
    tempInput.setAttribute("readonly", "");
    tempInput.style.position = "absolute";
    tempInput.style.left = "-9999px";
    document.body.append(tempInput);
    tempInput.select();

    let copied = false;
    try {
        copied = document.execCommand("copy");
    } catch {
        copied = false;
    }

    tempInput.remove();
    return copied;
}

function playAudioSafely(audioElement) {
    try {
        const playPromise = audioElement.play();
        if (playPromise && typeof playPromise.catch === "function") {
            playPromise.catch(() => {
                // Ignore blocked autoplay/playback errors.
            });
        }
    } catch {
        // Ignore blocked autoplay/playback errors.
    }
}

function shouldAutoplayOnSourceChange(audioElement) {
    return audioElement.dataset.autoplayRequested === "true" || audioElement.autoplay;
}

function queueAutoplayAfterSourceChange(audioElement) {
    audioElement.dataset.autoplayRequested = "false";

    const handleCanPlay = () => {
        playAudioSafely(audioElement);
    };

    audioElement.addEventListener("canplay", handleCanPlay, { once: true });
}

function shouldAutoplayNextTrack(audioElement) {
    const container = audioElement.closest(".story-player-content");
    if (!(container instanceof HTMLElement)) {
        return false;
    }

    return container.dataset.autoplayEnabled === "true";
}

function showShareFeedback(shareToggle, message) {
    if (!(shareToggle instanceof HTMLButtonElement)) {
        return;
    }

    const defaultLabel = shareToggle.dataset.defaultShareLabel || "Deel storie";
    const defaultTitle = shareToggle.dataset.defaultShareTitle || "Deel storie";
    shareToggle.dataset.defaultShareLabel = defaultLabel;
    shareToggle.dataset.defaultShareTitle = defaultTitle;
    shareToggle.setAttribute("aria-label", message);
    shareToggle.setAttribute("title", message);

    window.setTimeout(() => {
        shareToggle.setAttribute("aria-label", defaultLabel);
        shareToggle.setAttribute("title", defaultTitle);
    }, 1800);
}

function resolveStorySource(pathname) {
    if (typeof pathname !== "string") {
        return "unknown";
    }

    if (pathname.startsWith("/gratis/")) {
        return "gratis";
    }

    if (pathname.startsWith("/luister/")) {
        return "luister";
    }

    return "unknown";
}

function resolveReferrerPath() {
    if (!document.referrer) {
        return null;
    }

    try {
        const referrerUrl = new URL(document.referrer);
        if (referrerUrl.origin !== window.location.origin) {
            return null;
        }

        const localPath = `${referrerUrl.pathname}${referrerUrl.search}`;
        return localPath || null;
    } catch {
        return null;
    }
}

function createUuidV4Fallback() {
    const bytes = new Uint8Array(16);

    if (window.crypto && typeof window.crypto.getRandomValues === "function") {
        window.crypto.getRandomValues(bytes);
    } else {
        for (let index = 0; index < bytes.length; index += 1) {
            bytes[index] = Math.floor(Math.random() * 256);
        }
    }

    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, "0"));
    return `${hex[0]}${hex[1]}${hex[2]}${hex[3]}-${hex[4]}${hex[5]}-${hex[6]}${hex[7]}-${hex[8]}${hex[9]}-${hex[10]}${hex[11]}${hex[12]}${hex[13]}${hex[14]}${hex[15]}`;
}

function createTrackingSessionId() {
    if (window.crypto && typeof window.crypto.randomUUID === "function") {
        return window.crypto.randomUUID();
    }

    return createUuidV4Fallback();
}

function buildStoryTrackingState(audioElement) {
    const slug = (audioElement.dataset.storySlug || "").trim().toLowerCase();
    if (!slug) {
        return null;
    }

    const storyPath = window.location.pathname || "/";
    return {
        slug,
        source: resolveStorySource(storyPath),
        storyPath,
        sessionId: createTrackingSessionId(),
        referrerPath: resolveReferrerPath(),
        viewTracked: false,
        pendingListenSeconds: 0,
        lastTickAtMs: null,
        sendInFlight: false
    };
}

async function postStoryTracking(slug, endpointSuffix, payload, useKeepalive) {
    try {
        const response = await fetch(`${STORY_TRACKING_ENDPOINT_PREFIX}${encodeURIComponent(slug)}/${endpointSuffix}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            credentials: "same-origin",
            keepalive: Boolean(useKeepalive),
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            return null;
        }

        const responseContentType = response.headers.get("content-type") || "";
        if (!responseContentType.toLowerCase().includes("application/json")) {
            return null;
        }

        return await response.json();
    } catch {
        // Ignore network errors to avoid breaking playback controls.
        return null;
    }
}

function normalizeNotificationType(notificationType) {
    return typeof notificationType === "string" && notificationType.length > 0
        ? notificationType.trim().toLowerCase()
        : "";
}

async function fetchLatestCharacterUnlockNotification() {
    try {
        const response = await fetch(NOTIFICATION_ENDPOINT, {
            method: "GET",
            credentials: "same-origin",
            headers: {
                Accept: "application/json"
            }
        });

        if (!response.ok) {
            return null;
        }

        const responseContentType = response.headers.get("content-type") || "";
        if (!responseContentType.toLowerCase().includes("application/json")) {
            return null;
        }

        const payload = await response.json();
        const notifications = Array.isArray(payload?.notifications) ? payload.notifications : [];
        return notifications.find((notification) =>
            normalizeNotificationType(notification?.type) === CHARACTER_UNLOCK_NOTIFICATION_TYPE) ?? null;
    } catch {
        return null;
    }
}

function storePendingCharacterUnlockPopup(notification) {
    if (!notification || typeof notification !== "object") {
        return;
    }

    const popupPayload = {
        type: CHARACTER_UNLOCK_NOTIFICATION_TYPE,
        imagePath: typeof notification.imagePath === "string" && notification.imagePath.length > 0
            ? notification.imagePath
            : "/branding/schink-logo-text.png",
        imageAlt: typeof notification.imageAlt === "string" && notification.imageAlt.length > 0
            ? notification.imageAlt
            : "Karakter illustrasie",
        href: typeof notification.href === "string" && notification.href.length > 0
            ? notification.href
            : "/karakters",
        title: typeof notification.title === "string" && notification.title.length > 0
            ? notification.title
            : "Nuwe karakter oopgesluit",
        body: typeof notification.body === "string" && notification.body.length > 0
            ? notification.body
            : "Jou nuwe karakter wag vir jou op die Karakter blad.",
        createdAt: typeof notification.createdAt === "string" ? notification.createdAt : null
    };

    try {
        window.sessionStorage.setItem(CHARACTER_UNLOCK_POPUP_STORAGE_KEY, JSON.stringify(popupPayload));
    } catch {
        // Ignore session storage availability errors.
    }
}

function capturePendingCharacterUnlockPopup() {
    // Write a minimal payload immediately so fast navigation back to /luister
    // still has something to display; enrich it asynchronously if available.
    storePendingCharacterUnlockPopup({
        href: "/karakters"
    });

    void (async () => {
        const notification = await fetchLatestCharacterUnlockNotification();
        if (!notification) {
            return;
        }

        storePendingCharacterUnlockPopup(notification);
    })();
}

function trackStoryView(trackingState) {
    if (!trackingState || trackingState.viewTracked) {
        return;
    }

    trackingState.viewTracked = true;
    void postStoryTracking(trackingState.slug, "view", {
        storyPath: trackingState.storyPath,
        source: trackingState.source,
        referrerPath: trackingState.referrerPath
    }, false);
}

function captureListenDelta(trackingState) {
    if (!trackingState || trackingState.lastTickAtMs === null) {
        return;
    }

    const nowMs = Date.now();
    const elapsedSeconds = (nowMs - trackingState.lastTickAtMs) / 1000;
    trackingState.lastTickAtMs = nowMs;

    if (!Number.isFinite(elapsedSeconds) || elapsedSeconds <= 0 || elapsedSeconds > LISTEN_MAX_DELTA_SECONDS) {
        return;
    }

    trackingState.pendingListenSeconds += elapsedSeconds;
}

function flushStoryListen(audioElement, trackingState, eventType, force, useKeepalive) {
    if (!trackingState || trackingState.sendInFlight) {
        return;
    }

    const pendingSeconds = trackingState.pendingListenSeconds;
    const shouldFlush = force || pendingSeconds >= LISTEN_FLUSH_THRESHOLD_SECONDS;
    if (!shouldFlush || pendingSeconds < LISTEN_MIN_EVENT_SECONDS) {
        return;
    }

    trackingState.pendingListenSeconds = 0;
    trackingState.sendInFlight = true;

    const listenedSeconds = Math.min(pendingSeconds, LISTEN_MAX_EVENT_SECONDS);
    const positionSeconds = Number.isFinite(audioElement.currentTime) ? audioElement.currentTime : null;
    const durationSeconds = Number.isFinite(audioElement.duration) && audioElement.duration > 0
        ? audioElement.duration
        : null;

    const payload = {
        storyPath: trackingState.storyPath,
        source: trackingState.source,
        sessionId: trackingState.sessionId,
        eventType,
        listenedSeconds: Number(listenedSeconds.toFixed(3)),
        positionSeconds: positionSeconds === null ? null : Number(positionSeconds.toFixed(3)),
        durationSeconds: durationSeconds === null ? null : Number(durationSeconds.toFixed(3)),
        isCompleted: eventType === "ended"
    };

    void postStoryTracking(trackingState.slug, "listen", payload, useKeepalive)
        .then((result) => {
            if (!result || !Number.isFinite(result.newNotificationsCreated) || result.newNotificationsCreated <= 0) {
                return;
            }

            void capturePendingCharacterUnlockPopup();

            window.dispatchEvent(new CustomEvent(NOTIFICATION_REFRESH_EVENT, {
                detail: {
                    source: "story-listen",
                    count: result.newNotificationsCreated
                }
            }));
        })
        .finally(() => {
            trackingState.sendInFlight = false;
        });
}

function startListenTimer(trackingState) {
    if (!trackingState) {
        return;
    }

    trackingState.lastTickAtMs = Date.now();
}

function stopListenTimer(trackingState) {
    if (!trackingState) {
        return;
    }

    trackingState.lastTickAtMs = null;
}

async function shareStoryFromPlayer(audioElement, shareToggle) {
    const shareTitle = audioElement.dataset.storyTitle || "Schink Stories";
    const shareUrl = audioElement.dataset.shareUrl || window.location.href;

    if (typeof navigator.share === "function") {
        try {
            await navigator.share({
                title: shareTitle,
                text: shareTitle,
                url: shareUrl
            });
            return;
        } catch (error) {
            if (error instanceof DOMException && error.name === "AbortError") {
                return;
            }
            // Fall through to clipboard fallback.
        }
    }

    const copied = await copyTextToClipboard(shareUrl);
    if (copied) {
        showShareFeedback(shareToggle, "Skakel gekopieer");
    }
}

function getCustomPlayerElements(audioElement) {
    const container = audioElement.closest(".story-player-content");
    if (!(container instanceof HTMLElement)) {
        return null;
    }

    const customPlayer = container.querySelector(CUSTOM_PLAYER_SELECTOR);
    if (!(customPlayer instanceof HTMLElement)) {
        return null;
    }

    return {
        container,
        customPlayer,
        playToggle: customPlayer.querySelector(PLAY_TOGGLE_SELECTOR),
        playIcon: customPlayer.querySelector(PLAY_ICON_SELECTOR),
        storyPrevButton: customPlayer.querySelector(STORY_PREV_BUTTON_SELECTOR),
        storyNextButton: customPlayer.querySelector(STORY_NEXT_BUTTON_SELECTOR),
        progressSlider: customPlayer.querySelector(PROGRESS_SLIDER_SELECTOR),
        currentTime: customPlayer.querySelector(CURRENT_TIME_SELECTOR),
        totalTime: customPlayer.querySelector(TOTAL_TIME_SELECTOR),
        volumeSlider: customPlayer.querySelector(VOLUME_SLIDER_SELECTOR),
        muteToggle: customPlayer.querySelector(MUTE_TOGGLE_SELECTOR),
        muteIcon: customPlayer.querySelector(MUTE_ICON_SELECTOR),
        speedToggle: customPlayer.querySelector(SPEED_TOGGLE_SELECTOR),
        speedLabel: customPlayer.querySelector(SPEED_LABEL_SELECTOR),
        shareToggle: customPlayer.querySelector(SHARE_TOGGLE_SELECTOR)
    };
}

function updateRangeVisual(slider, percentage) {
    if (!(slider instanceof HTMLInputElement)) {
        return;
    }

    const clamped = Math.max(0, Math.min(100, percentage));
    slider.style.background = `linear-gradient(to right, #ffffff 0%, #ffffff ${clamped}%, rgba(242,247,251,0.28) ${clamped}%, rgba(242,247,251,0.28) 100%)`;
}

function stopAndReleaseAudio(audioElement) {
    persistAudioState(audioElement, "pagehide", true);

    const sources = audioElement.querySelectorAll("source");
    sources.forEach((source) => {
        source.setAttribute("src", "");
    });

    audioElement.removeAttribute("src");
    audioElement.load();
    lastBoundAudioSource.set(audioElement, "");
}

function bindAudioEvents(audioElement, dotNetRef) {
    const declaredSource = (audioElement.getAttribute("src") || "").trim();
    const lastDeclaredSource = lastBoundAudioSource.get(audioElement) || "";

    if (dotNetRef) {
        trackActionDotNetRefs.set(audioElement, dotNetRef);
    }

    if (boundAudios.has(audioElement)) {
        if (declaredSource && declaredSource !== lastDeclaredSource) {
            lastBoundAudioSource.set(audioElement, declaredSource);
            const shouldAutoplay = shouldAutoplayOnSourceChange(audioElement);
            if (shouldAutoplay) {
                queueAutoplayAfterSourceChange(audioElement);
            }

            try {
                audioElement.load();
            } catch {
                // Ignore browser-specific load failures.
            }
        } else if (!declaredSource && lastDeclaredSource) {
            lastBoundAudioSource.set(audioElement, "");
        }

        updateMediaMetadata(audioElement);
        updatePositionState(audioElement);
        return;
    }

    boundAudios.add(audioElement);

    if (declaredSource) {
        lastBoundAudioSource.set(audioElement, declaredSource);
    }

    audioElement.setAttribute("playsinline", "");
    audioElement.setAttribute("webkit-playsinline", "true");

    const customPlayerElements = getCustomPlayerElements(audioElement);
    const playerCapabilities = detectPlayerCapabilities(audioElement);
    const trackingState = buildStoryTrackingState(audioElement);
    if (trackingState) {
        storyTrackingStateCache.set(audioElement, trackingState);
        trackStoryView(trackingState);
    }

    if (customPlayerElements?.container instanceof HTMLElement) {
        customPlayerElements.container.classList.add("story-player-enhanced");
    }

    const updateCustomPlayerState = () => {
        if (!customPlayerElements) {
            return;
        }

        const prevStoryLink = customPlayerElements.container.querySelector(STORY_NAV_PREV_SELECTOR);
        const nextStoryLink = customPlayerElements.container.querySelector(STORY_NAV_NEXT_SELECTOR);
        const isPlaying = !audioElement.paused;
        const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : 0;
        const currentTime = Number.isFinite(audioElement.currentTime) ? audioElement.currentTime : 0;
        const progress = duration > 0 ? (currentTime / duration) * 100 : 0;
        const volumeValue = audioElement.muted ? 0 : Math.max(0, Math.min(1, audioElement.volume));
        const volumeProgress = volumeValue * 100;

        if (customPlayerElements.customPlayer instanceof HTMLElement) {
            customPlayerElements.customPlayer.classList.toggle("volume-unsupported", !playerCapabilities.canControlVolume);
            customPlayerElements.customPlayer.classList.toggle("speed-unsupported", !playerCapabilities.canChangePlaybackRate);
        }

        if (customPlayerElements.storyPrevButton instanceof HTMLButtonElement) {
            customPlayerElements.storyPrevButton.disabled = !(prevStoryLink instanceof HTMLAnchorElement);
        }

        if (customPlayerElements.storyNextButton instanceof HTMLButtonElement) {
            customPlayerElements.storyNextButton.disabled = !(nextStoryLink instanceof HTMLAnchorElement);
        }

        if (customPlayerElements.playToggle instanceof HTMLButtonElement) {
            customPlayerElements.playToggle.classList.toggle("is-playing", isPlaying);
            customPlayerElements.playToggle.setAttribute("aria-label", isPlaying ? "Pouse storie" : "Speel storie");
            customPlayerElements.playToggle.setAttribute("title", isPlaying ? "Pouse" : "Speel");
        }

        if (customPlayerElements.playIcon instanceof HTMLElement) {
            setFontAwesomeIcon(customPlayerElements.playIcon, isPlaying ? "fa-pause" : "fa-play");
        }

        if (customPlayerElements.progressSlider instanceof HTMLInputElement) {
            customPlayerElements.progressSlider.max = duration > 0 ? String(duration) : "100";
            customPlayerElements.progressSlider.value = duration > 0 ? String(currentTime) : "0";
            updateRangeVisual(customPlayerElements.progressSlider, progress);
        }

        if (customPlayerElements.currentTime instanceof HTMLElement) {
            customPlayerElements.currentTime.textContent = formatTime(currentTime);
        }

        if (customPlayerElements.totalTime instanceof HTMLElement) {
            customPlayerElements.totalTime.textContent = formatTime(duration);
        }

        if (customPlayerElements.volumeSlider instanceof HTMLInputElement) {
            customPlayerElements.volumeSlider.value = String(volumeValue);
            updateRangeVisual(customPlayerElements.volumeSlider, volumeProgress);
            customPlayerElements.volumeSlider.disabled = !playerCapabilities.canControlVolume;
            customPlayerElements.volumeSlider.setAttribute("aria-disabled", String(!playerCapabilities.canControlVolume));
        }

        if (customPlayerElements.muteIcon instanceof HTMLElement) {
            setFontAwesomeIcon(
                customPlayerElements.muteIcon,
                audioElement.muted || volumeValue <= 0.001 ? "fa-volume-xmark" : "fa-volume-high");
        }

        if (customPlayerElements.speedToggle instanceof HTMLButtonElement) {
            customPlayerElements.speedToggle.disabled = !playerCapabilities.canChangePlaybackRate;
            customPlayerElements.speedToggle.setAttribute("aria-disabled", String(!playerCapabilities.canChangePlaybackRate));
        }

        if (customPlayerElements.speedLabel instanceof HTMLElement) {
            customPlayerElements.speedLabel.textContent = `${audioElement.playbackRate.toFixed(2).replace(/\.00$/, ".0")}x`;
        }
    };

    const updateAll = () => {
        updateMediaMetadata(audioElement);
        updatePositionState(audioElement);
        updateCustomPlayerState();
    };

    let lastProgressSaveAt = 0;
    const maybeSaveProgress = () => {
        const now = Date.now();
        if (now - lastProgressSaveAt < PROGRESS_SAVE_INTERVAL_MS) {
            return;
        }

        lastProgressSaveAt = now;
        saveStoryProgress(audioElement);
    };

    const resolveTrackNavigation = () => {
        if (!customPlayerElements) {
            return { previous: null, next: null };
        }

        const previous = customPlayerElements.container.querySelector(STORY_NAV_PREV_SELECTOR);
        const next = customPlayerElements.container.querySelector(STORY_NAV_NEXT_SELECTOR);

        return {
            previous: previous instanceof HTMLAnchorElement ? previous : null,
            next: next instanceof HTMLAnchorElement ? next : null
        };
    };

    const trackActions = bindActionHandlers(audioElement, resolveTrackNavigation);
    restoreVolumeState(audioElement);

    if (declaredSource && shouldAutoplayOnSourceChange(audioElement)) {
        queueAutoplayAfterSourceChange(audioElement);
    }

    if (customPlayerElements) {
        if (customPlayerElements.playToggle instanceof HTMLButtonElement) {
            customPlayerElements.playToggle.addEventListener("click", async () => {
                if (audioElement.paused) {
                    playAudioSafely(audioElement);
                    return;
                }

                audioElement.pause();
            });
        }

        if (customPlayerElements.storyPrevButton instanceof HTMLButtonElement) {
            customPlayerElements.storyPrevButton.addEventListener("click", async () => {
                if (trackActions?.requestPreviousTrack) {
                    await trackActions.requestPreviousTrack();
                }
            });
        }

        if (customPlayerElements.storyNextButton instanceof HTMLButtonElement) {
            customPlayerElements.storyNextButton.addEventListener("click", async () => {
                if (trackActions?.requestNextTrack) {
                    await trackActions.requestNextTrack();
                }
            });
        }

        if (customPlayerElements.progressSlider instanceof HTMLInputElement) {
            customPlayerElements.progressSlider.addEventListener("input", () => {
                const nextTime = Number.parseFloat(customPlayerElements.progressSlider.value);
                if (!Number.isFinite(nextTime)) {
                    return;
                }

                audioElement.currentTime = Math.max(0, nextTime);
            });
        }

        if (customPlayerElements.volumeSlider instanceof HTMLInputElement) {
            customPlayerElements.volumeSlider.addEventListener("input", () => {
                if (!playerCapabilities.canControlVolume) {
                    return;
                }

                const nextVolume = Number.parseFloat(customPlayerElements.volumeSlider.value);
                const clamped = Number.isFinite(nextVolume) ? Math.max(0, Math.min(1, nextVolume)) : 1;
                audioElement.volume = clamped;
                audioElement.muted = clamped <= 0.001;
            });
        }

        if (customPlayerElements.muteToggle instanceof HTMLButtonElement) {
            customPlayerElements.muteToggle.addEventListener("click", () => {
                audioElement.muted = !audioElement.muted;
                if (!audioElement.muted && audioElement.volume <= 0.001) {
                    audioElement.volume = 0.6;
                }
            });
        }

        if (customPlayerElements.speedToggle instanceof HTMLButtonElement) {
            customPlayerElements.speedToggle.addEventListener("click", () => {
                if (!playerCapabilities.canChangePlaybackRate) {
                    return;
                }

                const currentIndex = SPEED_STEPS.findIndex((rate) => Math.abs(rate - audioElement.playbackRate) < 0.001);
                const nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SPEED_STEPS.length;
                audioElement.playbackRate = SPEED_STEPS[nextIndex];
                updateCustomPlayerState();
            });
        }

        if (customPlayerElements.shareToggle instanceof HTMLButtonElement) {
            customPlayerElements.shareToggle.addEventListener("click", async () => {
                await shareStoryFromPlayer(audioElement, customPlayerElements.shareToggle);
            });
        }
    }

    const coverImage = getStoryCoverImage(audioElement);
    if (coverImage) {
        const coverWrap = coverImage.closest(".story-cover-wrap");
        const coverPlayIcon = coverWrap instanceof HTMLElement
            ? coverWrap.querySelector(".story-cover-play-icon")
            : null;

        const togglePlayback = async () => {
            if (audioElement.paused) {
                playAudioSafely(audioElement);
                return;
            }

            audioElement.pause();
        };

        const updateCoverState = () => {
            const isPlaying = !audioElement.paused;
            coverImage.setAttribute("aria-pressed", String(isPlaying));
            coverImage.setAttribute("aria-label", isPlaying ? "Pouse storie" : "Speel storie");
            coverImage.setAttribute("title", isPlaying ? "Pouse storie" : "Speel storie");

            if (coverWrap instanceof HTMLElement) {
                coverWrap.classList.toggle("is-playing", isPlaying);
            }

            if (coverPlayIcon instanceof HTMLElement) {
                setFontAwesomeIcon(coverPlayIcon, isPlaying ? "fa-pause" : "fa-play");
            }
        };

        coverImage.addEventListener("click", (event) => {
            event.preventDefault();
            void togglePlayback();
        });

        coverImage.addEventListener("keydown", (event) => {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            event.preventDefault();
            void togglePlayback();
        });

        coverImage.addEventListener("load", updateAll);
        audioElement.addEventListener("play", updateCoverState);
        audioElement.addEventListener("pause", updateCoverState);
        audioElement.addEventListener("ended", updateCoverState);
        updateCoverState();
    }

    audioElement.addEventListener("loadedmetadata", () => {
        restoreStoryProgress(audioElement);
        updateAll();
    });
    audioElement.addEventListener("play", () => {
        if (trackingState) {
            trackStoryView(trackingState);
            startListenTimer(trackingState);
        }

        updateAll();
    });
    audioElement.addEventListener("pause", () => {
        if (trackingState) {
            captureListenDelta(trackingState);
            flushStoryListen(audioElement, trackingState, "pause", true, false);
            stopListenTimer(trackingState);
        }

        updateAll();
        saveStoryProgress(audioElement);
    });
    audioElement.addEventListener("timeupdate", () => {
        if (trackingState && !audioElement.paused) {
            captureListenDelta(trackingState);
            flushStoryListen(audioElement, trackingState, "progress", false, false);
        }

        updatePositionState(audioElement);
        updateCustomPlayerState();
        maybeSaveProgress();
    });
    audioElement.addEventListener("ratechange", () => {
        updatePositionState(audioElement);
        updateCustomPlayerState();
    });
    audioElement.addEventListener("seeked", () => {
        updatePositionState(audioElement);
        updateCustomPlayerState();
        saveStoryProgress(audioElement);
    });
    audioElement.addEventListener("ended", () => {
        if (trackingState) {
            captureListenDelta(trackingState);
            flushStoryListen(audioElement, trackingState, "ended", true, false);
            stopListenTimer(trackingState);
        }

        clearStoryProgress(audioElement);
        updateAll();

        if (shouldAutoplayNextTrack(audioElement)) {
            void (async () => {
                if (await tryInvokeTrackAction(audioElement, "HandleJsAutoplayNextTrackRequest")) {
                    return;
                }

                if (trackActions?.requestNextTrack) {
                    await trackActions.requestNextTrack();
                }
            })();
        }
    });
    audioElement.addEventListener("volumechange", () => {
        saveVolumeState(audioElement);
        updateCustomPlayerState();
    });

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "hidden") {
            if (trackingState) {
                captureListenDelta(trackingState);
                flushStoryListen(audioElement, trackingState, "visibilityhidden", true, true);
                stopListenTimer(trackingState);
            }

            saveStoryProgress(audioElement);
            saveVolumeState(audioElement);
            return;
        }

        if (trackingState && !audioElement.paused) {
            startListenTimer(trackingState);
        }
    });

    window.addEventListener("pagehide", () => {
        if (trackingState) {
            captureListenDelta(trackingState);
            flushStoryListen(audioElement, trackingState, "pagehide", true, true);
            stopListenTimer(trackingState);
        }

        saveStoryProgress(audioElement);
        saveVolumeState(audioElement);
    });

    updateAll();
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

function initializeStoryCarousels(root = document) {
    const carousels = root.querySelectorAll(STORY_CAROUSEL_SELECTOR);
    for (let index = 0; index < carousels.length; index += 1) {
        wireStoryCarouselDrag(carousels[index]);
    }
}

export function initializeStoryMediaSession(dotNetRef) {
    initializeStoryCarousels();

    const audioElement = document.querySelector(STORY_AUDIO_SELECTOR);
    if (!(audioElement instanceof HTMLAudioElement)) {
        return;
    }

    bindAudioEvents(audioElement, dotNetRef);
}

export function stopStoryAudioPlayback() {
    const audioElement = document.querySelector(STORY_AUDIO_SELECTOR);
    if (!(audioElement instanceof HTMLAudioElement)) {
        return;
    }

    stopAndReleaseAudio(audioElement);
}

export function prepareStoryAudioForSourceSwap() {
    const audioElement = document.querySelector(STORY_AUDIO_SELECTOR);
    if (!(audioElement instanceof HTMLAudioElement)) {
        return;
    }

    persistAudioState(audioElement, "pause", false);
}

function applyImmersiveChromeState(storyPage) {
    const shouldUseImmersiveMode = storyPage.classList.contains("manual-fullscreen-focus");

    document.body.classList.toggle("story-immersive-mode", shouldUseImmersiveMode);

    if (!shouldUseImmersiveMode) {
        storyPage.classList.remove(FULLSCREEN_CHROME_HIDDEN_CLASS);
    }
}

function clearFullscreenChromeHideTimer(binding) {
    if (!binding || !binding.hideChromeTimeoutId) {
        return;
    }

    window.clearTimeout(binding.hideChromeTimeoutId);
    binding.hideChromeTimeoutId = 0;
}

function setFullscreenChromeHidden(storyPage, isHidden) {
    if (!(storyPage instanceof HTMLElement)) {
        return;
    }

    if (!storyPage.classList.contains("manual-fullscreen-focus")) {
        storyPage.classList.remove(FULLSCREEN_CHROME_HIDDEN_CLASS);
        return;
    }

    storyPage.classList.toggle(FULLSCREEN_CHROME_HIDDEN_CLASS, Boolean(isHidden));
}

function shouldKeepFullscreenChromeVisible(storyPage) {
    if (!(storyPage instanceof HTMLElement) || !storyPage.classList.contains("manual-fullscreen-focus")) {
        return true;
    }

    const binding = fullscreenBindings.get(storyPage);
    if (!binding?.lastInputWasKeyboard) {
        return false;
    }

    const activeElement = document.activeElement;
    if (!(activeElement instanceof HTMLElement)) {
        return false;
    }

    const fullscreenToggle = storyPage.querySelector(".story-fullscreen-toggle");
    const favoriteToggle = storyPage.querySelector(".story-favorite-toggle");
    const customPlayer = storyPage.querySelector(CUSTOM_PLAYER_SELECTOR);

    return (fullscreenToggle instanceof HTMLElement && fullscreenToggle.contains(activeElement))
        || (favoriteToggle instanceof HTMLElement && favoriteToggle.contains(activeElement))
        || (customPlayer instanceof HTMLElement && customPlayer.contains(activeElement));
}

function revealFullscreenChrome(storyPage, binding, scheduleHide = true) {
    if (!(storyPage instanceof HTMLElement) || !binding) {
        return;
    }

    clearFullscreenChromeHideTimer(binding);
    setFullscreenChromeHidden(storyPage, false);

    if (!scheduleHide || !storyPage.classList.contains("manual-fullscreen-focus")) {
        return;
    }

    binding.hideChromeTimeoutId = window.setTimeout(() => {
        binding.hideChromeTimeoutId = 0;

        if (!storyPage.classList.contains("manual-fullscreen-focus")) {
            setFullscreenChromeHidden(storyPage, false);
            return;
        }

        if (shouldKeepFullscreenChromeVisible(storyPage)) {
            revealFullscreenChrome(storyPage, binding, true);
            return;
        }

        setFullscreenChromeHidden(storyPage, true);
    }, FULLSCREEN_IDLE_TIMEOUT_MS);
}

export async function setStoryPlayerFullscreenIntent(isEnabled) {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (!(storyPage instanceof HTMLElement)) {
        return;
    }

    const content = storyPage.querySelector(".story-player-content");
    storyPage.classList.toggle("manual-fullscreen-focus", Boolean(isEnabled));
    applyImmersiveChromeState(storyPage);

    const binding = fullscreenBindings.get(storyPage);
    if (binding) {
        if (isEnabled) {
            revealFullscreenChrome(storyPage, binding, true);
        } else {
            clearFullscreenChromeHideTimer(binding);
            setFullscreenChromeHidden(storyPage, false);
        }
    }

    if (isEnabled) {
        if (content instanceof HTMLElement) {
            content.scrollIntoView({ behavior: "smooth", block: "center" });
        }

        if (content instanceof HTMLElement) {
            await syncNativeStoryPlayerFullscreen(content, true);
        }
    } else if (content instanceof HTMLElement) {
        await syncNativeStoryPlayerFullscreen(content, false);
    }
}

export function consumePendingStoryPlayerFullscreenIntent() {
    try {
        const shouldResume = window.sessionStorage.getItem(PENDING_FULLSCREEN_INTENT_KEY) === "1";
        window.sessionStorage.removeItem(PENDING_FULLSCREEN_INTENT_KEY);
        return shouldResume;
    } catch {
        return false;
    }
}

export function initializeStoryPlayerFullscreenExperience(dotNetRef) {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (!(storyPage instanceof HTMLElement)) {
        return;
    }

    const content = storyPage.querySelector(STORY_PLAYER_CONTENT_SELECTOR);
    if (!(content instanceof HTMLElement)) {
        return;
    }

    if (fullscreenBindings.has(storyPage)) {
        return;
    }

    const handleUserActivity = () => {
        const binding = fullscreenBindings.get(storyPage);
        if (!binding) {
            return;
        }

        binding.lastInputWasKeyboard = false;

        if (!storyPage.classList.contains("manual-fullscreen-focus")) {
            clearFullscreenChromeHideTimer(binding);
            setFullscreenChromeHidden(storyPage, false);
            return;
        }

        revealFullscreenChrome(storyPage, binding, true);
    };

    const handleKeyboardActivity = () => {
        const binding = fullscreenBindings.get(storyPage);
        if (!binding) {
            return;
        }

        binding.lastInputWasKeyboard = true;

        if (!storyPage.classList.contains("manual-fullscreen-focus")) {
            clearFullscreenChromeHideTimer(binding);
            setFullscreenChromeHidden(storyPage, false);
            return;
        }

        revealFullscreenChrome(storyPage, binding, false);
    };

    const handleFocusIn = () => {
        const binding = fullscreenBindings.get(storyPage);
        if (!binding || !storyPage.classList.contains("manual-fullscreen-focus")) {
            return;
        }

        revealFullscreenChrome(storyPage, binding, binding.lastInputWasKeyboard ? false : true);
    };

    const handleFocusOut = () => {
        const binding = fullscreenBindings.get(storyPage);
        if (!binding || !storyPage.classList.contains("manual-fullscreen-focus")) {
            return;
        }

        window.setTimeout(() => {
            if (!fullscreenBindings.has(storyPage) || shouldKeepFullscreenChromeVisible(storyPage)) {
                return;
            }

            revealFullscreenChrome(storyPage, binding, true);
        }, 0);
    };

    const handleFullscreenToggleClick = async (event) => {
        const target = event.target instanceof Element
            ? event.target.closest(".story-fullscreen-toggle")
            : null;

        if (!(target instanceof HTMLElement) || !storyPage.contains(target) || !shouldUseNativeStoryPlayerFullscreen()) {
            return;
        }

        if (!storyPage.classList.contains("manual-fullscreen-focus")) {
            await syncNativeStoryPlayerFullscreen(content, true);
            return;
        }

        if (!getActiveFullscreenElement()) {
            return;
        }

        suppressFullscreenExitCallbacks += 1;
        try {
            await syncNativeStoryPlayerFullscreen(content, false);
        } finally {
            if (suppressFullscreenExitCallbacks > 0) {
                suppressFullscreenExitCallbacks -= 1;
            }
        }
    };

    const handleFullscreenChange = () => {
        const binding = fullscreenBindings.get(storyPage);
        const fullscreenElement = getActiveFullscreenElement();

        if (!fullscreenElement && storyPage.classList.contains("manual-fullscreen-focus")) {
            if (suppressFullscreenExitCallbacks > 0 || hasPendingStoryPlayerFullscreenIntent()) {
                return;
            }

            storyPage.classList.remove("manual-fullscreen-focus");
            applyImmersiveChromeState(storyPage);
            clearFullscreenChromeHideTimer(binding);
            setFullscreenChromeHidden(storyPage, false);

            if (dotNetRef && typeof dotNetRef.invokeMethodAsync === "function") {
                dotNetRef.invokeMethodAsync("HandleFullscreenExitedFromBrowser").catch(() => {
                    // Ignore callback failures after navigation/disconnect.
                });
            }
        } else if (fullscreenElement && storyPage.classList.contains("manual-fullscreen-focus") && binding) {
            revealFullscreenChrome(storyPage, binding, true);
        }
    };

    content.addEventListener("pointermove", handleUserActivity);
    content.addEventListener("pointerdown", handleUserActivity);
    content.addEventListener("touchstart", handleUserActivity, { passive: true });
    content.addEventListener("mousemove", handleUserActivity);
    document.addEventListener("keydown", handleKeyboardActivity, true);
    storyPage.addEventListener("focusin", handleFocusIn);
    storyPage.addEventListener("focusout", handleFocusOut);
    storyPage.addEventListener("click", handleFullscreenToggleClick, true);
    document.addEventListener("fullscreenchange", handleFullscreenChange);
    document.addEventListener("webkitfullscreenchange", handleFullscreenChange);
    fullscreenBindings.set(storyPage, {
        content,
        handleUserActivity,
        handleKeyboardActivity,
        handleFocusIn,
        handleFocusOut,
        handleFullscreenToggleClick,
        handleFullscreenChange,
        hideChromeTimeoutId: 0,
        lastInputWasKeyboard: false
    });
    applyImmersiveChromeState(storyPage);
    revealFullscreenChrome(storyPage, fullscreenBindings.get(storyPage), storyPage.classList.contains("manual-fullscreen-focus"));
}

export function disposeStoryPlayerFullscreenExperience() {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (storyPage instanceof HTMLElement) {
        const binding = fullscreenBindings.get(storyPage);
        if (binding) {
            if (binding.content instanceof HTMLElement) {
                binding.content.removeEventListener("pointermove", binding.handleUserActivity);
                binding.content.removeEventListener("pointerdown", binding.handleUserActivity);
                binding.content.removeEventListener("touchstart", binding.handleUserActivity);
                binding.content.removeEventListener("mousemove", binding.handleUserActivity);
            }

            document.removeEventListener("keydown", binding.handleKeyboardActivity, true);
            storyPage.removeEventListener("focusin", binding.handleFocusIn);
            storyPage.removeEventListener("focusout", binding.handleFocusOut);
            storyPage.removeEventListener("click", binding.handleFullscreenToggleClick, true);
            document.removeEventListener("fullscreenchange", binding.handleFullscreenChange);
            document.removeEventListener("webkitfullscreenchange", binding.handleFullscreenChange);
            clearFullscreenChromeHideTimer(binding);
            fullscreenBindings.delete(storyPage);
        }

        storyPage.classList.remove("manual-fullscreen-focus");
        storyPage.classList.remove(FULLSCREEN_CHROME_HIDDEN_CLASS);
    }

    document.body.classList.remove("story-immersive-mode");
}

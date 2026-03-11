const STORY_AUDIO_SELECTOR = ".story-audio";
const STORY_COVER_SELECTOR = ".story-cover";
const STORY_PAGE_SELECTOR = ".story-player-page";
const DEFAULT_IMAGE_PATH = "/branding/schink-logo-text.png";
const ARTWORK_SIZES = ["96x96", "128x128", "192x192", "256x256", "384x384", "512x512"];
const STORY_PROGRESS_PREFIX = "schink:story-progress:";
const PROGRESS_SAVE_INTERVAL_MS = 4000;
const AUDIO_VOLUME_KEY = "schink:audio-volume";

const boundAudios = new WeakSet();
const fullscreenBindings = new WeakMap();

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

function bindActionHandlers(audioElement) {
    if (!("mediaSession" in navigator)) {
        return;
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

function bindAudioEvents(audioElement) {
    if (boundAudios.has(audioElement)) {
        updateMediaMetadata(audioElement);
        updatePositionState(audioElement);
        return;
    }

    boundAudios.add(audioElement);

    const updateAll = () => {
        updateMediaMetadata(audioElement);
        updatePositionState(audioElement);
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

    bindActionHandlers(audioElement);
    restoreVolumeState(audioElement);

    const coverImage = getStoryCoverImage(audioElement);
    if (coverImage) {
        coverImage.addEventListener("load", updateAll);
    }

    audioElement.addEventListener("loadedmetadata", () => {
        restoreStoryProgress(audioElement);
        updateAll();
    });
    audioElement.addEventListener("play", updateAll);
    audioElement.addEventListener("pause", () => {
        updateAll();
        saveStoryProgress(audioElement);
    });
    audioElement.addEventListener("timeupdate", () => {
        updatePositionState(audioElement);
        maybeSaveProgress();
    });
    audioElement.addEventListener("ratechange", () => updatePositionState(audioElement));
    audioElement.addEventListener("seeked", () => {
        updatePositionState(audioElement);
        saveStoryProgress(audioElement);
    });
    audioElement.addEventListener("ended", () => {
        clearStoryProgress(audioElement);
        updateAll();
    });
    audioElement.addEventListener("volumechange", () => {
        saveVolumeState(audioElement);
    });

    updateAll();
}

export function initializeStoryMediaSession() {
    const audioElement = document.querySelector(STORY_AUDIO_SELECTOR);
    if (!(audioElement instanceof HTMLAudioElement)) {
        return;
    }

    bindAudioEvents(audioElement);
}

function applyImmersiveChromeState(storyPage) {
    const shouldUseImmersiveMode = storyPage.classList.contains("manual-fullscreen-focus");

    document.body.classList.toggle("story-immersive-mode", shouldUseImmersiveMode);
}

export async function setStoryPlayerFullscreenIntent(isEnabled) {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (!(storyPage instanceof HTMLElement)) {
        return;
    }

    const content = storyPage.querySelector(".story-player-content");
    storyPage.classList.toggle("manual-fullscreen-focus", Boolean(isEnabled));
    applyImmersiveChromeState(storyPage);

    if (isEnabled) {
        if (content instanceof HTMLElement) {
            content.scrollIntoView({ behavior: "smooth", block: "center" });
        }

        if (content instanceof HTMLElement && document.fullscreenEnabled && !document.fullscreenElement) {
            try {
                await content.requestFullscreen();
            } catch {
                // Ignore blocked fullscreen requests.
            }
        }
    } else if (document.fullscreenElement) {
        try {
            await document.exitFullscreen();
        } catch {
            // Ignore fullscreen exit errors.
        }
    }
}

export function initializeStoryPlayerFullscreenExperience(dotNetRef) {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (!(storyPage instanceof HTMLElement)) {
        return;
    }

    if (fullscreenBindings.has(storyPage)) {
        return;
    }

    const handleFullscreenChange = () => {
        if (!document.fullscreenElement && storyPage.classList.contains("manual-fullscreen-focus")) {
            storyPage.classList.remove("manual-fullscreen-focus");
            applyImmersiveChromeState(storyPage);

            if (dotNetRef && typeof dotNetRef.invokeMethodAsync === "function") {
                dotNetRef.invokeMethodAsync("HandleFullscreenExitedFromBrowser").catch(() => {
                    // Ignore callback failures after navigation/disconnect.
                });
            }
        }
    };

    document.addEventListener("fullscreenchange", handleFullscreenChange);
    fullscreenBindings.set(storyPage, { handleFullscreenChange });
    applyImmersiveChromeState(storyPage);
}

export function disposeStoryPlayerFullscreenExperience() {
    const storyPage = document.querySelector(STORY_PAGE_SELECTOR);
    if (storyPage instanceof HTMLElement) {
        const binding = fullscreenBindings.get(storyPage);
        if (binding) {
            document.removeEventListener("fullscreenchange", binding.handleFullscreenChange);
            fullscreenBindings.delete(storyPage);
        }

        storyPage.classList.remove("manual-fullscreen-focus");
    }

    document.body.classList.remove("story-immersive-mode");
}

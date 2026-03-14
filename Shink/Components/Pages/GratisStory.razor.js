const STORY_AUDIO_SELECTOR = ".story-audio";
const STORY_COVER_SELECTOR = ".story-cover";
const STORY_PAGE_SELECTOR = ".story-player-page";
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
const SPEED_STEPS = [1, 1.25, 1.5, 0.8];

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

function formatTime(seconds) {
    if (!Number.isFinite(seconds) || seconds < 0) {
        return "0:00";
    }

    const whole = Math.floor(seconds);
    const mins = Math.floor(whole / 60);
    const secs = whole % 60;
    return `${mins}:${String(secs).padStart(2, "0")}`;
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
        speedLabel: customPlayer.querySelector(SPEED_LABEL_SELECTOR)
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
    saveStoryProgress(audioElement);
    saveVolumeState(audioElement);
    audioElement.pause();

    const sources = audioElement.querySelectorAll("source");
    sources.forEach((source) => {
        source.setAttribute("src", "");
    });

    audioElement.removeAttribute("src");
    audioElement.load();
}

function bindAudioEvents(audioElement) {
    if (boundAudios.has(audioElement)) {
        updateMediaMetadata(audioElement);
        updatePositionState(audioElement);
        return;
    }

    boundAudios.add(audioElement);

    const customPlayerElements = getCustomPlayerElements(audioElement);
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
            customPlayerElements.playIcon.textContent = isPlaying ? "\u23F8" : "\u25B6";
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
        }

        if (customPlayerElements.muteIcon instanceof HTMLElement) {
            customPlayerElements.muteIcon.textContent = audioElement.muted || volumeValue <= 0.001 ? "\uD83D\uDD07" : "\uD83D\uDD0A";
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

    bindActionHandlers(audioElement);
    restoreVolumeState(audioElement);

    if (customPlayerElements) {
        if (customPlayerElements.playToggle instanceof HTMLButtonElement) {
            customPlayerElements.playToggle.addEventListener("click", async () => {
                if (audioElement.paused) {
                    try {
                        await audioElement.play();
                    } catch {
                        // Ignore blocked autoplay/playback errors.
                    }

                    return;
                }

                audioElement.pause();
            });
        }

        if (customPlayerElements.storyPrevButton instanceof HTMLButtonElement) {
            customPlayerElements.storyPrevButton.addEventListener("click", () => {
                const prevStoryLink = customPlayerElements.container.querySelector(STORY_NAV_PREV_SELECTOR);
                if (prevStoryLink instanceof HTMLAnchorElement) {
                    prevStoryLink.click();
                }
            });
        }

        if (customPlayerElements.storyNextButton instanceof HTMLButtonElement) {
            customPlayerElements.storyNextButton.addEventListener("click", () => {
                const nextStoryLink = customPlayerElements.container.querySelector(STORY_NAV_NEXT_SELECTOR);
                if (nextStoryLink instanceof HTMLAnchorElement) {
                    nextStoryLink.click();
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
                const currentIndex = SPEED_STEPS.findIndex((rate) => Math.abs(rate - audioElement.playbackRate) < 0.001);
                const nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SPEED_STEPS.length;
                audioElement.playbackRate = SPEED_STEPS[nextIndex];
                updateCustomPlayerState();
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
                try {
                    await audioElement.play();
                } catch {
                    // Ignore blocked autoplay/playback errors.
                }

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
                coverPlayIcon.textContent = isPlaying ? "\u23F8" : "\u25B6";
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
    audioElement.addEventListener("play", updateAll);
    audioElement.addEventListener("pause", () => {
        updateAll();
        saveStoryProgress(audioElement);
    });
    audioElement.addEventListener("timeupdate", () => {
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
        clearStoryProgress(audioElement);
        updateAll();
    });
    audioElement.addEventListener("volumechange", () => {
        saveVolumeState(audioElement);
        updateCustomPlayerState();
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

export function stopStoryAudioPlayback() {
    const audioElement = document.querySelector(STORY_AUDIO_SELECTOR);
    if (!(audioElement instanceof HTMLAudioElement)) {
        return;
    }

    stopAndReleaseAudio(audioElement);
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

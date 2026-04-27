let audioPlayer;
let maatAnimationLoop;
let maatAnimationTimeout;
let activeAudioTrigger;

function clearActiveAudioTrigger() {
  if (!activeAudioTrigger) {
    return;
  }

  activeAudioTrigger.classList.remove("is-playing");
  activeAudioTrigger = undefined;
}

function resolveAudioTrigger(triggerSlug) {
  if (!triggerSlug || typeof document === "undefined") {
    return undefined;
  }

  return document.querySelector(`[data-character-audio-slug="${triggerSlug}"]`) ?? undefined;
}

function setActiveAudioTrigger(triggerSlug) {
  const nextTrigger = resolveAudioTrigger(triggerSlug);
  if (activeAudioTrigger && activeAudioTrigger !== nextTrigger) {
    activeAudioTrigger.classList.remove("is-playing");
  }

  activeAudioTrigger = nextTrigger;
  activeAudioTrigger?.classList.add("is-playing");
}

function ensureAudioPlayer() {
  if (audioPlayer) {
    return audioPlayer;
  }

  audioPlayer = document.createElement("audio");
  audioPlayer.preload = "auto";
  audioPlayer.controls = false;
  audioPlayer.disablePictureInPicture = true;
  audioPlayer.setAttribute("playsinline", "true");
  audioPlayer.setAttribute("webkit-playsinline", "true");
  audioPlayer.setAttribute("controlslist", "nodownload noplaybackrate");
  audioPlayer.setAttribute("aria-hidden", "true");
  audioPlayer.hidden = true;
  audioPlayer.oncontextmenu = () => false;
  audioPlayer.addEventListener("ended", clearActiveAudioTrigger);
  audioPlayer.addEventListener("error", clearActiveAudioTrigger);
  document.body.appendChild(audioPlayer);
  return audioPlayer;
}

export async function playCharacterAudio(sourceUrl, triggerSlug) {
  if (!sourceUrl) {
    clearActiveAudioTrigger();
    return;
  }

  const player = ensureAudioPlayer();
  player.pause();
  player.src = sourceUrl;
  setActiveAudioTrigger(triggerSlug);

  try {
    await player.play();
  } catch {
    clearActiveAudioTrigger();
    // Ignore browser autoplay failures; the click gesture usually satisfies playback.
  }
}

function resolveAudioSourceFromButton(button) {
  if (!(button instanceof HTMLElement)) {
    return "";
  }

  const serializedSources = button.getAttribute("data-character-audio-urls") || "";
  if (serializedSources.length) {
    try {
      const parsedSources = JSON.parse(serializedSources);
      const sourceUrls = Array.isArray(parsedSources)
        ? parsedSources
            .map((source) => {
              if (typeof source === "string") {
                return source;
              }

              if (source && typeof source.Url === "string") {
                return source.Url;
              }

              if (source && typeof source.url === "string") {
                return source.url;
              }

              return "";
            })
            .filter((sourceUrl) => sourceUrl.length > 0)
        : [];

      if (sourceUrls.length) {
        return sourceUrls[Math.floor(Math.random() * sourceUrls.length)];
      }
    } catch {
      // Keep older single-source markup working.
    }
  }

  return button.getAttribute("data-character-audio-url") || "";
}

export function playCharacterAudioFromButton(button) {
  if (!(button instanceof HTMLElement)) {
    clearActiveAudioTrigger();
    return;
  }

  const sourceUrl = resolveAudioSourceFromButton(button);
  const triggerSlug = button.getAttribute("data-character-audio-slug") || "";
  void playCharacterAudio(sourceUrl, triggerSlug);
}

export function stopCharacterAudio() {
  if (!audioPlayer) {
    return;
  }

  audioPlayer.pause();
  audioPlayer.removeAttribute("src");
  audioPlayer.load();
  clearActiveAudioTrigger();
}

export function triggerCharacterHaptics() {
  if (typeof navigator === "undefined" || typeof navigator.vibrate !== "function") {
    return;
  }

  navigator.vibrate([24, 18, 40]);
}

function animateRandomMaat() {
  const maatTiles = Array.from(document.querySelectorAll(".character-profile-popup-card [data-maat-tile]"));
  if (!maatTiles.length) {
    return;
  }

  const randomTile = maatTiles[Math.floor(Math.random() * maatTiles.length)];
  randomTile.classList.remove("is-shuffling");
  void randomTile.offsetWidth;
  randomTile.classList.add("is-shuffling");

  if (maatAnimationTimeout) {
    window.clearTimeout(maatAnimationTimeout);
  }

  maatAnimationTimeout = window.setTimeout(() => {
    randomTile.classList.remove("is-shuffling");
  }, 900);
}

function queueNextMaatAnimation() {
  maatAnimationLoop = window.setTimeout(() => {
    animateRandomMaat();
    queueNextMaatAnimation();
  }, 3000);
}

export function startMaatAnimation() {
  stopMaatAnimation();
  animateRandomMaat();
  queueNextMaatAnimation();
}

export function stopMaatAnimation() {
  if (maatAnimationLoop) {
    window.clearTimeout(maatAnimationLoop);
    maatAnimationLoop = undefined;
  }

  if (maatAnimationTimeout) {
    window.clearTimeout(maatAnimationTimeout);
    maatAnimationTimeout = undefined;
  }

  document
    .querySelectorAll(".character-profile-popup-card [data-maat-tile].is-shuffling")
    .forEach((element) => element.classList.remove("is-shuffling"));
}

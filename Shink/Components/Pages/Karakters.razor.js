let audioPlayer;

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
  document.body.appendChild(audioPlayer);
  return audioPlayer;
}

export async function playCharacterAudio(sourceUrl) {
  if (!sourceUrl) {
    return;
  }

  const player = ensureAudioPlayer();
  player.pause();
  player.src = sourceUrl;
  player.load();

  try {
    await player.play();
  } catch {
    // Ignore browser autoplay failures; the click gesture usually satisfies playback.
  }
}

export function stopCharacterAudio() {
  if (!audioPlayer) {
    return;
  }

  audioPlayer.pause();
  audioPlayer.removeAttribute("src");
  audioPlayer.load();
}

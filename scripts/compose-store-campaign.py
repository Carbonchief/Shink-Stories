#!/usr/bin/env python3
"""Build truthful, conversion-focused App Store and Google Play artwork.

The generated campaign backgrounds provide the visual direction, while the
actual app screenshots, Schink logo, and Schink character artwork are layered
deterministically so store assets never invent or distort product UI.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Iterable

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
CAMPAIGN = ROOT / "artifacts" / "store-listing" / "campaign-v2"
BACKGROUND = CAMPAIGN / "generated" / "portrait-background.png"
FEATURE_BACKGROUND = CAMPAIGN / "generated" / "feature-background.png"
RAW = ROOT / "artifacts" / "store-listing" / "raw"
FINAL = CAMPAIGN / "final"

IPAD_CAMPAIGN = ROOT / "artifacts" / "store-listing" / "ipad-13"
IPAD_BACKGROUND = IPAD_CAMPAIGN / "generated" / "schink-teal-background.png"
IPAD_RAW = IPAD_CAMPAIGN / "raw"
IPAD_FINAL = IPAD_CAMPAIGN / "final"

PHONE_CAMPAIGN = ROOT / "artifacts" / "store-listing" / "phone-teal"
PHONE_BACKGROUND = PHONE_CAMPAIGN / "generated" / "schink-teal-phone-background.png"
IPHONE_FINAL = PHONE_CAMPAIGN / "iphone" / "final"
ANDROID_RAW = PHONE_CAMPAIGN / "android" / "raw"
ANDROID_FINAL = PHONE_CAMPAIGN / "android" / "final"

FRAME = Path("/Users/luanvanderwalt/.codex/skills/aso-appstore-screenshots/assets/device_frame.png")
FONT_BLACK = Path("/Library/Fonts/SF-Pro-Display-Black.otf")
FONT_BOLD = Path("/Library/Fonts/SF-Pro-Display-Bold.otf")
LOGO = ROOT / "Shink.Mobile" / "Resources" / "Images" / "schink_stories_logo_white.png"
CHARACTERS = ROOT / "Shink.Mobile" / "Resources" / "Images" / "schink_character_lineup.png"

STORE_SIZE = (1242, 2688)
FEATURE_SIZE = (1024, 500)
IPAD_STORE_SIZE = (2064, 2752)
IPHONE_STORE_SIZE = (1242, 2688)
ANDROID_STORE_SIZE = (1080, 1920)
TEAL = (14, 73, 93, 255)

SCREENSHOTS = (
    ("01-luister-na-afrikaanse-stories.png", "LUISTER", "NA AFRIKAANSE STORIES", RAW / "02-stories.png"),
    ("02-vind-die-perfekte-storie.png", "VIND", "DIE PERFEKTE STORIE", RAW / "03-search.png"),
    ("03-ontsluit-geliefde-karakters.png", "ONTSLUIT", "GELIEFDE KARAKTERS", RAW / "04-karakters.png"),
    ("04-geniet-storietyd-enige-plek.png", "GENIET", "STORIETYD ENIGE PLEK", RAW / "05-player.png"),
)

IPAD_SCREENSHOTS = (
    (
        "01-luister-na-afrikaanse-stories.png",
        "LUISTER",
        "NA AFRIKAANSE STORIES",
        IPAD_RAW / "01-luister-ipad-native.png",
        (
            ("hailey-hasie.png", "left", 470),
            ("rammetjie-uitnek.png", "right", 420),
        ),
    ),
    (
        "02-vind-die-perfekte-storie.png",
        "VIND",
        "DIE PERFEKTE STORIE",
        IPAD_RAW / "02-search-ipad-native.png",
        (
            ("roomie.png", "left", 430),
            ("jojo.png", "right", 405),
        ),
    ),
    (
        "03-ontsluit-geliefde-karakters.png",
        "ONTSLUIT",
        "GELIEFDE KARAKTERS",
        IPAD_RAW / "03-karakters-ipad-native.png",
        (
            ("suurlemoentjie.png", "left", 420),
            ("dankie.png", "right", 410),
        ),
    ),
    (
        "04-geniet-storietyd-enige-plek.png",
        "GENIET",
        "STORIETYD ENIGE PLEK",
        IPAD_RAW / "04-player-ipad-native.png",
        (
            ("tiekie.png", "left", 410),
            ("vuma.png", "right", 410),
        ),
    ),
)

PHONE_SCREENSHOTS = (
    (
        "01-luister-na-afrikaanse-stories.png",
        "LUISTER",
        "NA AFRIKAANSE STORIES",
        RAW / "02-stories.png",
        ANDROID_RAW / "01-stories.png",
        (("hailey-hasie.png", "left"), ("rammetjie-uitnek.png", "right")),
    ),
    (
        "02-vind-die-perfekte-storie.png",
        "VIND",
        "DIE PERFEKTE STORIE",
        RAW / "03-search.png",
        ANDROID_RAW / "02-search.png",
        (("roomie.png", "left"), ("jojo.png", "right")),
    ),
    (
        "03-ontsluit-geliefde-karakters.png",
        "ONTSLUIT",
        "GELIEFDE KARAKTERS",
        RAW / "04-karakters.png",
        ANDROID_RAW / "03-karakters.png",
        (("suurlemoentjie.png", "left"), ("dankie.png", "right")),
    ),
    (
        "04-geniet-storietyd-enige-plek.png",
        "GENIET",
        "STORIETYD ENIGE PLEK",
        RAW / "05-player.png",
        ANDROID_RAW / "04-player.png",
        (("tiekie.png", "left"), ("vuma.png", "right")),
    ),
)

CHARACTER_DIR = ROOT / "Shink" / "wwwroot" / "branding" / "characters"


def cover(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    return ImageOps.fit(image.convert("RGBA"), size, method=Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def contain(image: Image.Image, width: int) -> Image.Image:
    ratio = width / image.width
    return image.resize((width, round(image.height * ratio)), Image.Resampling.LANCZOS)


def fit_font(text: str, max_width: int, max_size: int, min_size: int) -> ImageFont.FreeTypeFont:
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    for size in range(max_size, min_size - 1, -2):
        font = ImageFont.truetype(str(FONT_BLACK), size)
        if probe.textbbox((0, 0), text, font=font)[2] <= max_width:
            return font
    return ImageFont.truetype(str(FONT_BLACK), min_size)


def draw_centered_with_shadow(
    image: Image.Image,
    text: str,
    y: int,
    font: ImageFont.FreeTypeFont,
    *,
    shadow_offset: int = 7,
) -> None:
    shadow = Image.new("RGBA", image.size, (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    x = image.width // 2
    shadow_draw.text((x + shadow_offset, y + shadow_offset), text, font=font, fill=(9, 42, 52, 155), anchor="ma")
    shadow = shadow.filter(ImageFilter.GaussianBlur(5))
    image.alpha_composite(shadow)
    ImageDraw.Draw(image).text((x, y), text, font=font, fill="white", anchor="ma")


def add_soft_shadow(
    canvas: Image.Image,
    layer: Image.Image,
    position: tuple[int, int],
    *,
    blur: int = 28,
    opacity: int = 115,
    offset: tuple[int, int] = (0, 18),
) -> None:
    alpha = layer.getchannel("A")
    shadow_alpha = alpha.point(lambda value: value * opacity // 255)
    shadow = Image.new("RGBA", layer.size, (3, 28, 37, 0))
    shadow.putalpha(shadow_alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(shadow, (position[0] + offset[0], position[1] + offset[1]))
    canvas.alpha_composite(layer, position)


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0], size[1]), radius=radius, fill=255)
    return mask


def compose_ipad_screenshot(
    output_name: str,
    verb: str,
    descriptor: str,
    screenshot_path: Path,
    character_specs: tuple[tuple[str, str, int], ...],
) -> None:
    canvas = cover(Image.open(IPAD_BACKGROUND), IPAD_STORE_SIZE)

    verb_font = fit_font(verb, 1420, 228, 170)
    descriptor_font = fit_font(descriptor, 1460, 106, 76)
    draw_centered_with_shadow(canvas, verb, 118, verb_font, shadow_offset=6)
    draw_centered_with_shadow(canvas, descriptor, 385, descriptor_font, shadow_offset=4)

    screen = Image.open(screenshot_path).convert("RGBA")
    screen_w = 1590
    screen_h = round(screen_w * screen.height / screen.width)
    screen = screen.resize((screen_w, screen_h), Image.Resampling.LANCZOS)
    screen.putalpha(rounded_mask(screen.size, 48))

    bezel = 24
    shell_padding = 42
    shell_w = screen_w + (shell_padding * 2)
    shell_h = screen_h + (shell_padding * 2)
    shell = Image.new("RGBA", (shell_w, shell_h), (0, 0, 0, 0))
    shell_draw = ImageDraw.Draw(shell)
    shell_draw.rounded_rectangle(
        (0, 0, shell_w - 1, shell_h - 1),
        radius=74,
        fill=(22, 25, 28, 255),
        outline=(100, 111, 117, 255),
        width=4,
    )
    shell_draw.rounded_rectangle(
        (bezel, bezel, shell_w - bezel, shell_h - bezel),
        radius=62,
        outline=(2, 7, 9, 210),
        width=8,
    )
    shell.alpha_composite(screen, (shell_padding, shell_padding))

    device_position = ((IPAD_STORE_SIZE[0] - shell_w) // 2, 570)
    add_soft_shadow(canvas, shell, device_position, blur=34, opacity=155, offset=(0, 24))

    for filename, side, width in character_specs:
        if "mystery" in filename.lower():
            raise RuntimeError(f"Mystery artwork cannot be used in store listing: {filename}")

        character = contain(Image.open(CHARACTER_DIR / filename).convert("RGBA"), width)
        y = 510
        x = -round(width * 0.28) if side == "left" else IPAD_STORE_SIZE[0] - round(width * 0.72)
        add_soft_shadow(canvas, character, (x, y), blur=20, opacity=125, offset=(0, 16))

    output = IPAD_FINAL / output_name
    canvas.convert("RGB").save(output, "PNG", optimize=True)


def compose_phone_screenshot(
    output_name: str,
    verb: str,
    descriptor: str,
    screenshot_path: Path,
    character_specs: tuple[tuple[str, str], ...],
    *,
    target_size: tuple[int, int],
    output_dir: Path,
) -> None:
    """Compose a phone storefront image in the approved iPad campaign style."""
    canvas = cover(Image.open(PHONE_BACKGROUND), target_size)
    width, height = target_size
    scale = width / IPHONE_STORE_SIZE[0]

    verb_y = round(84 * scale)
    descriptor_y = round(344 * scale)
    verb_font = fit_font(verb, round(width * 0.84), round(218 * scale), round(142 * scale))
    descriptor_font = fit_font(
        descriptor,
        round(width * 0.86),
        round(104 * scale),
        round(68 * scale),
    )
    draw_centered_with_shadow(canvas, verb, verb_y, verb_font, shadow_offset=max(4, round(7 * scale)))
    draw_centered_with_shadow(
        canvas,
        descriptor,
        descriptor_y,
        descriptor_font,
        shadow_offset=max(3, round(5 * scale)),
    )

    screen = Image.open(screenshot_path).convert("RGBA")
    if target_size == ANDROID_STORE_SIZE:
        # Remove the emulator-only system status strip so every Google Play
        # screenshot has a clean, consistent app-first presentation.
        screen = screen.crop((0, 128, screen.width, screen.height))
    screen_w = round(width * (0.84 if target_size == IPHONE_STORE_SIZE else 0.82))
    screen_h = round(screen_w * screen.height / screen.width)
    screen = screen.resize((screen_w, screen_h), Image.Resampling.LANCZOS)
    corner_radius = max(34, round(width * 0.038))
    screen.putalpha(rounded_mask(screen.size, corner_radius))

    shell_padding = max(22, round(width * 0.026))
    shell_w = screen_w + (shell_padding * 2)
    shell_h = screen_h + (shell_padding * 2)
    shell = Image.new("RGBA", (shell_w, shell_h), (0, 0, 0, 0))
    shell_draw = ImageDraw.Draw(shell)
    shell_draw.rounded_rectangle(
        (0, 0, shell_w - 1, shell_h - 1),
        radius=corner_radius + shell_padding,
        fill=(20, 24, 27, 255),
        outline=(100, 111, 117, 255),
        width=max(3, round(width * 0.003)),
    )
    shell_draw.rounded_rectangle(
        (round(shell_padding * 0.42), round(shell_padding * 0.42), shell_w - round(shell_padding * 0.42), shell_h - round(shell_padding * 0.42)),
        radius=corner_radius + round(shell_padding * 0.48),
        outline=(2, 7, 9, 220),
        width=max(5, round(width * 0.006)),
    )
    shell.alpha_composite(screen, (shell_padding, shell_padding))

    device_y = round(height * (0.232 if target_size == IPHONE_STORE_SIZE else 0.225))
    device_position = ((width - shell_w) // 2, device_y)
    add_soft_shadow(
        canvas,
        shell,
        device_position,
        blur=max(22, round(width * 0.026)),
        opacity=155,
        offset=(0, max(15, round(width * 0.018))),
    )

    character_width = round(width * 0.27)
    character_y = device_y - round(character_width * 0.20)
    for filename, side in character_specs:
        if "mystery" in filename.lower():
            raise RuntimeError(f"Mystery artwork cannot be used in store listing: {filename}")

        character = contain(Image.open(CHARACTER_DIR / filename).convert("RGBA"), character_width)
        x = -round(character_width * 0.36) if side == "left" else width - round(character_width * 0.64)
        add_soft_shadow(
            canvas,
            character,
            (x, character_y),
            blur=max(15, round(width * 0.018)),
            opacity=125,
            offset=(0, max(10, round(width * 0.012))),
        )

    output_dir.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(output_dir / output_name, "PNG", optimize=True)


def compose_phone_campaigns() -> None:
    for output_name, verb, descriptor, iphone_raw, android_raw, characters in PHONE_SCREENSHOTS:
        compose_phone_screenshot(
            output_name,
            verb,
            descriptor,
            iphone_raw,
            characters,
            target_size=IPHONE_STORE_SIZE,
            output_dir=IPHONE_FINAL,
        )
        compose_phone_screenshot(
            output_name,
            verb,
            descriptor,
            android_raw,
            characters,
            target_size=ANDROID_STORE_SIZE,
            output_dir=ANDROID_FINAL,
        )

    iphone_outputs = [IPHONE_FINAL / item[0] for item in PHONE_SCREENSHOTS]
    android_outputs = [ANDROID_FINAL / item[0] for item in PHONE_SCREENSHOTS]
    validate(iphone_outputs, IPHONE_STORE_SIZE)
    validate(android_outputs, ANDROID_STORE_SIZE)

    for output in [*iphone_outputs, *android_outputs]:
        with Image.open(output) as image:
            if image.mode not in {"RGB", "L"}:
                raise RuntimeError(f"{output} includes an alpha channel")
        print(output.relative_to(ROOT))


def compose_store_screenshot(output_name: str, verb: str, descriptor: str, screenshot_path: Path) -> None:
    canvas = cover(Image.open(BACKGROUND), STORE_SIZE)

    verb_font = fit_font(verb, 1050, 222, 150)
    descriptor_font = fit_font(descriptor, 1050, 112, 78)
    draw_centered_with_shadow(canvas, verb, 165, verb_font)
    draw_centered_with_shadow(canvas, descriptor, 430, descriptor_font, shadow_offset=5)

    frame = Image.open(FRAME).convert("RGBA")
    device_x = (STORE_SIZE[0] - frame.width) // 2
    device_y = 675
    screen_x = device_x + 15
    screen_y = device_y + 15
    screen_w = frame.width - 30

    screenshot = Image.open(screenshot_path).convert("RGBA")
    screenshot = contain(screenshot, screen_w)

    screen_layer = Image.new("RGBA", STORE_SIZE, (0, 0, 0, 0))
    screen_draw = ImageDraw.Draw(screen_layer)
    screen_draw.rounded_rectangle(
        (screen_x, screen_y, screen_x + screen_w, STORE_SIZE[1] + 200),
        radius=62,
        fill=(0, 0, 0, 255),
    )
    screen_layer.alpha_composite(screenshot, (screen_x, screen_y))

    mask = Image.new("L", STORE_SIZE, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (screen_x, screen_y, screen_x + screen_w, STORE_SIZE[1] + 200),
        radius=62,
        fill=255,
    )
    screen_layer.putalpha(ImageChops_lighter(screen_layer.getchannel("A"), mask))
    canvas.alpha_composite(screen_layer)
    canvas.alpha_composite(frame, (device_x, device_y))

    output = FINAL / output_name
    canvas.convert("RGB").save(output, "PNG", optimize=True)


def ImageChops_lighter(alpha: Image.Image, mask: Image.Image) -> Image.Image:
    """Keep screenshot alpha while clipping the device screen to rounded corners."""
    # The screenshot is opaque; multiplying by the rounded mask gives the exact clip.
    return Image.composite(alpha, Image.new("L", alpha.size, 0), mask)


def compose_feature_graphic() -> None:
    canvas = cover(Image.open(FEATURE_BACKGROUND), FEATURE_SIZE)

    logo = contain(Image.open(LOGO).convert("RGBA"), 360)
    canvas.alpha_composite(logo, (62, 50))

    characters = contain(Image.open(CHARACTERS).convert("RGBA"), 535)
    canvas.alpha_composite(characters, (445, 155))

    draw = ImageDraw.Draw(canvas)
    tagline_font = ImageFont.truetype(str(FONT_BLACK), 46)
    body_font = ImageFont.truetype(str(FONT_BOLD), 24)
    draw.text((65, 260), "SPANDEER TYD", font=tagline_font, fill="white", stroke_width=2, stroke_fill=TEAL)
    draw.text((65, 313), "MET 'N STORIE.", font=tagline_font, fill="white", stroke_width=2, stroke_fill=TEAL)
    draw.text((67, 385), "Afrikaanse oudiostories vir die hele gesin", font=body_font, fill=TEAL)

    canvas.convert("RGB").save(FINAL / "google-play-feature-graphic.png", "PNG", optimize=True)


def validate(paths: Iterable[Path], expected: tuple[int, int]) -> None:
    for path in paths:
        with Image.open(path) as image:
            if image.size != expected:
                raise RuntimeError(f"{path} is {image.size}, expected {expected}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--ipad-only",
        action="store_true",
        help="Build only the 13-inch iPad App Store screenshot set.",
    )
    parser.add_argument(
        "--phone-only",
        action="store_true",
        help="Build only the matching iPhone and Google Play phone screenshot sets.",
    )
    args = parser.parse_args()

    if args.ipad_only and args.phone_only:
        parser.error("--ipad-only and --phone-only cannot be used together")

    if args.phone_only:
        compose_phone_campaigns()
        return

    if not args.ipad_only:
        FINAL.mkdir(parents=True, exist_ok=True)
        for output_name, verb, descriptor, screenshot in SCREENSHOTS:
            compose_store_screenshot(output_name, verb, descriptor, screenshot)
        compose_feature_graphic()

        screenshot_outputs = [FINAL / item[0] for item in SCREENSHOTS]
        validate(screenshot_outputs, STORE_SIZE)
        validate([FINAL / "google-play-feature-graphic.png"], FEATURE_SIZE)
        for output in [*screenshot_outputs, FINAL / "google-play-feature-graphic.png"]:
            print(output.relative_to(ROOT))

    IPAD_FINAL.mkdir(parents=True, exist_ok=True)
    for output_name, verb, descriptor, screenshot, characters in IPAD_SCREENSHOTS:
        compose_ipad_screenshot(output_name, verb, descriptor, screenshot, characters)

    ipad_outputs = [IPAD_FINAL / item[0] for item in IPAD_SCREENSHOTS]
    validate(ipad_outputs, IPAD_STORE_SIZE)
    for output in ipad_outputs:
        with Image.open(output) as image:
            if image.mode not in {"RGB", "L"}:
                raise RuntimeError(f"{output} includes an alpha channel")
        print(output.relative_to(ROOT))

    compose_phone_campaigns()


if __name__ == "__main__":
    main()

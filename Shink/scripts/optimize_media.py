#!/usr/bin/env python3
"""Batch-optimize website image assets in place.

Supported formats: .jpg/.jpeg, .png, .webp
The script preserves file paths so existing app references continue to work.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import os
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image

SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}


@dataclass(frozen=True)
class OptimizeConfig:
    jpeg_quality: int
    webp_quality: int
    min_savings_bytes: int
    min_savings_ratio: float
    dry_run: bool


@dataclass(frozen=True)
class OptimizeResult:
    path: Path
    original_size: int
    optimized_size: int
    optimized: bool
    skipped_reason: str | None = None

    @property
    def bytes_saved(self) -> int:
        return self.original_size - self.optimized_size


def iter_image_files(base_dir: Path, relative_paths: Iterable[str]) -> list[Path]:
    files: list[Path] = []
    for relative in relative_paths:
        root = (base_dir / relative).resolve()
        if not root.exists() or not root.is_dir():
            continue

        for current_root, _, names in os.walk(root):
            current_root_path = Path(current_root)
            # Skip third-party dependencies.
            if "lib" in {part.lower() for part in current_root_path.parts}:
                continue

            for name in names:
                path = current_root_path / name
                if path.suffix.lower() not in SUPPORTED_EXTENSIONS:
                    continue
                files.append(path)

    return files


def _save_image_to_temp(source: Path, destination: Path, config: OptimizeConfig) -> str | None:
    ext = source.suffix.lower()
    with Image.open(source) as image:
        exif = image.info.get("exif")
        icc_profile = image.info.get("icc_profile")

        save_kwargs: dict[str, object] = {}
        save_format: str
        image_to_save = image

        if ext in {".jpg", ".jpeg"}:
            # JPEG has no alpha channel; skip files that would require flattening.
            if "A" in image.getbands():
                return "alpha_jpeg"

            save_format = "JPEG"
            if image.mode not in {"RGB", "L"}:
                image_to_save = image.convert("RGB")

            save_kwargs.update(
                quality=config.jpeg_quality,
                optimize=True,
                progressive=True,
            )
            if exif:
                save_kwargs["exif"] = exif
            if icc_profile:
                save_kwargs["icc_profile"] = icc_profile

        elif ext == ".png":
            save_format = "PNG"
            save_kwargs.update(optimize=True, compress_level=9)

        elif ext == ".webp":
            save_format = "WEBP"
            is_lossless = bool(image.info.get("lossless", False))
            save_kwargs.update(method=6)
            if is_lossless:
                save_kwargs["lossless"] = True
            else:
                save_kwargs["quality"] = config.webp_quality
            if icc_profile:
                save_kwargs["icc_profile"] = icc_profile
            if exif:
                save_kwargs["exif"] = exif

        else:
            return "unsupported"

        image_to_save.save(destination, format=save_format, **save_kwargs)

    return None


def optimize_image(path: Path, config: OptimizeConfig) -> OptimizeResult:
    original_size = path.stat().st_size

    with tempfile.NamedTemporaryFile(
        delete=False,
        dir=str(path.parent),
        prefix=f"{path.name}.__opt_",
        suffix=path.suffix,
    ) as tmp_file:
        tmp_path = Path(tmp_file.name)

    try:
        skip_reason = _save_image_to_temp(path, tmp_path, config)
        if skip_reason:
            tmp_path.unlink(missing_ok=True)
            return OptimizeResult(
                path=path,
                original_size=original_size,
                optimized_size=original_size,
                optimized=False,
                skipped_reason=skip_reason,
            )

        optimized_size = tmp_path.stat().st_size

        saved_bytes = original_size - optimized_size
        saved_ratio = (saved_bytes / original_size) if original_size else 0.0
        should_replace = (
            saved_bytes >= config.min_savings_bytes
            and saved_ratio >= config.min_savings_ratio
        )

        if should_replace:
            if not config.dry_run:
                os.replace(tmp_path, path)
            else:
                tmp_path.unlink(missing_ok=True)

            return OptimizeResult(
                path=path,
                original_size=original_size,
                optimized_size=optimized_size,
                optimized=True,
            )

        tmp_path.unlink(missing_ok=True)
        return OptimizeResult(
            path=path,
            original_size=original_size,
            optimized_size=original_size,
            optimized=False,
            skipped_reason="no_gain",
        )

    except Exception as exc:  # pragma: no cover - best-effort batch tool
        tmp_path.unlink(missing_ok=True)
        return OptimizeResult(
            path=path,
            original_size=original_size,
            optimized_size=original_size,
            optimized=False,
            skipped_reason=f"error:{type(exc).__name__}",
        )


def format_bytes(value: int) -> str:
    units = ["B", "KB", "MB", "GB"]
    size = float(value)
    for unit in units:
        if size < 1024 or unit == units[-1]:
            return f"{size:.2f} {unit}"
        size /= 1024
    return f"{size:.2f} GB"


def main() -> int:
    parser = argparse.ArgumentParser(description="Optimize website image media in place.")
    parser.add_argument(
        "--base-dir",
        type=Path,
        default=Path("wwwroot"),
        help="Base directory that contains media folders.",
    )
    parser.add_argument(
        "--paths",
        nargs="+",
        default=["branding", "stories", "media"],
        help="Relative media paths under base-dir to optimize.",
    )
    parser.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 4) // 2))
    parser.add_argument("--jpeg-quality", type=int, default=82)
    parser.add_argument("--webp-quality", type=int, default=80)
    parser.add_argument("--min-savings-bytes", type=int, default=1024)
    parser.add_argument("--min-savings-ratio", type=float, default=0.01)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--top", type=int, default=20, help="Show top N largest savings.")
    args = parser.parse_args()

    base_dir = args.base_dir.resolve()
    files = iter_image_files(base_dir, args.paths)
    if not files:
        print("No supported image files found for optimization.")
        return 0

    config = OptimizeConfig(
        jpeg_quality=args.jpeg_quality,
        webp_quality=args.webp_quality,
        min_savings_bytes=args.min_savings_bytes,
        min_savings_ratio=args.min_savings_ratio,
        dry_run=args.dry_run,
    )

    print(f"Base dir: {base_dir}")
    print(f"Files found: {len(files)}")
    print(f"Dry run: {config.dry_run}")
    print(f"Workers: {args.workers}")

    results: list[OptimizeResult] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as executor:
        futures = [executor.submit(optimize_image, path, config) for path in files]
        for index, future in enumerate(concurrent.futures.as_completed(futures), start=1):
            results.append(future.result())
            if index % 250 == 0 or index == len(futures):
                print(f"Processed {index}/{len(futures)}")

    optimized = [result for result in results if result.optimized]
    failed = [result for result in results if result.skipped_reason and result.skipped_reason.startswith("error:")]

    total_original = sum(result.original_size for result in results)
    total_optimized = sum(result.optimized_size for result in results)
    total_saved = total_original - total_optimized

    print("\nSummary")
    print(f"Optimized files: {len(optimized)}")
    print(f"Failed files: {len(failed)}")
    print(f"Total before: {format_bytes(total_original)}")
    print(f"Total after : {format_bytes(total_optimized)}")
    print(f"Saved       : {format_bytes(total_saved)}")

    if optimized:
        print(f"\nTop {min(args.top, len(optimized))} savings")
        for item in sorted(optimized, key=lambda entry: entry.bytes_saved, reverse=True)[: args.top]:
            print(
                f"{item.bytes_saved:>10} bytes | "
                f"{item.original_size:>10} -> {item.optimized_size:>10} | "
                f"{item.path}"
            )

    if failed:
        print("\nSample failures")
        for item in failed[:10]:
            print(f"{item.skipped_reason}: {item.path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

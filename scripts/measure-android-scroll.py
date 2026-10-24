#!/usr/bin/env python3
"""Measure the currently visible Android page with repeatable up/down gestures.

Navigate to the same page/start position and let artwork load before each build.
Use a Release APK, the same emulator settings and at least three repetitions.
This only scrolls; it does not sign in, tap actions, clear data or install APKs.
"""

import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--package", default="com.schink.stories.mobile")
    parser.add_argument("--repetitions", type=int, default=3)
    parser.add_argument("--swipes", type=int, default=4, help="Swipes in each direction")
    parser.add_argument("--duration", type=int, default=450)
    parser.add_argument("--horizontal", action="store_true")
    parser.add_argument("--expect-text", help="Require visible text before each run, e.g. an account's unlocked-character count")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    def adb(*command):
        return subprocess.check_output(["adb", "-s", args.serial, *command])

    size = adb("shell", "wm", "size").decode()
    width, height = map(int, re.findall(r"(\d+)x(\d+)", size)[-1])
    if args.horizontal:
        start, end = (int(width * .85), int(height * .55)), (int(width * .15), int(height * .55))
    else:
        start, end = (int(width * .48), int(height * .76)), (int(width * .48), int(height * .28))

    (args.output / "device.txt").write_text(size + adb("shell", "getprop", "ro.build.version.sdk").decode())
    (args.output / "start.png").write_bytes(adb("exec-out", "screencap", "-p"))
    summaries = []
    for repetition in range(1, args.repetitions + 1):
        if args.expect_text:
            adb("shell", "uiautomator", "dump", "/sdcard/schink-scroll-check.xml")
            tree = ET.fromstring(adb("shell", "cat", "/sdcard/schink-scroll-check.xml"))
            if not any(args.expect_text in node.get("text", "") or
                       args.expect_text in node.get("content-desc", "")
                       for node in tree.iter("node")):
                raise RuntimeError(f"Expected page/account text is not visible: {args.expect_text!r}; discard this sample")
        pid = adb("shell", "pidof", args.package).strip()
        windows = adb("shell", "dumpsys", "window").decode()
        focus = re.search(r"mCurrentFocus=(.+)", windows)
        if not focus or args.package not in focus[1]:
            raise RuntimeError("The app must be in the foreground; dismiss system panels first")
        adb("shell", "dumpsys", "gfxinfo", args.package, "reset")
        for origin, destination in [(start, end), (end, start)]:
            for _ in range(args.swipes):
                adb("shell", "input", "swipe", *map(str, (*origin, *destination, args.duration)))
                if adb("shell", "pidof", args.package).strip() != pid:
                    raise RuntimeError("App process changed; stop gestures and discard this sample")
                time.sleep(.25)
        # Include the final fling and any deferred work after it settles.
        time.sleep(2)
        if adb("shell", "pidof", args.package).strip() != pid:
            raise RuntimeError("App process changed during the run; discard this sample")
        raw = adb("shell", "dumpsys", "gfxinfo", args.package, "framestats").decode()
        (args.output / f"run-{repetition}.txt").write_text(raw)
        summary = {"run": repetition}
        for name, expression, conversion in [
            ("frames", r"Total frames rendered: (\d+)", int),
            ("janky_frames", r"Janky frames: (\d+)", int),
            ("janky_percent", r"Janky frames: \d+ \(([\d.]+)%\)", float),
            ("legacy_janky_percent", r"Janky frames \(legacy\): \d+ \(([\d.]+)%\)", float),
            *[(f"p{p}_ms", rf"{p}th percentile: (\d+)ms", int) for p in (50, 90, 95, 99)],
        ]:
            match = re.search(expression, raw)
            if not match:
                raise RuntimeError(f"No {name} in gfxinfo; check the app is visible and rendering")
            summary[name] = conversion(match[1])
        if summary["frames"] == 0:
            raise RuntimeError("No frames rendered; this is not a scrolling sample")
        histogram = re.search(r"^HISTOGRAM: (.+)$", raw, re.MULTILINE)
        buckets = [(int(ms), int(count)) for ms, count in re.findall(r"(\d+)ms=(\d+)", histogram[1])]
        summary["frames_over_50ms"] = sum(count for ms, count in buckets if ms > 50)
        summary["frames_over_700ms"] = sum(count for ms, count in buckets if ms > 700)
        summaries.append(summary)
        print(json.dumps(summary), flush=True)
    (args.output / "end.png").write_bytes(adb("exec-out", "screencap", "-p"))
    result = {"package": args.package, "serial": args.serial, "size": [width, height],
              "swipes_per_direction": args.swipes, "duration_ms": args.duration,
              "horizontal": args.horizontal, "runs": summaries}
    result["expected_visible_text"] = args.expect_text
    (args.output / "summary.json").write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    main()

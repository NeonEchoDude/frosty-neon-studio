"""Make a clean copy of this repository that is fit to publish. Publishes nothing.

    python tools/release/make_public_copy.py <empty or new output folder>

What it does:
  * copies only files tracked by Git (so nothing ignored or untracked leaks out);
  * leaves out the private working files listed in EXCLUDE (agent instructions, the session status
    log, handover notes, reports with absolute paths) and the private-strings file itself;
  * replaces README.md with tools/release/PUBLIC_README.md;
  * removes the VRChat avatar ID from prefabs and scenes, so a stranger's upload creates their own avatar;
  * blanks the folder part of the source .blend path inside FBX files (see export_avatar_fbx.py);
  * replaces any private strings listed in tools/release/private_strings.json (kept out of the copy);
  * scans every copied file, text and binary, for user folders, e-mail addresses, avatar IDs and the
    private strings, and exits 1 if anything is left;
  * checks every picture against tools/release/reviewed_images.json (a text scan can't read pixels): a new or
    changed picture fails until someone has looked at it and run
        python tools/release/make_public_copy.py --approve-image <path>.

The copy has no Git history. To publish, push it to the public repository as a new commit. This script
never pushes or publishes anything itself.
"""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
PRIVATE_STRINGS = HERE / "private_strings.json"

REVIEWED_IMAGES = HERE / "reviewed_images.json"
IMAGE_SUFFIX = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tga", ".psd", ".tif", ".tiff", ".ico", ".icns"}
EXCLUDE_EXACT = {"CLAUDE.md", "PROJECT_STATUS.md", "README.md", "tools/release/private_strings.json",
                 "tools/release/reviewed_images.json", "tools/release/LAUNCH_CHECKLIST.md",
                 "tools/release/DISCORD_SETUP.md", "docs/RELEASE.md"}
# Logo drafts and the scripts that drew them: design history, not part of the brand set.
EXCLUDE_PREFIX = ("handover/", "brand/explorations/", "brand/tools/explore")
EXCLUDE_SUFFIX = ("_report.json",)
TEXT_SUFFIX = {".md", ".cs", ".py", ".json", ".ps1", ".meta", ".prefab", ".unity", ".mat", ".asset", ".txt",
               ".gitignore", ".vsconfig", ".yml", ".yaml", "", ".html", ".css", ".js", ".svg", ".xaml", ".uss", ".uxml",
               ".cmd", ".bat", ".asmdef", ".audiolink_pipeline", ".xml", ".csv", ".ini", ".cfg", ".toml", ".shader",
               ".cginc", ".hlsl", ".controller", ".anim", ".mask", ".overrideController"}

GENERIC_PATTERNS = {
    # Case-insensitive: Python, Unity and many logs write the drive and the Users folder in lower case.
    "Windows user folder": rb"(?i)[a-z]:[\\/]+users[\\/]+[^\\/\s\"']+",
    "e-mail address": rb"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
    "VRChat avatar ID": rb"avtr_[0-9a-f]{8}-[0-9a-f]{4}-",
}
# Addresses that are fine to publish (VRChat's and the SDK's own contact addresses inside metadata).
ALLOWED_EMAILS = {b"developer@vrchat.com", b"noreply@anthropic.com"}


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "-C", str(ROOT), "ls-files", "-z"], capture_output=True, check=True).stdout
    return [f for f in out.decode("utf-8").split("\0") if f]


def excluded(path: str) -> bool:
    return (path in EXCLUDE_EXACT or path.startswith(EXCLUDE_PREFIX) or path.endswith(EXCLUDE_SUFFIX))


def strip_fbx_source_path(data: bytes) -> bytes:
    """Same rule as export_avatar_fbx.strip_source_path, without needing Blender."""
    key = b"Original|ApplicationNativeFile"
    at = data.find(key)
    if at < 0:
        return data
    cursor = at + len(key)
    for _ in range(3):
        if data[cursor:cursor + 1] != b"S":
            return data
        cursor += 5 + int.from_bytes(data[cursor + 1:cursor + 5], "little")
    if data[cursor:cursor + 1] != b"S":
        return data
    length = int.from_bytes(data[cursor + 1:cursor + 5], "little")
    start = cursor + 5
    value = data[start:start + length]
    name = value.replace(b"\\", b"/").rsplit(b"/", 1)[-1]
    return data[:start] + b"_" * (length - len(name)) + name + data[start + length:]


def private_strings() -> list[tuple[str, str]]:
    if not PRIVATE_STRINGS.exists():
        return []
    table = json.loads(PRIVATE_STRINGS.read_text(encoding="utf-8"))
    return [(row["find"], row["replace"]) for row in table["replacements"]]


def scan(folder: Path, extra: list[str]) -> list[str]:
    hits = []
    patterns = {name: re.compile(p) for name, p in GENERIC_PATTERNS.items()}
    for number, text in enumerate((s for s in extra if s), 1):
        patterns[f"private string {number}"] = re.compile(re.escape(text.encode("utf-8")))
        # Windows tools often save text as UTF-16, which a scan for the UTF-8 bytes would miss.
        patterns[f"private string {number} (UTF-16)"] = re.compile(re.escape(text.encode("utf-16-le")))
    for file in sorted(p for p in folder.rglob("*") if p.is_file()):
        data = file.read_bytes()
        for name, pattern in patterns.items():
            for match in pattern.finditer(data):
                if name == "e-mail address" and match.group(0).lower() in ALLOWED_EMAILS:
                    continue
                hits.append(f"{file.relative_to(folder)}: {name}: {match.group(0)[:80]!r}")
    return hits


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reviewed_images() -> dict:
    return json.loads(REVIEWED_IMAGES.read_text(encoding="utf-8")) if REVIEWED_IMAGES.exists() else {}


def image_hits(folder: Path) -> list[str]:
    """Text scans can't read pixels: every picture must have been looked at (account names, avatar IDs, faces)."""
    reviewed = reviewed_images()
    hits = []
    for file in sorted(p for p in folder.rglob("*") if p.is_file() and p.suffix.lower() in IMAGE_SUFFIX):
        rel = file.relative_to(folder).as_posix()
        if reviewed.get(rel) != file_hash(file):
            hits.append(f"{rel}: picture not reviewed (or changed since). Look at it for account names, avatar IDs, "
                        f"user folders and faces, then: python tools/release/make_public_copy.py --approve-image {rel}")
    return hits


def approve_image(rel: str) -> int:
    path = ROOT / rel
    if not path.is_file():
        print(f"No such file: {rel}")
        return 1
    reviewed = reviewed_images()
    reviewed[Path(rel).as_posix()] = file_hash(path)
    REVIEWED_IMAGES.write_text(json.dumps(dict(sorted(reviewed.items())), indent=2) + "\n", encoding="utf-8")
    print(f"Marked as reviewed: {rel}")
    return 0


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == "--approve-image":
        return approve_image(sys.argv[2])
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    out = Path(sys.argv[1]).resolve()
    if out.exists() and (not out.is_dir() or any(out.iterdir())):
        print(f"Refusing to write into a folder that is not empty: {out}")
        return 1
    if ROOT in out.parents or out == ROOT:
        print("Choose an output folder outside the repository.")
        return 1
    replacements = private_strings()
    copied, skipped = 0, []
    for rel in tracked_files():
        source = ROOT / rel
        # A tracked symbolic link could point anywhere on this PC; its target is never copied.
        if excluded(rel) or source.is_symlink() or not source.is_file():
            skipped.append(rel)
            continue
        target = out / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        data = source.read_bytes()
        if rel.lower().endswith(".fbx"):
            data = strip_fbx_source_path(data)
        elif Path(rel).suffix.lower() in TEXT_SUFFIX:
            text = data.decode("utf-8", errors="surrogateescape")
            # Unity files: blank the ID so the field is empty, as before any upload. Other text: a placeholder.
            avatar_id = r"avtr_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
            unity_file = Path(rel).suffix.lower() in {".prefab", ".unity", ".asset"}
            text = re.sub(avatar_id, "" if unity_file else "<avatar ID>", text)
            for find, replace in replacements:
                text = text.replace(find, replace)
            data = text.encode("utf-8", errors="surrogateescape")
        target.write_bytes(data)
        copied += 1
    shutil.copyfile(HERE / "PUBLIC_README.md", out / "README.md")

    hits = scan(out, [find for find, _ in replacements]) + image_hits(out)
    print(f"Copied {copied} files to {out}; left out {len(skipped)}: {', '.join(skipped[:12])}{' ...' if len(skipped) > 12 else ''}")
    if hits:
        print(f"{len(hits)} possible personal detail(s) remain; fix them before publishing:")
        for hit in hits[:50]:
            print("  " + hit)
        return 1
    print("Scan clean: no user folders, e-mail addresses, avatar IDs or listed private strings found.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

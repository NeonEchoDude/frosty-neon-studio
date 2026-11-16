"""Cuts the website's pictures from the two reviewed app screenshots (no other source).

    python brand/tools/site_images.py

Every output is a crop of docs/images/frosty_neon_studio.png or avatar_guide.png,
saved as lossless WebP (small, sharp text; every current browser shows it).
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "site", "assets")
studio = Image.open(os.path.join(ROOT, "docs", "images", "frosty_neon_studio.png")).convert("RGB")
guide = Image.open(os.path.join(ROOT, "docs", "images", "avatar_guide.png")).convert("RGB")

CROPS = {
    "app-window": (studio, (16, 16, 1151, 648)),      # the window, without the outer margin and the activity log
    "step-pick": (studio, (16, 232, 324, 648)),       # 2 PICK YOUR AVATAR
    "step-check": (studio, (336, 100, 1004, 438)),     # verdict, badges, the three pictures
    "step-fix": (studio, (346, 440, 1068, 486)),      # Fix it for me / Make a Quest version row
    "step-upload": (studio, (346, 484, 618, 530)),    # Upload for PC / for Quest
    "step-findings": (guide, (14, 488, 490, 672)),    # findings explained, from the Unity Avatar Guide
}

for name, (im, box) in CROPS.items():
    c = im.crop(box)
    c.save(os.path.join(OUT, name + ".webp"), lossless=True, method=6)
    print(name, c.size)

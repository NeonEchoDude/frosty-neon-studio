# Frosty Neon Studio brand

Every file here is built by [tools/build_brand.py](tools/build_brand.py): run `python brand/tools/build_brand.py` (needs Pillow and fontTools; it reads Inter from Blender 5.2's install, or from the path in `INTER_FONT`). There is no VRChat artwork and no generated imagery.

## The mark

**The chevron crystal:** six solid spikes on the 60-degree grid of a snow crystal. Near each tip a chevron-shaped gap splits off an arrowhead. A hexagonal well at the centre holds a cyan core, the mark's only colour. Solid shapes only: no outlines, gradients or glow.

Small sizes are drawn separately, not shrunk:

- **48 px and up** (and every SVG except the favicon): the full mark.
- **32 px:** wider cuts and a bigger core.
- **24 and 16 px:** no cuts, just six solid points around a large core. They are placed on the pixel grid, with the core's sides on whole pixels (8 and 6 px wide).

| File | Use |
| --- | --- |
| `logo.svg` | Mark and wordmark, for dark backgrounds (the default). |
| `logo-on-light.svg` | The same in navy with a deeper cyan, for light backgrounds. |
| `mark.svg` | The mark alone, transparent background. |
| `icon.ico` | Windows app and shortcut icon: 16, 24, 32, 48, 64, 128 and 256 px, on a navy tile. |
| `icon-512.png` | Discord server icon: full-bleed navy, so the round crop never cuts the mark. |
| `social-card.png` | 1200 x 630 link preview (also in `site/assets/`). |

The site uses `site/assets/icon.svg` (full mark on the tile), `favicon.svg` (the 16 px drawing), `favicon-32.png`, `apple-touch-icon.png` and `logo.svg`, all from the same script.

Rules: keep clear space of at least one spike's length around the mark. Don't recolour it (ice white or navy, cyan core), outline it, add glow or shadows, rotate or stretch it. Below 160 px wide, use the mark or icon instead of the full logo.

## Colours

| Colour | Hex | Role |
| --- | --- | --- |
| Navy | `#0D0F1E` | Background. Everything sits on it. The mark on light backgrounds. |
| Card | `#181B35` | Raised surfaces; `#11142A` for wells, `#2A2F5A` for hairlines and the icon tile's rim. |
| Ice white | `#E8ECFF` | Main text, the wordmark and the mark on dark. |
| Mist | `#9AA3D6` | Secondary text and "Studio" in the wordmark (7.5:1 on navy). |
| Cyan | `#3EF2FF` | The mark's core, links, focus rings. On light backgrounds: `#0A8C9C`. |
| Pink | `#FF4FD8` | The one main action per view (Download, Upload). Navy text on it. Never in the mark. |
| Violet | `#8A5CFF` | Lines and decoration only. As text use `#B39BFF`, because plain violet fails AA. |

## Type

The wordmark is set in **Inter**: "Frosty Neon" in Bold (700), "Studio" in Regular (400), tracked -1.5 % with the font's own kerning. The capitals are centred on the mark. In the SVG logos the letters are converted to outlines, so no font file ships.

Inter is copyright 2016 The Inter Project Authors and is licensed under the SIL Open Font License, Version 1.1 (https://openfontlicense.org). The OFL allows its use in logos and pictures. The font itself, if ever shipped, must keep its licence and not be sold on its own.

The website's text uses system fonts: Segoe UI Variable / Segoe UI on Windows, with the system UI font elsewhere. Labels and file names use Cascadia Mono or Consolas. No web fonts are loaded.

## Voice

1. Plain words: say what happens, not how amazing it is.
2. Specific: name the file, the button, the step (".vrm", "Fix it for me", "Avatars > Uploaded").
3. Short: one idea per sentence; cut every word that isn't doing work.
4. Honest: say what isn't tested yet, and that the SDK's own build is the final word.
5. Warm, not cute: a friendly line is fine ("Looking fresh!"), but no hype, no emoji bullets.

Frosty Neon Studio is fan-made and not affiliated with VRChat Inc. Never use VRChat's logo or imply endorsement.

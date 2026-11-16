"""Builds every brand file from the geometry below. Needs Python with Pillow and fontTools.

    python brand/tools/build_brand.py

Writes brand/mark.svg, logo.svg, logo-on-light.svg, icon.ico, icon-512.png,
social-card.png, the site's icon, favicons, logo and social card, and a check sheet
in brand/explorations/. The mark is drawn here by hand. The wordmark is Inter
(SIL Open Font License 1.1), converted to outlines by brand/tools/inter.py, so no
font file ships.
"""
import math
import os
import sys
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(__file__))
from vec import Path, Shape, to_svg, render
import inter

HERE = os.path.dirname(os.path.abspath(__file__))
BRAND = os.path.dirname(HERE)
ROOT = os.path.dirname(BRAND)
SITE_ASSETS = os.path.join(ROOT, "site", "assets")

NAVY, CARD, RIM, ICE, MIST = "#0D0F1E", "#181B35", "#2A2F5A", "#E8ECFF", "#9AA3D6"
CYAN, CYAN_DEEP = "#3EF2FF", "#0A8C9C"
DARK = {"fg": ICE, "core": CYAN, "text": ICE, "text2": MIST}
LIGHT = {"fg": NAVY, "core": CYAN_DEEP, "text": NAVY, "text2": "#4A5180"}


def pol(cx, cy, r, deg):
    return (cx + r * math.cos(math.radians(deg)), cy + r * math.sin(math.radians(deg)))


def poly(*pts):
    return Path().poly(*pts, closed=True)


def local(c, a, pts):
    """Points given as (along, across) an arm pointing at angle a, to x/y."""
    ux, uy = math.cos(math.radians(a)), math.sin(math.radians(a))
    return [(c[0] + r * ux - s * uy, c[1] + r * uy + s * ux) for r, s in pts]


# ---------------------------------------------------------------- the mark
# The chevron crystal: six solid spikes on the 60-degree grid of a snow crystal.
# Near each tip a chevron-shaped gap splits off an arrowhead. At the centre a
# hexagonal well, cut where the spikes meet, holds a cyan core: the only accent.
#
# Each size has its own geometry (units are the drawing box: 100 for the master,
# pixels for 16 and 24):
#   master (SVG, 48 px and up)  every cut
#   32 px                       wider cuts, bigger core
#   24 and 16 px                no cuts: six solid kites and a big core, on the pixel grid
SPEC = {
    "master": dict(box=100, h=7.5, tip=48, shoulder=38, cut=27, dv=7.5, gap=3.6, well=12.5, core=8.0),
    32: dict(box=100, h=9.0, tip=48, shoulder=37, cut=28, dv=9.0, gap=6.5, well=16.0, core=11.0),
    # core radius r makes the hexagon r * sqrt(3) wide: 8 px and 6 px, so its sides sit on pixel edges
    24: dict(box=24, h=2.4, tip=11.7, shoulder=8.5, cut=None, well=6.4, core=8 / math.sqrt(3)),
    16: dict(box=16, h=1.6, tip=7.8, shoulder=5.6, cut=None, well=4.5, core=6 / math.sqrt(3)),
}


def mark_shapes(spec="master", theme=DARK):
    p = SPEC[spec]
    c = (p["box"] / 2, p["box"] / 2)
    h, R = p["h"], p["well"]
    notch = R - h / math.sqrt(3)       # where a spike's edges meet the well's sides
    shapes = []
    for k in range(6):
        a = -90 + 60 * k
        if p["cut"] is None:
            arm = [(notch, -h), (p["shoulder"], -h), (p["tip"], 0), (p["shoulder"], h), (notch, h), (R, 0)]
            shapes.append(Shape(poly(*local(c, a, arm)), theme["fg"]))
            continue
        a1, dv, g = p["cut"], p["dv"], p["gap"]
        inner = [(notch, -h), (a1 - dv, -h), (a1, 0), (a1 - dv, h), (notch, h), (R, 0)]
        head = [(a1 - dv + g, -h), (p["shoulder"], -h), (p["tip"], 0), (p["shoulder"], h), (a1 - dv + g, h), (a1 + g, 0)]
        shapes += [Shape(poly(*local(c, a, inner)), theme["fg"]), Shape(poly(*local(c, a, head)), theme["fg"])]
    shapes.append(Shape(poly(*[pol(*c, p["core"], -90 + 60 * k) for k in range(6)]), theme["core"]))
    return shapes


def rounded_square(size, r):
    p = Path()
    p.M(r, 0).L(size - r, 0).A(size - r, r, r, -90, 0).L(size, size - r).A(size - r, size - r, r, 0, 90)
    p.L(r, size).A(r, size - r, r, 90, 180).L(0, r).A(r, r, r, 180, 270).Z()
    return p


def icon_image(px):
    """App icon: the mark on a navy tile. 16 and 24 px use their own pixel-grid drawing."""
    tile = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    ImageDraw.Draw(tile).rounded_rectangle([0, 0, px - 1, px - 1], round(px * 0.2), fill=NAVY)
    if px >= 48:  # a faint rim separates the tile from dark taskbars
        w = max(1, round(px / 64))
        ImageDraw.Draw(tile).rounded_rectangle([0, 0, px - 1, px - 1], round(px * 0.2), outline=RIM, width=w)
    if px in (16, 24):
        tile.alpha_composite(render(mark_shapes(px), (0, 0, px, px), px, ss=8))
        return tile
    inset = 0.13 if px >= 48 else 0.06
    s = round(px * (1 - 2 * inset))
    m = render(mark_shapes(32 if px <= 32 else "master"), (0, 0, 100, 100), s, ss=8)
    tile.alpha_composite(m, ((px - s) // 2, (px - s) // 2))
    return tile


def full_bleed(px, frac):
    """Square navy picture with the mark centred: Discord and Apple add their own corners."""
    im = Image.new("RGBA", (px, px), NAVY)
    s = round(px * frac)
    im.alpha_composite(render(mark_shapes(), (0, 0, 100, 100), s), ((px - s) // 2, (px - s) // 2))
    return im.convert("RGB")


# ---------------------------------------------------------------- the lockup
# Mark (48 x 48) + "Frosty Neon" in Inter Bold + "Studio" in Inter Regular.
# The capitals are centred on the mark; viewBox height 48.
LOCK_H, TEXT_SIZE, TEXT_X = 48.0, 27.5, 61.0


def lockup_layout():
    cap = 0.727 * TEXT_SIZE                    # Inter's cap height is 0.727 em
    base = LOCK_H / 2 + cap / 2
    bold, end = inter.layout("Frosty Neon", 700, TEXT_X, base, TEXT_SIZE)
    reg, end = inter.layout("Studio", 400, end + TEXT_SIZE * 0.27, base, TEXT_SIZE)
    return bold, reg, end + 1


def lockup_svg(theme):
    bold, reg, W = lockup_layout()
    s = 48 / 100
    mark = mark_shapes("master", theme)
    for sh in mark:
        sh.path = scaled(sh.path, s)
    body = to_svg(mark, (0, 0, W, LOCK_H), title="Frosty Neon Studio")
    text = (f'  <path d="{inter.svg_path(700, bold)}" fill="{theme["text"]}"/>\n'
            f'  <path d="{inter.svg_path(400, reg)}" fill="{theme["text2"]}"/>\n')
    return body.replace("</svg>", text + "</svg>"), W


def scaled(path, s, dx=0.0, dy=0.0):
    q = Path()
    for pts, closed in path.subs:
        q.poly(*[(dx + x * s, dy + y * s) for x, y in pts], closed=closed)
    return q


def lockup_png(theme, height):
    """The lockup as a transparent PNG, height px tall (for the social card and checks)."""
    bold, reg, W = lockup_layout()
    ss = 4
    k = height * ss / LOCK_H
    im = Image.new("RGBA", (round(W * k), round(LOCK_H * k)), (0, 0, 0, 0))
    im.alpha_composite(render(mark_shapes("master", theme), (0, 0, 100, 100), round(48 * k), ss=2))
    inter.draw(im, 700, bold, k, theme["text"])
    inter.draw(im, 400, reg, k, theme["text2"])
    return im.resize((round(W * height / LOCK_H), height), Image.LANCZOS)


# ---------------------------------------------------------------- social card
def social_card():
    W, H = 1200, 630
    im = Image.new("RGBA", (W, H), NAVY)
    # the mark, very large and quiet, running off the right edge
    big = render([Shape(sh.path, "#141731") for sh in mark_shapes()], (0, 0, 100, 100), 760)
    im.alpha_composite(big, (830, -65))
    im.alpha_composite(lockup_png(DARK, 88), (88, 132))
    head = inter.layout("Your avatar into VRChat,", 600, 0, 0, 58)[0]
    head2 = inter.layout("without learning Unity.", 600, 0, 0, 58)[0]
    sub = inter.layout("Checks it with VRChat's rules, fixes import settings, makes a Quest version.", 400, 0, 0, 27, 0)[0]
    foot = inter.layout("Free and open source  ·  Windows", 500, 0, 0, 27, 0)[0]

    def put(placed, x, y, weight, color):
        inter.draw(im, weight, [(c, g, gx + x, y, sz) for c, g, gx, _, sz in placed], 1.0, color)

    put(head, 88, 340, 600, ICE)
    put(head2, 88, 408, 600, ICE)
    put(sub, 88, 470, 400, MIST)
    ImageDraw.Draw(im).line([(88, 518), (W - 88, 518)], fill=RIM, width=2)
    put(foot, 88, 566, 500, CYAN)
    return im.convert("RGB")


# ---------------------------------------------------------------- output
def write(path, text):
    with open(path, "w", newline="\n", encoding="utf-8") as f:
        f.write(text)


def tile_svg(spec, size_hint):
    """The mark on the navy tile as an SVG (site icon and favicon)."""
    if spec in (16, 24):
        box = SPEC[spec]["box"]
        shapes = mark_shapes(spec)
        tile = f'  <rect width="{box}" height="{box}" rx="{box * 0.2:g}" fill="{NAVY}"/>'
        return to_svg(shapes, (0, 0, box, box), before=tile)
    inset = 13
    shapes = mark_shapes(spec)
    for sh in shapes:
        sh.path = scaled(sh.path, (100 - 2 * inset) / 100, inset, inset)
    tile = f'  <rect width="100" height="100" rx="20" fill="{NAVY}"/>\n  <rect x=".75" y=".75" width="98.5" height="98.5" rx="19.4" fill="none" stroke="{RIM}" stroke-width="1.5"/>'
    return to_svg(shapes, (0, 0, 100, 100), before=tile)


def main():
    os.makedirs(SITE_ASSETS, exist_ok=True)
    write(os.path.join(BRAND, "mark.svg"), to_svg(mark_shapes(), (0, 0, 100, 100), title="Frosty Neon Studio"))
    logo, _ = lockup_svg(DARK)
    write(os.path.join(BRAND, "logo.svg"), logo)
    write(os.path.join(SITE_ASSETS, "logo.svg"), logo)
    write(os.path.join(BRAND, "logo-on-light.svg"), lockup_svg(LIGHT)[0])
    # favicon.svg is shown at 16 to 32 px, so it uses the 16 px drawing; icon.svg is the full mark
    write(os.path.join(SITE_ASSETS, "favicon.svg"), tile_svg(16, 16))
    write(os.path.join(SITE_ASSETS, "icon.svg"), tile_svg("master", 100))

    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = {s: icon_image(s) for s in sizes}
    imgs[256].save(os.path.join(BRAND, "icon.ico"), sizes=[(s, s) for s in sizes],
                   append_images=[imgs[s] for s in sizes if s != 256])
    full_bleed(512, 0.62).save(os.path.join(BRAND, "icon-512.png"), optimize=True)
    imgs[32].save(os.path.join(SITE_ASSETS, "favicon-32.png"), optimize=True)
    full_bleed(180, 0.66).save(os.path.join(SITE_ASSETS, "apple-touch-icon.png"), optimize=True)
    card = social_card()
    card.save(os.path.join(BRAND, "social-card.png"), optimize=True)
    card.save(os.path.join(SITE_ASSETS, "social-card.png"), optimize=True)
    check_sheet(imgs)
    print("ok")


def check_sheet(imgs):
    """brand/explorations/final-check.png: every icon size, the circle crop and both logos."""
    W, H = 1500, 900
    im = Image.new("RGB", (W, H), "#08091A")
    x = 40
    for s in sorted(imgs):
        im.paste(imgs[s], (x, 40), imgs[s]); x += s + 24
    x = 40
    for s in (16, 24, 32, 48):
        z = imgs[s].resize((s * 6, s * 6), Image.NEAREST)
        im.paste(z, (x, 330), z); x += s * 6 + 24
    disc = full_bleed(200, 0.62)
    mask = Image.new("L", (800, 800), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, 799, 799], fill=255)
    im.paste(Image.new("RGB", (240, 240), "#313338"), (1220, 330))
    im.paste(disc, (1240, 350), mask.resize((200, 200), Image.LANCZOS))
    im.paste(Image.new("RGB", (W, 260), "#F4F5FB"), (0, 640))
    for i, s in enumerate((16, 24, 32, 48)):
        im.paste(imgs[s], (40 + i * 80, 690), imgs[s])
    for theme, y, bg in ((DARK, 40, None), (LIGHT, 790, None)):
        lk = lockup_png(theme, 64)
        im.paste(lk, (860 if theme is DARK else 420, y + (200 if theme is DARK else 0)), lk)
    os.makedirs(os.path.join(BRAND, "explorations"), exist_ok=True)
    im.save(os.path.join(BRAND, "explorations", "final-check.png"))


if __name__ == "__main__":
    main()

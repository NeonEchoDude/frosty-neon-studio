"""Tiny vector kit: one shape description, written as SVG and drawn with Pillow.

Only what the Frosty Neon marks need: lines, circular arcs, strokes with round
caps and joins, fills, solid colours and linear gradients. Raster output is
supersampled so edges are smooth; the SVG and the PNGs come from the same numbers.
"""
import math
from PIL import Image, ImageChops, ImageDraw, ImageFilter


def _fmt(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s == "-0" else s


class Path:
    def __init__(self):
        self.cmds = []      # SVG path commands
        self.subs = []      # [ [points], closed ]
        self.cur = None

    def M(self, x, y):
        self.cmds.append(f"M{_fmt(x)} {_fmt(y)}")
        self.subs.append([[(x, y)], False])
        self.cur = (x, y)
        return self

    def L(self, x, y):
        self.cmds.append(f"L{_fmt(x)} {_fmt(y)}")
        self.subs[-1][0].append((x, y))
        self.cur = (x, y)
        return self

    def A(self, cx, cy, r, a0, a1):
        """Arc around (cx, cy) from angle a0 to a1 in degrees (screen coordinates,
        y down, so increasing angle turns clockwise). Starts a subpath if needed."""
        sx, sy = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
        if self.cur is None or math.dist(self.cur, (sx, sy)) > 1e-6:
            if self.cur is None:
                self.M(sx, sy)
            else:
                self.L(sx, sy)
        span = a1 - a0
        steps = max(2, int(abs(span) / 4))
        pts = [(cx + r * math.cos(math.radians(a0 + span * i / steps)),
                cy + r * math.sin(math.radians(a0 + span * i / steps))) for i in range(1, steps + 1)]
        # SVG cannot draw a full circle in one arc; split at 180 degrees.
        pieces = [(a0, a1)] if abs(span) <= 180 else [(a0, a0 + span / 2), (a0 + span / 2, a1)]
        for b0, b1 in pieces:
            ex, ey = cx + r * math.cos(math.radians(b1)), cy + r * math.sin(math.radians(b1))
            large = 1 if abs(b1 - b0) > 180 else 0
            sweep = 1 if b1 > b0 else 0
            self.cmds.append(f"A{_fmt(r)} {_fmt(r)} 0 {large} {sweep} {_fmt(ex)} {_fmt(ey)}")
        self.subs[-1][0].extend(pts)
        self.cur = pts[-1]
        return self

    def Z(self):
        self.cmds.append("Z")
        self.subs[-1][1] = True
        self.cur = None
        return self

    def poly(self, *pts, closed=False):
        self.M(*pts[0])
        for p in pts[1:]:
            self.L(*p)
        if closed:
            self.Z()
        return self

    def circle(self, cx, cy, r):
        self.A(cx, cy, r, -90, 270)
        return self.Z()

    @property
    def d(self):
        return " ".join(self.cmds)


class Shape:
    def __init__(self, path, paint, width=None, opacity=1.0, rule="evenodd"):
        self.path, self.paint, self.width, self.opacity = path, paint, width, opacity  # width None = fill
        self.rule = rule  # "nonzero" for type outlines, which overlap inside a glyph


class Grad:
    def __init__(self, gid, x1, y1, x2, y2, stops):
        self.id, self.x1, self.y1, self.x2, self.y2, self.stops = gid, x1, y1, x2, y2, stops


def _rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def to_svg(shapes, viewbox, grads=(), extra_defs="", before="", after="", title=None, size=None):
    x, y, w, h = viewbox
    dims = f' width="{size[0]}" height="{size[1]}"' if size else ""
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{_fmt(x)} {_fmt(y)} {_fmt(w)} {_fmt(h)}"{dims}'
           + (' role="img" aria-label="' + title + '"' if title else "") + ">"]
    if title:
        out.append(f"  <title>{title}</title>")
    if grads or extra_defs:
        out.append("  <defs>")
        for g in grads:
            out.append(f'    <linearGradient id="{g.id}" gradientUnits="userSpaceOnUse" x1="{_fmt(g.x1)}" y1="{_fmt(g.y1)}" x2="{_fmt(g.x2)}" y2="{_fmt(g.y2)}">')
            for off, col in g.stops:
                out.append(f'      <stop offset="{_fmt(off)}" stop-color="{col}"/>')
            out.append("    </linearGradient>")
        if extra_defs:
            out.append(extra_defs)
        out.append("  </defs>")
    if before:
        out.append(before)
    for s in shapes:
        paint = f"url(#{s.paint.id})" if isinstance(s.paint, Grad) else s.paint
        op = f' opacity="{_fmt(s.opacity)}"' if s.opacity < 1 else ""
        if s.width is None:
            out.append(f'  <path d="{s.path.d}" fill="{paint}" fill-rule="{s.rule}"{op}/>')
        else:
            out.append(f'  <path d="{s.path.d}" fill="none" stroke="{paint}" stroke-width="{_fmt(s.width)}" '
                       f'stroke-linecap="round" stroke-linejoin="round"{op}/>')
    if after:
        out.append(after)
    out.append("</svg>")
    return "\n".join(out) + "\n"


def _mask(shape, viewbox, W, H, ss):
    x0, y0, vw, vh = viewbox
    k = W * ss / vw
    m = Image.new("L", (W * ss, H * ss), 0)
    d = ImageDraw.Draw(m)
    tf = lambda p: ((p[0] - x0) * k, (p[1] - y0) * k)
    if shape.width is None and shape.rule == "nonzero":
        # outlines wound like the outer contour add, the others (counters) cut
        area = lambda P: sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(P, P[1:] + P[:1])) / 2
        subs = [[tf(p) for p in pts] for pts, _ in shape.path.subs]
        outer = max(subs, key=lambda P: abs(area(P)))
        sign = area(outer) > 0
        pos, neg = Image.new("L", m.size, 0), Image.new("L", m.size, 0)
        for P in subs:
            ImageDraw.Draw(pos if (area(P) > 0) == sign else neg).polygon(P, fill=255)
        return ImageChops.subtract(pos, neg)
    for pts, closed in shape.path.subs:
        P = [tf(p) for p in pts]
        if shape.width is None:
            # even-odd: each outline flips what is under it, so inner outlines are holes
            t = Image.new("L", m.size, 0)
            ImageDraw.Draw(t).polygon(P, fill=255)
            m = ImageChops.difference(m, t)
            d = ImageDraw.Draw(m)
            continue
        w = shape.width * k
        r = w / 2
        if closed:
            P = P + [P[0]]
        # each segment as a thick line, a disc at every vertex: exact round caps and joins
        for a, b in zip(P, P[1:]):
            d.line([a, b], fill=255, width=max(1, round(w)))
        for (px, py) in P:
            d.ellipse([px - r, py - r, px + r, py + r], fill=255)
    return m


def _paint_img(paint, viewbox, W, H, ss):
    if not isinstance(paint, Grad):
        return Image.new("RGB", (W * ss, H * ss), _rgb(paint))
    import numpy as np
    x0, y0, vw, vh = viewbox
    k = W * ss / vw
    ys, xs = np.mgrid[0:H * ss, 0:W * ss].astype(float)
    px, py = xs / k + x0, ys / k + y0
    dx, dy = paint.x2 - paint.x1, paint.y2 - paint.y1
    t = np.clip(((px - paint.x1) * dx + (py - paint.y1) * dy) / (dx * dx + dy * dy), 0, 1)
    offs = [o for o, _ in paint.stops]
    cols = np.array([_rgb(c) for _, c in paint.stops], float)
    out = np.stack([np.interp(t, offs, cols[:, i]) for i in range(3)], -1)
    return Image.fromarray(out.astype("uint8"), "RGB")


def render(shapes, viewbox, W, H=None, bg=None, ss=8, glow=0.0, glow_radius=0.06):
    """Draw shapes to an RGBA image W x H. glow adds a soft halo of the same colours."""
    H = H or W
    canvas = Image.new("RGBA", (W * ss, H * ss), (0, 0, 0, 0) if bg is None else _rgb(bg) + (255,))
    layers = []
    for s in shapes:
        m = _mask(s, viewbox, W, H, ss)
        if s.opacity < 1:
            m = m.point(lambda v, o=s.opacity: int(v * o))
        layer = _paint_img(s.paint, viewbox, W, H, ss).convert("RGBA")
        layer.putalpha(m)
        layers.append(layer)
    if glow:
        halo = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        for l in layers:
            halo = Image.alpha_composite(halo, l)
        halo = halo.filter(ImageFilter.GaussianBlur(W * ss * glow_radius))
        a = halo.getchannel("A").point(lambda v: int(min(255, v * glow)))
        halo.putalpha(a)
        canvas = Image.alpha_composite(canvas, halo)
    for l in layers:
        canvas = Image.alpha_composite(canvas, l)
    return canvas.resize((W, H), Image.LANCZOS if ss > 1 else Image.NEAREST)

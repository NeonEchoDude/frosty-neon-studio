"""Inter for the wordmark: outlines for SVG and FreeType rendering for PNG, same layout.

Inter is by The Inter Project Authors, under the SIL Open Font License 1.1. It is read
from Blender's bundled copy (or the path in the INTER_FONT environment variable); only
outlines and pictures leave this script, never the font file itself. A static instance
for FreeType is written to the temp folder, not the repository.
"""
import os
import tempfile

BLENDER_INTER = r"C:/Program Files/Blender Foundation/Blender 5.2/5.2/datafiles/fonts/Inter.woff2"
_cache = {}


def _source():
    path = os.environ.get("INTER_FONT", BLENDER_INTER)
    if not os.path.exists(path):
        raise SystemExit(f"Inter not found at {path}. Set INTER_FONT to Inter's .ttf/.woff2 (OFL, rsms.me/inter).")
    return path


def font(weight):
    """(TTFont static instance at weight and display optical size, kerning pairs)."""
    if weight not in _cache:
        from fontTools.ttLib import TTFont
        from fontTools.varLib.instancer import instantiateVariableFont
        f = TTFont(_source())
        if "fvar" in f:
            instantiateVariableFont(f, {"wght": weight, "opsz": 32}, inplace=True)
        f.flavor = None
        _cache[weight] = (f, _kerning(f))
    return _cache[weight]


def _kerning(f):
    """Pair kerning from GPOS (PairPos formats 1 and 2) as {(left, right): value}."""
    pairs = {}
    if "GPOS" not in f:
        return pairs
    for lookup in f["GPOS"].table.LookupList.Lookup:
        subs = lookup.SubTable
        if lookup.LookupType == 9:
            subs = [s.ExtSubTable for s in subs]
        elif lookup.LookupType != 2:
            continue
        for st in subs:
            if getattr(st, "LookupType", 2) != 2:
                continue
            if st.Format == 1:
                for g, ps in zip(st.Coverage.glyphs, st.PairSet):
                    for rec in ps.PairValueRecord:
                        pairs.setdefault((g, rec.SecondGlyph), getattr(rec.Value1, "XAdvance", 0) or 0)
            elif st.Format == 2:
                c1, c2 = st.ClassDef1.classDefs, st.ClassDef2.classDefs
                for g in st.Coverage.glyphs:
                    row = st.Class1Record[c1.get(g, 0)].Class2Record
                    for g2, k2 in c2.items():
                        x = getattr(row[k2].Value1, "XAdvance", 0) if row[k2].Value1 else 0
                        if x:
                            pairs.setdefault((g, g2), x)
    return pairs


def layout(text, weight, x, baseline, size, tracking=-0.015):
    """[(glyph name, x, baseline, size)], end x. Kerning from GPOS, tracking in em."""
    f, kern = font(weight)
    cmap, gs, upm = f.getBestCmap(), f.getGlyphSet(), f["head"].unitsPerEm
    k, pen, prev, out = size / upm, 0.0, None, []
    for ch in text:
        g = cmap[ord(ch)]
        if prev:
            pen += kern.get((prev, g), 0)
        out.append((ch, g, x + pen * k, baseline, size))
        pen += gs[g].width + tracking * upm
        prev = g
    return out, x + (pen - tracking * upm) * k


def svg_path(weight, placed):
    """One SVG path (nonzero fill) for laid-out glyphs, in viewBox units, y down."""
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen
    f, _ = font(weight)
    gs, upm = f.getGlyphSet(), f["head"].unitsPerEm
    pen = SVGPathPen(gs, ntos=lambda v: f"{v:.2f}".rstrip("0").rstrip("."))
    for ch, g, x, base, size in placed:
        k = size / upm
        gs[g].draw(TransformPen(pen, (k, 0, 0, -k, x, base)))
    return pen.getCommands()


def draw(img, weight, placed, scale, color):
    """Draw laid-out glyphs on a Pillow image; FreeType fills them (nonzero winding)."""
    from PIL import ImageDraw, ImageFont
    path = os.path.join(tempfile.gettempdir(), f"frosty-inter-{weight}.ttf")
    if not os.path.exists(path):
        font(weight)[0].save(path)
    d = ImageDraw.Draw(img)
    fonts = {}
    for ch, g, x, base, size in placed:
        px = round(size * scale)
        if px not in fonts:
            fonts[px] = ImageFont.truetype(path, px)
        d.text((x * scale, base * scale), ch, font=fonts[px], anchor="ls", fill=color)

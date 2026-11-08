"""Main menu text and cog sprites for Tick & Tock (1 px = 1 art pixel).

Text is drawn with the font, then the coverage is thresholded to hard pixels
and recoloured in layers (engraving highlight, outline, fill), so it stays
crisp at any scale, like the concept page did in the browser.

Writes into the output folder:
  title_<font>.png              "Tick & Tock" in brass, one per title font:
                                jersey (Jersey 10, 64 px), tiny (Tiny5, 56 px),
                                workbench (Workbench, 46 px), pixelify (Pixelify Sans, 52 px)
  label_<style>_<i>_<state>.png button labels (Play, Select Level, Settings,
                                Quit) for each plate material, idle and selected,
                                in Pixelify Sans 16 px
  menu_cog.png                  8-frame strip of 15x15 brass cogs (one tooth = 45 degrees)

Fonts come from the Google Fonts repository (SIL Open Font License):
github.com/google/fonts/tree/main/ofl/{jersey10,tiny5,workbench,pixelifysans}.
Point --fonts at a folder holding Jersey10.ttf, Tiny5.ttf, Workbench.ttf and
PixelifySans.ttf (the variable fonts as downloaded, renamed).

  py tools/menu/make_menu_text.py --fonts <folder> Assets/Art/UI/Menu
"""
import argparse
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


def hx(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


C = {k: hx(v) for k, v in dict(
    outline="22101e", edge="8040a8", spark="f4e0ff",
    o="1c0c06", b0="824616", b1="ba6e24", b2="f0b446", b3="fcec8c",
    cream="f4e6c0", plum="512243", plum2="733a49", sand="f6bd78", leather="4a2418",
).items()}

LABELS = ["Play", "Select Level", "Settings", "Quit"]
TITLE_FONTS = {
    "jersey": ("Jersey10.ttf", 64, None),
    "tiny": ("Tiny5.ttf", 56, None),
    "workbench": ("Workbench.ttf", 46, None),
    "pixelify": ("PixelifySans.ttf", 52, [400]),
}
LABEL_FONT = ("PixelifySans.ttf", 16, [400])


def font(folder, spec):
    name, size, axes = spec
    f = ImageFont.truetype(str(Path(folder) / name), size)
    if axes:
        f.set_variation_by_axes(axes)
    return f


def mask(text, f):
    """Hard-edged coverage of the text, cropped: (width, height, set of (x, y))."""
    l, t, r, b = f.getbbox(text)
    w, h = r - l + 8, b - t + 8
    im = Image.new("L", (w, h), 0)
    ImageDraw.Draw(im).text((4 - l, 4 - t), text, font=f, fill=255)
    px = im.load()
    on = {(x, y) for y in range(h) for x in range(w) if px[x, y] > 120}
    xs = [p[0] for p in on]
    ys = [p[1] for p in on]
    x0, y0 = min(xs), min(ys)
    on = {(x - x0, y - y0) for x, y in on}
    return max(xs) - x0 + 1, max(ys) - y0 + 1, on


def render(text, f, layers, rows=None, pad=3):
    """layers: [(dx, dy, colour or 'rows')], drawn in order over the glyph mask."""
    w, h, on = mask(text, f)
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    px = im.load()
    for dx, dy, col in layers:
        for (x, y) in on:
            c = rows(y / max(1, h - 1)) if col == "rows" else col
            px[x + pad + dx, y + pad + dy] = c + (255,)
    return im


def ring(col):
    return [(-1, 0, col), (1, 0, col), (0, -1, col), (0, 1, col)]


def label_layers(style, sel):
    if style == "stone":
        return [(0, 1, C["b3"]), (0, 0, C["plum"])] if sel else [(0, 1, C["sand"]), (0, 0, C["plum2"])]
    if style == "brass":
        return [(0, 1, C["b3"]), (0, 0, C["o"])] if sel else [(0, 1, C["b3"]), (0, 0, C["leather"])]
    if sel:
        return ring(C["edge"]) + [(0, 1, C["outline"]), (0, 0, C["spark"])]
    return ring(C["o"]) + [(0, 0, C["cream"])]


def title_rows(t):
    return C["b3"] if t < 0.22 else C["b2"] if t < 0.62 else C["b1"] if t < 0.85 else C["b0"]


def cog(frame):
    im = Image.new("RGBA", (15, 15), (0, 0, 0, 0))
    px = im.load()
    spin = frame * 45 / 8
    for y in range(15):
        for x in range(15):
            dx, dy = x - 7, y - 7
            r = math.hypot(dx, dy)
            a = (math.degrees(math.atan2(-dy, dx)) - spin) % 45
            outer = 6.6 if a < 22.5 else 5.2
            if r > outer + 0.8:
                continue
            if r > outer or r < 1.2:
                c = C["o"]
            elif r < 2.4:
                c = C["b1"]
            elif -dx - dy > 0.5:
                c = C["b3"] if r > outer - 1 else C["b2"]
            else:
                c = C["b1"]
            px[x, y] = c + (255,)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--fonts", required=True)
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)

    shadow = [(0, 3, C["outline"]), (1, 3, C["outline"])]
    for key, spec in TITLE_FONTS.items():
        f = font(a.fonts, spec)
        layers = shadow + ring(C["o"]) + [(1, 1, C["o"]), (0, 0, "rows")]
        render("Tick & Tock", f, layers, rows=title_rows).save(out / f"title_{key}.png")

    lf = font(a.fonts, LABEL_FONT)
    for style in ("stone", "brass", "rift"):
        for i, text in enumerate(LABELS):
            for sel in (False, True):
                render(text, lf, label_layers(style, sel)).save(out / f"label_{style}_{i}_{'sel' if sel else 'idle'}.png")

    strip = Image.new("RGBA", (15 * 8, 15), (0, 0, 0, 0))
    for k in range(8):
        strip.paste(cog(k), (k * 15, 0))
    strip.save(out / "menu_cog.png")
    print("wrote", out)


if __name__ == "__main__":
    main()

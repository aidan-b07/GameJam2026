"""Level select art for Tick & Tock (pixel art, 1 px = 1 art pixel).

Same sandstone, cracks, chipped corners and autumn ivy as the main menu plates
(make_menu_buttons.py) and the same thresholded text (make_menu_text.py).

Writes into the output folder:
  card_<n>_idle.png, card_<n>_sel.png   n = 1, the open level (Main Level). 140x120 canvas,
                                        the 116x100 card's top-left at (12, 10).
                                        A recessed window with a little vignette
                                        of the level, the level number in brass
                                        and the level's name engraved.
  card_<n>_locked.png                   n = 2..8, coming soon: weathered stone,
                                        boarded window, brass padlock, "Coming soon".
  header_select_level.png               "Select Level" in brass (Jersey 10, 48 px)
  back_idle.png, back_sel.png           84x34 canvas, 64x22 plate at (10, 6), "Back"

Open levels are NAMES and VIGNETTES below, in card order; the rest are locked.

  py tools/menu/make_level_select.py --fonts <folder> Assets/Art/UI/LevelSelect
"""
import argparse
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

NAMES = ["Main Level"]
LOCKED = range(len(NAMES) + 1, 9)


def hx(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


STONE = dict(out=hx("512243"), dark=hx("733a49"), shade=hx("b15c53"), base=hx("d97d58"),
             mid=hx("e08757"), light=hx("f2a062"), hi=hx("f9ae67"), top=hx("f6bd78"), mortar=hx("b76a58"))
# weathered: darker, greyer sandstone for the locked cards
WORN = dict(out=hx("3a2030"), dark=hx("543444"), shade=hx("7e5a58"), base=hx("9a7468"),
            mid=hx("a47c6c"), light=hx("ae8676"), hi=hx("b48e7c"), top=hx("bc9a86"), mortar=hx("806060"))
BRASS = dict(b0=hx("824616"), b1=hx("ba6e24"), b2=hx("f0b446"), b3=hx("fcec8c"))
DIM_BRASS = dict(b0=hx("4a3424"), b1=hx("6a4a32"), b2=hx("8a6a46"), b3=hx("a88c62"))
O = hx("1c0c06")
OUTLINE = hx("22101e")
IVY = [hx("512243"), hx("7e3c4e"), hx("9a3d42"), hx("cf5a39"), hx("e6884f")]
DEAD = [hx("3a1a2a"), hx("6a4a2a"), hx("8a6a3a"), hx("a88848"), hx("c8a868")]
STEM = [hx("3a1a2a"), hx("5c2a44")]
WOOD = dict(d=hx("401e0e"), m=hx("7a421e"), l=hx("a8622e"))
NIGHT = dict(k=hx("1a0e1c"), d=hx("28143a"), m=hx("3a2050"))
STEEL = dict(s1=hx("322a2e"), s2=hx("504648"), s3=hx("766a68"), s4=hx("a89c96"))
GLOW = dict(g0=hx("6a1e10"), g1=hx("c0401c"), g2=hx("f08a2a"), g3=hx("fcd06a"))


class Plate:
    """A rounded plate of size (w, h) at (mx, my) on a canvas of (cw, ch)."""

    def __init__(self, cw, ch, mx, my, w, h, r):
        self.cw, self.ch, self.mx, self.my, self.w, self.h, self.r = cw, ch, mx, my, w, h, r
        self.im = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        self.px = self.im.load()

    def inside(self, x, y):
        r = self.r
        cx = min(max(x + 0.5, r), self.w - r)
        cy = min(max(y + 0.5, r), self.h - r)
        return (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r

    def edge(self, x, y):
        for d in range(6):
            for dx, dy in ((d + 1, 0), (-d - 1, 0), (0, d + 1), (0, -d - 1)):
                if not self.inside(x + dx, y + dy):
                    return d
        return 6

    def put(self, x, y, c, plate=True):
        X, Y = (x + self.mx, y + self.my) if plate else (x, y)
        if 0 <= X < self.cw and 0 <= Y < self.ch:
            self.px[X, Y] = c + (255,)

    def clear(self, x, y):
        X, Y = x + self.mx, y + self.my
        if 0 <= X < self.cw and 0 <= Y < self.ch:
            self.px[X, Y] = (0, 0, 0, 0)

    def get(self, x, y, plate=True):
        X, Y = (x + self.mx, y + self.my) if plate else (x, y)
        if 0 <= X < self.cw and 0 <= Y < self.ch:
            return self.px[X, Y]
        return (0, 0, 0, 0)


def face(pl, rng, p, selected, block=38, rows=3):
    """Sandstone ashlar face; selected adds the brass inlay ring."""
    rh = pl.h / rows
    for y in range(pl.h):
        for x in range(pl.w):
            if not pl.inside(x, y):
                continue
            d = pl.edge(x, y)
            if d == 0:
                c = p["out"]
            elif d == 1:
                c = p["top"] if (y < pl.h / 2 and x < pl.w - 6) or x < 3 else p["shade"]
                if y >= pl.h - 3:
                    c = p["dark"]
            elif d == 2 and selected:
                c = BRASS["b2"] if y < pl.h / 2 else BRASS["b1"]
            elif d == 3 and selected:
                c = BRASS["b0"]
            else:
                row = int(y // rh)
                off = (block // 2) * (row % 2)
                c = p["light"] if selected else p["base"]
                if rows > 1 and any(y == int(rh * k) for k in range(1, rows)) and 3 < x < pl.w - 4:
                    c = p["mortar"]
                elif (x + off) % block == 0 and 3 < y < pl.h - 4:
                    c = p["mortar"]
                else:
                    n = rng.random()
                    if n < 0.025:
                        c = p["mid"] if not selected else p["hi"]
                    elif n < 0.045:
                        c = p["shade"] if not selected else p["base"]
            pl.put(x, y, c)


def crack(pl, rng, x, y, dx, steps, dark, light, keep_out=None):
    pts = []
    for _ in range(steps):
        if not pl.inside(x, y) or pl.edge(x, y) < 1:
            break
        if keep_out and keep_out(x, y):
            break
        pts.append((x, y))
        r = rng.random()
        if r < 0.55:
            x += dx
        elif r < 0.8:
            y += 1
        else:
            y -= 1
        y = max(2, min(pl.h - 3, y))
        if rng.random() < 0.12 and steps > 6:
            crack(pl, rng, x, y, dx, steps // 3, dark, light, keep_out)
    for (x, y) in pts:
        pl.put(x, y, dark)
        if pl.inside(x + 1, y + 1) and pl.edge(x + 1, y + 1) >= 1 and (x + 1, y + 1) not in pts:
            if not (keep_out and keep_out(x + 1, y + 1)):
                pl.put(x + 1, y + 1, light)


def chip(pl, rng):
    corner = rng.choice(("tl", "tr", "bl", "br"))
    x0 = 0 if corner in ("tl", "bl") else pl.w - 1
    y0 = 0 if corner in ("tl", "tr") else pl.h - 1
    sx = 1 if x0 == 0 else -1
    sy = 1 if y0 == 0 else -1
    depth = rng.randint(2, 3)
    nx = x0 + sx * rng.randint(9, 16)
    for k in range(depth):
        for t in range(depth - k + 1):
            pl.clear(nx + sx * t, y0 + sy * k)


def outline_alpha(pl, color):
    src = pl.im.copy().load()
    for y in range(pl.h):
        for x in range(pl.w):
            if src[x + pl.mx, y + pl.my][3] == 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                X, Y = x + dx + pl.mx, y + dy + pl.my
                if 0 <= X < pl.cw and 0 <= Y < pl.ch and src[X, Y][3] == 0 and pl.inside(x + dx, y + dy):
                    pl.put(x, y, color)
                    break


# ---- vines (as make_menu_buttons.py, generalised to any plate) ----------------

IVY_BIG = [".X.X.", "XXXXX", "XXXXX", ".XXX.", "..X.."]
IVY_MID = ["X.X", "XXX", ".X."]
IVY_SMALL = ["XX", "X."]


def leaf(pl, x, y, rng, palette, big=False):
    rows = IVY_BIG if big and rng.random() < 0.6 else IVY_MID if big or rng.random() < 0.6 else IVY_SMALL
    if rng.random() < 0.5:
        rows = [r[::-1] for r in rows]
    oy, ox = len(rows) // 2, len(rows[0]) // 2
    shape = [(cx - ox, cy - oy) for cy, r in enumerate(rows) for cx, ch in enumerate(r) if ch == "X"]
    dark, mid, light = palette[1], palette[rng.randint(2, 3)], palette[-1]
    for (dx, dy) in shape:
        lit = dy < 0 or (dy == 0 and dx < 0)
        c = light if lit and rng.random() < 0.7 else dark if dy >= len(rows) - oy - 1 else mid
        pl.put(x + dx, y + dy, c, plate=False)
    for dx, dy in shape:
        X, Y = x + dx, y + dy + 1
        if 0 <= X < pl.cw and 0 <= Y < pl.ch and pl.px[X, Y][3] == 0:
            pl.put(X, Y, palette[0], plate=False)


def vine(pl, rng, side, palette, length, creep=(6, 12), avoid=None):
    """Grows from the top corner on that side: along the top edge, then down the side."""
    if side == "left":
        x, y, dirx = pl.mx + rng.randint(2, 12), pl.my + rng.randint(0, 2), -1
    else:
        x, y, dirx = pl.mx + pl.w - 1 - rng.randint(2, 12), pl.my + rng.randint(0, 2), 1
    stem = []
    for _ in range(rng.randint(*creep)):
        stem.append((x, y))
        x += dirx
        if rng.random() < 0.3:
            y += rng.choice((-1, 0, 1))
        y = max(pl.my - 2, min(pl.my + 1, y))
    lo, hi = (pl.mx - pl.mx + 1, pl.mx + 1) if side == "left" else (pl.mx + pl.w - 2, pl.cw - 2)
    for _ in range(length):
        stem.append((x, y))
        y += 1
        if rng.random() < 0.35:
            x += rng.choice((-1, 1))
        x = max(lo, min(hi, x))
        if y >= pl.ch - 2:
            break
    for i, (sx, sy) in enumerate(stem):
        pl.put(sx, sy, STEM[i % 2] if i % 5 else STEM[0], plate=False)
    for i, (sx, sy) in enumerate(stem):
        if i % rng.randint(1, 3) == 0:
            lx, ly = sx + rng.choice((-1, 1)) * rng.randint(1, 2), sy + rng.randint(-1, 1)
            if avoid and avoid(lx - pl.mx, ly - pl.my):
                continue
            leaf(pl, lx, ly, rng, palette, big=rng.random() < 0.45)


def moss(pl, rng, palette, n=(2, 4)):
    for _ in range(rng.randint(*n)):
        x = pl.mx + rng.randint(12, pl.w - 12)
        for k in range(rng.randint(1, 4)):
            pl.put(x, pl.my + pl.h + k, palette[min(k, len(palette) - 1)] if k else STEM[0], plate=False)


# ---- text ---------------------------------------------------------------------

def font(folder, name, size, axes=None):
    f = ImageFont.truetype(str(Path(folder) / name), size)
    if axes:
        f.set_variation_by_axes(axes)
    return f


def mask(text, f):
    l, t, r, b = f.getbbox(text)
    w, h = r - l + 8, b - t + 8
    im = Image.new("L", (w, h), 0)
    ImageDraw.Draw(im).text((4 - l, 4 - t), text, font=f, fill=255)
    px = im.load()
    on = {(x, y) for y in range(h) for x in range(w) if px[x, y] > 120}
    xs = [p[0] for p in on]
    ys = [p[1] for p in on]
    x0, y0 = min(xs), min(ys)
    return max(xs) - x0 + 1, max(ys) - y0 + 1, {(x - x0, y - y0) for x, y in on}


def ring(col):
    return [(-1, 0, col), (1, 0, col), (0, -1, col), (0, 1, col)]


def stamp(pl, text, f, x, y, layers, rows=None):
    """Draws text with its glyph box top-left at plate (x, y). Returns (w, h)."""
    w, h, on = mask(text, f)
    for dx, dy, col in layers:
        for (gx, gy) in on:
            c = rows(gy / max(1, h - 1)) if col == "rows" else col
            pl.put(x + gx + dx, y + gy + dy, c)
    return w, h


def brass_rows(b):
    return lambda t: b["b3"] if t < 0.22 else b["b2"] if t < 0.62 else b["b1"] if t < 0.85 else b["b0"]


def card_text(p, selected):
    """Engraved like the plates' labels, but cut deeper (darkest stone) to read at card size."""
    return [(0, 1, BRASS["b3"]), (0, 0, p["out"])] if selected else [(0, 1, p["top"]), (0, 0, p["out"])]


def engraved(p, selected):
    return [(0, 1, BRASS["b3"]), (0, 0, p["out"])] if selected else [(0, 1, p["top"]), (0, 0, p["dark"])]


# ---- card parts -----------------------------------------------------------------

CW, CH, MX, MY, W, H = 140, 120, 12, 10, 116, 100
WX, WY, WW, WH = 12, 8, 92, 44   # window, plate coordinates


def in_window(x, y, pad=0):
    return WX - pad <= x < WX + WW + pad and WY - pad <= y < WY + WH + pad


def window_frame(pl, p):
    """Recessed window: outline, dark inner shadow on top/left, lit lip bottom/right."""
    for y in range(WY - 1, WY + WH + 1):
        for x in range(WX - 1, WX + WW + 1):
            border = x in (WX - 1, WX + WW) or y in (WY - 1, WY + WH)
            if border:
                pl.put(x, y, p["out"])
    for x in range(WX - 1, WX + WW + 1):
        pl.put(x, WY + WH + 1, p["top"])     # lit sill under the window
    for y in range(WY - 1, WY + WH + 2):
        pl.put(WX + WW + 1, y, p["top"])


def sky(pl, top, bottom):
    for y in range(WY, WY + WH):
        t = (y - WY) / (WH - 1)
        c = top if t < 0.45 else bottom
        for x in range(WX, WX + WW):
            pl.put(x, y, c)


def gear_px(cx, cy, x, y, r_out, r_in, teeth, spin=0.0):
    """'out', 'body', 'hole' or None for pixel (x, y) of a gear centred at (cx, cy)."""
    dx, dy = x + 0.5 - cx, y + 0.5 - cy
    r = math.hypot(dx, dy)
    a = (math.degrees(math.atan2(-dy, dx)) - spin) % (360 / teeth)
    outer = r_out if a < 180 / teeth else r_in
    if r > outer + 0.9:
        return None
    if r > outer:
        return "out"
    if r < max(1.5, r_in * 0.25):
        return "hole"
    return "body"


def vignette_clockworks(pl):
    sky(pl, hx("f0a868"), hx("e08a58"))
    # big clock face on the right, gears on the left, warm dusk
    cx, cy, R = WX + 62, WY + 22, 17
    for y in range(WY, WY + WH):
        for x in range(WX, WX + WW):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            if r <= R + 1:
                pl.put(x, y, O if r > R else hx("f4e6c0") if r < R - 2.5 else BRASS["b1"] if r < R - 1 else BRASS["b2"])
            # tower below the clock
            if abs(x + 0.5 - cx) <= 13 and y >= cy + 14:
                pl.put(x, y, hx("733a49") if abs(x + 0.5 - cx) <= 12 else O)
    for k in range(12):
        a = math.radians(k * 30)
        pl.put(int(round(cx + math.cos(a) * (R - 4.5) - 0.5)), int(round(cy - math.sin(a) * (R - 4.5) - 0.5)), hx("504648"))
    for s in range(0, 11):   # hour hand to ~10, minute hand to 12
        a = math.radians(150)
        if s <= 8:
            pl.put(int(round(cx - 0.5 + math.cos(a) * s * 0.9)), int(round(cy - 0.5 - math.sin(a) * s * 0.9)), O)
        pl.put(int(round(cx - 0.5)), int(round(cy - 0.5 - s * 1.2)), O)
    pl.put(int(cx), int(cy), hx("c0301c"))
    # gear silhouettes, lower left
    for (gx, gy, ro, ri, t) in ((WX + 18, WY + 34, 13, 10.5, 10), (WX + 33, WY + 18, 8, 6.2, 8)):
        for y in range(WY, WY + WH):
            for x in range(WX, WX + WW):
                g = gear_px(gx, gy, x, y, ro, ri, t, 7)
                if g == "out":
                    pl.put(x, y, O)
                elif g == "body":
                    pl.put(x, y, hx("8a4a3a") if (x - gx) + (y - gy) > 0 else hx("a8604a"))
                elif g == "hole":
                    pl.put(x, y, O)


def vignette_foundry(pl):
    sky(pl, hx("5a2a3a"), hx("7a3438"))
    # chimneys with smoke, a furnace mouth glowing, molten channel
    for (x0, w, top) in ((WX + 8, 7, WY + 6), (WX + 20, 6, WY + 12), (WX + 74, 8, WY + 4)):
        for y in range(top, WY + WH):
            for x in range(x0, x0 + w):
                pl.put(x, y, O if x in (x0, x0 + w - 1) else hx("3a2a2e"))
        for x in range(x0 - 1, x0 + w + 1):
            pl.put(x, top, O)
        for k in range(5):   # smoke puffs drifting right
            sx, sy = x0 + w // 2 + k * 3, top - 2 - k * 2
            for dx in (-1, 0, 1):
                if sy >= WY:
                    pl.put(sx + dx, sy, hx("8a6a72") if k % 2 else hx("a8888a"))
    # furnace building
    fx, fy, fw, fh = WX + 32, WY + 16, 36, WH - 16
    for y in range(fy, WY + WH):
        for x in range(fx, fx + fw):
            pl.put(x, y, O if x in (fx, fx + fw - 1) or y == fy else hx("4a3236"))
    # glowing arch mouth
    mx, my, mw = fx + 9, fy + 9, 18
    for y in range(my, WY + WH - 6):
        for x in range(mx, mx + mw):
            dx = x + 0.5 - (mx + mw / 2)
            if y < my + 6 and dx * dx + (y - my - 6) ** 2 * 3 > (mw / 2) ** 2:
                continue
            depth = (y - my) / max(1, WY + WH - 6 - my)
            c = GLOW["g3"] if abs(dx) < 4 and depth > 0.3 else GLOW["g2"] if abs(dx) < 7 else GLOW["g1"]
            pl.put(x, y, c)
    # molten channel along the floor
    for y in range(WY + WH - 6, WY + WH):
        for x in range(WX, WX + WW):
            c = GLOW["g0"] if y == WY + WH - 6 else GLOW["g2"] if (x + y) % 5 else GLOW["g3"]
            if y > WY + WH - 3:
                c = GLOW["g1"]
            pl.put(x, y, c)
    # sparks
    for (x, y) in ((fx + 14, my - 3), (fx + 20, my - 6), (fx + 24, my - 2), (fx + 8, my - 7)):
        pl.put(x, y, GLOW["g3"])


def vignette_main(pl):
    """Both lanes in one window: Tick's by day on top, Tock's by night below,
    the Unraveling rift between them, a player standing in each."""
    mid = WY + WH // 2
    for y in range(WY, WY + WH):
        for x in range(WX, WX + WW):
            if y < mid:
                c = hx("86a9d0") if y < WY + 8 else hx("9cb8d8")
            else:
                c = hx("28143a") if y < mid + 10 else hx("3a2050")
            pl.put(x, y, c)
    # sun and moon
    for (cx, cy, r, col) in ((WX + 76, WY + 6, 3.6, hx("fcec8c")), (WX + 16, mid + 6, 3.0, hx("f4e6c0"))):
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                if math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= r:
                    pl.put(x, y, col)
    for (x, y) in ((WX + 40, mid + 4), (WX + 58, mid + 7), (WX + 84, mid + 3), (WX + 30, mid + 9)):
        pl.put(x, y, hx("f4e0ff"))   # stars
    # skyline silhouettes and ground strips in each lane
    for lane_top, sil, ground in ((WY, hx("b0705a"), hx("d97d58")), (mid, hx("482668"), hx("8a5a6a"))):
        base = lane_top + WH // 2 - 5
        for x in range(WX, WX + WW):
            hgt = 3 + ((x * 7) % 5 if x % 9 else 7)
            for y in range(base - hgt, base):
                pl.put(x, y, sil)
            for y in range(base, lane_top + WH // 2):
                pl.put(x, y, O if y == base else ground)
    # clock tower in Tick's lane
    tx = WX + 54
    for y in range(WY + 6, WY + WH // 2 - 5):
        for x in range(tx - 4, tx + 5):
            pl.put(x, y, O if x in (tx - 4, tx + 4) else hx("8a4a3a"))
    for y in range(WY + 8, WY + 13):
        for x in range(tx - 2, tx + 3):
            pl.put(x, y, hx("f4e6c0"))
    pl.put(tx, WY + 10, O)
    # the rift across the seam: outline, lit rim, purple core, sparkles
    for x in range(WX, WX + WW):
        jag = (1 if (x // 4) % 3 == 0 else 0)
        pl.put(x, mid - 2 - jag, hx("22101e"))
        pl.put(x, mid - 1 - jag, hx("c478e0"))
        pl.put(x, mid, hx("8040a8"))
        pl.put(x, mid + 1 + (1 - jag), hx("22101e"))
        if x % 11 == 3:
            pl.put(x, mid, hx("f4e0ff"))
    # Tick (orange) above, Tock (teal) below, standing on their ground
    for (px_, ground_y, body, shade) in ((WX + 24, WY + WH // 2 - 5, hx("f08a2a"), hx("c05a1c")),
                                         (WX + 68, mid + WH // 2 - 5, hx("3ac8c0"), hx("1e8a8a"))):
        for y in range(ground_y - 6, ground_y):
            for x in range(px_ - 3, px_ + 3):
                edge = x in (px_ - 3, px_ + 2) or y == ground_y - 6
                pl.put(x, y, O if edge else body if y < ground_y - 2 else shade)
        pl.put(px_ - 1, ground_y - 4, O)
        pl.put(px_ + 1, ground_y - 4, O)
        pl.put(px_, ground_y - 8, BRASS["b2"])   # the wind-up key
        pl.put(px_, ground_y - 7, BRASS["b1"])


VIGNETTES = [vignette_main]


def boarded(pl):
    for y in range(WY, WY + WH):
        for x in range(WX, WX + WW):
            pl.put(x, y, NIGHT["k"] if (x + y) % 7 else NIGHT["d"])
    # three planks, slightly crooked, with nails
    for i, (y0, slope) in enumerate(((WY + 5, 0.06), (WY + 19, -0.05), (WY + 33, 0.04))):
        for x in range(WX - 2, WX + WW + 2):
            yy = int(round(y0 + (x - WX) * slope))
            for k in range(7):
                c = O if k in (0, 6) else WOOD["l"] if k == 1 else WOOD["m"] if k < 5 else WOOD["d"]
                if k in (2, 4) and (x * 7 + i * 3) % 11 == 0:
                    c = WOOD["d"]   # grain
                pl.put(x, yy + k, c)
        for nx in (WX + 3, WX + WW - 4):
            yy = int(round(y0 + (nx - WX) * slope)) + 3
            pl.put(nx, yy, STEEL["s4"])
            pl.put(nx + 1, yy + 1, STEEL["s1"])


PADLOCK = [
    "....OOOOOOOO....",
    "...OsssssssSO...",
    "..OsO......OSO..",
    "..OsO......OSO..",
    "..OsO......OSO..",
    "..OsO......OSO..",
    "..OsO......OSO..",
    "OOOOOOOOOOOOOOOO",
    "O33333333333332O",
    "O32222222222221O",
    "O32222222222221O",
    "O322222OO222221O",
    "O32222O00O22221O",
    "O32222O00O22221O",
    "O322222O0222221O",
    "O322222O0222221O",
    "O322222OO222221O",
    "O32222222222221O",
    "O11111111111110O",
    "OOOOOOOOOOOOOOOO",
]


def padlock(pl, x0, y0):
    cmap = {"O": O, "s": STEEL["s4"], "S": STEEL["s2"], "3": BRASS["b3"], "2": BRASS["b2"],
            "1": BRASS["b1"], "0": BRASS["b0"]}
    for y, row in enumerate(PADLOCK):
        for x, ch in enumerate(row):
            if ch in cmap:
                pl.put(x0 + x, y0 + y, cmap[ch])


def medallion(pl, folder, index, b):
    """Round brass badge with the level number, over the window's top-left corner."""
    cx, cy, r = WX + 5, WY + 5, 9.6
    for y in range(int(cy - r - 2), int(cy + r + 2)):
        for x in range(int(cx - r - 2), int(cx + r + 2)):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            if d > r + 0.9:
                continue
            if d > r:
                c = O
            elif d > r - 1.6:
                c = b["b3"] if -dx - dy > 0 else b["b1"]
            elif d > r - 2.4:
                c = b["b0"]
            else:
                c = b["b2"] if -dx - dy > 3 else b["b1"]
            pl.put(x, y, c)
    f = font(folder, "Jersey10.ttf", 20)
    w, h, _ = mask(str(index), f)
    stamp(pl, str(index), f, int(cx - w / 2), int(cy - h / 2),
          [(0, 1, b["b3"]), (0, 0, O)])


def card(index, folder, selected=False):
    """index is 1-based; open for 1..len(NAMES), locked after that."""
    locked = index in LOCKED
    rng = random.Random(f"level-{index}")
    pl = Plate(CW, CH, MX, MY, W, H, 6.2)
    p = WORN if locked else STONE
    face(pl, random.Random(f"level-{index}-face"), p, selected and not locked, block=38, rows=3)
    window_frame(pl, p)
    if locked:
        boarded(pl)
    else:
        VIGNETTES[index - 1](pl)

    # name (or "Coming soon") centred under the window, at the largest crisp size that fits
    area_x0, area_x1 = 6, W - 6
    area_y0, area_y1 = WY + WH + 3, H - 4
    if locked:
        lines = ["Coming soon"]
        f = font(folder, "PixelifySans.ttf", 12, [400])
    else:
        name = NAMES[index - 1]
        lines = [name]
        # Pixelify only keeps its pixel grid at 16 and 12 px; at 12 a two-word name may wrap
        for size in (16, 12):
            f = font(folder, "PixelifySans.ttf", size, [400])
            if mask(name, f)[0] <= area_x1 - area_x0:
                break
        else:
            if " " in name:
                lines = name.split(" ", 1)
    sizes = [mask(t, f) for t in lines]
    gap = 3
    total = sum(sz[1] for sz in sizes) + gap * (len(lines) - 1)
    y = area_y0 + (area_y1 - area_y0 - total) // 2
    for t, sz in zip(lines, sizes):
        x = area_x0 + (area_x1 - area_x0 - sz[0]) // 2
        stamp(pl, t, f, x, y, card_text(p, selected and not locked))
        y += sz[1] + gap

    medallion(pl, folder, index, DIM_BRASS if locked else BRASS)

    if locked:
        padlock(pl, WX + WW // 2 - 8, WY + WH // 2 - 10)

    # cracks, kept off the window and the text
    def keep_out(x, y):
        return in_window(x, y, 2) or (y > WY + WH + 2 and 4 < x < W - 3 and y < H - 4)
    dark, light = p["out"], p["top"]
    n = rng.randint(3, 4) if locked else rng.randint(1, 2)
    for _ in range(n):
        side = rng.choice((1, -1))
        x = rng.randint(3, 8) if side == 1 else W - 1 - rng.randint(3, 8)
        crack(pl, rng, x, rng.randint(4, H - 6), side, rng.randint(6, 14) if not locked else rng.randint(10, 20),
              dark, light, keep_out)
    chip(pl, rng)
    outline_alpha(pl, p["out"])

    def avoid(x, y):
        return in_window(x, y, 1) or (y > WY + WH and 3 < x < W - 3 and y < H - 3)

    pal = DEAD if locked else IVY
    sides = ["left", "right"] if index % 2 else ["right", "left"]
    vine(pl, rng, sides[0], pal, rng.randint(40, 70) if locked else rng.randint(26, 50), avoid=avoid)
    vine(pl, rng, sides[1], pal, rng.randint(14, 30), avoid=avoid)
    if locked:
        vine(pl, rng, sides[1], IVY, rng.randint(30, 60), avoid=avoid)
        vine(pl, rng, sides[0], pal, rng.randint(8, 16), avoid=avoid)
    moss(pl, rng, pal, (3, 6) if locked else (2, 3))
    return pl.im


def back_plate(folder, selected):
    rng = random.Random("back")
    pl = Plate(84, 34, 10, 6, 64, 22, 4.2)
    face(pl, random.Random("back-face"), STONE, selected, block=32, rows=1)
    f = font(folder, "PixelifySans.ttf", 16, [400])
    w, h, _ = mask("Back", f)
    stamp(pl, "Back", f, (64 - w) // 2, (22 - h) // 2, engraved(STONE, selected))
    crack(pl, rng, 3, 14, 1, 5, STONE["out"], STONE["top"],
          keep_out=lambda x, y: (64 - w) // 2 - 2 <= x <= (64 + w) // 2 + 2)
    outline_alpha(pl, STONE["out"])
    vine(pl, rng, "left", IVY, rng.randint(8, 14), creep=(3, 6))
    return pl.im


def header(folder):
    f = font(folder, "Jersey10.ttf", 48)
    w, h, on = mask("Select Level", f)
    pad = 3
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    px = im.load()
    rows = brass_rows(BRASS)
    for dx, dy, col in [(0, 3, OUTLINE), (1, 3, OUTLINE)] + ring(O) + [(1, 1, O), (0, 0, "rows")]:
        for (x, y) in on:
            c = rows(y / max(1, h - 1)) if col == "rows" else col
            px[x + pad + dx, y + pad + dy] = c + (255,)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--fonts", required=True)
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    for n in range(1, 9):
        if n in LOCKED:
            card(n, a.fonts).save(out / f"card_{n}_locked.png")
        else:
            card(n, a.fonts, False).save(out / f"card_{n}_idle.png")
            card(n, a.fonts, True).save(out / f"card_{n}_sel.png")
    header(a.fonts).save(out / "header_select_level.png")
    back_plate(a.fonts, False).save(out / "back_idle.png")
    back_plate(a.fonts, True).save(out / "back_sel.png")
    print("wrote", out)


if __name__ == "__main__":
    main()

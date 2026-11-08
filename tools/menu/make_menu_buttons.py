"""Main menu button plates for Tick & Tock (pixel art, 1 px = 1 art pixel).

Three materials, each with an idle and a selected state, four buttons each
(seeded, so every button gets its own cracks and vines):
  stone - sandstone tablet in the ground tiles' palette, brass inlay when selected
  brass - riveted brass plate on a slate bezel (plunger / winch palette)
  rift  - slate iron plate split by cracks that leak the Unraveling's purple

Each sprite is 212x56: the 168x32 plate with its top-left at (22, 12), plus a
margin for the vines. Labels are separate sprites (make_menu_text.py).

  py tools/menu/make_menu_buttons.py Assets/Art/UI/Menu
"""
import math
import random
import sys
from pathlib import Path
from PIL import Image

PW, PH = 168, 32
MX, MY = 22, 12
W, H = PW + 2 * MX, PH + 2 * MY
R = 5.2  # corner radius


def hx(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


# palettes taken from the game's art
STONE = dict(out=hx("512243"), dark=hx("733a49"), shade=hx("b15c53"), base=hx("d97d58"),
             mid=hx("e08757"), light=hx("f2a062"), hi=hx("f9ae67"), top=hx("f6bd78"), mortar=hx("b76a58"))
STEEL = dict(out=hx("1c0c06"), s1=hx("322a2e"), s2=hx("504648"), s3=hx("766a68"), s4=hx("a89c96"))
BRASS = dict(b0=hx("824616"), b1=hx("ba6e24"), b2=hx("f0b446"), b3=hx("fcec8c"))
RIFT = dict(outline=hx("22101e"), deep=hx("100818"), void=hx("28143a"), mid=hx("482668"),
            edge=hx("8040a8"), hi=hx("c478e0"), spark=hx("f4e0ff"))
IVY = [hx("512243"), hx("7e3c4e"), hx("9a3d42"), hx("cf5a39"), hx("e6884f")]
STEM = [hx("3a1a2a"), hx("5c2a44")]
DEADLEAF = [hx("6a4a2a"), hx("8a6a3a"), hx("a88848")]


def inside(x, y, r=R):
    """Pixel (x, y) of the plate (0..PW-1, 0..PH-1) inside the rounded rect?"""
    cx = min(max(x + 0.5, r), PW - r)
    cy = min(max(y + 0.5, r), PH - r)
    return (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r


def edge_dist(x, y):
    """0 on the outline ring, 1 one pixel in, ..."""
    for d in range(6):
        for dx, dy in ((d + 1, 0), (-d - 1, 0), (0, d + 1), (0, -d - 1)):
            if not inside(x + dx, y + dy):
                return d
    return 6


class Canvas:
    def __init__(self):
        self.im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        self.px = self.im.load()

    def put(self, x, y, c, plate=True):
        X, Y = (x + MX, y + MY) if plate else (x, y)
        if 0 <= X < W and 0 <= Y < H:
            self.px[X, Y] = c + (255,)

    def get(self, x, y):
        X, Y = x + MX, y + MY
        if 0 <= X < W and 0 <= Y < H:
            return self.px[X, Y]
        return (0, 0, 0, 0)


# ---- faces ------------------------------------------------------------------

def face_stone(cv, rng, selected):
    p = STONE
    for y in range(PH):
        for x in range(PW):
            if not inside(x, y):
                continue
            d = edge_dist(x, y)
            if d == 0:
                c = p["out"]
            elif d == 1:
                # bevel: lit top-left, shaded bottom-right
                c = p["top"] if (y < PH / 2 and x < PW - 6) or x < 3 else p["shade"]
                if y >= PH - 3:
                    c = p["dark"]
            elif d == 2 and selected:
                c = BRASS["b2"] if y < PH / 2 else BRASS["b1"]
            elif d == 3 and selected:
                c = BRASS["b0"]
            else:
                # big ashlar blocks: mortar lines every 42 px, offset by row
                row = 0 if y < PH // 2 else 1
                off = 21 * row
                c = p["light"] if selected else p["base"]
                if y == PH // 2 and 3 < x < PW - 4:
                    c = p["mortar"]
                elif (x + off) % 42 == 0 and 3 < y < PH - 4:
                    c = p["mortar"]
                else:
                    n = rng.random()
                    if n < 0.025:
                        c = p["mid"] if not selected else p["hi"]
                    elif n < 0.045:
                        c = p["shade"] if not selected else p["base"]
            cv.put(x, y, c)


def face_brass(cv, rng, selected):
    b, s = BRASS, STEEL
    for y in range(PH):
        for x in range(PW):
            if not inside(x, y):
                continue
            d = edge_dist(x, y)
            if d == 0:
                c = s["out"]
            elif d <= 2:
                c = s["s4"] if (d == 1 and y < PH / 2) else s["s3"] if d == 1 else s["s2"]
            elif d == 3:
                c = s["out"]
            else:
                # brushed brass with horizontal streaks
                top = y < 9
                c = b["b3"] if (top and selected) else b["b2"]
                if y > PH - 8:
                    c = b["b1"]
                if (y * 7 + x // 9) % 11 == 0 and 5 < y < PH - 8:
                    c = b["b3"] if selected else hx("f8d070")
                if not selected and rng.random() < 0.05:
                    c = b["b1"]   # tarnish
            cv.put(x, y, c)
    # rivets in the bezel
    for (x, y) in ((6, 5), (PW - 7, 5), (6, PH - 6), (PW - 7, PH - 6)):
        cv.put(x, y, s["s4"])
        cv.put(x + 1, y + 1, s["out"])
        cv.put(x + 1, y, s["s2"])


def face_rift(cv, rng, selected):
    s = STEEL
    for y in range(PH):
        for x in range(PW):
            if not inside(x, y):
                continue
            d = edge_dist(x, y)
            if d == 0:
                c = s["out"]
            elif d == 1:
                c = s["s4"] if y < PH / 2 else s["s1"]
            elif d == 2:
                c = BRASS["b2"] if selected and y < PH / 2 else BRASS["b1"] if selected else s["s2"]
            else:
                c = s["s3"] if y < PH * 0.55 else s["s2"]
                if x % 28 == 0 and 3 < y < PH - 3:
                    c = s["s2"]
                if rng.random() < 0.04:
                    c = s["s2"]
            cv.put(x, y, c)
    for x in range(14, PW - 14, 28):
        for y in (5, PH - 6):
            cv.put(x, y, BRASS["b2"])
            cv.put(x + 1, y + 1, s["s1"])


# ---- cracks -----------------------------------------------------------------

def crack(cv, rng, x, y, dx, steps, dark, light, glow=None):
    pts = []
    for _ in range(steps):
        if not inside(x, y) or edge_dist(x, y) < 1:
            break
        pts.append((x, y))
        r = rng.random()
        if r < 0.55:
            x += dx
        elif r < 0.8:
            y += 1
        else:
            y -= 1
        y = max(2, min(PH - 3, y))
        if rng.random() < 0.12 and steps > 6:
            crack(cv, rng, x, y, dx, steps // 3, dark, light, glow)
    if glow:
        for (x, y) in pts:
            for ox, oy in ((0, -2), (0, 2), (-1, -1), (1, 1), (1, -1), (-1, 1)):
                X, Y = x + ox, y + oy
                if inside(X, Y) and edge_dist(X, Y) >= 2 and (X + Y) % 2 == 0 and (X, Y) not in pts:
                    cv.put(X, Y, glow[2])
    for (x, y) in pts:
        if glow and edge_dist(x, y) >= 2:
            # the Unraveling shows through: lit core, outlined
            cv.put(x, y, glow[0])
            for ox, oy in ((0, -1), (0, 1)):
                if inside(x + ox, y + oy) and cv.get(x + ox, y + oy)[:3] != glow[0]:
                    cv.put(x + ox, y + oy, glow[1])
        else:
            cv.put(x, y, dark)
            if inside(x + 1, y + 1) and edge_dist(x + 1, y + 1) >= 1 and (x + 1, y + 1) not in pts:
                cv.put(x + 1, y + 1, light)
    return pts


def chip(cv, rng, corner):
    """Knock a few pixels off a corner of the plate (outline follows the break)."""
    x0 = 0 if corner in ("tl", "bl") else PW - 1
    y0 = 0 if corner in ("tl", "tr") else PH - 1
    sx = 1 if x0 == 0 else -1
    sy = 1 if y0 == 0 else -1
    depth = rng.randint(2, 3)
    # a small stepped notch near the corner; outline_alpha then traces the break
    nx = x0 + sx * rng.randint(9, 16)
    for k in range(depth):
        for t in range(depth - k + 1):
            X, Y = nx + sx * t, y0 + sy * k
            cv.px[X + MX, Y + MY] = (0, 0, 0, 0)
    return nx, y0, sx, sy, depth


def outline_alpha(cv, color):
    """Outline any opaque pixel that touches a clear one inside the plate box."""
    src = cv.im.copy().load()
    for y in range(PH):
        for x in range(PW):
            if src[x + MX, y + MY][3] == 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                X, Y = x + dx + MX, y + dy + MY
                if src[X, Y][3] == 0 and inside(x + dx, y + dy):
                    cv.put(x, y, color)
                    break


# ---- vines ------------------------------------------------------------------

IVY_BIG = [".X.X.", "XXXXX", "XXXXX", ".XXX.", "..X.."]
IVY_MID = ["X.X", "XXX", ".X."]
IVY_SMALL = ["XX", "X."]


def leaf(cv, x, y, rng, palette, big=False):
    rows = IVY_BIG if big and rng.random() < 0.6 else IVY_MID if big or rng.random() < 0.6 else IVY_SMALL
    if rng.random() < 0.5:
        rows = [r[::-1] for r in rows]
    oy, ox = len(rows) // 2, len(rows[0]) // 2
    shape = [(cx - ox, cy - oy) for cy, r in enumerate(rows) for cx, ch in enumerate(r) if ch == "X"]
    dark, mid, light = palette[1], palette[rng.randint(2, 3)], palette[-1]
    for (dx, dy) in shape:
        lit = dy < 0 or (dy == 0 and dx < 0)
        c = light if lit and rng.random() < 0.7 else dark if dy >= len(rows) - oy - 1 else mid
        cv.put(x + dx, y + dy, c, plate=False)
    # outline under the leaf so it reads against stone
    for dx, dy in shape:
        X, Y = x + dx, y + dy + 1
        if 0 <= X < W and 0 <= Y < H and cv.px[X, Y][3] == 0:
            cv.put(X, Y, palette[0], plate=False)


def vine(cv, rng, side, palette, length):
    """Grows from an anchor on the plate's side: over the top, then hangs down."""
    # anchor around the top corner on that side, in canvas coordinates
    if side == "left":
        x, y, dirx = MX + rng.randint(2, 14), MY + rng.randint(0, 2), -1
    else:
        x, y, dirx = MX + PW - 1 - rng.randint(2, 14), MY + rng.randint(0, 2), 1
    stem = []
    # creep along the top edge outward
    for _ in range(rng.randint(6, 12)):
        stem.append((x, y))
        x += dirx
        if rng.random() < 0.3:
            y += rng.choice((-1, 0, 1))
        y = max(MY - 2, min(MY + 1, y))
    # drop down past the side of the plate, wobbling
    for _ in range(length):
        stem.append((x, y))
        y += 1
        if rng.random() < 0.35:
            x += rng.choice((-1, 1))
        lo, hi = (MX - 12, MX + 1) if side == "left" else (MX + PW - 2, MX + PW + 11)
        x = max(lo, min(hi, x))
        if y >= H - 2:
            break
    for i, (sx, sy) in enumerate(stem):
        cv.put(sx, sy, STEM[i % 2] if i % 5 else STEM[0], plate=False)
    for i, (sx, sy) in enumerate(stem):
        if i % rng.randint(1, 3) == 0:
            ox = rng.choice((-1, 1)) * rng.randint(1, 2)
            leaf(cv, sx + ox, sy + rng.randint(-1, 1), rng, palette, big=rng.random() < 0.45)
    # a few tendrils over the plate face
    for _ in range(rng.randint(1, 3)):
        sx, sy = rng.choice(stem[:12])
        for k in range(rng.randint(3, 7)):
            sx += -dirx
            sy += rng.choice((0, 1))
            cv.put(sx, sy, STEM[1], plate=False)
        leaf(cv, sx, sy, rng, palette)


def hanging_moss(cv, rng, palette):
    """Dead leaves / dry moss caught under the bottom edge."""
    for _ in range(rng.randint(2, 4)):
        x = MX + rng.randint(12, PW - 12)
        for k in range(rng.randint(1, 4)):
            cv.put(x, MY + PH + k, palette[min(k, len(palette) - 1)] if k else STEM[0], plate=False)


# ---- build ------------------------------------------------------------------

STYLES = {"stone": face_stone, "brass": face_brass, "rift": face_rift}


def button(style, index, selected):
    rng = random.Random(f"{style}-{index}")
    cv = Canvas()
    STYLES[style](cv, random.Random(f"{style}-{index}-face"), selected)
    # cracks
    if style == "stone":
        dark, light, glow = STONE["out"], STONE["top"], None
    elif style == "brass":
        dark, light, glow = BRASS["b0"], BRASS["b3"], None
    else:
        dark, light = STEEL["s1"], STEEL["s4"]
        glow = (RIFT["spark"] if selected else RIFT["hi"], RIFT["edge"] if selected else RIFT["mid"],
                RIFT["mid"] if selected else RIFT["void"])
    n = rng.randint(2, 3)
    starts = [(rng.randint(4, 30), rng.randint(4, PH - 5), 1), (PW - 1 - rng.randint(4, 30), rng.randint(4, PH - 5), -1),
              (rng.randint(50, PW - 50), 3, rng.choice((-1, 1)))]
    for (x, y, d) in starts[:n]:
        steps = rng.randint(22, 40) if glow else rng.randint(10, 20)
        crack(cv, rng, x, y, d, steps, dark, light, glow)
    # chipped corner, outlined in the plate's outline colour
    nx, y0, sx, sy, depth = chip(cv, rng, rng.choice(("tl", "tr", "bl", "br")))
    outline = STONE["out"] if style == "stone" else STEEL["out"]
    outline_alpha(cv, outline)
    # foliage: autumn ivy on stone and rift, dried leaves on brass
    pal = IVY if style != "brass" else [STEM[0]] + DEADLEAF + [hx("c8a868")]
    sides = ["left", "right"] if index % 2 == 0 else ["right", "left"]
    vine(cv, rng, sides[0], pal, rng.randint(24, 40))
    vine(cv, rng, sides[0], pal, rng.randint(8, 16))
    vine(cv, rng, sides[1], pal, rng.randint(10, 22))
    hanging_moss(cv, rng, pal)
    return cv.im


def main(out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    for style in STYLES:
        for i in range(4):
            for sel in (False, True):
                button(style, i, sel).save(out / f"btn_{style}_{i}_{'sel' if sel else 'idle'}.png")
    print("ok", out)


if __name__ == "__main__":
    main(sys.argv[1])

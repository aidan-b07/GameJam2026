# Key attach points

Made by `tools/make_player.py`. Pixel in the 32x32 cell (x from the left,
y from the top) where the key's pivot pixel (12, 15) goes. Draw the
key behind the body. Unity offset from the body's bottom-centre pivot:
((x - 16) / 32, (32 - y) / 32) units at 32 PPU.

| Character | Sheet | Frame | x | y |
|---|---|---|---|---|
| tick | tick_body_32px | 0 | 13 | 14 |
| tick | tick_body_32px | 1 | 12 | 15 |
| tick | tick_body_32px | 2 | 13 | 17 |
| tick | tick_body_32px | 3 | 13 | 14 |
| tick | tick_body_32px | 4 | 13 | 12 |
| tick | tick_body_32px | 5 | 12 | 8 |
| tick | tick_body_32px | 6 | 12 | 14 |
| tick | tick_idle_32px | 0 | 13 | 14 |
| tick | tick_idle_32px | 1 | 13 | 15 |
| tick | tick_idle_32px | 2 | 13 | 14 |
| tick | tick_idle_32px | 3 | 13 | 14 |
| tock | tock_body_32px | 0 | 13 | 13 |
| tock | tock_body_32px | 1 | 12 | 14 |
| tock | tock_body_32px | 2 | 13 | 17 |
| tock | tock_body_32px | 3 | 13 | 14 |
| tock | tock_body_32px | 4 | 13 | 12 |
| tock | tock_body_32px | 5 | 13 | 7 |
| tock | tock_body_32px | 6 | 12 | 14 |
| tock | tock_idle_32px | 0 | 13 | 13 |
| tock | tock_idle_32px | 1 | 13 | 14 |
| tock | tock_idle_32px | 2 | 13 | 13 |
| tock | tock_idle_32px | 3 | 13 | 13 |

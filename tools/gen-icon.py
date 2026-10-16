# Generates src/WolfMark.App/Assets/WolfMark.ico (paper card with a mini tag-coloured calendar).
# Pure Python (zlib + struct) so it runs anywhere. Usage: python3 tools/gen-icon.py
import struct, zlib, os

PAPER = (0xFB, 0xFA, 0xF7)
EDGE = (0xD9, 0xD3, 0xC8)
TODAY = (0x1E, 0x24, 0x33)
EMPTY = (0xEC, 0xE8, 0xE1)
COLORS = [(0x3D,0x6F,0xE0),(0xF4,0xC5,0x31),(0xF2,0x8C,0x28),(0xE5,0x48,0x4D),(0x3F,0xA6,0x5C),(0x22,0xA6,0xA0),(0x8E,0x5B,0xE8)]
# 3x3 grid of cells: colour index, -1 empty, -2 today
GRID = [[0, 1, -1], [2, -2, 4], [5, 3, 6]]

def rounded(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def pixel(u, v):
    # u, v in [0,1); returns RGBA
    if not rounded(u, v, 0.04, 0.04, 0.96, 0.96, 0.2):
        return (0, 0, 0, 0)
    if not rounded(u, v, 0.07, 0.07, 0.93, 0.93, 0.18):
        return EDGE + (255,)
    pad, gap = 0.17, 0.05
    size = (1 - 2 * pad - 2 * gap) / 3
    for row in range(3):
        for col in range(3):
            x0 = pad + col * (size + gap)
            y0 = pad + row * (size + gap)
            if rounded(u, v, x0, y0, x0 + size, y0 + size, size * 0.22):
                c = GRID[row][col]
                if c == -2:
                    # hatched today cell
                    stripe = int((u + v) * 40) % 3 == 0
                    base = TODAY if not stripe else (0x3A, 0x42, 0x55)
                    return base + (255,)
                return (EMPTY if c == -1 else COLORS[c]) + (255,)
    return PAPER + (255,)

def render(n, ss=4):
    rows = []
    for y in range(n):
        row = bytearray([0])
        for x in range(n):
            acc = [0, 0, 0, 0]
            for sy in range(ss):
                for sx in range(ss):
                    p = pixel((x + (sx + 0.5) / ss) / n, (y + (sy + 0.5) / ss) / n)
                    a = p[3]
                    acc[0] += p[0] * a; acc[1] += p[1] * a; acc[2] += p[2] * a; acc[3] += a
            a = acc[3]
            if a == 0:
                row += bytes(4)
            else:
                row += bytes([acc[0] // a, acc[1] // a, acc[2] // a, a // (ss * ss)])
        rows.append(bytes(row))
    raw = b"".join(rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

sizes = [16, 24, 32, 48, 64, 256]
images = [render(s, 4 if s < 128 else 2) for s in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
entries = b""
for s, img in zip(sizes, images):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(img), offset)
    offset += len(img)
out = os.path.join(os.path.dirname(__file__), "..", "src", "WolfMark.App", "Assets", "WolfMark.ico")
with open(out, "wb") as f:
    f.write(header + entries + b"".join(images))
with open(os.path.join(os.path.dirname(out), "WolfMark-256.png"), "wb") as f:
    f.write(images[-1])
print("wrote", out)

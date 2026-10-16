# Generates src/WolfMark.App/Theming/FormatIconPaths.cs: vector outlines of the text icons
# (H1, H2, H3, ¶, “, 1., S + strike) taken from Barlow Condensed SemiBold, centred in a 16x16 box.
# Menu items size icons in a fixed 16x16 slot; text fonts don't fit it (FontIcon assumes an icon
# font), so the menu uses PathIcons built from these outlines.
# Usage: python3 tools/gen-icon-paths.py
import os, struct

FONT = os.path.join(os.path.dirname(__file__), "..", "src", "WolfMark.App", "Assets", "Fonts", "BarlowCondensed-SemiBold.ttf")
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "WolfMark.App", "Theming", "FormatIconPaths.cs")

ICONS = {  # name: (text, extra strike bar, size factor within the box)
    "Heading1": ("H1", False, 1.0),
    "Heading2": ("H2", False, 1.0),
    "Heading3": ("H3", False, 1.0),
    "Paragraph": ("\u00b6", False, 0.9),
    "Quote": ("\u201c", False, 0.55),
    "NumberedList": ("1.", False, 0.85),
    "Strikethrough": ("S", True, 0.95),
}

data = open(FONT, "rb").read()
num_tables = struct.unpack(">H", data[4:6])[0]
tables = {}
for i in range(num_tables):
    tag, _, off, length = struct.unpack(">4sIII", data[12 + 16 * i:28 + 16 * i])
    tables[tag.decode()] = (off, length)

def u16(o): return struct.unpack(">H", data[o:o + 2])[0]
def i16(o): return struct.unpack(">h", data[o:o + 2])[0]
def u32(o): return struct.unpack(">I", data[o:o + 4])[0]

head = tables["head"][0]
units_per_em = u16(head + 18)
index_to_loc = i16(head + 50)
num_glyphs = u16(tables["maxp"][0] + 4)
loca = tables["loca"][0]
glyf = tables["glyf"][0]
hmtx = tables["hmtx"][0]
num_hmetrics = u16(tables["hhea"][0] + 34)

def glyph_offset(gid):
    if index_to_loc == 0:
        return glyf + u16(loca + 2 * gid) * 2, glyf + u16(loca + 2 * gid + 2) * 2
    return glyf + u32(loca + 4 * gid), glyf + u32(loca + 4 * gid + 4)

def advance(gid):
    return u16(hmtx + 4 * min(gid, num_hmetrics - 1))

# cmap format 4 (Windows Unicode BMP)
cmap = tables["cmap"][0]
sub = None
for i in range(u16(cmap + 2)):
    pid, eid, off = struct.unpack(">HHI", data[cmap + 4 + 8 * i:cmap + 12 + 8 * i])
    if pid == 3 and eid == 1:
        sub = cmap + off
assert sub is not None and u16(sub) == 4
seg = u16(sub + 6) // 2
ends = sub + 14
starts = ends + 2 * seg + 2
deltas = starts + 2 * seg
ranges = deltas + 2 * seg

def glyph_id(ch):
    c = ord(ch)
    for i in range(seg):
        if u16(ends + 2 * i) >= c:
            if u16(starts + 2 * i) > c:
                return 0
            ro = u16(ranges + 2 * i)
            if ro == 0:
                return (c + i16(deltas + 2 * i)) & 0xFFFF
            g = u16(ranges + 2 * i + ro + 2 * (c - u16(starts + 2 * i)))
            return (g + i16(deltas + 2 * i)) & 0xFFFF if g else 0
    return 0

def contours(gid, dx=0.0, dy=0.0):
    start, end = glyph_offset(gid)
    if start == end:
        return []
    n = i16(start)
    if n < 0:  # composite
        result, o = [], start + 10
        while True:
            flags, comp = u16(o), u16(o + 2)
            o += 4
            if flags & 1:
                a, b = i16(o), i16(o + 2); o += 4
            else:
                a, b = struct.unpack(">bb", data[o:o + 2]); o += 2
            if flags & 8: o += 2
            elif flags & 0x40: o += 4
            elif flags & 0x80: o += 8
            result += contours(comp, dx + a, dy + b)
            if not flags & 0x20:
                return result
    end_pts = [u16(start + 10 + 2 * i) for i in range(n)]
    count = end_pts[-1] + 1
    o = start + 10 + 2 * n
    o += 2 + u16(o)
    flags = []
    while len(flags) < count:
        f = data[o]; o += 1
        flags.append(f)
        if f & 8:
            r = data[o]; o += 1
            flags += [f] * r
    xs, ys, v = [], [], 0
    for f in flags:
        if f & 2:
            d = data[o]; o += 1
            v += d if f & 16 else -d
        elif not f & 16:
            v += i16(o); o += 2
        xs.append(v)
    v = 0
    for f in flags:
        if f & 4:
            d = data[o]; o += 1
            v += d if f & 32 else -d
        elif not f & 32:
            v += i16(o); o += 2
        ys.append(v)
    result, s = [], 0
    for e in end_pts:
        result.append([(xs[k] + dx, ys[k] + dy, bool(flags[k] & 1)) for k in range(s, e + 1)])
        s = e + 1
    return result

def outline(text, strike):
    cs, x = [], 0
    for ch in text:
        g = glyph_id(ch)
        assert g, ch
        cs += contours(g, x, 0)
        x += advance(g)
    if strike:
        xs = [p[0] for c in cs for p in c]
        ys = [p[1] for c in cs for p in c]
        mid, h = (min(ys) + max(ys)) / 2, units_per_em * 0.07
        pad = units_per_em * 0.06
        l, r = min(xs) - pad, max(xs) + pad
        cs.append([(l, mid - h, True), (r, mid - h, True), (r, mid + h, True), (l, mid + h, True)])
    return cs

def to_path(cs, factor, box=16.0, margin=1.5):
    xs = [p[0] for c in cs for p in c]
    ys = [p[1] for c in cs for p in c]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    scale = (box - 2 * margin) / max(w, h) * factor
    ox = (box - w * scale) / 2 - min(xs) * scale
    oy = (box - h * scale) / 2 + max(ys) * scale
    def P(x, y): return f"{ox + x * scale:.2f},{oy - y * scale:.2f}"
    parts = []
    for c in cs:
        pts = c
        # start on an on-curve point
        k = next((i for i, p in enumerate(pts) if p[2]), None)
        if k is None:
            a, b = pts[0], pts[1]
            pts = [((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, True)] + pts[1:] + [pts[0]]
        else:
            pts = pts[k:] + pts[:k]
        parts.append("M" + P(pts[0][0], pts[0][1]))
        i, n = 1, len(pts)
        while i <= n:
            p = pts[i % n]
            if p[2]:
                parts.append("L" + P(p[0], p[1]))
                i += 1
            else:
                q = pts[(i + 1) % n]
                if q[2]:
                    end = q
                    i += 2
                else:
                    end = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2, True)
                    i += 1
                parts.append("Q" + P(p[0], p[1]) + " " + P(end[0], end[1]))
        parts.append("Z")
    return "".join(parts)

lines = []
for name, (text, strike, factor) in ICONS.items():
    lines.append(f'        [EditorCommand.{name}] = "{to_path(outline(text, strike), factor)}",')

cs = '''// <auto-generated> by tools/gen-icon-paths.py from Barlow Condensed SemiBold (SIL OFL 1.1). Do not edit. </auto-generated>
using WolfMark.Editor;

namespace WolfMark.App.Theming;

/// <summary>Text icons as vector paths in a 16x16 box (M/L/Q/Z, absolute).</summary>
internal static class FormatIconPaths
{
    public static IReadOnlyDictionary<EditorCommand, string> Paths { get; } = new Dictionary<EditorCommand, string>
    {
''' + "\n".join(lines) + '''
    };
}
'''
open(OUT, "w", encoding="utf-8").write(cs)
print("wrote", OUT)

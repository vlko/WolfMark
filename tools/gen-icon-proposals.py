# Renders the WolfMark app icon proposals (paper, tag colours, Barlow) into
# src/WolfMark.App/Assets/IconProposals: 256px PNG + multi-size ICO per proposal, and sheet.png.
# Pure Python; reuses the TrueType reader of tools/gen-icon-paths.py. To switch the app icon, copy a
# proposal's .ico over src/WolfMark.App/Assets/WolfMark.ico.
# Usage: python3 tools/gen-icon-proposals.py
import math, struct, zlib, sys, os
# --- reuse the TrueType outline reader from gen-icon-paths.py without running its generator
TOOLS = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(TOOLS, "gen-icon-paths.py")).read()
src = src[:src.index("lines = []")]
g = {"__file__": os.path.join(TOOLS, "gen-icon-paths.py")}
exec(src, g)

FONTS = os.path.join(TOOLS, "..", "src", "WolfMark.App", "Assets", "Fonts")
_font_ns = {}

def font_ns(name):
    """Namespace of the TrueType reader bound to one font file."""
    if name not in _font_ns:
        code = src.replace('FONT = os.path.join(os.path.dirname(__file__), "..", "src", "WolfMark.App", "Assets", "Fonts", "BarlowCondensed-SemiBold.ttf")',
                           "FONT = " + repr(os.path.join(FONTS, name)))
        ns = {"__file__": g["__file__"]}
        exec(code, ns)
        _font_ns[name] = ns
    return _font_ns[name]

def glyph_polys(text, font="BarlowCondensed-SemiBold.ttf"):
    """Outline polygons (font units, y up) of text, quadratic curves flattened."""
    cs = font_ns(font)["outline"](text, False)
    polys = []
    for c in cs:
        pts = c
        k = next((i for i, p in enumerate(pts) if p[2]), None)
        if k is None:
            a, b = pts[0], pts[1]; pts = [((a[0]+b[0])/2, (a[1]+b[1])/2, True)] + pts[1:] + [pts[0]]
        else:
            pts = pts[k:] + pts[:k]
        out = [(pts[0][0], pts[0][1])]; i, n = 1, len(pts)
        cur = (pts[0][0], pts[0][1])
        while i <= n:
            p = pts[i % n]
            if p[2]:
                cur = (p[0], p[1]); out.append(cur); i += 1
            else:
                q = pts[(i+1) % n]
                end = (q[0], q[1]) if q[2] else ((p[0]+q[0])/2, (p[1]+q[1])/2)
                i += 2 if q[2] else 1
                for t in [j/10 for j in range(1, 11)]:
                    out.append(((1-t)**2*cur[0]+2*(1-t)*t*p[0]+t*t*end[0], (1-t)**2*cur[1]+2*(1-t)*t*p[1]+t*t*end[1]))
                cur = end
        polys.append(out)
    return polys

def fit(polys, x, y, w, h):
    xs=[p[0] for c in polys for p in c]; ys=[p[1] for c in polys for p in c]
    s=min(w/(max(xs)-min(xs)), h/(max(ys)-min(ys)))
    ox=x+(w-(max(xs)-min(xs))*s)/2-min(xs)*s; oy=y+(h-(max(ys)-min(ys))*s)/2+max(ys)*s
    return [[(ox+px*s, oy-py*s) for px,py in c] for c in polys]

class Canvas:
    def __init__(self, n, ss=4):
        self.n, self.ss = n, ss
        self.N = n*ss
        self.px = [[(0,0,0,0)]*self.N for _ in range(self.N)]
    def fill(self, polys, color, even_odd=False, clip=None):
        """polys in 0..1 units; nonzero winding by default."""
        N=self.N
        P=[[(x*N, y*N) for x,y in poly] for poly in polys]
        ymin=max(0,int(min(p[1] for c in P for p in c))); ymax=min(N-1,int(max(p[1] for c in P for p in c))+1)
        for yy in range(ymin, ymax+1):
            y=yy+0.5; xs=[]
            for poly in P:
                for i in range(len(poly)):
                    (x1,y1),(x2,y2)=poly[i],poly[(i+1)%len(poly)]
                    if (y1<=y<y2) or (y2<=y<y1):
                        xs.append((x1+(y-y1)*(x2-x1)/(y2-y1), 1 if y2>y1 else -1))
            xs.sort()
            wind=0
            for i in range(len(xs)-1):
                wind = (wind+1)%2 if even_odd else wind+xs[i][1]
                if (wind%2 if even_odd else wind)!=0:
                    for xx in range(max(0,int(xs[i][0]+0.5)), min(N, int(xs[i+1][0]+0.5))):
                        if clip and not clip(xx/N, yy/N): continue
                        self.blend(xx,yy,color)
    def blend(self,x,y,c):
        r,g_,b,a=c; br,bg,bb,ba=self.px[y][x]
        A=a/255; out=A+ba/255*(1-A)
        if out==0: return
        self.px[y][x]=(int((r*A+br*ba/255*(1-A))/out),int((g_*A+bg*ba/255*(1-A))/out),int((b*A+bb*ba/255*(1-A))/out),int(out*255))
    def image(self, n=None):
        n=n or self.n; f=self.N//n
        rows=[]
        for y in range(n):
            row=[]
            for x in range(n):
                r=g_=b=a=0
                for dy in range(f):
                    for dx in range(f):
                        pr,pg,pb,pa=self.px[y*f+dy][x*f+dx]
                        r+=pr*pa; g_+=pg*pa; b+=pb*pa; a+=pa
                row.append((r//a,g_//a,b//a,a//(f*f)) if a else (0,0,0,0))
            rows.append(row)
        return rows

def rrect(x,y,w,h,r,seg=8):
    pts=[]
    for cx,cy,a0 in ((x+w-r,y+r,-90),(x+w-r,y+h-r,0),(x+r,y+h-r,90),(x+r,y+r,180)):
        for i in range(seg+1):
            a=math.radians(a0+90*i/seg); pts.append((cx+r*math.cos(a), cy+r*math.sin(a)))
    return [pts]

def rect(x,y,w,h): return [[(x,y),(x+w,y),(x+w,y+h),(x,y+h)]]
def hexc(h,a=255): return (int(h[1:3],16),int(h[3:5],16),int(h[5:7],16),a)

PAPER=hexc("#FAF9F4"); EDGE=hexc("#E2DDD2"); INK=hexc("#1F1F1E"); BLUE=hexc("#1E88F5"); PINK=hexc("#F49CBB"); YELLOW=hexc("#F7C62F")
TEAL=hexc("#12A5B5"); SALMON=hexc("#F57F72"); GREEN=hexc("#5CCB86"); PURPLE=hexc("#A47BF2"); ORANGE=hexc("#F5922A"); RED=hexc("#F2564B"); SKY=hexc("#45BDF2"); LIME=hexc("#9FD13F")

def base(c):
    c.fill(rrect(0.04,0.04,0.92,0.92,0.16), EDGE)
    c.fill(rrect(0.055,0.055,0.89,0.89,0.145), PAPER)

def hatch_clip(period=0.045, on=0.5):
    return lambda x,y: ((x+y)/period)%1 < on

DAY = "Barlow-ExtraBold.ttf"
SAGE = hexc("#C9D2C2"); SAGE_EDGE = hexc("#B3BEAC")

def circle(cx, cy, r, seg=40):
    return [[(cx + r*math.cos(2*math.pi*i/seg), cy + r*math.sin(2*math.pi*i/seg)) for i in range(seg)]]

def ellipse(cx, cy, rx, ry, seg=40):
    return [[(cx + rx*math.cos(2*math.pi*i/seg), cy + ry*math.sin(2*math.pi*i/seg)) for i in range(seg)]]

def text(c, s, x, y, w, h, color, font=DAY):
    c.fill(fit(glyph_polys(s, font), x, y, w, h), color)

def darker(col, t=0.5):
    return (int(col[0]*(1-t)), int(col[1]*(1-t)), int(col[2]*(1-t)), 255)

def today_frame(c, x, y, w, h, t):
    for poly in (rect(x,y,w,t), rect(x,y+h-t,w,t), rect(x,y,t,h), rect(x+w-t,y,t,h)):
        c.fill(poly, YELLOW)
        c.fill(poly, INK, clip=lambda px,py: ((px+py)/0.04)%1<0.5)

def brackets(c, top, h, left=0.08, right=0.88, bt=0.03, arm=0.045):
    c.fill(rect(left, top, bt, h), INK); c.fill(rect(left, top, arm, bt), INK); c.fill(rect(left, top+h-bt, arm, bt), INK)
    c.fill(rect(right+arm-bt, top, bt, h), INK); c.fill(rect(right, top, arm, bt), INK); c.fill(rect(right, top+h-bt, arm, bt), INK)

def flip_v(polys):
    return [[(px, -py) for px, py in poly] for poly in polys]

def riso(c, polys, x, y, w, h, main=None, cut_middle=None):
    """
    Glyph in the riso print style of the app's lead numbers: yellow and pink offsets under the blue.
    cut_middle (0..1): for the "M" – the middle V is cut off flat at that fraction of the height.
    """
    for col, dx, dy in ((YELLOW, 0.025*w/0.66, 0.02*h/0.6), (PINK, -0.028*w/0.66, -0.014*h/0.6), (main or BLUE, 0, 0)):
        placed = fit(polys, x+dx, y+dy, w, h)
        clip, bridge = middle_cut(placed, cut_middle) if cut_middle else (None, None)
        c.fill(placed, col, clip=clip)
        if bridge:
            c.fill(bridge, col)

def middle_cut(polys, fraction):
    """Clip that removes the middle V of the upside-down W below a horizontal line → flat middle bottom."""
    ys = [py for poly in polys for px, py in poly]
    top, bottom = min(ys), max(ys)
    ycut = top + (bottom - top) * fraction
    xs = []
    for poly in polys:
        for k in range(len(poly)):
            (x1, y1), (x2, y2) = poly[k], poly[(k + 1) % len(poly)]
            if (y1 <= ycut < y2) or (y2 <= ycut < y1):
                xs.append(x1 + (ycut - y1) * (x2 - x1) / (y2 - y1))
    xs.sort()
    if len(xs) < 8:
        return None, None
    # At the cut line: [outer leg L][inner leg L][inner leg R][outer leg R], each with two edges.
    left_inner, right_inner = xs[1], xs[-2]       # inner edges of the outer legs
    stroke = xs[3] - xs[2]                        # width of an inner leg at the cut
    # Below the cut the middle V is removed; a bar of the stroke's thickness joins the two inner legs
    # there, so the middle ends in a flat, connected base.
    bridge = [[(xs[2], ycut - stroke * 0.75), (xs[5], ycut - stroke * 0.75), (xs[5], ycut), (xs[2], ycut)]]
    return (lambda px, py: not (py > ycut and left_inner < px < right_inner)), bridge

def mark_m():
    """The "M" of WolfMark: the same W glyph turned upside down."""
    return flip_v(glyph_polys("W"))

def app_pencil(c, tipx, tipy, length, angle_deg=-45, width=None, color=None):
    """
    The pencil next to the app name (Fluent "Edit" glyph): even outline in the accent, light inside,
    round end cap, a line where the tip starts. Positioned by its tip, so it can "write" at a point.
    """
    color = color or BLUE
    width = width or length*0.2
    t = width*0.17
    a = math.radians(angle_deg); ca, sa = math.cos(a), math.sin(a)
    def pt(x, y):
        # rotate (x, y) by angle and mirror so the body goes from the tip towards the upper right
        return (tipx + x*math.cos(-a) - y*math.sin(-a), tipy - x*math.sin(-a) - y*math.cos(-a))
    def shape(r, tip_len, x0):
        pts = [pt(x0, 0), pt(tip_len, -r)]
        cx = length - width/2
        pts.append(pt(cx, -r))
        for k in range(1, 16):
            ang = -math.pi/2 + math.pi*k/16
            pts.append(pt(cx + r*math.cos(ang), r*math.sin(ang)))
        pts.append(pt(cx, r))
        pts.append(pt(tip_len, r))
        return pts
    tip_len = width*1.25
    outer = shape(width/2, tip_len, 0)
    inner = shape(width/2 - t, tip_len + t*0.35, t*2.4)
    c.fill([outer], PAPER)
    c.fill([outer, inner], color, even_odd=True)
    def band(x):
        return [[pt(x - t/2, -width/2 + t*0.5), pt(x + t/2, -width/2 + t*0.5), pt(x + t/2, width/2 - t*0.5), pt(x - t/2, width/2 - t*0.5)]]
    c.fill(band(tip_len), color)                         # where the tip starts
    c.fill(band(length - width*1.35), color)             # ferrule line near the cap

def scribble(c, x0, y, n=6, step=0.04, size=0.05, color=None):
    for k in range(n):
        c.fill(rotated_rect(x0 + k*step, y + (0.014 if k % 2 else -0.014), size, 0.016, -0.9 if k % 2 else 0.9), color or INK)

# 1 week band with dates: past (hatched), today (dashed frame), future
def icon1(c):
    base(c)
    top, h, w, x0 = 0.34, 0.32, 0.24, 0.14
    # 15 – past: salmon, hatched, number in a dark shade of the tag colour
    c.fill(rect(x0, top, w, h), SALMON)
    c.fill(rect(x0, top, w, h), (255,255,255,120), clip=hatch_clip(0.05))
    text(c, "15", x0+0.035, top+0.08, w-0.07, 0.14, darker(SALMON, 0.5))
    # 16 – today: teal/yellow split, dashed frame
    c.fill([[(x0+w,top),(x0+2*w,top),(x0+w,top+h)]], TEAL); c.fill([[(x0+2*w,top),(x0+2*w,top+h),(x0+w,top+h)]], YELLOW)
    text(c, "16", x0+w+0.035, top+0.08, w-0.07, 0.14, (255,255,255,255))
    today_frame(c, x0+w, top, w, h, 0.022)
    # 17 – future: blue
    c.fill(rect(x0+2*w, top, w, h), BLUE)
    text(c, "17", x0+2*w+0.035, top+0.08, w-0.07, 0.14, (255,255,255,255))
    brackets(c, top-0.05, h+0.10)

# 2 WolfMark monogram: W at full size, the "M" (upside-down W, shorter middle) behind it at 2/3 size
def icon2(c):
    base(c)
    # behind the W, bottom-aligned, stretched in height so part of its left leg shows next to the W
    riso(c, mark_m(), 0.585, 0.38, 0.31, 0.42, cut_middle=0.8)
    riso(c, glyph_polys("W"), 0.13, 0.2, 0.62, 0.6)

def icon2_w(c):
    base(c)
    riso(c, glyph_polys("W"), 0.17, 0.2, 0.66, 0.6)

# 3 notebook page linked to the calendar: week band with dates on top, three time/text rows below
def icon3(c):
    base(c)
    cells = [(SALMON,"14",True),(TEAL,"15",True),(BLUE,"16",False),(hexc("#ECE8E1"),"17",False)]
    x0, cw, ch, cy = 0.13, 0.185, 0.21, 0.14
    for k,(col,num,past) in enumerate(cells):
        x = x0 + k*cw
        c.fill(rect(x, cy, cw, ch), col)
        if past:
            c.fill(rect(x, cy, cw, ch), (255,255,255,110), clip=hatch_clip(0.045))
        ink = darker(col,0.5) if past else (hexc("#A8A49B") if col[0]==0xEC else (255,255,255,255))
        text(c, num, x+0.03, cy+0.055, cw-0.06, 0.10, ink)
    today_frame(c, x0+2*cw, cy, cw, ch, 0.02)
    # dotted gutter + three rows: time mark and a line of text
    for i in range(10):
        c.fill(rect(0.32, 0.43+i*0.045, 0.014, 0.024), hexc("#A8A49B"))
    for i,(lw,col) in enumerate(((0.44,INK),(0.34,hexc("#A8A49B")),(0.40,INK))):
        y = 0.47 + i*0.14
        c.fill(rect(0.14,y,0.13,0.05), hexc("#A8A49B"))
        c.fill(rect(0.38,y,lw,0.05), col)

# 4 tear-off calendar page: stacked sheets, paper binding rings, riso "16", tag-coloured dog-ear
def icon4(c):
    base(c)
    c.fill(rect(0.22,0.20,0.60,0.66), hexc("#E6E1D7"))      # sheets behind
    c.fill(rect(0.20,0.18,0.60,0.66), hexc("#F1EDE5"))
    page = [(0.18,0.16),(0.78,0.16),(0.78,0.70),(0.64,0.82),(0.18,0.82)]
    c.fill([page], hexc("#FFFFFF"))
    c.fill(rect(0.18,0.16,0.60,0.11), BLUE)
    for x in (0.31, 0.61):                                   # binding rings
        c.fill(rrect(x, 0.10, 0.06, 0.13, 0.03), hexc("#D3CDC2"))
        c.fill(rrect(x+0.015, 0.12, 0.03, 0.09, 0.015), hexc("#FAF9F4"))
    d = glyph_polys("16", "BarlowCondensed-Light.ttf")
    for col, dx, dy in ((YELLOW, 0.02, 0.016), (PINK, -0.022, -0.012), (BLUE, 0, 0)):
        c.fill(fit(d, 0.26+dx, 0.33+dy, 0.44, 0.33), col)
    c.fill([[(0.78,0.70),(0.64,0.82),(0.64,0.70)]], SALMON)   # dog-ear in a tag colour
    c.fill([[(0.78,0.70),(0.64,0.82),(0.68,0.74)]], darker(SALMON, 0.15))
    six = fit(glyph_polys("16", "BarlowCondensed-Light.ttf"), 0.26, 0.33, 0.44, 0.33)
    right = max(px for poly in six for px, py in poly)
    app_pencil(c, right - 0.005, 0.50, 0.34)                   # the app's pencil, writing the 6

# 5 wolf in the flat geometric style of "wolfdeck": split grey head, cream muzzle, pink cheeks (no background)
WOLF_GL, WOLF_GD = hexc("#A9A79F"), hexc("#8C8A83")
WOLF_CL, WOLF_CD = hexc("#EEE8DC"), hexc("#DAD3C5")
WOLF_INNER, WOLF_CHEEK = hexc("#6E6157"), hexc("#E6A2A5")
WOLF_OUTLINE = [(0.20,0.13),(0.40,0.31),(0.50,0.27),(0.60,0.31),(0.80,0.13),(0.80,0.41),(0.82,0.58),(0.67,0.76),(0.50,0.84),(0.33,0.76),(0.18,0.58),(0.20,0.41)]

def draw_wolf(c, ox=0.0, oy=0.0, sc=1.0, halo=None):
    T = lambda pts: [[(ox + x*sc, oy + y*sc) for x, y in pts]]
    C = lambda cx, cy, r: circle(ox + cx*sc, oy + cy*sc, r*sc)
    if halo is not None:   # paper outline that separates the wolf from what is behind it
        cx, cy = 0.5, 0.5
        c.fill([[(ox + (cx + (x-cx)*1.12)*sc, oy + (cy + (y-cy)*1.12)*sc) for x, y in WOLF_OUTLINE]], halo)
    c.fill(T([(0.20,0.13),(0.40,0.33),(0.22,0.44)]), WOLF_GL); c.fill(T([(0.24,0.21),(0.35,0.33),(0.25,0.39)]), WOLF_INNER)
    c.fill(T([(0.80,0.13),(0.60,0.33),(0.78,0.44)]), WOLF_GD); c.fill(T([(0.76,0.21),(0.65,0.33),(0.75,0.39)]), WOLF_INNER)
    c.fill(T([(0.50,0.27),(0.50,0.84),(0.33,0.76),(0.18,0.58),(0.20,0.41),(0.36,0.30)]), WOLF_GL)
    c.fill(T([(0.50,0.27),(0.64,0.30),(0.80,0.41),(0.82,0.58),(0.67,0.76),(0.50,0.84)]), WOLF_GD)
    c.fill(T([(0.18,0.50),(0.36,0.47),(0.50,0.60),(0.50,0.84),(0.33,0.76),(0.18,0.58)]), WOLF_CL)
    c.fill(T([(0.82,0.50),(0.64,0.47),(0.50,0.60),(0.50,0.84),(0.67,0.76),(0.82,0.58)]), WOLF_CD)
    c.fill(T([(0.43,0.40),(0.50,0.62),(0.57,0.40),(0.50,0.36)]), WOLF_GD)
    c.fill(C(0.30,0.63,0.055), WOLF_CHEEK); c.fill(C(0.70,0.63,0.055), WOLF_CHEEK)
    c.fill(C(0.38,0.53,0.024), INK); c.fill(C(0.62,0.53,0.024), INK)
    c.fill([[(ox + (0.50 + 0.055*math.cos(a))*sc, oy + (0.67 + 0.035*math.sin(a))*sc) for a in [2*math.pi*k/40 for k in range(40)]]], INK)

def icon5(c):
    draw_wolf(c, -0.07, -0.02, 1.14)

# 6 calendar meets writing: 2x2 dated cells and a pencil writing over the corner
def rotated_rect(cx, cy, length, width, angle):
    ca, sa = math.cos(angle), math.sin(angle)
    pts = [(-length/2,-width/2),(length/2,-width/2),(length/2,width/2),(-length/2,width/2)]
    return [[(cx + x*ca - y*sa, cy + x*sa + y*ca) for x, y in pts]]

def icon6(c):
    base(c)
    s_ = 0.39
    x0 = 0.11
    cells = [((x0,x0),SALMON,"15",True),((x0+s_,x0),"split","16",False),((x0,x0+s_),BLUE,"17",False),((x0+s_,x0+s_),hexc("#ECE8E1"),None,False)]
    for (x,y),col,num,past in cells:
        if col == "split":
            c.fill([[(x,y),(x+s_,y),(x,y+s_)]], TEAL); c.fill([[(x+s_,y),(x+s_,y+s_),(x,y+s_)]], YELLOW); ink=(255,255,255,255)
        else:
            c.fill(rect(x,y,s_,s_), col)
            ink = hexc("#A8A49B") if col[0] == 0xEC else (255,255,255,255)
            if past:
                c.fill(rect(x,y,s_,s_), (255,255,255,110), clip=hatch_clip(0.05)); ink = darker(col,0.5)
        if num is not None:
            text(c, num, x+0.08, y+0.12, s_-0.16, 0.15, ink)
    today_frame(c, x0+s_, x0, s_, s_, 0.026)
    # the day being written: a bullet list, the app's pencil writing its last line
    bx, by = x0 + s_ + 0.06, x0 + s_ + 0.08
    for k, lw in enumerate((0.22, 0.17, 0.10)):
        y = by + k*0.095
        c.fill(circle(bx + 0.012, y + 0.012, 0.014), INK)
        c.fill(rect(bx + 0.045, y, lw, 0.026), INK)
    app_pencil(c, bx + 0.045 + 0.10 + 0.01, by + 2*0.095 + 0.02, 0.30)

# 7 Wolf + Mark: full-height "M", the wolf's head (1/4 of the icon) at the foot of its left leg
def icon7(c):
    base(c)
    riso(c, mark_m(), 0.30, 0.14, 0.58, 0.64, cut_middle=0.8)
    sc = 0.66                                   # head ≈ 1/4 of the icon's area (about half its width)
    ox, oy = -0.02, 0.92 - 0.84*sc
    T = lambda dx, dy: [[(ox + dx + x*sc, oy + dy + y*sc) for x, y in WOLF_OUTLINE]]
    c.fill(T(0.03*sc, 0.025*sc), YELLOW)
    c.fill(T(-0.035*sc, -0.018*sc), PINK)
    draw_wolf(c, ox, oy, sc)

def png(rows):
    h=len(rows); w=len(rows[0])
    raw=b"".join(b"\x00"+b"".join(bytes(p) for p in r) for r in rows)
    ch=lambda t,d: struct.pack(">I",len(d))+t+d+struct.pack(">I",zlib.crc32(t+d)&0xffffffff)
    return b"\x89PNG\r\n\x1a\n"+ch(b"IHDR",struct.pack(">IIBBBBB",w,h,8,6,0,0,0))+ch(b"IDAT",zlib.compress(raw,9))+ch(b"IEND",b"")

out=os.path.join(TOOLS, "..", "src", "WolfMark.App", "Assets", "IconProposals")
os.makedirs(out, exist_ok=True)
NAMES=["week-band","riso-wm","notebook-calendar","calendar-page","wolf","calendar-pencil","wolf-mark"]
SIZES=[256,64,48,32,24,16]
icons=[icon1,icon2,icon3,icon4,icon5,icon6,icon7]
renders=[]
for i,(f,name) in enumerate(zip(icons,NAMES),1):
    c=Canvas(256,6); f(c)          # 1536px supersampled canvas divides evenly into every ICO size
    images={n:c.image(n) for n in SIZES}
    open(os.path.join(out,f"{i}-{name}.png"),"wb").write(png(images[256]))
    pngs=[png(images[n]) for n in SIZES]
    hdr=struct.pack("<HHH",0,1,len(SIZES)); off=6+16*len(SIZES); entries=b""
    for n,d in zip(SIZES,pngs):
        entries+=struct.pack("<BBBBHHII",n%256,n%256,0,0,1,32,len(d),off); off+=len(d)
    open(os.path.join(out,f"{i}-{name}.ico"),"wb").write(hdr+entries+b"".join(pngs))
    renders.append((images[256],(images[32],images[16])))
    print("rendered",name)
W=len(icons)*276+20; H=256+90
sheet=[[(250,249,244,255)]*W for _ in range(H)]
def paste(img,x0,y0):
    for y,r in enumerate(img):
        for x,p in enumerate(r):
            a=p[3]/255; b=sheet[y0+y][x0+x]
            sheet[y0+y][x0+x]=(int(p[0]*a+b[0]*(1-a)),int(p[1]*a+b[1]*(1-a)),int(p[2]*a+b[2]*(1-a)),255)
for i,(big,(s32,s16)) in enumerate(renders):
    x=20+i*276; paste(big,x,10); paste(s32,x+72,282); paste(s16,x+132,290)
open(os.path.join(out,"sheet.png"),"wb").write(png(sheet))
print("ok")

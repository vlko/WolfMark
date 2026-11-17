# Renders the WolfMark app icon proposals (paper, tag colours, Barlow) into
# src/WolfMark.App/Assets/IconProposals: 256px PNG + multi-size ICO per proposal, and sheet.png.
# Pure Python; reuses the TrueType reader of tools/gen-icon-paths.py. The default (AppIcons.Default,
# riso-white) is also copied to Assets/WolfMark.ico (.exe icon) and Assets/WolfMark-256.png (Desktop app).
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

def hatch_clip(period=0.045, on=0.5):
    return lambda x,y: ((x+y)/period)%1 < on

DAY = "Barlow-ExtraBold.ttf"
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

def riso(c, polys, x, y, w, h, main=None):
    """Glyph in the riso print style of the app's lead numbers: yellow and pink offsets under the blue."""
    for col, dx, dy in ((YELLOW, 0.025*w/0.66, 0.02*h/0.6), (PINK, -0.028*w/0.66, -0.014*h/0.6), (main or BLUE, 0, 0)):
        c.fill(fit(polys, x+dx, y+dy, w, h), col)

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

NIGHT = hexc("#1B2440"); WHITE = (255,255,255,255)
TILE = (0.03, 0.03, 0.94, 0.94, 0.21)

def in_tile(px, py):
    x, y, w, h, r = TILE
    if px < x or px > x+w or py < y or py > y+h: return False
    cx = min(max(px, x+r), x+w-r); cy = min(max(py, y+r), y+h-r)
    return (px-cx)**2 + (py-cy)**2 <= r*r

def tile(c, color):
    c.fill(rrect(*TILE), color)

def capsule(x1, y1, x2, y2, w, seg=14):
    a = math.atan2(y2-y1, x2-x1); r = w/2
    pts = [(x2 + r*math.cos(a - math.pi/2 + math.pi*k/seg), y2 + r*math.sin(a - math.pi/2 + math.pi*k/seg)) for k in range(seg+1)]
    pts += [(x1 + r*math.cos(a + math.pi/2 + math.pi*k/seg), y1 + r*math.sin(a + math.pi/2 + math.pi*k/seg)) for k in range(seg+1)]
    return [pts]

def in_rr(px, py, x, y, w, h, r):
    if px < x or px > x+w or py < y or py > y+h: return False
    cx = min(max(px, x+r), x+w-r); cy = min(max(py, y+r), y+h-r)
    return (px-cx)**2 + (py-cy)**2 <= r*r

def tile_frame(c, x, y, w, h, t):
    """Dashed "today" frame of a cell that touches the tile edge: follows the rounded corner."""
    tx, ty, tw, th, tr = TILE
    inner = lambda px, py: x+t <= px <= x+w-t and y+t <= py <= y+h-t and in_rr(px, py, tx+t, ty+t, tw-2*t, th-2*t, tr-t)
    ring = lambda px, py: in_tile(px, py) and not inner(px, py)
    c.fill(rect(x, y, w, h), YELLOW, clip=ring)
    c.fill(rect(x, y, w, h), INK, clip=lambda px, py: ring(px, py) and ((px+py)/0.04)%1 < 0.5)

# week band as the whole tile: past day (hatched), today (dashed frame), future day
def week_band(c):
    x0, w, top, h = 0.03, 0.94/3, 0.03, 0.94
    hatch = hatch_clip(0.05)
    c.fill(rect(x0, top, w, h), SALMON, clip=in_tile)
    c.fill(rect(x0, top, w, h), (255,255,255,120), clip=lambda px, py: in_tile(px, py) and hatch(px, py))
    c.fill([[(x0+w,top),(x0+2*w,top),(x0+w,top+h)]], TEAL); c.fill([[(x0+2*w,top),(x0+2*w,top+h),(x0+w,top+h)]], YELLOW)
    c.fill(rect(x0+2*w, top, w, h), BLUE, clip=in_tile)
    for k, (num, ink) in enumerate((("15", darker(SALMON, 0.5)), ("16", WHITE), ("17", WHITE))):
        text(c, num, x0+k*w+0.055, 0.40, w-0.11, 0.20, ink)
    today_frame(c, x0+w, top, w, h, 0.03)

# notebook page linked to the calendar: week band with dates on top, three time/text rows below
def notebook_calendar(c):
    tile(c, INK)
    grey = hexc("#8F8B83")
    cells = [(SALMON,"14",True),(TEAL,"15",True),(BLUE,"16",False),(hexc("#ECE8E1"),"17",False)]
    x0, cw, ch, cy = 0.03, 0.235, 0.27, 0.03
    hatch = hatch_clip(0.045)
    for k,(col,num,past) in enumerate(cells):
        x = x0 + k*cw
        c.fill(rect(x, cy, cw, ch), col, clip=in_tile)
        if past:
            c.fill(rect(x, cy, cw, ch), (255,255,255,110), clip=lambda px, py: in_tile(px, py) and hatch(px, py))
        ink = darker(col,0.5) if past else (hexc("#A8A49B") if col[0]==0xEC else WHITE)
        text(c, num, x+0.04, cy+0.085, cw-0.08, 0.12, ink)
    today_frame(c, x0+2*cw, cy, cw, ch, 0.024)
    # dotted gutter + three rows: time mark and a line of text
    for i in range(10):
        c.fill(rect(0.315, 0.40+i*0.05, 0.016, 0.027), grey)
    for i,(lw,col) in enumerate(((0.46,PAPER),(0.34,grey),(0.41,PAPER))):
        y = 0.43 + i*0.155
        c.fill(rect(0.13,y,0.13,0.06), grey)
        c.fill(rect(0.385,y,lw,0.06), col)

# tear-off calendar page: stacked sheets, paper binding rings, riso "16", tag-coloured dog-ear
def calendar_page(c):
    tile(c, TEAL)
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

# wolf in the flat geometric style of "wolfdeck": split grey head, cream muzzle, pink cheeks, paper outline, no tile
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

def wolf(c):
    draw_wolf(c, -0.1, 0.5 - 0.483*1.2, 1.2, halo=PAPER)

# calendar meets writing: 2x2 dated cells and a pencil writing over the corner
def calendar_pencil(c):
    s_ = 0.47
    x0 = 0.03
    hatch = hatch_clip(0.05)
    cells = [((x0,x0),SALMON,"15",True),((x0+s_,x0),"split","16",False),((x0,x0+s_),BLUE,"17",False),((x0+s_,x0+s_),hexc("#ECE8E1"),None,False)]
    for (x,y),col,num,past in cells:
        if col == "split":
            c.fill([[(x,y),(x+s_,y),(x,y+s_)]], TEAL, clip=in_tile); c.fill([[(x+s_,y),(x+s_,y+s_),(x,y+s_)]], YELLOW, clip=in_tile); ink=WHITE
        else:
            c.fill(rect(x,y,s_,s_), col, clip=in_tile)
            ink = WHITE
            if past:
                c.fill(rect(x,y,s_,s_), (255,255,255,110), clip=lambda px, py: in_tile(px, py) and hatch(px, py)); ink = darker(col,0.5)
        if num is not None:
            text(c, num, x+0.10, y+0.145, s_-0.20, 0.18, ink)
    tile_frame(c, x0+s_, x0, s_, s_, 0.03)
    # the day being written: a bullet list, the app's pencil writing its last line
    bx, by = x0 + s_ + 0.06, x0 + s_ + 0.09
    for k, lw in enumerate((0.24, 0.18, 0.10)):
        y = by + k*0.105
        c.fill(circle(bx + 0.014, y + 0.014, 0.016), INK)
        c.fill(rect(bx + 0.05, y, lw, 0.03), INK)
    app_pencil(c, bx + 0.05 + 0.10 + 0.01, by + 2*0.105 + 0.022, 0.31)

# --- second round: full-bleed tiles (no pale card + thin border), one idea each, built for 16 px
# the riso W turned inside out: accent-blue tile, paper W, pink and yellow offsets
def riso_blue(c, tile_color=None, letter=None):
    tile(c, tile_color or BLUE)
    polys = glyph_polys("W")
    x, y, w, h = 0.13, 0.17, 0.74, 0.66
    for col, dx, dy in ((YELLOW, 0.034, 0.028), (PINK, -0.034, -0.02), (letter or PAPER, 0, 0)):
        c.fill(fit(polys, x+dx, y+dy, w, h), col, clip=in_tile)

# W of four strokes, each in a tag colour, on an ink tile
W_PTS = [(0.19,0.27),(0.345,0.73),(0.50,0.36),(0.655,0.73),(0.81,0.27)]
def tag_strokes(c):
    tile(c, INK)
    for k, col in ((1, PINK), (2, YELLOW), (0, BLUE), (3, TEAL)):
        (x1,y1),(x2,y2) = W_PTS[k], W_PTS[k+1]
        c.fill(capsule(x1,y1,x2,y2,0.135), col)

# wolf mark: one-colour paper wolf head on ink (cheek ruff, long muzzle); ears and brow read as an "M"
WOLF_MARK = [(0.19,0.11),(0.40,0.30),(0.50,0.27),(0.60,0.30),(0.81,0.11),(0.81,0.40),(0.88,0.56),(0.79,0.60),(0.80,0.68),(0.68,0.72),
             (0.585,0.86),(0.50,0.90),(0.415,0.86),(0.32,0.72),(0.20,0.68),(0.21,0.60),(0.12,0.56),(0.19,0.40)]
def draw_wolf_mark(c, ox=0.0, oy=-0.005, sc=1.0, ink=None):
    ink = ink or INK
    T = lambda pts: [[(ox + x*sc, oy + y*sc) for x, y in pts]]
    c.fill(T(WOLF_MARK), PAPER)
    c.fill(T(WOLF_MARK[2:12]), hexc("#DDD7CA"))                               # shaded right half
    c.fill(T([(0.24,0.20),(0.345,0.315),(0.245,0.38)]), PINK); c.fill(T([(0.76,0.20),(0.655,0.315),(0.755,0.38)]), PINK)
    c.fill(T([(0.30,0.47),(0.43,0.515),(0.40,0.58),(0.325,0.55)]), ink)       # slanted eyes
    c.fill(T([(0.70,0.47),(0.57,0.515),(0.60,0.58),(0.675,0.55)]), ink)
    c.fill(T([(0.44,0.775),(0.56,0.775),(0.50,0.855)]), ink)                  # nose

def wolf_ink(c):
    tile(c, INK)
    draw_wolf_mark(c)

# calendar page as the whole tile: blue header with ring holes, riso W where the date would be
def calendar_w(c):
    tile(c, BLUE)
    c.fill(rrect(0.085, 0.27, 0.83, 0.645, 0.15), WHITE)
    c.fill(rect(0.085, 0.27, 0.83, 0.16), WHITE)
    for x in (0.30, 0.70):
        c.fill(circle(x, 0.15, 0.048), darker(BLUE, 0.35))
    riso(c, glyph_polys("W"), 0.21, 0.37, 0.58, 0.44)

# a pencil writing a W in one stroke, on a yellow tile
def pencil_w(c):
    tile(c, YELLOW)
    pts = [(0.14,0.42),(0.27,0.80),(0.40,0.50),(0.53,0.80),(0.64,0.50)]
    for a, b in zip(pts, pts[1:]):
        c.fill(capsule(a[0],a[1],b[0],b[1],0.095), INK)
    # solid pencil in the icon's own colours (ink, paper, yellow), its tip just past the end of the stroke;
    # a ring of the tile colour around it keeps the graphite tip apart from the stroke
    ang = math.radians(58); L, w, t = 0.35, 0.105, 0.015
    dx, dy = math.cos(ang), -math.sin(ang)
    tx, ty = pts[-1][0] + 0.058*dx, pts[-1][1] + 0.058*dy
    P = lambda u, v: (tx + u*dx - v*dy, ty + u*dy + v*dx)
    quad = lambda u0, u1, v0, v1: [[P(u0,v0), P(u1,v0), P(u1,v1), P(u0,v1)]]
    cone = 0.125; body_end, ferrule_end = L - 0.075, L - 0.04
    shape = lambda m: [[P(-0.035-2.4*m,0), P(cone-0.005,-w/2-t-m), P(L+t+m,-w/2-t-m), P(L+t+m,w/2+t+m), P(cone-0.005,w/2+t+m)]]
    c.fill(shape(0.012), YELLOW, clip=in_tile)
    c.fill(shape(0), INK)                                                                                  # outline
    c.fill([[P(0,0), P(cone,-w/2), P(cone,w/2)]], PAPER)                                                   # wood
    g = 0.068
    c.fill([[P(-0.01,0), P(g,-w/2*g/cone-0.004), P(g,w/2*g/cone+0.004)]], INK)                             # graphite
    c.fill(quad(cone, body_end, -w/2, -w/6), WHITE); c.fill(quad(cone, body_end, -w/6, w/6), PAPER)
    c.fill(quad(cone, body_end, w/6, w/2), EDGE)
    c.fill(quad(body_end, ferrule_end, -w/2, w/2), INK)                                                    # ferrule
    c.fill(quad(ferrule_end + t/2, L, -w/2, w/2), darker(YELLOW, 0.14))                                    # eraser
    c.fill(quad(cone - t/2, cone + t/2, -w/2, w/2), INK)


# --- free-form icons – the motif is the icon's own shape, no tile
# task-list checkbox with ears: the check mark sits where the muzzle would be
def wolf_check(c):
    body = rrect(0.10, 0.24, 0.80, 0.73, 0.17)
    ears = [[(0.10,0.02),(0.38,0.26),(0.10,0.44)], [(0.90,0.02),(0.62,0.26),(0.90,0.44)]]
    dark = darker(GREEN, 0.22)
    c.fill(body, GREEN); c.fill(ears, GREEN)
    c.fill(body, dark, clip=lambda px, py: px >= 0.5); c.fill(ears, dark, clip=lambda px, py: px >= 0.5)
    for a, b in (((0.29,0.60),(0.44,0.76)), ((0.44,0.76),(0.72,0.45))):
        c.fill(capsule(a[0],a[1],b[0],b[1],0.105), WHITE)

# the faceted wolf carrying a pencil in its mouth
def wolf_pencil(c):
    draw_wolf(c, -0.07, -0.085, 1.14)
    y, w = 0.775, 0.10
    x0, x1 = 0.02, 0.98
    q = lambda a, b, v0, v1, col: c.fill(rect(a, y + v0, b - a, v1 - v0), col)
    q(x0, x0+0.09, -w/2, w/2, PINK)                                                        # eraser
    q(x0+0.09, x0+0.15, -w/2, w/2, hexc("#C9C3B8")); q(x0+0.105, x0+0.118, -w/2, w/2, hexc("#A8A49B")); q(x0+0.128, x0+0.141, -w/2, w/2, hexc("#A8A49B"))
    q(x0+0.15, x1-0.14, -w/2, -w/6, hexc("#FBD95B")); q(x0+0.15, x1-0.14, -w/6, w/6, YELLOW); q(x0+0.15, x1-0.14, w/6, w/2, darker(YELLOW, 0.14))
    c.fill([[(x1-0.14, y-w/2), (x1, y), (x1-0.14, y+w/2)]], hexc("#F6E2BC"))               # wood
    c.fill([[(x1-0.05, y-w/2*0.05/0.14), (x1, y), (x1-0.05, y+w/2*0.05/0.14)]], INK)       # graphite

# the inverse of riso_blue: paper tile, accent-blue W, the same pink and yellow offsets
def riso_white(c):
    riso_blue(c, PAPER, BLUE)

def png(rows):
    h=len(rows); w=len(rows[0])
    raw=b"".join(b"\x00"+b"".join(bytes(p) for p in r) for r in rows)
    ch=lambda t,d: struct.pack(">I",len(d))+t+d+struct.pack(">I",zlib.crc32(t+d)&0xffffffff)
    return b"\x89PNG\r\n\x1a\n"+ch(b"IHDR",struct.pack(">IIBBBBB",w,h,8,6,0,0,0))+ch(b"IDAT",zlib.compress(raw,9))+ch(b"IEND",b"")

out=os.path.join(TOOLS, "..", "src", "WolfMark.App", "Assets", "IconProposals")
os.makedirs(out, exist_ok=True)
# In the order of the picker (AppIcons.All); the file name is the icon's id.
ICONS=[("riso-white",riso_white),("riso-blue",riso_blue),("tag-strokes",tag_strokes),("calendar-w",calendar_w),("pencil-w",pencil_w),
       ("wolf",wolf),("wolf-pencil",wolf_pencil),("week-band",week_band),("notebook-calendar",notebook_calendar),
       ("calendar-page",calendar_page),("calendar-pencil",calendar_pencil),("wolf-ink",wolf_ink),("wolf-check",wolf_check)]
DEFAULT="riso-white"
SIZES=[256,64,48,32,24,16]
# Optional arguments: ids to render (default all). The overview sheet is written to sheet.png only for
# a full run; a partial run writes it to $SHEET when set.
ids=[n for n,_ in ICONS]
only=sys.argv[1:] or ids
renders=[]
for name in only:
    f=dict(ICONS)[name]
    c=Canvas(256,6); f(c)          # 1536px supersampled canvas divides evenly into every ICO size
    images={n:c.image(n) for n in SIZES}
    open(os.path.join(out,f"{name}.png"),"wb").write(png(images[256]))
    pngs=[png(images[n]) for n in SIZES]
    hdr=struct.pack("<HHH",0,1,len(SIZES)); off=6+16*len(SIZES); entries=b""
    for n,d in zip(SIZES,pngs):
        entries+=struct.pack("<BBBBHHII",n%256,n%256,0,0,1,32,len(d),off); off+=len(d)
    open(os.path.join(out,f"{name}.ico"),"wb").write(hdr+entries+b"".join(pngs))
    renders.append((images[256],(images[32],images[16])))
    print("rendered",name)
# Sheet: 7 per row; under each icon its position in the picker and its 32 and 16 px sizes on the light sheet and on a dark chip.
COLS=7; CELL_W=276; CELL_H=256+90
W=min(len(renders),COLS)*CELL_W+20; H=((len(renders)+COLS-1)//COLS)*CELL_H
sheet=[[(250,249,244,255)]*W for _ in range(H)]
def paste(img,x0,y0):
    for y,r in enumerate(img):
        for x,p in enumerate(r):
            a=p[3]/255; b=sheet[y0+y][x0+x]
            sheet[y0+y][x0+x]=(int(p[0]*a+b[0]*(1-a)),int(p[1]*a+b[1]*(1-a)),int(p[2]*a+b[2]*(1-a)),255)
for i,(big,(s32,s16)) in enumerate(renders):
    x=20+(i%COLS)*CELL_W; y=10+(i//COLS)*CELL_H
    paste(big,x,y); paste(s32,x+36,y+276); paste(s16,x+84,y+284)
    lab=Canvas(26,4); text(lab, str(ids.index(only[i])+1), 0.02, 0.2, 0.96, 0.6, hexc("#8F8B83")); paste(lab.image(), x, y+279)   # position in the picker
    for yy in range(y+268,y+316):
        for xx in range(x+136,x+232): sheet[yy][xx]=(32,32,32,255)
    paste(s32,x+150,y+276); paste(s16,x+198,y+284)
sheet_path=os.path.join(out,"sheet.png") if len(only)==len(ICONS) else os.environ.get("SHEET")
if sheet_path: open(sheet_path,"wb").write(png(sheet))
if DEFAULT in only:
    assets=os.path.join(out,"..")
    open(os.path.join(assets,"WolfMark.ico"),"wb").write(open(os.path.join(out,DEFAULT+".ico"),"rb").read())
    open(os.path.join(assets,"WolfMark-256.png"),"wb").write(open(os.path.join(out,DEFAULT+".png"),"rb").read())

# Generates the three pictures of Docs/GDD.md:
#   Docs/gdd/routes.svg         where the three kinds of enemy walk on one board
#   Docs/gdd/screen-build.svg   the target screen in a build phase
#   Docs/gdd/screen-wave.svg    the target screen early in a wave of the adaptive version
# and a PNG of each beside it (through headless Edge), for viewers that do not show SVG.
# The board (rocks, walls, towers) is board.py. The walker and sapper routes are not
# drawn by hand: routes.txt is what the game's own flow field gives for that board.
import os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, 'Docs', 'gdd')
sys.path.insert(0, HERE)
import board as B


def edge():
    for p in (r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', r'C:\Program Files\Microsoft\Edge\Application\msedge.exe'):
        if os.path.exists(p): return p
    return None

W, H = 1280, 720
N = B.N
ROUTES = B.read_routes(os.path.join(HERE, 'routes.txt'))
WALKER = [(x, y) for x, y, d in ROUTES['ground']['tiles']]
SAPPER = [(x, y) for x, y, d in ROUTES['sapper']['tiles']]
DUG = [(x, y) for x, y, d in ROUTES['sapper']['tiles'] if d]      # where sappers go through a wall
assert len(DUG) == 2 and not [t for t in ROUTES['ground']['tiles'] if t[2]]

BG = '#0b0f19'; PANEL = '#121a2b'; EDGE = '#24304a'; TEXT = '#e5e7eb'; DIM = '#94a3b8'
GOLD = '#fbbf24'; TEAL = '#2dd4bf'; PINK = '#ff3df0'; RED = '#f87171'; ORANGE = '#fb923c'; BLUE = '#60a5fa'
WALL = '#a5b4fc'; ROCK = '#333d58'; ROCK_EDGE = '#4a5677'
FONT = "font-family=\"Segoe UI, Arial, Helvetica, sans-serif\""
DOT = '  &#183;  '
DTYPE = {'physical': '#e2e8f0', 'fire': '#fb923c', 'frost': '#7dd3fc'}


def render(mode, path):
    out = []
    add = out.append
    big = mode == 'routes'
    wave = mode == 'wave'
    BX, BY, CELL = (28, 38, 17) if big else (28, 58, 14)
    size = N * CELL

    def cx(tx): return BX + (tx + 0.5) * CELL
    def cy(ty): return BY + (N - 1 - ty + 0.5) * CELL

    def text(x, y, s, size=13, fill=TEXT, weight='400', anchor='start', extra=''):
        add('<text x="%.1f" y="%.1f" font-size="%d" fill="%s" font-weight="%s" text-anchor="%s" %s %s>%s</text>'
            % (x, y, size, fill, weight, anchor, FONT, extra, s))

    def marker(x, y, n):
        add('<circle cx="%.1f" cy="%.1f" r="11" fill="%s" stroke="#0b0f19" stroke-width="2"/>' % (x, y, GOLD))
        text(x, y + 4.5, str(n), 13, '#0b0f19', '700', 'middle')

    def tower(tx, ty, dtype, ghost=False):
        op = '0.55' if ghost else '1'
        r = CELL * 0.44
        add('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="#0b0f19" stroke="%s" stroke-width="2.6" opacity="%s"/>' % (cx(tx), cy(ty), r, DTYPE[dtype], op))
        add('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s" opacity="%s"/>' % (cx(tx), cy(ty), r * 0.4, DTYPE[dtype], op))

    def polyline(tiles, colour, width, dash=None, glow=False):
        pts = ' '.join('%.1f,%.1f' % (cx(x), cy(y)) for x, y in tiles)
        if glow:
            add('<polyline points="%s" fill="none" stroke="%s" stroke-width="%.1f" stroke-opacity="0.16" stroke-linejoin="round" stroke-linecap="round"/>' % (pts, colour, width * 3))
        add('<polyline points="%s" fill="none" stroke="%s" stroke-width="%.1f" stroke-linejoin="round" stroke-linecap="round"%s/>'
            % (pts, colour, width, ' stroke-dasharray="%s"' % dash if dash else ''))

    def sample(x, y, colour, dash=None, width=3.0):
        add('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="%.1f" stroke-linecap="round"%s/>'
            % (x, y, x + 44, y, colour, width, ' stroke-dasharray="%s"' % dash if dash else ''))

    # the sappers of the wave picture: single file along their own route, through the
    # breached wall, past the towers, the first of them chewing the second thin wall
    enemies, head = [], 0
    if wave:
        head = SAPPER.index(DUG[1])
        killed = {19, 23, 26}                          # gaps the towers have made
        enemies = [(SAPPER[i][0], SAPPER[i][1], i) for i in range(1, head) if i not in killed]
        fx, fy = DUG[1]
        enemies += [(fx - 0.35, fy - 0.4, head), (fx - 0.55, fy + 0.05, head + 1), (fx + 0.05, fy - 0.6, head + 2)]

    add('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" width="%d" height="%d">' % (W, H, W, H))
    add('<rect width="%d" height="%d" fill="%s"/>' % (W, H, BG))

    # ---------------------------------------------------------------- top bar
    if not big:
        add('<rect x="0" y="0" width="930" height="46" fill="#0d1321"/>')
        text(28, 29, 'CORE', 13, DIM, '600')
        for i in range(10):
            add('<rect x="%d" y="16" width="13" height="16" rx="2" fill="%s"/>' % (76 + i * 17, TEAL if i < 8 else '#1f2a40'))
        text(252, 29, '8 / 10', 14, TEXT, '600')
        if wave:
            text(470, 29, 'Wave 7', 15, TEXT, '700', 'middle')
            text(506, 29, '%d of 52 on the board' % len(enemies), 13, DIM)
        else:
            text(480, 29, 'Wave 6 cleared', 15, TEXT, '600', 'middle')
            text(545, 29, 'wave 7 starts in 0:08', 13, DIM)

    # ---------------------------------------------------------------- board
    add('<rect x="%d" y="%d" width="%d" height="%d" fill="#0e1524"/>' % (BX, BY, size, size))
    for i in range(1, N):
        add('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#18233a" stroke-width="0.6"/>' % (BX + i * CELL, BY, BX + i * CELL, BY + size))
        add('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#18233a" stroke-width="0.6"/>' % (BX, BY + i * CELL, BX + size, BY + i * CELL))

    for (tx, ty) in sorted(B.ROCKS):
        add('<rect x="%d" y="%d" width="%d" height="%d" fill="%s" stroke="%s" stroke-width="0.7"/>' % (BX + tx * CELL, BY + (N - 1 - ty) * CELL, CELL, CELL, ROCK, ROCK_EDGE))
    add('<rect x="%d" y="%d" width="%d" height="%d" fill="none" stroke="#5eead4" stroke-width="2" stroke-opacity="0.8"/>' % (BX, BY, size, size))

    breached = DUG[0] if wave else None      # the first thin wall is already gone in the wave picture
    chewed = DUG[1] if wave else None        # the second is being chewed
    for (tx, ty) in B.WALLS:
        x0, y0, s = BX + tx * CELL + 0.5, BY + (N - 1 - ty) * CELL + 0.5, CELL - 1
        if (tx, ty) == breached:
            add('<rect x="%.1f" y="%.1f" width="%d" height="%d" rx="2" fill="none" stroke="%s" stroke-width="1.4" stroke-dasharray="3 3"/>' % (x0, y0, s, s, ORANGE))
        elif (tx, ty) == chewed:
            add('<rect x="%.1f" y="%.1f" width="%d" height="%d" rx="2" fill="%s"/>' % (x0 + 3, y0 + 3, s - 6, s - 6, WALL))
            add('<rect x="%.1f" y="%.1f" width="%d" height="%d" fill="none" stroke="%s" stroke-width="2"/>' % (x0 - 1.5, y0 - 1.5, s + 3, s + 3, ORANGE))
        else:
            add('<rect x="%.1f" y="%.1f" width="%d" height="%d" rx="2" fill="%s"/>' % (x0, y0, s, s, WALL))

    # routes: in a build phase one line per kind of enemy; in a wave only the kind that is coming
    lw = 3.2 if big else 2.6
    if not wave:
        add('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="%.1f" stroke-dasharray="1.5 6.5" stroke-linecap="round"/>'
            % (cx(B.SPAWN[0]), cy(B.SPAWN[1]), cx(B.CORE[0]), cy(B.CORE[1]), BLUE, lw + 0.6))
        polyline(WALKER, PINK, lw, glow=True)
    add('<g transform="translate(2.6 2.6)">')
    polyline(SAPPER, ORANGE, lw - 0.4, dash='7 5')
    add('</g>')

    add('<rect x="%.1f" y="%.1f" width="20" height="20" rx="3" fill="#0b0f19" stroke="%s" stroke-width="2.5"/>' % (cx(B.SPAWN[0]) - 10, cy(B.SPAWN[1]) - 10, TEXT))
    text(cx(B.SPAWN[0]), cy(B.SPAWN[1]) + 4.5, 'S', 12, TEXT, '700', 'middle')
    text(cx(B.SPAWN[0]) + 15, cy(B.SPAWN[1]) + 19, 'spawn', 11, DIM)
    add('<circle cx="%.1f" cy="%.1f" r="17" fill="%s" fill-opacity="0.18"/>' % (cx(B.CORE[0]), cy(B.CORE[1]), TEAL))
    add('<circle cx="%.1f" cy="%.1f" r="10" fill="#0b0f19" stroke="%s" stroke-width="3"/>' % (cx(B.CORE[0]), cy(B.CORE[1]), TEAL))
    text(cx(B.CORE[0]), cy(B.CORE[1]) + 4, 'C', 11, TEAL, '700', 'middle')
    text(cx(B.CORE[0]) + 14, cy(B.CORE[1]) + 22, 'core', 11, DIM)

    towers = list(B.TOWERS) + ([B.GHOST] if wave else [])

    if wave:
        by_index = dict((i, (x, y)) for x, y, i in enemies)
        shots = [((20, 14), 18, 'physical'), ((25, 15), 21, 'fire'), ((24, 18), 22, 'frost'), ((27, 17), 24, 'physical'), ((29, 22), 27, 'physical')]
        for (tw, idx, d) in shots:
            ex, ey = by_index[idx]
            add('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="1.6" stroke-opacity="0.9"/>' % (cx(tw[0]), cy(tw[1]), cx(ex), cy(ey), DTYPE[d]))
            if d == 'fire':
                add('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s" fill-opacity="0.18" stroke="%s" stroke-opacity="0.6"/>' % (cx(ex), cy(ey), 0.75 * CELL, ORANGE, ORANGE))
        for (ex, ey, i) in enemies:
            x, y = cx(ex), cy(ey)
            add('<rect x="%.1f" y="%.1f" width="7" height="7" rx="1.5" fill="%s" stroke="#0b0f19" stroke-width="1" transform="rotate(45 %.1f %.1f)"/>' % (x - 3.5, y - 3.5, ORANGE, x, y))
            if 18 <= i < head and i % 2 == 0:
                hp = 0.3 + 0.6 * ((i * 7) % 10) / 10.0
                add('<rect x="%.1f" y="%.1f" width="11" height="2.4" fill="#1f2a40"/>' % (x - 5.5, y - 9.5))
                add('<rect x="%.1f" y="%.1f" width="%.1f" height="2.4" fill="#4ade80"/>' % (x - 5.5, y - 9.5, 11 * hp))

    for tx, ty, d in towers:
        tower(tx, ty, d)

    if mode == 'build':
        gx, gy, gd = B.GHOST
        add('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="#ffffff" fill-opacity="0.05" stroke="#ffffff" stroke-opacity="0.7" stroke-width="1.5" stroke-dasharray="5 4"/>' % (cx(gx), cy(gy), 3.5 * CELL))
        tower(gx, gy, gd, ghost=True)
        add('<path d="M %.1f %.1f l 0 15 l 4 -4 l 3 7 l 3 -1.3 l -3 -7 l 5.5 0 z" fill="#ffffff" stroke="#0b0f19" stroke-width="1"/>' % (cx(gx) + 5, cy(gy) + 3))
        tx0, ty0 = cx(gx) + 24, cy(gy) + 18
        add('<rect x="%.1f" y="%.1f" width="128" height="40" rx="5" fill="#0b0f19" fill-opacity="0.94" stroke="%s"/>' % (tx0, ty0, EDGE))
        text(tx0 + 9, ty0 + 16, 'Archer', 12, TEXT, '700')
        text(tx0 + 119, ty0 + 16, '10', 12, GOLD, '700', 'end')
        text(tx0 + 9, ty0 + 32, 'range 3.5 tiles', 11, DIM)

    if wave:
        add('<rect x="76" y="66" width="404" height="42" rx="6" fill="#190f2e" fill-opacity="0.95" stroke="#c084fc" stroke-width="1.5"/>')
        text(92, 83, 'WAVE 7', 10, '#c084fc', '700', extra='letter-spacing="1.2"')
        text(92, 100, 'Sappers. They dig where your wall is one tile thick.', 13, TEXT, '600')

    NX = 952
    if big:
        # ------------------------------------------------------------ the explanation
        NX = 716
        text(NX, 62, 'Where enemies walk', 22, TEXT, '700')
        text(NX, 84, 'One board, three kinds of enemy, three routes. Nobody steers an enemy:', 13, DIM)
        text(NX, 102, 'each one takes the cheapest way to the core for its own kind.', 13, DIM)

        def entry(y, colour, dash, head, lines, width=3.2):
            sample(NX, y - 5, colour, dash, width)
            text(NX + 58, y, head, 15, TEXT, '700')
            for k, ln in enumerate(lines): text(NX + 58, y + 20 + k * 17, ln, 12.5, DIM)

        entry(146, PINK, None, 'Walkers go round',
              ['To a walker, a wall tile counts like 15 tiles of walking. Going',
               'round is cheaper here, so they take the long way: about 71 tiles,',
               'past all nine towers. They chew nothing.'])
        entry(246, ORANGE, '7 5', 'Sappers go through',
              ['To a sapper, a wall tile counts like 3 tiles. It cuts through the',
               'two walls that are one tile thick (1 and 2), leaves the thick wall',
               '(3) alone, and is there after about 50 tiles, past five towers.'])
        entry(346, BLUE, '1.5 6.5', 'Flyers go straight',
              ['A straight line over rocks (4), walls and towers: 44 tiles. Nothing',
               'you build bends it. Only towers that can hit flyers shoot at them;',
               'here one of those is in reach.'], width=3.8)

        add('<rect x="%d" y="424" width="536" height="250" rx="8" fill="%s" stroke="%s"/>' % (NX, PANEL, EDGE))
        y = 452
        for head, lines in [
            ('Same kind, same entrance, same route.', ['Enemies of one kind walk in single file along one line. The line is', 'drawn on the board before the wave, so you can build against it.']),
            ('The director chooses the kind, never the path.', ['It decides what comes (and later, from which side). After that every', 'enemy finds its own way by the rule above. It cannot aim one at a tile.']),
            ('A route can change in mid-wave.', ['When a wall is chewed through, or you build, every enemy on the', 'board works out its way again from where it stands.']),
        ]:
            text(NX + 18, y, head, 13.5, TEXT, '700')
            for k, ln in enumerate(lines): text(NX + 18, y + 19 + k * 17, ln, 12.5, DIM)
            y += 78

        (ax, ay), (bx, by) = DUG
        marks = [(cx(ax) - 20, cy(ay)), (cx(bx) + 21, cy(by) - 2), (cx(12.6), cy(23.7)), (cx(17.6), cy(20.6))]
        for i, (mx, my) in enumerate(marks): marker(mx, my, i + 1)
        text(BX, BY + size + 20, 'Dark blocks: rocks, part of the map. Light tiles: walls the player drew. Rings: towers. The pink and orange routes are what the game\'s own path rule gives for this board.', 10, '#64748b')
        add('</svg>')
        open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
        print(path, len(out), 'elements')
        return

    # ---------------------------------------------------------------- shop panel
    SX, SY, SW = 584, 58, 336
    add('<rect x="%d" y="%d" width="%d" height="532" rx="8" fill="%s" stroke="%s"/>' % (SX, SY, SW, PANEL, EDGE))
    text(SX + 16, SY + 27, 'TOWER SHOP', 14, TEXT, '700', extra='letter-spacing="1"')
    text(SX + 16, SY + 45, 'new offers after every wave', 11, DIM)
    text(SX + SW - 16, SY + 27, 'Budget', 11, DIM, '400', 'end')
    text(SX + SW - 16, SY + 46, '9.2' if wave else '14.6', 17, GOLD, '700', 'end')

    first = 'bought' if wave else 'selected'
    cards = [('Archer', 'physical', 'single target' + DOT + 'hits flyers', '10', first),
             ('Cannon', 'fire', 'splash' + DOT + 'ground only', '18', 'poor'),
             ('Frost', 'frost', 'slows' + DOT + 'ground only', '12', 'bought'),
             ('Cannon', 'fire', 'splash' + DOT + 'ground only', '18', 'poor')]
    y = SY + 58
    for name, dtype, tags, price, state in cards:
        border, fill, op = EDGE, '#0f1626', '1'
        if state == 'selected': border, fill = TEAL, '#10231f'
        if state == 'bought': op = '0.42'
        add('<g opacity="%s">' % op)
        add('<rect x="%d" y="%d" width="%d" height="80" rx="7" fill="%s" stroke="%s" stroke-width="%s"/>' % (SX + 14, y, SW - 28, fill, border, '2' if state == 'selected' else '1'))
        add('<circle cx="%d" cy="%d" r="18" fill="#0b0f19" stroke="%s" stroke-width="3"/>' % (SX + 48, y + 40, DTYPE[dtype]))
        add('<circle cx="%d" cy="%d" r="7" fill="%s"/>' % (SX + 48, y + 40, DTYPE[dtype]))
        text(SX + 82, y + 30, name, 15, TEXT, '700')
        text(SX + 82, y + 48, dtype, 12, DTYPE[dtype], '600')
        text(SX + 82, y + 66, tags, 11, DIM)
        text(SX + SW - 30, y + 35, price, 18, RED if state == 'poor' else GOLD, '700', 'end')
        add('</g>')
        if state == 'selected': text(SX + SW - 30, y + 66, 'click a tile to place', 10, TEAL, '600', 'end')
        if state == 'bought': text(SX + SW - 30, y + 66, 'BOUGHT', 10, DIM, '700', 'end')
        if state == 'poor': text(SX + SW - 30, y + 66, 'not enough budget', 10, RED, '400', 'end')
        y += 88

    # the key to the route lines, under the shop cards
    add('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s"/>' % (SX + 14, y + 8, SX + SW - 14, y + 8, EDGE))
    text(SX + 16, y + 30, 'ROUTES', 11, DIM, '700', extra='letter-spacing="1"')
    text(SX + SW - 16, y + 30, 'the wave now on the board' if wave else 'every kind of enemy you have met', 11, DIM, anchor='end')
    rows = [(ORANGE, '7 5', 'Sappers', 'through walls one tile thick', 2.6)] if wave else \
           [(PINK, None, 'Walkers', 'round your walls', 3.0), (ORANGE, '7 5', 'Sappers', 'through walls one tile thick', 2.6), (BLUE, '1.5 6.5', 'Flyers', 'straight, over everything', 3.4)]
    ry = y + 54
    for colour, dash, name, what, width in rows:
        sample(SX + 16, ry - 4, colour, dash, width)
        text(SX + 72, ry, name, 12, TEXT, '700')
        text(SX + 132, ry, what, 11, DIM)
        ry += 22
    legend_y = y + 26

    # ---------------------------------------------------------------- bottom bar
    BY2 = 606
    add('<rect x="28" y="%d" width="892" height="96" rx="8" fill="%s" stroke="%s"/>' % (BY2, PANEL, EDGE))

    def tool(x, w, label, hot=False, off=False):
        add('<g opacity="%s">' % ('0.4' if off else '1'))
        add('<rect x="%d" y="%d" width="%d" height="72" rx="6" fill="%s" stroke="%s" stroke-width="%s"/>' % (x, BY2 + 12, w, '#10231f' if hot else '#0f1626', TEAL if hot else EDGE, '2' if hot else '1'))
        text(x + 12, BY2 + 31, label, 12, TEXT, '700', extra='letter-spacing="1"')
        add('</g>')

    tool(44, 232, 'WALL')
    add('<rect x="56" y="%d" width="16" height="16" rx="2.5" fill="%s"/>' % (BY2 + 46, WALL))
    text(82, BY2 + 52, 'click a tile, or drag to draw a line', 11, DIM)
    text(82, BY2 + 69, '1 budget a tile', 11, GOLD, '600')
    tool(288, 232, 'SELL', off=wave)
    if wave:
        text(300, BY2 + 52, 'opens again when the wave is over', 11, DIM)
    else:
        text(300, BY2 + 52, 'click a tower or a wall tile', 11, DIM)
        text(300, BY2 + 69, 'half its price back, between waves', 11, DIM)

    if wave:
        add('<rect x="548" y="%d" width="150" height="42" rx="7" fill="#1b2437" stroke="%s" opacity="0.5"/>' % (BY2 + 14, EDGE))
        text(623, BY2 + 41, 'WAVE RUNNING', 13, DIM, '700', 'middle')
    else:
        add('<rect x="548" y="%d" width="150" height="42" rx="7" fill="%s"/>' % (BY2 + 14, TEAL))
        text(623, BY2 + 41, 'START WAVE', 14, '#06231f', '700', 'middle')
    add('<rect x="710" y="%d" width="60" height="42" rx="7" fill="#1b2437" stroke="%s"/>' % (BY2 + 14, EDGE))
    text(740, BY2 + 41, 'x1', 14, TEXT, '700', 'middle')
    add('<rect x="782" y="%d" width="92" height="42" rx="7" fill="#1b2437" stroke="%s"/>' % (BY2 + 14, EDGE))
    text(828, BY2 + 41, 'PAUSE', 13, TEXT, '700', 'middle')
    text(548, BY2 + 78, 'Esc put down' + DOT + 'Space pause' + DOT + '1 2 3 speed', 11, DIM)

    # ---------------------------------------------------------------- markers and notes
    (ax, ay), (bx, by) = DUG
    if wave:
        marks = [(76, 87), (cx(5.4), cy(7.9)), (cx(ax) - 26, cy(ay) - 4), (cx(bx) + 22, cy(by) - 6), (cx(22.6), cy(18.2)), (SX + 150, SY + 22), (296, BY2 + 16), (700, 24)]
        title = 'A wave has just started'
        sub = 'adaptive version; notes, not part of the game'
        notes = [
            ('The wave banner.', ['One written line as the wave', 'starts: what is coming, and what', 'in your build it is aimed at.']),
            ('The enemies.', ['Sappers, in single file along', 'their own route (the dashed', 'line). Nobody steers them.']),
            ('A wall chewed through.', ['The tile is gone. Every route', 'is worked out again, at once.']),
            ('A wall being chewed.', ['The tile shrinks as it is eaten.', 'One tile thick: cheap to cross.']),
            ('Towers at work.', ['Each fires at the enemy nearest', 'the core. Shots are drawn; the', 'damage itself is instant.']),
            ('The shop stays open.', ['You can still buy towers and', 'draw walls in mid-wave.']),
            ('No selling now.', ['So that a gap cannot be opened', 'and closed under fire.']),
            ('How far along.', ['How many of the wave are on the', 'board.']),
        ]
    else:
        marks = [(310, 24), (690, 24), (cx(17.6), cy(20.6)), (cx(22.5), cy(22.4)), (SX + 96, legend_y), (cx(30.6), cy(18.3)), (SX + 150, SY + 22), (36, BY2 + 6)]
        title = 'A build phase'
        sub = 'notes, not part of the game'
        notes = [
            ('Core health.', ['Every enemy that reaches the', 'core takes one point. At zero', 'the session ends.']),
            ('The clock.', ['Ten seconds between waves (45', 'before the first). Start Wave', 'skips the rest; Pause stops it.']),
            ('Rocks.', ['Part of the map, the same for', 'everyone. Not yours: no building', 'on them, no way through on foot.']),
            ('Your walls.', ['Single tiles, drawn by click or', 'drag. They bend the routes and', 'never block: all can be chewed.']),
            ('The routes.', ['One line for each kind of enemy', 'you have met, worked out again', 'each time you build.']),
            ('Placing a tower.', ['A bought offer follows the mouse', 'with its range ring, and goes on', 'any free tile.']),
            ('The shop.', ['Four offers a wave, each bought', 'once. Grey: bought. Red price:', 'not enough budget.']),
            ('Tools.', ['Wall and Sell. One is on at a', 'time; Esc puts it down.']),
        ]
    for i, (mx, my) in enumerate(marks): marker(mx, my, i + 1)

    add('<line x1="936" y1="20" x2="936" y2="700" stroke="%s" stroke-dasharray="3 5"/>' % EDGE)
    text(NX, 40, title, 15, TEXT, '700')
    text(NX, 58, sub, 11, DIM)
    y = 90
    for i, (head, lines) in enumerate(notes):
        marker(NX + 11, y - 4, i + 1)
        text(NX + 32, y, head, 13, TEXT, '700')
        for k, line in enumerate(lines): text(NX + 32, y + 18 + k * 15, line, 12, DIM)
        y += 30 + 15 * len(lines)

    text(28, 716, 'Mockup of the target screen. Not final art. The board is seen from straight above; it is the board of the picture of the routes.', 10, '#64748b')
    add('</svg>')
    open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print(path, len(out), 'elements')


os.makedirs(OUT, exist_ok=True)
for mode, name in (('routes', 'routes'), ('build', 'screen-build'), ('wave', 'screen-wave')):
    svg = os.path.join(OUT, name + '.svg')
    render(mode, svg)
    if edge():
        # one at a time, with a pause: two headless runs back to back lose the second picture
        subprocess.run([edge(), '--headless', '--disable-gpu', '--hide-scrollbars', '--window-size=%d,%d' % (W, H),
                        '--screenshot=' + os.path.join(OUT, name + '.png'), 'file:///' + svg.replace(os.sep, '/')],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        time.sleep(2)
    else:
        print('Edge not found: no PNG for', name)

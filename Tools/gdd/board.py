# The sample board of the GDD pictures: rocks (part of the map), the walls and towers a
# player might have by wave 7. Tiles are (x, y), y up, 38 x 38, as in the game.
# Run as a script it writes out/board.txt and out/towers.txt for routetool, which works
# out the walker and sapper routes with the game's own flow field (see README.md).
import math, os, sys

N = 38
SPAWN = (3, 3)
CORE = (34, 34)


def blob(cx, cy, along, across, angle=135.0):
    # an ellipse of tiles; `along` lies in direction `angle` (135 = the anti-diagonal)
    a = math.radians(angle)
    ux, uy = math.cos(a), math.sin(a)
    out = set()
    for x in range(N):
        for y in range(N):
            dx, dy = x - cx, y - cy
            u = dx * ux + dy * uy
            v = -dx * uy + dy * ux
            if (u / along) ** 2 + (v / across) ** 2 <= 1.0: out.add((x, y))
    return out


def line(a, b):
    # 8-connected line of tiles from a to b (what a drag from a to b would paint)
    (x0, y0), (x1, y1) = a, b
    n = max(abs(x1 - x0), abs(y1 - y0))
    return [(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n)) for i in range(n + 1)] if n else [a]


ROCKS = set()
# ridge 1 (nearest the spawn): gaps at about (6,16), (11,11), (16,6)
ROCKS |= blob(2.0, 20.0, 2.8, 1.25) | blob(8.2, 13.8, 2.0, 1.3) | blob(13.8, 8.2, 2.0, 1.2) | blob(19.8, 2.2, 3.0, 1.25)
# ridge 2 (the middle): rock on the straight line, gaps at about (14.5,23.5) and (23.5,14.5)
ROCKS |= blob(1.6, 36.4, 2.6, 1.3) | blob(36.4, 1.6, 2.6, 1.3) | blob(5.5, 32.5, 4.4, 1.3) | blob(10.6, 27.4, 2.4, 1.7) | blob(19.0, 19.0, 3.3, 1.5) | blob(27.4, 10.6, 2.4, 1.7) | blob(32.5, 5.5, 4.4, 1.3)
# ridge 3 (nearest the core): gaps at about (21.5,32.5), (27,27), (32.5,21.5)
ROCKS |= blob(18.2, 35.8, 2.2, 1.2) | blob(24.4, 29.6, 2.2, 1.3) | blob(29.6, 24.4, 2.2, 1.3) | blob(35.6, 18.4, 2.4, 1.2)
# a few loose stones
ROCKS |= {(27, 3), (28, 3), (9, 34), (30, 33), (4, 10), (33, 12)}

WALL_LINES = [
    ((10, 12), (12, 10)),      # ridge 1, the gap on the straight line: one tile thick
    ((15, 7), (17, 5)),        # ridge 1, lower gap
    ((13, 25), (16, 22)),      # ridge 2, upper gap: two tiles thick
    ((13, 26), (17, 22)),
    ((26, 28), (28, 26)),      # ridge 3, the gap on the straight line: one tile thick
    ((31, 23), (34, 20)),      # ridge 3, lower gap
    ((9, 16), (9, 18)),        # a short baffle up from ridge 1
    ((18, 17), (18, 13)),      # a baffle down from the middle rock
    ((21, 21), (27, 21)),      # a baffle to the right of the middle rock
]
WALLS = []
for a, b in WALL_LINES:
    for t in line(a, b):
        if t not in ROCKS and t not in WALLS: WALLS.append(t)

# (x, y, damage type)
TOWERS = [(6, 19, 'physical'), (10, 21, 'frost'), (15, 16, 'fire'), (20, 14, 'physical'), (25, 15, 'fire'),
          (24, 18, 'frost'), (29, 22, 'physical'), (20, 30, 'fire'), (31, 30, 'physical')]
GHOST = (27, 17, 'physical')   # the tower being placed in the build picture; standing in the wave picture


def ascii_board():
    rows = []
    for y in range(N - 1, -1, -1):
        row = ''
        for x in range(N):
            t = (x, y)
            row += 'S' if t == SPAWN else 'C' if t == CORE else 'X' if t in ROCKS else '#' if t in WALLS else '.'
        rows.append(row)
    return '\n'.join(rows) + '\n'


def read_routes(path):
    # lines of routetool: "<Ground|Sapper> <spawn> <cost> x,y x,y! ..."  ('!' = through a built tile)
    routes = {}
    for ln in open(path, encoding='utf-8').read().split('\n'):
        p = ln.split()
        if len(p) < 4: continue
        tiles = []
        for s in p[3:]:
            dug = s.endswith('!')
            x, y = s.rstrip('!').split(',')
            tiles.append((int(x), int(y), dug))
        routes[p[0].lower()] = {'cost': float(p[2]), 'tiles': tiles}
    return routes


if __name__ == '__main__':
    here = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
    os.makedirs(here, exist_ok=True)
    assert SPAWN not in ROCKS and CORE not in ROCKS
    assert not [t for t in TOWERS if (t[0], t[1]) in ROCKS or (t[0], t[1]) in WALLS], 'tower on a rock or wall'
    open(os.path.join(here, 'board.txt'), 'w', encoding='utf-8', newline='\n').write(ascii_board())
    open(os.path.join(here, 'towers.txt'), 'w', encoding='utf-8', newline='\n').write(''.join('%d,%d\n' % (t[0], t[1]) for t in TOWERS))
    print('rocks', len(ROCKS), '(%.1f%% of the board)' % (100.0 * len(ROCKS) / (N * N)), 'walls', len(WALLS), 'towers', len(TOWERS))

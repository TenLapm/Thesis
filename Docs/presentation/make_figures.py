# Makes the charts and crops of the committee deck from real data. Run from the repository root:
#   python Docs/presentation/make_figures.py
import json
import math
import os

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle
import numpy as np
from PIL import Image

OUT = 'Docs/presentation/figures'
DATA = 'Docs/presentation/data'
os.makedirs(OUT, exist_ok=True)
plt.rcParams.update({'font.family': 'Arial', 'font.size': 20, 'axes.edgecolor': 'black', 'text.color': 'black',
                     'axes.labelcolor': 'black', 'xtick.color': 'black', 'ytick.color': 'black', 'axes.spines.top': False,
                     'axes.spines.right': False, 'savefig.facecolor': 'white', 'figure.facecolor': 'white'})


def save(fig, name):
    fig.savefig(os.path.join(OUT, name), dpi=100)
    plt.close(fig)


# --- crops of the Unity captures (session 20261031-142905, seed 27590234) ------------------
def crop(src, box, name):
    img = Image.open(src).convert('RGB')
    s = img.width / 1400.0
    img.crop(tuple(int(v * s) for v in box)).save(os.path.join(OUT, name))


S = 'Docs/presentation/captures/'
Image.open(S + 'deck_wave1.png').convert('RGB').save(os.path.join(OUT, 'game_wave.png'))
crop(S + 'deck_power_zoom.png', (300, 170, 860, 510), 'power.png')
crop(S + 'deck_field_zoom.png', (360, 110, 1060, 620), 'field.png')
Image.open('Docs/proposal/figures/f_raid_plan.png').convert('RGB').save(os.path.join(OUT, 'raid.png'))

# --- walk round or dig: the real FlowField on four small boards ----------------------------
cases = {c['name']: c for c in json.load(open(os.path.join(DATA, 'routes.json')))}
titles = [('open', 'Nothing built'), ('short', 'Short wall: walkers go round'),
          ('gap', 'Long wall with a gap: walkers round, sappers through'), ('sealed', 'Sealed wall: walkers must dig')]
fig, axes = plt.subplots(2, 2, figsize=(17.4, 8.4))
for ax, (key, title) in zip(axes.flat, titles):
    c = cases[key]
    w, h = c['w'], c['h']
    for x in range(w + 1):
        ax.plot([x, x], [0, h], color='0.85', lw=0.6)
    for y in range(h + 1):
        ax.plot([0, w], [y, y], color='0.85', lw=0.6)
    for x, y in c['walls']:
        ax.add_patch(Rectangle((x, y), 1, 1, color='black'))
    px = [p[0] + .5 for p in c['walker']]
    py = [p[1] + .5 for p in c['walker']]
    ax.plot(px, py, color='black', lw=3.2, label='walker: %.1f tiles' % (c['walkerCost'] / 10))
    if key != 'open':
        qx = [p[0] + .5 for p in c['sapper']]
        qy = [p[1] + .5 for p in c['sapper']]
        ax.plot(qx, qy, color='0.45', lw=3.2, ls=(0, (3, 2)), label='sapper: %.1f tiles' % (c['sapperCost'] / 10))
    ax.text(c['spawn'][0] + .5, c['spawn'][1] + .5, 'S', ha='center', va='center', fontsize=20, fontweight='bold',
            bbox=dict(boxstyle='circle,pad=0.2', fc='white', ec='black'))
    ax.text(c['core'][0] + .5, c['core'][1] + .5, 'C', ha='center', va='center', fontsize=20, fontweight='bold',
            bbox=dict(boxstyle='circle,pad=0.2', fc='white', ec='black'))
    ax.set_xlim(0, w)
    ax.set_ylim(0, h)
    ax.set_aspect('equal')
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_visible(False)
    ax.set_title(title, fontsize=22, loc='left')
    ax.legend(loc='upper center', bbox_to_anchor=(0.5, -0.02), ncol=2, fontsize=19, frameon=False)
fig.tight_layout()
save(fig, 'routes.png')

# --- rebuild time against board size, thesis bench ------------------------------------------
bench = json.load(open(os.path.join(DATA, 'bench.json')))
fig, ax = plt.subplots(figsize=(8.6, 6.3))
styles = {0: ('-', 'o', 'no walls'), 0.1: ('--', 's', '10% built'), 0.25: (':', '^', '25% built')}
for d, (ls, m, lab) in styles.items():
    rows = [r for r in bench['rebuildSweep'] if abs(r['density'] - d) < 1e-6]
    ax.plot([r['size'] for r in rows], [r['bothFieldsMeanMs'] for r in rows], ls=ls, marker=m, color='black', lw=2.4, ms=9, label=lab)
ax.set_xlabel('board side (tiles)')
ax.set_ylabel('ground + sapper rebuild (ms)')
ax.set_ylim(0, 4.6)
ax.legend(frameon=False)
fig.tight_layout()
save(fig, 'rebuild.png')

# --- what telemetry showed: the tester's 77 waves ------------------------------------------
rows = [json.loads(l) for l in open('Results/2026-10-28_tester-session/telemetry.jsonl', encoding='utf-8')]
by_wave = {}
for r in rows:
    if r['outcome']:
        by_wave[r['wave']] = r['outcome']
waves = sorted(by_wave)
fig, ax = plt.subplots(figsize=(17.4, 5.6))
ax.bar(waves, [by_wave[w]['leaked'] for w in waves], color='black', width=0.8)
ax.set_xlabel('wave')
ax.set_ylabel('enemies through to the core')
ax.set_xlim(0, 78)
ax2 = ax.twinx()
ax2.spines['right'].set_visible(True)
ax2.plot(waves, [by_wave[w]['budgetAfter'] for w in waves], color='0.45', lw=2.4, ls='--')
ax2.set_ylabel('ore held after the wave (dashed)')
fig.tight_layout()
save(fig, 'tester.png')

# --- primary metric on the same session ----------------------------------------------------
counts = [0] * 36
for r in rows:
    if r['wave'] < 1:
        continue
    for p in r['placements']:
        k = p['kind']
        if k in ('wall', 'tower', 'moveTower', 'deploy', 'upgrade', 'collector'):
            for x, y in (p['tiles'][1:] if k == 'moveTower' else p['tiles']):
                counts[(6 * x // 48) * 6 + (6 * y // 48)] += 1
total = sum(counts)
H = -sum(c / total * math.log(c / total) for c in counts if c) / math.log(36)
print('tiles', total, 'H', round(H, 3))
grid = np.array(counts).reshape(6, 6).T[::-1]   # columns x, rows y upward
fig, ax = plt.subplots(figsize=(7.4, 7.4))
ax.imshow(grid, cmap='Greys', vmin=0, vmax=max(counts) * 1.25)
for j in range(6):
    for i in range(6):
        v = grid[j, i]
        ax.text(i, j, str(v) if v else '', ha='center', va='center', fontsize=22, color='white' if v > max(counts) * .62 else 'black')
ax.set_xticks(np.arange(-.5, 6, 1), minor=True)
ax.set_yticks(np.arange(-.5, 6, 1), minor=True)
ax.grid(which='minor', color='black', lw=1.2)
ax.tick_params(which='both', length=0)
ax.set_xticks([])
ax.set_yticks([])
fig.tight_layout()
save(fig, 'entropy.png')

# --- why bins: a threshold against a straight line (illustration, not study data) -----------
x = np.linspace(0, 1, 400)
y = np.where(x < 0.55, 0.2, 0.8)
A = np.vstack([x, np.ones_like(x)]).T
m, c = np.linalg.lstsq(A, y, rcond=None)[0]
edges = np.linspace(0, 1, 6)
fig, ax = plt.subplots(figsize=(8.6, 6.3))
ax.plot(x, y, color='black', lw=3.2, label='true expected reward')
ax.plot(x, m * x + c, color='0.45', lw=2.8, ls='--', label='best straight line')
for a, b in zip(edges[:-1], edges[1:]):
    v = y[(x >= a) & (x < b)].mean() if b < 1 else y[x >= a].mean()
    ax.plot([a, b], [v, v], color='black', lw=7, alpha=.35, solid_capstyle='butt', label='5 bins' if a == 0 else None)
ax.set_xlabel('one feature of the build, e.g. maze length')
ax.set_ylabel('reward of "send sappers"')
ax.set_ylim(0, 1)
ax.legend(frameon=False, loc='upper left', fontsize=18)
fig.tight_layout()
save(fig, 'threshold.png')

# --- what a Beta posterior says ------------------------------------------------------------
def beta_pdf(t, a, b):
    return np.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)) * t ** (a - 1) * (1 - t) ** (b - 1)


t = np.linspace(0.001, 0.999, 500)
fig, ax = plt.subplots(figsize=(8.6, 6.3))
ax.plot(t, beta_pdf(t, 1, 1), color='0.45', lw=2.6, ls=':', label='Beta(1, 1): no data')
ax.plot(t, beta_pdf(t, 3, 2), color='black', lw=2.6, ls='--', label='Beta(3, 2): 2 of 3 worked')
ax.plot(t, beta_pdf(t, 15, 7), color='black', lw=3.2, label='Beta(15, 7): 14 of 20 worked')
ax.set_xlabel('share of waves that made the player rebuild')
ax.set_ylabel('belief')
ax.set_yticks([])
ax.legend(frameon=False, loc='upper left', fontsize=18)
fig.tight_layout()
save(fig, 'beta.png')
print('done')

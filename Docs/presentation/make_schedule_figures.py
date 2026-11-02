# The schedule chart and the reward timeline of the committee deck. Run from the repository root:
#   python Docs/presentation/make_schedule_figures.py
# Weeks come from CLAUDE.md section 9 and the status slide; S1 W5 is assumed to begin on 14 September 2026.
import os

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import FancyArrowPatch, Rectangle

OUT = 'Docs/presentation/figures'
plt.rcParams.update({'font.family': 'Arial', 'font.size': 20, 'text.color': 'black', 'savefig.facecolor': 'white'})

# Columns: S1 W12-W16, the break (4 weeks), S2 W1-W15.
COLS = ['12', '13', '14', '15', '16', 'B1', 'B2', 'B3', 'B4'] + [str(i) for i in range(1, 16)]
ROWS = [
    ('Game: only what the director needs', '12', '13'),
    ('Learner (WP7)', '12', '12'),
    ('Build profile, strategies, cost table (WP8, WP9)', '12', '13'),
    ('Layers 1 and 2, announcements (WP10, WP14)', '13', '13'),
    ('Reward, full director (WP11)', '14', '14'),
    ('Offline ladder, gates G1 to G3 (WP12, WP13)', '14', '15'),
    ('Semester report', '16', '16'),
    ('Pilot n = 5, threshold freeze', 'B1', 'B2'),
    ('UX pass', 's1', 's3'),
    ('Balance pass, explore the director for balance', 's1', 's4'),
    ('Second pilot, build freeze', 's5', 's6'),
    ('Main study, n = 24', 's7', 's11'),
    ('Analysis, writing', 's12', 's15'),
]


def col(name):
    return 9 + int(name[1:]) - 1 if name.startswith('s') else COLS.index(name)


fig, ax = plt.subplots(figsize=(17.44, 6.9))
n = len(ROWS)
for x in range(len(COLS) + 1):
    ax.plot([x, x], [0, n], color='0.85', lw=0.7, zorder=0)
for edge, label in ((0, None), (5, None), (9, None)):
    ax.plot([edge, edge], [0, n + 1.3], color='black', lw=1.4, zorder=1)
ax.plot([24, 24], [0, n + 1.3], color='black', lw=1.4, zorder=1)
for y, (name, a, b) in enumerate(ROWS):
    top = n - y - 1
    ax.add_patch(Rectangle((col(a), top + 0.18), col(b) - col(a) + 1, 0.64, color='0.25', zorder=2))
    ax.text(-0.2, top + 0.5, name, ha='right', va='center', fontsize=20)
for x, label in enumerate(COLS):
    ax.text(x + 0.5, n + 0.2, label, ha='center', va='bottom', fontsize=15)
ax.text(2.5, n + 0.62, 'Semester 1', ha='center', va='bottom', fontsize=20, fontweight='bold')
ax.text(7, n + 0.62, 'Break', ha='center', va='bottom', fontsize=20, fontweight='bold')
ax.text(16.5, n + 0.62, 'Semester 2', ha='center', va='bottom', fontsize=20, fontweight='bold')
ax.set_xlim(0, 24)
ax.set_ylim(0, n + 1.35)
ax.axis('off')
fig.subplots_adjust(left=0.385, right=0.995, top=0.995, bottom=0.005)
fig.savefig(os.path.join(OUT, 'd_schedule.png'), dpi=150)
plt.close(fig)

# When each part of the reward is known, with the next wave announced ahead (SimConfig.PlanAhead).
steps = ['Wave N\nplays', 'Wave N+1\nplanned and shown', 'Player\nrebuilds', 'Wave N+1\nplays', 'Wave N+2 planned:\nthe rebuild is now seen,\nwave N can be graded']
fig, ax = plt.subplots(figsize=(17.44, 1.45))
w = 2.75
gap = 0.45
for i, label in enumerate(steps):
    x = i * (w + gap)
    ax.add_patch(Rectangle((x, 0.02), w, 0.96, fill=False, ec='black', lw=1.4))
    ax.text(x + w / 2, 0.5, label, ha='center', va='center', fontsize=19)
    if i < len(steps) - 1:
        ax.add_patch(FancyArrowPatch((x + w, 0.5), (x + w + gap, 0.5), arrowstyle='-|>', mutation_scale=18, lw=1.4, color='black'))
ax.set_xlim(-0.02, len(steps) * (w + gap) - gap + 0.02)
ax.set_ylim(0, 1)
ax.axis('off')
fig.subplots_adjust(left=0.002, right=0.998, top=0.99, bottom=0.01)
fig.savefig(os.path.join(OUT, 'd_reward_timeline.png'), dpi=150)
plt.close(fig)

# The director's steps in plain words. Numbers follow the arrows; row 2 runs right to left, so no arrows cross.
BOXES = [
    (0, 2, "1. Read\nthe board and this\nwave's budget", False),
    (1, 2, '2. Describe the build\n5 numbers, then one\nof 5 groups', False),
    (2, 2, '3. Layer 1: filter\nremove strategies that\nare not allowed now', False),
    (2, 1, '4. Layer 2: rules\nscore each strategy\nwith written rules', False),
    (1, 1, '5. Layer 3: experience\nnudge each score by\nwhat worked before', False),
    (0, 1, '6. Pick\nthe highest\ntotal score', False),
    (0, 0, '7. Build, check\nand announce\nthe wave', False),
    (1, 0, 'The player plays,\nthen rebuilds', True),
    (2, 0, '8. Grade and remember\nmainly: how much did\nthe build change?', False),
]
fig, ax = plt.subplots(figsize=(17.44, 6.5))
bw, bh, gx, gy = 4.3, 1.5, 1.3, 0.72


def corner(c, r):
    return c * (bw + gx), r * (bh + gy)


for c, r, label, shaded in BOXES:
    x, y = corner(c, r)
    ax.add_patch(Rectangle((x, y), bw, bh, facecolor='0.88' if shaded else 'white', edgecolor='black', lw=1.6))
    ax.text(x + bw / 2, y + bh / 2, label, ha='center', va='center', fontsize=21, linespacing=1.25)


def arrow(a, b, **kw):
    ax.add_patch(FancyArrowPatch(a, b, arrowstyle='-|>', mutation_scale=22, lw=1.6, color='black', shrinkA=0, shrinkB=0, **kw))


for i in range(len(BOXES) - 1):
    c1, r1, _, _ = BOXES[i]
    c2, r2, _, _ = BOXES[i + 1]
    x1, y1 = corner(c1, r1)
    x2, y2 = corner(c2, r2)
    if r1 == r2 and c2 > c1:
        arrow((x1 + bw, y1 + bh / 2), (x2, y2 + bh / 2))
    elif r1 == r2:
        arrow((x1, y1 + bh / 2), (x2 + bw, y2 + bh / 2))
    else:
        arrow((x1 + bw / 2, y1), (x2 + bw / 2, y2 + bh))
# The grade goes back to step 5's record.
gx0, gy0 = corner(2, 0)
tx, ty = corner(1, 1)
mid = gy0 + bh + gy / 2
ax.plot([gx0 + bw * 0.25, gx0 + bw * 0.25, tx + bw * 0.75], [gy0 + bh, mid, mid], color='black', lw=1.6, ls=(0, (4, 3)))
arrow((tx + bw * 0.75, mid), (tx + bw * 0.75, ty), linestyle=(0, (4, 3)))
ax.text((gx0 + tx + bw) / 2 + 0.25, mid + 0.06, 'updates the record', ha='center', va='bottom', fontsize=18)
ax.set_xlim(-0.03, 3 * bw + 2 * gx + 0.03)
ax.set_ylim(-0.03, 3 * bh + 2 * gy + 0.03)
ax.axis('off')
fig.subplots_adjust(left=0.002, right=0.998, top=0.998, bottom=0.002)
fig.savefig(os.path.join(OUT, 'd_director_plain.png'), dpi=150)
plt.close(fig)

# Plain-language versions of the charts on the learning and measuring slides.
from math import gamma
import numpy as np


def beta_pdf(x, a, b):
    return gamma(a + b) / (gamma(a) * gamma(b)) * x ** (a - 1) * (1 - x) ** (b - 1)


x = np.linspace(0.001, 0.999, 400)
fig, ax = plt.subplots(figsize=(8.2, 6.0))
ax.plot(x, [beta_pdf(v, 1, 1) for v in x], color='0.45', ls=':', lw=3.2, label='No results yet')
ax.plot(x, [beta_pdf(v, 3, 2) for v in x], color='black', ls='--', lw=3.2, label='2 of 3 worked')
ax.plot(x, [beta_pdf(v, 15, 7) for v in x], color='black', lw=3.8, label='14 of 20 worked')
ax.set_xlabel('How often this strategy makes the player rebuild', fontsize=20)
ax.set_ylabel('How much the director believes it', fontsize=20)
ax.set_xticks([0, 0.25, 0.5, 0.75, 1])
ax.set_xticklabels(['0%', '25%', '50%', '75%', '100%'], fontsize=18)
ax.set_yticks([])
ax.spines['top'].set_visible(False)
ax.spines['right'].set_visible(False)
ax.legend(frameon=False, fontsize=20, loc='upper left')
fig.tight_layout()
fig.savefig(os.path.join(OUT, 'plain_belief.png'), dpi=150)
plt.close(fig)

# Three small boards: how spread out building is, and the score each one gets.
examples = [('All in one place', [8, 0, 0, 0], '0'), ('Mostly in one place', [6, 2, 0, 0], '0.41'), ('Spread evenly', [2, 2, 2, 2], '1')]
fig, axes = plt.subplots(1, 3, figsize=(10.8, 4.0))
for ax, (title, counts, score) in zip(axes, examples):
    for i, c in enumerate(counts):
        px, py = i % 2, 1 - i // 2
        shade = 1 - c / 8 * 0.85
        ax.add_patch(Rectangle((px, py), 1, 1, facecolor=str(shade), edgecolor='black', lw=1.6))
        ax.text(px + 0.5, py + 0.5, str(c), ha='center', va='center', fontsize=26, color='white' if shade < 0.5 else 'black')
    ax.set_xlim(-0.03, 2.03)
    ax.set_ylim(-0.03, 2.03)
    ax.set_aspect('equal')
    ax.axis('off')
    ax.set_title(title, fontsize=21, pad=8)
    ax.text(1, -0.2, 'Score: ' + score, ha='center', va='top', fontsize=24, fontweight='bold')
fig.subplots_adjust(left=0.01, right=0.99, top=0.88, bottom=0.14, wspace=0.12)
fig.savefig(os.path.join(OUT, 'plain_spread.png'), dpi=150)
plt.close(fig)

# -*- coding: utf-8 -*-
"""Builds PerformanceReport.pdf from the benchmark JSON files + screenshots."""
import json, os, datetime
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FixedLocator

ROOT = r"D:\Ten\InternProj"
SHOTS = os.path.join(ROOT, "Assets", "Screenshots")
OUT = os.path.dirname(os.path.abspath(__file__))
CHARTS = os.path.join(OUT, "charts")
os.makedirs(CHARTS, exist_ok=True)

# ---------- palette (validated defaults from the dataviz system) ----------
BLUE, ORANGE, AQUA, YELLOW, MAGENTA = "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4"
SEQ = ["#86b6ef", "#2a78d6", "#104281"]            # sequential blue ramp (light->dark)
INK, SEC, MUTED, SURFACE, GRID = "#0b0b0b", "#52514e", "#898781", "#fcfcfb", "#e5e4e0"

def load(name):
    with open(os.path.join(ROOT, name), encoding="utf-8") as f:
        return json.load(f)

game = load("BenchmarkResults.json")
scen = {lbl: load("Benchmark_%s.json" % lbl) for lbl in ["Open", "Maze", "Choke", "Stress100"]}
env = game["environment"]

def style_ax(ax):
    ax.set_facecolor(SURFACE)
    for s in ["top", "right"]:
        ax.spines[s].set_visible(False)
    for s in ["left", "bottom"]:
        ax.spines[s].set_color(MUTED)
    ax.tick_params(colors=SEC, labelsize=9)
    ax.grid(True, axis="y", color=GRID, linewidth=0.7)
    ax.set_axisbelow(True)

def newfig(w=7.0, h=3.6):
    fig, ax = plt.subplots(figsize=(w, h), dpi=200)
    fig.patch.set_facecolor(SURFACE)
    style_ax(ax)
    return fig, ax

def save(fig, name):
    p = os.path.join(CHARTS, name)
    fig.tight_layout()
    fig.savefig(p, facecolor=SURFACE, bbox_inches="tight")
    plt.close(fig)
    return p

# ---------- chart 1: rebuild sweep ----------
fig, ax = newfig()
dens = [0.0, 0.10, 0.25]
sizes = sorted({r["size"] for r in game["rebuildSweep"]})
nodes = [s * s for s in sizes]
for d, c in zip(dens, SEQ):
    ys = [next(r["meanMs"] for r in game["rebuildSweep"] if r["size"] == s and abs(r["density"] - d) < 1e-6) for s in sizes]
    ax.plot(nodes, ys, color=c, linewidth=2, marker="o", markersize=6, label="%d%% walls" % round(d * 100))
ax.axhline(16.7, color=MUTED, linewidth=1, linestyle=(0, (4, 3)))
ax.text(nodes[0], 17.3, "60 fps frame budget (16.7 ms)", ha="left", va="bottom", fontsize=8, color=MUTED)
ax.xaxis.set_major_locator(FixedLocator(nodes))
ax.set_xticklabels(["%d\n(%d×%d)" % (s * s, s, s) for s in sizes])
ax.set_xlabel("grid nodes", color=SEC, fontsize=9)
ax.set_ylabel("full rebuild, mean ms", color=SEC, fontsize=9)
ax.set_title("Flow-field rebuild time vs grid size and wall density", color=INK, fontsize=11, loc="left", pad=10)
leg = ax.legend(frameon=False, fontsize=9, loc="upper left")
for t in leg.get_texts():
    t.set_color(SEC)
c1 = save(fig, "rebuild_sweep.png")

# ---------- chart 2: flow field vs per-agent A* ----------
fig, ax = newfig()
runs = game["astarComparison"]["runs"]
fieldMs = game["astarComparison"]["fieldRebuildMs"]
xs = range(len(runs))
w = 0.36
b1 = ax.bar([x - w / 2 for x in xs], [fieldMs] * len(runs), width=w, color=BLUE, label="Shared flow field (one rebuild serves all)", zorder=3)
b2 = ax.bar([x + w / 2 for x in xs], [r["totalMs"] for r in runs], width=w, color=ORANGE, label="Per-agent A* (one search each)", zorder=3)
ax.set_yscale("log")
ax.set_ylim(1, 400)
for x, r in zip(xs, runs):
    ax.text(x + w / 2, r["totalMs"] * 1.12, "%.0f ms" % r["totalMs"], ha="center", fontsize=8.5, color=INK)
    ax.text(x - w / 2, fieldMs * 1.12, "%.1f" % fieldMs, ha="center", fontsize=8.5, color=INK)
    ax.text(x, max(r["totalMs"], fieldMs) * 1.75, "×%.0f" % (r["totalMs"] / fieldMs), ha="center", fontsize=9, color=SEC, style="italic")
ax.set_xticks(list(xs))
ax.set_xticklabels(["%d agents" % r["agents"] for r in runs])
ax.set_ylabel("total repath cost, ms (log)", color=SEC, fontsize=9)
ax.set_title("Cost of repathing the whole crowd: shared field vs per-agent A*  (38×38, 10% walls)", color=INK, fontsize=11, loc="left", pad=10)
leg = ax.legend(frameon=False, fontsize=9, loc="upper left")
for t in leg.get_texts():
    t.set_color(SEC)
c2 = save(fig, "astar_comparison.png")

# ---------- chart 3: fps vs agents across scenes ----------
fig, ax = newfig(7.0, 4.0)
series = [
    ("Game map (38×38)", game["agentTiers"], BLUE),
    ("Open (38×38)", scen["Open"]["tiers"], ORANGE),
    ("Maze (38×38)", scen["Maze"]["tiers"], AQUA),
    ("Choke (38×38)", scen["Choke"]["tiers"], YELLOW),
    ("Stress (100×100)", scen["Stress100"]["tiers"], MAGENTA),
]
for name, tiers, c in series:
    ax.plot([t["agents"] for t in tiers], [t["fps"] for t in tiers], color=c, linewidth=2, marker="o", markersize=6, label=name)
for fps, lab in [(60, "60 fps"), (30, "30 fps")]:
    ax.axhline(fps, color=MUTED, linewidth=1, linestyle=(0, (4, 3)))
    ax.text(1010, fps + 2, lab, fontsize=8, color=MUTED, ha="right")
ax.set_xticks([100, 250, 500, 1000])
ax.set_xlabel("live agents", color=SEC, fontsize=9)
ax.set_ylabel("average fps", color=SEC, fontsize=9)
ax.set_title("Frame rate vs live agent count, per scene", color=INK, fontsize=11, loc="left", pad=10)
leg = ax.legend(frameon=False, fontsize=9)
for t in leg.get_texts():
    t.set_color(SEC)
c3 = save(fig, "fps_tiers.png")

# ---------- chart 4: dynamic edits ----------
fig, ax = newfig(6.4, 3.4)
steady = next(t for t in game["agentTiers"] if t["agents"] == 500)
edits = game["dynamicEdits"]
metrics = ["avgMs", "p95Ms", "worstMs"]
labels = ["average", "p95", "worst"]
xs = range(len(metrics))
w = 0.36
ax.bar([x - w / 2 for x in xs], [steady[m] for m in metrics], width=w, color=BLUE, label="500 agents, static map", zorder=3)
ax.bar([x + w / 2 for x in xs], [edits[m] for m in metrics], width=w, color=ORANGE, label="500 agents + edits every 0.5 s", zorder=3)
for x, m in zip(xs, metrics):
    ax.text(x - w / 2, steady[m] + 1.2, "%.0f" % steady[m], ha="center", fontsize=8.5, color=INK)
    ax.text(x + w / 2, edits[m] + 1.2, "%.0f" % edits[m], ha="center", fontsize=8.5, color=INK)
ax.set_xticks(list(xs))
ax.set_xticklabels(labels)
ax.set_ylabel("frame time, ms", color=SEC, fontsize=9)
ax.set_title("Frame stability while the map is edited under load (game map)", color=INK, fontsize=11, loc="left", pad=10)
leg = ax.legend(frameon=False, fontsize=9, loc="upper left")
for t in leg.get_texts():
    t.set_color(SEC)
c4 = save(fig, "dynamic_edits.png")

# =========================== PDF ===========================
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import cm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from reportlab.platypus import (BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer,
                                Image, Table, TableStyle, PageBreak, KeepTogether)

PDF = os.path.join(ROOT, "PerformanceReport.pdf")
PAGE_W, PAGE_H = A4
M = 2.0 * cm

styles = {
    "title": ParagraphStyle("title", fontName="Helvetica-Bold", fontSize=21, leading=26, textColor=INK, spaceAfter=4),
    "subtitle": ParagraphStyle("subtitle", fontName="Helvetica", fontSize=11, leading=15, textColor=SEC, spaceAfter=14),
    "h1": ParagraphStyle("h1", fontName="Helvetica-Bold", fontSize=14, leading=18, textColor=INK, spaceBefore=16, spaceAfter=6),
    "h2": ParagraphStyle("h2", fontName="Helvetica-Bold", fontSize=11.5, leading=15, textColor=INK, spaceBefore=10, spaceAfter=4),
    "body": ParagraphStyle("body", fontName="Helvetica", fontSize=9.8, leading=14, textColor=INK, spaceAfter=6),
    "bullet": ParagraphStyle("bullet", fontName="Helvetica", fontSize=9.8, leading=14, textColor=INK, leftIndent=14, bulletIndent=4, spaceAfter=3),
    "caption": ParagraphStyle("caption", fontName="Helvetica-Oblique", fontSize=8.5, leading=11, textColor=SEC, spaceBefore=2, spaceAfter=10),
    "cell": ParagraphStyle("cell", fontName="Helvetica", fontSize=9, leading=12, textColor=INK),
}

def footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(colors.HexColor(MUTED))
    canvas.drawString(M, 1.1 * cm, "InternProj — Flow-Field Pathfinding Performance Evaluation")
    canvas.drawRightString(PAGE_W - M, 1.1 * cm, "Page %d" % doc.page)
    canvas.restoreState()

doc = BaseDocTemplate(PDF, pagesize=A4, leftMargin=M, rightMargin=M, topMargin=1.8 * cm, bottomMargin=1.8 * cm,
                      title="Flow-Field Pathfinding — Performance Evaluation",
                      author="InternProj automated benchmark harness")
frame = Frame(M, 1.8 * cm, PAGE_W - 2 * M, PAGE_H - 3.6 * cm, id="main")
doc.addPageTemplates([PageTemplate(id="page", frames=[frame], onPage=footer)])

def img(path, target_w):
    ir = ImageReader(path)
    iw, ih = ir.getSize()
    return Image(path, width=target_w, height=target_w * ih / iw)

def chart(path):
    return img(path, PAGE_W - 2 * M)

def bullets(items):
    return [Paragraph(t, styles["bullet"], bulletText="•") for t in items]

TBL_STYLE = TableStyle([
    ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
    ("FONTSIZE", (0, 0), (-1, -1), 8.6),
    ("TEXTCOLOR", (0, 0), (-1, 0), colors.HexColor(INK)),
    ("TEXTCOLOR", (0, 1), (-1, -1), colors.HexColor(SEC)),
    ("LINEBELOW", (0, 0), (-1, 0), 0.7, colors.HexColor(MUTED)),
    ("LINEBELOW", (0, 1), (-1, -2), 0.3, colors.HexColor(GRID)),
    ("TOPPADDING", (0, 0), (-1, -1), 3.5),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 3.5),
    ("LEFTPADDING", (0, 0), (-1, -1), 2),
    ("RIGHTPADDING", (0, 0), (-1, -1), 6),
])

story = []
today = datetime.date(2026, 7, 25).strftime("%B %d, %Y")

# ---------- title ----------
story.append(Paragraph("Flow-Field Pathfinding", styles["title"]))
story.append(Paragraph("Performance Evaluation Report", styles["title"]))
story.append(Paragraph("InternProj tower-defense prototype · automated benchmark harness · %s" % today, styles["subtitle"]))

envrows = [["Engine", "Unity %s (URP), editor play mode, vSync off, uncapped" % env["unity"]],
           ["CPU", "%s (%d threads)" % (env["cpu"], env["cores"])],
           ["GPU", env["gpu"]],
           ["Memory", "%.0f GB" % (env["ramMB"] / 1024.0)],
           ["OS", env["os"]]]
t = Table([[Paragraph("<b>%s</b>" % a, styles["cell"]), Paragraph(b, styles["cell"])] for a, b in envrows],
          colWidths=[3.0 * cm, PAGE_W - 2 * M - 3.0 * cm])
t.setStyle(TableStyle([("TOPPADDING", (0, 0), (-1, -1), 2), ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
                       ("LEFTPADDING", (0, 0), (-1, -1), 0)]))
story.append(t)

story.append(Paragraph("1. Executive summary", styles["h1"]))
story += bullets([
    "A full flow-field rebuild on the live game map (38×38, 1,444 nodes) costs <b>~2.6 ms</b> — cheap enough to run "
    "synchronously on every wall placement and breach, which is exactly what the game does.",
    "One shared field replaces a pathfinding query per enemy: repathing the whole crowd is <b>4× cheaper than per-agent A* "
    "at 100 agents and 55× cheaper at 1,000</b>, and the field's cost does not grow with crowd size at all.",
    "Frame rate is <b>agent-bound, not pathfinding-bound</b>: every scene (open, maze, choke points, 100×100 stress) holds "
    "~57–73 fps at 500 live agents and ~22–27 fps at 1,000; scene layout and even a 7× larger grid barely move the curve.",
    "Rebuild cost scales with node count: ~20 ms at 10,000 nodes (40 ms at 25% wall density). Below ~5,000 nodes synchronous "
    "rebuilds are free; beyond that they should be amortized or moved off the hot path.",
    "Continuous map editing under 500-agent load raises average frame time from 17 ms to 44 ms (worst 85 ms). Coalescing "
    "breach-triggered rebuilds to at most one per frame is the highest-value optimization found.",
    "Real gameplay peaks near ~100 simultaneous enemies, where all scenes sustain <b>150+ fps</b> — a large safety margin.",
])

story.append(Paragraph("2. System under test", styles["h1"]))
story.append(Paragraph(
    "Enemies navigate with a <b>flow field</b>: a single weighted flood fill (Dijkstra-style SPFA relaxation) runs from the "
    "player core outward over the grid whenever the map changes, storing each tile's cost-to-goal and a pointer to its best "
    "neighbor. Cardinal and diagonal steps cost 10/14 (≈1:√2), scaled by per-tile terrain cost. Player walls are not "
    "obstacles but <i>expensive terrain</i> (dig cost ≈ a 15-tile detour) with health, so a path always exists: enemies "
    "route around walls when the detour is cheap and chew through them when it is not. Every agent then follows the field "
    "with an O(1) lookup per frame — pathfinding cost is paid once per map change, not per agent.", styles["body"]))
story.append(Paragraph(
    "New for this evaluation, a <b>runtime vector-field visualization</b> (FlowFieldVisualizer) draws the field directly on "
    "the ground as procedural geometry — per-tile direction arrows plus a cost heatmap (green = cheap → red = expensive, "
    "orange = walls, dark = unwalkable). It works in play mode and standalone builds with no editor, toggled with "
    "<b>V</b> (Off → Arrows → Heatmap → Both). Geometry rebuilds only when the field version changes, so its steady-state "
    "cost is one integer comparison per frame.", styles["body"]))
story.append(KeepTogether([
    img(os.path.join(SHOTS, "game_scene_vectorfield.png"), PAGE_W - 2 * M - 2 * cm),
    Paragraph("The game scene with the new vector-field view enabled (heatmap + arrows) — running live with the full HUD.", styles["caption"]),
]))

story.append(Paragraph("3. Method", styles["h1"]))
story.append(Paragraph(
    "Five scenes were evaluated. Four purpose-built benchmark scenes (Bench_Open, Bench_Maze, Bench_Choke, Bench_Stress) "
    "each construct a deterministic wall layout at startup, then an automated harness (ScenarioBenchmark) measures: "
    "(a) full field rebuild time over 50 iterations, (b) the spawn-to-goal path, and (c) frame time while 100 / 250 / 500 / "
    "1,000 pooled agents follow the field — 1 s warm-up then 4 s sampling per tier, with agents respawned on arrival so the "
    "live count stays constant. The fifth run used the existing BenchmarkRunner on the real game scene for the grid-size "
    "sweep, the A* comparison and the dynamic-edit test. All numbers are editor play mode with vSync off and an uncapped "
    "frame rate; standalone builds are typically faster, so results are conservative.", styles["body"]))

srows = [["Scene", "Layout", "Grid", "Walls", "Spawn→goal path", "Rebuild (mean)"]]
srows.append(["SampleScene (game)", "empty game map", "38×38", "0", "35 tiles", "%.1f ms" % next(r["meanMs"] for r in game["rebuildSweep"] if r["size"] == 38 and r["density"] == 0)])
for lbl, layout in [("Open", "no obstacles"), ("Maze", "serpentine corridors"), ("Choke", "3 walls, staggered gaps"), ("Stress100", "10% random scatter")]:
    d = scen[lbl]
    srows.append(["Bench_%s" % (lbl if lbl != "Stress100" else "Stress"), layout,
                  "%d×%d" % (d["grid"]["x"], d["grid"]["y"]), str(d["grid"]["walls"]),
                  "%d tiles" % d["path"]["tiles"], "%.1f ms" % d["rebuild"]["meanMs"]])
t = Table(srows, colWidths=[3.4 * cm, 4.1 * cm, 2.0 * cm, 1.6 * cm, 3.0 * cm, 2.6 * cm])
t.setStyle(TBL_STYLE)
story.append(t)
story.append(Paragraph("Table 1 — evaluated scenes. Scenario walls use dig cost 200 and effectively infinite health so layouts stay static during measurement.", styles["caption"]))

half = (PAGE_W - 2 * M - 0.4 * cm) / 2
grid_imgs = Table([
    [img(os.path.join(SHOTS, "bench_open_field-1.png"), half), img(os.path.join(SHOTS, "bench_maze.png"), half)],
    [Paragraph("Bench_Open — 450 agents marching on an empty field.", styles["caption"]),
     Paragraph("Bench_Maze — serpentine corridors; note the cost bands per leg.", styles["caption"])],
    [img(os.path.join(SHOTS, "bench_choke.png"), half), img(os.path.join(SHOTS, "bench_stress.png"), half)],
    [Paragraph("Bench_Choke — staggered choke points swing the crowd across the map.", styles["caption"]),
     Paragraph("Bench_Stress — 100×100 nodes, 1,000 scatter walls.", styles["caption"])],
], colWidths=[half + 0.2 * cm, half + 0.2 * cm])
grid_imgs.setStyle(TableStyle([("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                               ("TOPPADDING", (0, 0), (-1, -1), 2), ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
                               ("VALIGN", (0, 0), (-1, -1), "TOP")]))
story.append(KeepTogether([grid_imgs,
    Paragraph("Figure 1 — the four benchmark scenes with the vector-field view enabled.", styles["caption"])]))

story.append(PageBreak())

story.append(Paragraph("4. Results", styles["h1"]))

story.append(Paragraph("4.1 Field rebuild scaling", styles["h2"]))
story.append(chart(c1))
story.append(Paragraph(
    "Rebuild time grows roughly linearly with node count and rises with wall density (the SPFA relaxation re-queues nodes "
    "more often when expensive tiles distort the cost surface). The game's 38×38 grid rebuilds in 2.6–4.2 ms across "
    "densities — well inside a 60 fps frame. A 100×100 grid costs ~20 ms (40 ms at 25% walls): still fine for occasional "
    "edits, but too much to spend synchronously every frame.", styles["body"]))

story.append(Paragraph("4.2 One shared field vs per-agent A*", styles["h2"]))
story.append(chart(c2))
story.append(Paragraph(
    "Repathing the full population after a map change costs one field rebuild (%.1f ms) regardless of crowd size, while "
    "per-agent A* (binary heap, identical 10/14 cost model, same grid) scales linearly with agent count — %.0f ms for "
    "1,000 agents, a ×%.0f gap. A* would additionally pay per-agent storage and path-repair complexity; the shared field is "
    "the structurally right choice for this game's one-destination crowds." % (fieldMs, runs[-1]["totalMs"], runs[-1]["totalMs"] / fieldMs),
    styles["body"]))

story.append(Paragraph("4.3 Frame rate vs crowd size across scenes", styles["h2"]))
story.append(chart(c3))

trows = [["Agents", "Game map", "Open", "Maze", "Choke", "Stress 100×100"]]
for i, n in enumerate([100, 250, 500, 1000]):
    row = ["%d" % n]
    for name, tiers, _ in series:
        t_ = tiers[i]
        row.append("%.0f fps (p95 %.0f ms)" % (t_["fps"], t_["p95Ms"]))
    trows.append(row)
t = Table(trows, colWidths=[1.7 * cm] + [3.0 * cm] * 5)
t.setStyle(TBL_STYLE)
story.append(t)
story.append(Paragraph("Table 2 — average fps and 95th-percentile frame time per tier.", styles["caption"]))
story.append(Paragraph(
    "The five curves nearly coincide: neither layout complexity nor a 6.9× increase in node count (1,444 → 10,000) "
    "meaningfully changes per-frame cost, confirming that agents' O(1) field lookups decouple crowd size from pathfinding "
    "load. The marginal cost is ~37 µs per agent per frame (≈27 fps-capacity per 1,000 agents), dominated by per-agent "
    "MonoBehaviour updates and world-space health bars — not by the field. 500 agents stay at or above ~57 fps everywhere; "
    "the game's real waves (~100 concurrent enemies at wave 18) run at 150+ fps.", styles["body"]))

story.append(PageBreak())

story.append(Paragraph("4.4 Stability while the map is edited under load", styles["h2"]))
story.append(chart(c4))
story.append(Paragraph(
    "With 500 live agents, toggling a 2×2 wall block every 0.5 s (16 edits, field rebuilt after each) roughly doubles the "
    "average frame time and produces an 85 ms worst frame. The scheduled rebuilds themselves account for only ~2.6 ms every "
    "0.5 s, so the overhead comes from the surrounding churn — most notably wall breaches: every agent-completed dig "
    "triggers its own immediate full rebuild, so several breaches in one frame pay the rebuild price several times. "
    "The frame-time floor stays above 20 fps throughout, and at realistic gameplay scale (≤150 agents, occasional edits) "
    "the effect is not perceptible.", styles["body"]))

story.append(Paragraph("5. Recommendations", styles["h1"]))
story += bullets([
    "<b>Coalesce rebuilds:</b> replace direct GenerateFlowField() calls with a dirty flag consumed once per frame (LateUpdate). "
    "This caps rebuild cost at one per frame no matter how many walls are placed or breached simultaneously — the cheapest fix "
    "for the 4.4 worst-frames.",
    "<b>Keep grids ≤ ~5,000 nodes for synchronous edits.</b> At 10,000+ nodes (20–40 ms rebuilds) move the fill to a background "
    "thread or split it over frames; agents can safely follow the stale field for a few frames.",
    "<b>Crowd scaling beyond 500 agents</b> is an agent-update problem, not a pathfinding problem: batching movement into Jobs/"
    "Burst or replacing per-agent world-space canvases with an instanced bar renderer would raise the ~37 µs/agent/frame floor.",
    "<b>Numbers are conservative:</b> all measurements are editor play mode; standalone builds remove editor overhead.",
])

story.append(Paragraph("Appendix A — presentation kit", styles["h1"]))
story += bullets([
    "<b>Scenes</b> (all in Build Settings): SampleScene (game), Bench_Open, Bench_Maze, Bench_Choke, Bench_Stress. "
    "Each Bench_* scene auto-runs its benchmark on play and keeps the crowd marching afterwards; press <b>B</b> to re-run.",
    "<b>V</b> — cycle vector-field view: Off → Arrows → Heatmap → Arrows+Heatmap (works in builds, no editor needed).",
    "<b>Camera:</b> WASD/arrows pan, scroll zooms, hold RMB for free-look (Q/E roll), C resets orientation.",
    "<b>Game controls:</b> Space pauses, 1/2/3 set game speed.",
    "<b>Raw data:</b> BenchmarkResults.json, Benchmark_Open/Maze/Choke/Stress100.json (project root); screenshots in "
    "Assets/Screenshots.",
])

doc.build(story)
print("PDF written:", PDF)

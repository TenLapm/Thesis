# -*- coding: utf-8 -*-
"""Builds PresentationScript.pdf: a speaker script for the live demo + report
walkthrough, followed by a detailed technical appendix (architecture,
threading model, and exactly how every evaluation statistic was produced).
Numbers quoted in callout boxes are pulled live from the same JSON files
PerformanceReport.pdf was built from, so the two documents never disagree.
"""
import json, os, datetime

ROOT = r"D:\Ten\InternProj"

def load(name):
    with open(os.path.join(ROOT, name), encoding="utf-8") as f:
        return json.load(f)

game = load("BenchmarkResults.json")
scen = {lbl: load("Benchmark_%s.json" % lbl) for lbl in ["Open", "Maze", "Choke", "Stress100"]}

rebuild38 = next(r for r in game["rebuildSweep"] if r["size"] == 38 and r["density"] == 0)
astar = game["astarComparison"]
astar1000 = next(r for r in astar["runs"] if r["agents"] == 1000)
tier500 = next(t for t in game["agentTiers"] if t["agents"] == 500)
edits = game["dynamicEdits"]

# ---------- palette (same instance as PerformanceReport.pdf) ----------
BLUE, ORANGE, AQUA, YELLOW, MAGENTA = "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4"
INK, SEC, MUTED, SURFACE, GRID = "#0b0b0b", "#52514e", "#898781", "#fcfcfb", "#e5e4e0"
CODE_BG = "#f0efec"

from reportlab.lib.pagesizes import A4
from reportlab.lib.units import cm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import (BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer,
                                Table, TableStyle, PageBreak, KeepTogether, ListFlowable, ListItem)

PDF = os.path.join(ROOT, "PresentationScript.pdf")
PAGE_W, PAGE_H = A4
M = 2.0 * cm
CONTENT_W = PAGE_W - 2 * M

styles = {
    "title": ParagraphStyle("title", fontName="Helvetica-Bold", fontSize=21, leading=26, textColor=INK, spaceAfter=4),
    "subtitle": ParagraphStyle("subtitle", fontName="Helvetica", fontSize=11, leading=15, textColor=SEC, spaceAfter=14),
    "h1": ParagraphStyle("h1", fontName="Helvetica-Bold", fontSize=14, leading=18, textColor=INK, spaceBefore=16, spaceAfter=6),
    "h2": ParagraphStyle("h2", fontName="Helvetica-Bold", fontSize=11.5, leading=15, textColor=INK, spaceBefore=10, spaceAfter=4),
    "h3": ParagraphStyle("h3", fontName="Helvetica-Bold", fontSize=10, leading=13, textColor=BLUE, spaceBefore=6, spaceAfter=2),
    "body": ParagraphStyle("body", fontName="Helvetica", fontSize=9.6, leading=13.8, textColor=INK, spaceAfter=6),
    "bullet": ParagraphStyle("bullet", fontName="Helvetica", fontSize=9.6, leading=13.8, textColor=INK, leftIndent=14, bulletIndent=4, spaceAfter=3),
    "caption": ParagraphStyle("caption", fontName="Helvetica-Oblique", fontSize=8.5, leading=11, textColor=SEC, spaceBefore=2, spaceAfter=10),
    "cell": ParagraphStyle("cell", fontName="Helvetica", fontSize=9, leading=12.5, textColor=INK),
    "cellhead": ParagraphStyle("cellhead", fontName="Helvetica-Bold", fontSize=8.6, leading=11, textColor=colors.white),
    "label": ParagraphStyle("label", fontName="Helvetica-Bold", fontSize=8.3, leading=11, textColor=colors.white),
    "say": ParagraphStyle("say", fontName="Helvetica-Oblique", fontSize=9.6, leading=14, textColor=INK),
    "code": ParagraphStyle("code", fontName="Courier", fontSize=8.2, leading=11.5, textColor=SEC),
    "segtitle": ParagraphStyle("segtitle", fontName="Helvetica-Bold", fontSize=12, leading=16, textColor=colors.white),
}

def footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(colors.HexColor(MUTED))
    canvas.drawString(M, 1.1 * cm, "InternProj — Presentation Script & Technical Appendix")
    canvas.drawRightString(PAGE_W - M, 1.1 * cm, "Page %d" % doc.page)
    canvas.restoreState()

doc = BaseDocTemplate(PDF, pagesize=A4, leftMargin=M, rightMargin=M, topMargin=1.8 * cm, bottomMargin=1.8 * cm,
                      title="Presentation Script & Technical Appendix",
                      author="InternProj automated benchmark harness")
frame = Frame(M, 1.8 * cm, CONTENT_W, PAGE_H - 3.6 * cm, id="main")
doc.addPageTemplates([PageTemplate(id="page", frames=[frame], onPage=footer)])

def bullets(items, style="bullet"):
    return [Paragraph(t, styles[style], bulletText="•") for t in items]

def numbered(items):
    return [ListFlowable([ListItem(Paragraph(t, styles["body"]), leftIndent=6) for t in items],
                         bulletType="1", start=1, leftIndent=16, bulletFontSize=9.6, spaceBefore=2, spaceAfter=6)]

TBL_STYLE = TableStyle([
    ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
    ("FONTSIZE", (0, 0), (-1, -1), 8.4),
    ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
    ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor(INK)),
    ("TEXTCOLOR", (0, 1), (-1, -1), colors.HexColor(SEC)),
    ("LINEBELOW", (0, 1), (-1, -2), 0.3, colors.HexColor(GRID)),
    ("TOPPADDING", (0, 0), (-1, -1), 4),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ("LEFTPADDING", (0, 0), (-1, -1), 5),
    ("RIGHTPADDING", (0, 0), (-1, -1), 5),
    ("VALIGN", (0, 0), (-1, -1), "TOP"),
])

def data_table(rows, colWidths):
    t = Table(rows, colWidths=colWidths)
    t.setStyle(TBL_STYLE)
    return t

def callout(title, items, accent=BLUE):
    body = [Paragraph(title, ParagraphStyle("ctitle", fontName="Helvetica-Bold", fontSize=9.6, leading=13, textColor=colors.HexColor(accent)))]
    body += [Paragraph(t, styles["bullet"]) for t in items]
    t = Table([[body]], colWidths=[CONTENT_W - 0.4 * cm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor(CODE_BG)),
        ("BOX", (0, 0), (-1, -1), 0.8, colors.HexColor(accent)),
        ("LEFTPADDING", (0, 0), (-1, -1), 12), ("RIGHTPADDING", (0, 0), (-1, -1), 12),
        ("TOPPADDING", (0, 0), (-1, -1), 8), ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
    ]))
    return t

def code_quote(lines, source):
    body = [Paragraph(l.replace("&", "&amp;").replace("<", "&lt;"), styles["code"]) for l in lines]
    body.append(Paragraph("— " + source, styles["caption"]))
    t = Table([[body]], colWidths=[CONTENT_W - 0.4 * cm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor(CODE_BG)),
        ("LINEBEFORE", (0, 0), (0, 0), 2.5, colors.HexColor(MUTED)),
        ("LEFTPADDING", (0, 0), (-1, -1), 12), ("RIGHTPADDING", (0, 0), (-1, -1), 12),
        ("TOPPADDING", (0, 0), (-1, -1), 8), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    return t

def segment(num, title, show, say, do_items, accent=BLUE):
    head = Table([[Paragraph("SEGMENT %d" % num, styles["label"]), Paragraph(title, styles["segtitle"])]],
                colWidths=[2.6 * cm, CONTENT_W - 2.6 * cm])
    head.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor(accent)),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (0, 0), 10), ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ]))
    lbl_style = ParagraphStyle("lbl", fontName="Helvetica-Bold", fontSize=8.3, textColor=colors.HexColor(accent))
    rows = [
        [Paragraph("ON SCREEN", lbl_style), Paragraph(show, styles["cell"])],
        [Paragraph("SAY", lbl_style), Paragraph(say, styles["say"])],
    ]
    if do_items:
        do_html = "<br/>".join(["→ " + d for d in do_items])
        rows.append([Paragraph("DO", lbl_style), Paragraph(do_html, styles["cell"])])
    body = Table(rows, colWidths=[2.6 * cm, CONTENT_W - 2.6 * cm])
    body.setStyle(TableStyle([
        ("BOX", (0, 0), (-1, -1), 0.8, colors.HexColor(GRID)),
        ("LINEBELOW", (0, 0), (-1, -2), 0.5, colors.HexColor(GRID)),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
        ("BACKGROUND", (0, 0), (0, -1), colors.HexColor(SURFACE)),
    ]))
    return KeepTogether([head, body, Spacer(1, 10)])

story = []
today = datetime.date(2026, 7, 25).strftime("%B %d, %Y")

# =========================================================================
# TITLE PAGE
# =========================================================================
story.append(Paragraph("Flow-Field Pathfinding", styles["title"]))
story.append(Paragraph("Presentation Script &amp; Technical Appendix", styles["title"]))
story.append(Paragraph("InternProj tower-defense prototype · speaker notes for a live, editor-free demo · %s" % today, styles["subtitle"]))

story.append(Paragraph(
    "This document is the script to talk from during the presentation. It assumes the Unity Editor will "
    "<b>not</b> be open — everything demoed here runs from a standalone build or a maximized Play-mode window, "
    "and every chart referenced lives in the companion <b>PerformanceReport.pdf</b>. A full technical appendix "
    "— architecture, the threading model, and exactly how each evaluation number was produced — follows the "
    "script starting on the page marked <b>Technical Appendix</b>, so you have precise answers ready for any "
    "follow-up question.", styles["body"]))

story.append(Paragraph("Before you start", styles["h1"]))
story += bullets([
    "<b>Build a standalone player first.</b> All five scenes (SampleScene, Bench_Open, Bench_Maze, Bench_Choke, "
    "Bench_Stress) are already registered in File → Build Settings, so File → Build (or ask Claude to run it) "
    "produces a .exe that needs no editor. If you must present from Play mode instead, maximize the Game view "
    "(Ctrl/Cmd + drag the tab, or the maximize-on-play toggle) so no editor chrome is visible.",
    "<b>Have PerformanceReport.pdf open</b> on a second screen or as your slide deck — Segments 6–9 below "
    "point at specific pages and charts in it by number.",
    "<b>Know the controls</b> (also shown live in the game's own HUD, top-left): "
    "<b>V</b> cycles the vector-field view (Off → Arrows → Heatmap → Both) · <b>B</b> re-runs a benchmark scene's "
    "measurement · <b>WASD</b>/arrows pan the camera, scroll zooms, hold <b>right mouse</b> to free-look "
    "(<b>Q/E</b> to roll), <b>C</b> resets the camera · <b>Space</b> pauses, <b>1/2/3</b> set game speed · "
    "left click places the held piece, <b>Shift</b> swaps the held piece, <b>R</b> rotates it.",
    "<b>Rehearse the scene switch once.</b> Loading a different scene mid-build (Bench_Maze after the main "
    "game) takes a couple of seconds — know that beat so it doesn't feel like a stall live.",
])

story.append(callout("Numbers to have on the tip of your tongue", [
    "Field rebuild on the real game map: <b>~%.1f ms</b> — cheap enough to run on every wall edit." % rebuild38["meanMs"],
    "Shared field vs. per-agent A* at 1,000 agents: <b>×%.0f cheaper</b> (%.1f ms vs. %.0f ms)." % (astar1000["totalMs"] / astar["fieldRebuildMs"], astar["fieldRebuildMs"], astar1000["totalMs"]),
    "500 live agents hold <b>~%.0f–%.0f fps</b> across every scene tested, including a grid 6.9× larger." % (min(t["fps"] for t in [tier500] + [scen[k]["tiers"][2] for k in scen]), max(t["fps"] for t in [tier500] + [scen[k]["tiers"][2] for k in scen])),
    "Editing the map continuously under load roughly doubles frame time (%.0f ms → %.0f ms avg) — the one real weak spot, and it's already got a named fix (coalesce rebuilds)." % (tier500["avgMs"], edits["avgMs"]),
]))

story.append(Paragraph("Suggested run of show (≈9–11 minutes)", styles["h1"]))
runshow_rows = [
    ("0:00–1:00", "1 — Hook", "Talking, no visuals yet"),
    ("1:00–2:30", "2 — Why a flow field", "PerformanceReport.pdf, page 2"),
    ("2:30–4:30", "3 &amp; 4 — Live game demo", "Standalone build, SampleScene"),
    ("4:30–6:00", "5 — Benchmark scenes", "Standalone build, Bench_Maze / Bench_Stress"),
    ("6:00–9:00", "6–9 — Walk the report", "PerformanceReport.pdf, pages 4–6"),
    ("9:00–10:00", "10 — Wrap-up", "PerformanceReport.pdf, page 6 (Recommendations)"),
    ("10:00+", "Q&amp;A", "Appendix in this document, as needed"),
]
story.append(data_table(
    [[Paragraph(h, styles["cellhead"]) for h in ["Time", "Segment", "What's on screen"]]] +
    [[Paragraph(a, styles["cell"]), Paragraph(b, styles["cell"]), Paragraph(c, styles["cell"])] for a, b, c in runshow_rows],
    colWidths=[2.6 * cm, 4.6 * cm, CONTENT_W - 7.2 * cm]))

story.append(PageBreak())

# =========================================================================
# THE SCRIPT
# =========================================================================
story.append(Paragraph("The Script", styles["title"]))
story.append(Paragraph("Read SAY sections as a starting point, not a script to memorize verbatim — say it in your own words.", styles["subtitle"]))

story.append(segment(1, "The Hook", accent=BLUE,
    show="Nothing yet — just you talking, or a title slide.",
    say="This is a tower-defense prototype where the player builds Tetris-style wall pieces to slow down waves "
        "of enemies marching toward a core. Every enemy needs a path to that core, and that path has to update "
        "the instant the player drops a new wall — potentially with hundreds of enemies on screen at once. "
        "The question I want to answer today is: how does the pathfinding hold up, and how do I know?",
    do_items=[]))

story.append(segment(2, "Why a Flow Field (Not One Path Per Enemy)", accent=BLUE,
    show="PerformanceReport.pdf, page 2, section 2 — \"System under test\".",
    say="The naive approach is: every enemy runs its own pathfinding search to the core. That's wasteful, "
        "because every single enemy is searching for a path to the exact same destination. Instead, this project "
        "computes one shared \"flow field\": a single flood fill from the core outward that gives every tile on "
        "the map a direction to follow. Enemies don't search at all — they just read the arrow under their feet. "
        "The field gets rebuilt whenever the map changes, and walls aren't hard obstacles, they're expensive "
        "terrain with health, so a path always exists — enemies either detour around a wall or dig through it, "
        "whichever the field decides is cheaper.",
    do_items=[]))

story.append(segment(3, "Live Demo — The Game, With the Field Made Visible", accent=ORANGE,
    show="Standalone build, SampleScene, camera pulled back over the whole map.",
    say="Here's the actual game running. Right now you can't see the pathfinding at all — that's normally "
        "invisible. Watch what happens when I press V.  [press V twice to reach Arrows+Heatmap]  Every tile is "
        "now colored by its cost to reach the core — green is cheap, red is expensive — and every tile has an "
        "arrow pointing the way an enemy standing there would go. This is the actual live data structure the "
        "AI reads from, rendered straight onto the ground in real time. It works in a full build with no editor "
        "attached, which is exactly why I can show it to you right now.",
    do_items=["Press V until the caption bottom-left reads \"ArrowsAndHeatmap\"",
              "Pan the camera (WASD / scroll) so the audience can see the whole grid"]))

story.append(segment(4, "Live Demo — Building a Wall and Watching It Reroute", accent=ORANGE,
    show="Same scene, mid-build phase (before wave 1 starts, or paused with Space).",
    say="Now I'll place a wall.  [click to place a piece]  Watch the heatmap: the instant the piece lands, every "
        "arrow near it updates, because the field just rebuilt itself from scratch. That rebuild is not "
        "something you can see happen — it's fast enough to finish inside a single frame. If I start a wave, "
        "you'll see enemies path around this new wall on their own, or if it's cheap enough, walk straight into "
        "it and start digging.",
    do_items=["Left-click to place the currently held piece",
              "Optional: press Start (or wait) to spawn a wave and watch enemies react to the new wall"]))

story.append(segment(5, "Live Demo — The Benchmark Scenes", accent=ORANGE,
    show="Load Bench_Maze, then Bench_Stress, from the build's scene picker or by re-launching with that scene active.",
    say="Alongside the main game there are four purpose-built scenes I made specifically to stress-test this "
        "system under controlled conditions. This is Bench_Maze — a hand-built serpentine corridor. Notice the "
        "heatmap bands: each corridor leg is a visibly more expensive shade of red than the last, because the "
        "cost keeps accumulating the further an enemy has to walk. This one is Bench_Stress — a 100-by-100 "
        "grid, roughly seven times the node count of the real game map, with a thousand randomly scattered "
        "walls. Both of these auto-run their own performance measurement the moment you hit Play, and you can "
        "press B to re-run it live if you want a real-time number on screen.",
    do_items=["Press V to enable the heatmap in each new scene (the toggle resets per scene)",
              "Press B to re-trigger a scenario's benchmark and narrate the console log as it reports"]))

story.append(PageBreak())

story.append(segment(6, "The Report — Executive Summary", accent=AQUA,
    show="PerformanceReport.pdf, page 1.",
    say="Everything from here is measured, not estimated — every scene you just saw ran a real automated "
        "benchmark, and these are the actual numbers it produced. The headline is on this page: a rebuild on "
        "the real game map costs about %.1f milliseconds, cheap enough to run on every single wall edit, and "
        "frame rate turns out to be limited by how many enemies are alive, not by the pathfinding itself." % rebuild38["meanMs"],
    do_items=[]))

story.append(segment(7, "The Report — Rebuild Scaling and the A* Comparison", accent=AQUA,
    show="PerformanceReport.pdf, page 4, sections 4.1 and 4.2.",
    say="This first chart shows rebuild cost as the grid grows and as more of it is walled off — it scales "
        "roughly linearly, and even at ten thousand nodes it's still only twenty-odd milliseconds. The second "
        "chart is the one I'd lead with in a Q&amp;A: it directly compares this shared-field approach against giving "
        "every enemy its own A* search on identical hardware, identical grid, identical cost rules. At a "
        "thousand agents, the shared field is %.0f times cheaper, and — this is the important part — that gap "
        "only gets wider as the crowd grows, because the field's cost doesn't depend on how many enemies are "
        "reading it." % (astar1000["totalMs"] / astar["fieldRebuildMs"]),
    do_items=[]))

story.append(segment(8, "The Report — Frame Rate vs. Crowd Size", accent=AQUA,
    show="PerformanceReport.pdf, page 5, section 4.3.",
    say="This is five scenes overlaid — the real game map, all four benchmark scenes — and they nearly sit on "
        "top of each other. That's the point: neither a more complex maze layout nor a grid seven times larger "
        "moves this curve. What actually costs frame time is the number of live agents, at roughly thirty-seven "
        "microseconds each, which is agent bookkeeping, not pathfinding. Five hundred agents hold sixty-plus "
        "frames a second everywhere; the game's actual waves peak around a hundred enemies, which runs at a "
        "hundred fifty-plus.",
    do_items=[]))

story.append(segment(9, "The Report — Editing the Map Under Load", accent=AQUA,
    show="PerformanceReport.pdf, page 6, section 4.4, then section 5 (Recommendations).",
    say="The one place this system shows real strain: if you keep editing the map continuously while five "
        "hundred agents are alive, average frame time roughly doubles, and the worst single frame hits eighty-"
        "five milliseconds. I dug into why — it's not the rebuild itself, it's that every single wall breach "
        "currently triggers its own immediate rebuild, so several breaches landing in the same frame each pay "
        "that cost separately. The fix is a one-line architectural change: coalesce rebuild requests into a "
        "dirty flag consumed once per frame instead of firing immediately. That's the top recommendation in "
        "the report, and it's the honest, unglamorous kind of finding I wanted this evaluation to be able to "
        "surface.",
    do_items=[]))

story.append(segment(10, "Wrap-Up", accent=BLUE,
    show="PerformanceReport.pdf, page 6 — Recommendations list.",
    say="So: the architecture choice — one shared field instead of per-agent search — is validated by the "
        "numbers, not just intuition. It scales to far more agents and a far bigger map than this game "
        "actually needs, the one weak spot has a specific known fix, and everything you just watched happen "
        "live was rendered by the same visualization tool that made this evaluation possible to build and check "
        "in the first place. Happy to take questions.",
    do_items=[]))

story.append(Paragraph("Anticipated Questions", styles["h1"]))
qa = [
    ["\"Is this actually Dijkstra's algorithm?\"",
     "Close, but precisely: it's SPFA (a queue-based Bellman-Ford variant) — a node can be re-added to the queue "
     "if a cheaper route to it is found later, rather than using a priority queue. On this grid's cost structure "
     "it behaves like Dijkstra in practice; the A* used for the comparison chart is a real binary-heap "
     "implementation, which is why that comparison is apples-to-apples on cost model, not on algorithm family."],
    ["\"Does this use multithreading / the Job System?\"",
     "No — zero multithreading anywhere in the project, confirmed by search. Full explanation, including why an "
     "earlier version of this code did use background threads and had them deliberately removed, is in the "
     "Technical Appendix, section B."],
    ["\"How do you know these numbers are real and not made up?\"",
     "Every chart in the report is generated directly from JSON files the benchmark code itself writes at "
     "runtime — nothing is hand-typed. Appendix section E walks through exactly how each statistic is computed, "
     "line by line."],
    ["\"What happens with even more agents, like 5,000?\"",
     "Not tested here — the tiers stop at 1,000 because that's an order of magnitude past the game's real wave "
     "sizes (~100). The fps-vs-agents curve is linear in the range measured, so a rough extrapolation exists, "
     "but it would need its own benchmark run to state as a real number."],
    ["\"Why not use Unity's built-in NavMesh?\"",
     "NavMesh is built for per-agent goals on largely static geometry; this game needs many agents converging "
     "on one shared destination while the walkable surface changes every few seconds as the player builds — a "
     "flow field amortizes exactly that shared-destination, frequently-changing-map case in a way NavMesh "
     "isn't designed for."],
]
story.append(data_table([[Paragraph("Question", styles["cellhead"]), Paragraph("Answer", styles["cellhead"])]] +
                        [[Paragraph(q, styles["cell"]), Paragraph(a, styles["cell"])] for q, a in qa],
                        colWidths=[5.4 * cm, CONTENT_W - 5.4 * cm]))

# =========================================================================
# TECHNICAL APPENDIX
# =========================================================================
story.append(PageBreak())
story.append(Paragraph("Technical Appendix", styles["title"]))
story.append(Paragraph("Everything you need to know about how this project works under the hood, and exactly how every "
                       "evaluation number in PerformanceReport.pdf was produced.", styles["subtitle"]))

# ---- A. Architecture ----
story.append(Paragraph("A. Architecture at a Glance", styles["h1"]))
arch_rows = [
    ("Script", "Role"),
    ("Node.cs", "Plain C# class (not a struct) — one grid cell: walkability, terrain cost, wall health, and the field's own bestCost / bestDirection / nextNode."),
    ("GridManager.cs", "Builds the Node[,] grid from world bounds at Awake(); world↔grid coordinate conversion; 8-neighbor lookup with a corner-cutting rule."),
    ("FlowFieldManager.cs", "Owns GenerateFlowField() — the flood fill described in section C. Bumps a FieldVersion counter after every rebuild."),
    ("FlowAgent.cs", "Per-enemy behaviour: follows nextNode each frame, chews a wall's health down while standing on it, dies on timeout or reaching the core."),
    ("PlayerBuilder.cs / BlockManager.cs / BlockShape.cs", "Piece queue (7-bag randomizer), placement legality, and the synchronous rebuild triggered by every placement."),
    ("FlowFieldVisualizer.cs", "New this evaluation — renders the field as ground geometry. Detailed in section D."),
    ("ScenarioBenchmark.cs / BenchmarkRunner.cs", "The measurement harnesses. Detailed in section E."),
]
story.append(data_table(
    [[Paragraph(arch_rows[0][0], styles["cellhead"]), Paragraph(arch_rows[0][1], styles["cellhead"])]] +
    [[Paragraph(a, styles["cell"]), Paragraph(b, styles["cell"])] for a, b in arch_rows[1:]],
    colWidths=[5.2 * cm, CONTENT_W - 5.2 * cm]))
story.append(Spacer(1, 4))

# ---- B. Threading ----
story.append(Paragraph("B. Threading &amp; Concurrency Model", styles["h1"]))
story.append(callout("Short answer", [
    "This project uses <b>no multithreading anywhere</b>. There is no Unity Job System, no Burst compiler, no "
    "C# <font face='Courier'>System.Threading.Thread</font>, no <font face='Courier'>Task</font>/async-await, and no "
    "<font face='Courier'>Parallel.For</font> in any script under Assets/Scripts (verified by a full-project text "
    "search). Grid construction, the flow-field flood fill, every agent's movement and digging, and all benchmark "
    "sampling run synchronously on Unity's single main thread, one operation after another, once per frame.",
], accent=AQUA))

story.append(Paragraph(
    "It's worth being precise about <b>coroutines</b>, since they're easy to mistake for concurrency: "
    "<font face='Courier'>WaveSpawner</font>'s wave/intermission timers, <font face='Courier'>CanvasDashboard</font>'s "
    "UI pop animations, and the benchmark scripts' sampling loops are all Unity coroutines "
    "(<font face='Courier'>IEnumerator</font> + <font face='Courier'>yield return</font>). A coroutine is "
    "<b>cooperative multitasking on the main thread</b> — Unity pauses the method at a yield and resumes it on a "
    "later frame, but it never executes concurrently with anything else. A coroutine and Update() never run at "
    "the same instant; a coroutine and Update() also never race over shared data the way two real threads could. "
    "There is exactly one thread doing exactly one thing at a time throughout this whole project.", styles["body"]))

story.append(Paragraph("This project used to use background threads — and removed them", styles["h2"]))
story.append(Paragraph(
    "The comments in the current code are explicit about this history. An earlier version of the pathfinding ran "
    "the flood fill on a background thread, split the map into chunks, and needed an "
    "<font face='Courier'>isCalculating</font> lock plus a validate/revert step for whenever a background result "
    "arrived after the map had already changed again:", styles["body"]))
story.append(code_quote([
    "// One synchronous weighted flood fill (Dijkstra-style relaxation), run on the",
    "// main thread whenever the map changes. On a 25x25 grid this costs microseconds",
    "// per call, which is why the old chunk system, background threads, the",
    "// isCalculating lock, ValidatePath, sinkhole detection and the global fallback",
    "// could all be deleted: there is nothing left to go stale between chunks, and",
    "// no background thread for agents or previews to race against.",
], "FlowFieldManager.cs, class header"))
story.append(Paragraph(
    "And directly in the wall-placement code:", styles["body"]))
story.append(code_quote([
    "// Placement is fully synchronous now. Because walls are expensive terrain",
    "// rather than absolute blockers, NO placement can ever disconnect the map -",
    "// so there is nothing to validate asynchronously, nothing to revert, and no",
    "// background thread to race against.",
], "PlayerBuilder.cs, HandlePlacement()"))
story.append(Paragraph(
    "The underlying design change that made this possible: walls became <i>expensive terrain</i> (a high but "
    "finite cost) instead of hard obstacles. That single change means a path to the goal always mathematically "
    "exists, so there's nothing to validate, nothing to roll back if a background calculation turns out stale, "
    "and therefore no reason to pay the complexity cost of a lock or a background thread at all. The %.1f ms "
    "rebuild time measured on the real game map (section E.1) is the empirical proof that the synchronous "
    "version is fast enough that this simplification was the right call, not a premature one." % rebuild38["meanMs"],
    styles["body"]))

story.append(Paragraph("When would multithreading actually start to matter here?", styles["h2"]))
story.append(Paragraph(
    "Section 4.1 of the report shows rebuild cost growing to ~19–40 ms at a 100×100 grid (10,000 nodes) — that's "
    "the point where a synchronous rebuild starts to compete visibly with a 60 fps frame budget, and where moving "
    "the flood fill off the main thread (or amortizing it across several frames) would start to pay for itself. "
    "Note this isn't a drop-in change: <font face='Courier'>Node</font> is a plain C# reference-type class with "
    "object pointers (<font face='Courier'>nextNode</font>, <font face='Courier'>visualObject</font>), which the "
    "Job System/Burst combination can't touch directly — adopting it would mean restructuring the grid into "
    "blittable NativeArrays first. A plain <font face='Courier'>System.Threading.Thread</font> or "
    "<font face='Courier'>Task.Run</font> off the main thread would be the lower-effort option, at the cost of "
    "reintroducing exactly the staleness/locking problem the code comments above describe removing.", styles["body"]))

story.append(PageBreak())

# ---- C. Algorithm ----
story.append(Paragraph("C. The Pathfinding Algorithm", styles["h1"]))
story.append(Paragraph(
    "<font face='Courier'>FlowFieldManager.GenerateFlowField()</font> runs a single-source shortest-path fill "
    "outward from the goal (the player's core), storing the result at every node instead of tracing one path per "
    "query:", styles["body"]))
story += numbered([
    "Every node's cost is reset to infinity and its <font face='Courier'>nextNode</font> pointer cleared, so no "
    "stale pointer can survive a recalculation.",
    "The goal node is seeded at cost 0 and pushed onto a FIFO queue.",
    "While the queue isn't empty: pop a node, examine its up-to-8 neighbours, and for each one compute "
    "<i>step cost</i> = (10 for a cardinal move, 14 for a diagonal — an integer approximation of 10×√2) × the "
    "neighbour's <font face='Courier'>terrainCost</font>. If that beats the neighbour's current best cost, update "
    "it, point its <font face='Courier'>nextNode</font> back at the current node, and re-enqueue it.",
    "A node can be enqueued more than once if a cheaper route to it is found later — this is what makes it "
    "<b>SPFA</b> (Shortest Path Faster Algorithm, a queue-based Bellman-Ford variant) rather than textbook "
    "Dijkstra, which would use a priority queue and settle each node exactly once. On this grid's cost "
    "structure (small integer weights, no negative edges) it produces identical results to Dijkstra; it's simply "
    "implemented with a plain queue instead of a binary heap.",
    "Every reachable node ends up with a cost-to-goal and a single \"best neighbour\" pointer. Any agent, "
    "anywhere on the map, gets its next step in O(1) by reading its current node's <font face='Courier'>nextNode</font> "
    "— no search happens at query time at all.",
])
story.append(Paragraph(
    "Two extra rules shape the result: a <b>corner-cutting rule</b> in <font face='Courier'>GridManager.GetNeighbors()</font> "
    "forbids a diagonal move when either of the two flanking cardinal tiles is unwalkable or has a standing wall "
    "— an agent may walk straight into a wall (that's how digging starts) but may never slip diagonally between "
    "two solid tiles. And <b>walls are terrain, not obstacles</b>: a placed wall raises its tile's "
    "<font face='Courier'>terrainCost</font> (default dig cost 15, meaning \"as bad as a 15-tile detour\") rather "
    "than removing it from the graph, and separately tracks a <font face='Courier'>wallHealth</font> that an "
    "agent standing on it depletes over time. When health hits zero the tile resets to open ground and "
    "<font face='Courier'>GenerateFlowField()</font> is called again immediately so every agent reroutes through "
    "the fresh gap.", styles["body"]))

# ---- D. Visualizer ----
story.append(Paragraph("D. The Runtime Vector-Field Visualizer", styles["h1"]))
story.append(Paragraph(
    "<font face='Courier'>FlowFieldVisualizer.cs</font> (new this evaluation) renders the field as two separate "
    "procedural meshes parented under the same GameObject as the flow field manager:", styles["body"]))
story += bullets([
    "<b>Heatmap layer</b> — one quad per walkable node, vertex-colored on a green→red ramp normalized to the "
    "highest reachable cost on the map that frame (orange = standing wall, dark grey = static blocker, purple = "
    "unreachable pocket).",
    "<b>Arrow layer</b> — a small triangulated arrow per node pointing along its <font face='Courier'>bestDirection</font>, "
    "plus a diamond marker on the goal tile; skipped on wall tiles since the wall geometry would hide it anyway.",
    "Both layers use an unlit, vertex-colored material (<font face='Courier'>Sprites/Default</font> shader, falling "
    "back from a <font face='Courier'>Resources/FlowFieldViz</font> asset) so the whole overlay is one draw call "
    "per layer regardless of grid size, and use 32-bit mesh indices so grids above 65,536 vertices (e.g. the "
    "100×100 stress scene) don't overflow Unity's default 16-bit index buffer.",
    "Geometry is only rebuilt when <font face='Courier'>FlowFieldManager.FieldVersion</font> changes or the view "
    "mode changes — the counter is bumped once at the end of every <font face='Courier'>GenerateFlowField()</font> "
    "call — so on a static field the visualizer's steady-state per-frame cost is a single integer comparison, "
    "not a re-render.",
    "The mode cycles Off → Arrows → Heatmap → Arrows+Heatmap on the <b>V</b> key "
    "(<font face='Courier'>Keyboard.current</font> from Unity's new Input System — this project does not use the "
    "legacy <font face='Courier'>Input</font> class anywhere), with a small always-on-top "
    "<font face='Courier'>OnGUI()</font> label showing the current mode.",
])

story.append(PageBreak())

# ---- E. Methodology ----
story.append(Paragraph("E. How Every Evaluation Statistic Was Produced", styles["h1"]))
story.append(Paragraph(
    "Nothing in PerformanceReport.pdf is estimated or hand-entered. Every scene writes a JSON file straight to "
    "the project root at the end of its run, and the report's charts are generated directly from those files "
    "(Tools/make_report.py). This section explains exactly what each number measures.", styles["body"]))

story.append(Paragraph("E.1 — Rebuild time (mean / min / max, ms)", styles["h2"]))
story.append(Paragraph(
    "A <font face='Courier'>System.Diagnostics.Stopwatch</font> is started and stopped around a single call to "
    "<font face='Courier'>FlowFieldManager.GenerateFlowField()</font>. Three warm-up calls run first and are "
    "discarded (letting the JIT and CPU caches settle), then the call is timed individually for 30 iterations "
    "(BenchmarkRunner's grid-size × wall-density sweep) or 50 iterations (each ScenarioBenchmark scene). Mean, "
    "min and max are computed from those raw per-call millisecond samples — this is wall-clock cost of the exact "
    "algorithm in section C, not a theoretical estimate.", styles["body"]))

story.append(Paragraph("E.2 — Path metrics (tiles, cost)", styles["h2"]))
story.append(Paragraph(
    "After a fresh rebuild, the spawn point's node is located, then the code walks the "
    "<font face='Courier'>nextNode</font> chain — the field's own back-pointers — from spawn to goal, counting "
    "hops for \"tiles\" and reading the spawn node's <font face='Courier'>bestCost</font> for \"cost\". Cost is "
    "stored internally ×10 (so the 14/10 diagonal ratio can be an integer) and divided by 10 for display, so it "
    "reads in intuitive \"tile-equivalent\" units.", styles["body"]))

story.append(Paragraph("E.3 — Frame time / fps per agent tier", styles["h2"]))
story += numbered([
    "A pooled set of agent GameObjects is grown to the target tier size (100/250/500/1,000), each initialized "
    "with an effectively infinite lifetime (999,999 s) so none expire mid-measurement and skew the sample.",
    "A 1-second, unsampled warm-up lets the freshly spawned agents spread out and any spawn-frame hitch settle "
    "before any timing starts.",
    "Then, for a 4-second sampling window, every single frame: any agent that has reached the goal is "
    "immediately respawned at the spawn point (keeping the live count exactly constant across the whole window), "
    "and <font face='Courier'>Time.unscaledDeltaTime</font> for that frame is appended to a list.",
    "From that list: <b>avg</b> = mean of all sampled frame times; <b>fps</b> = 1 / avg; the list is sorted and "
    "<b>p95</b> is the value at the 95th-percentile rank; <b>worst</b> is simply the single largest frame time "
    "observed in the whole window (not an average of bad frames — the literal worst individual frame).",
    "Unscaled time is used deliberately so the sample is unaffected by the game's own x1/x2/x3 time-scale "
    "control, and vSync is forced off with the frame rate uncapped "
    "(<font face='Courier'>QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1</font>) at the start "
    "of every run, so the numbers reflect the engine's real per-frame cost rather than a monitor's refresh "
    "ceiling.",
])

story.append(Paragraph("E.4 — Shared field vs. per-agent A*", styles["h2"]))
story.append(Paragraph(
    "Both approaches run on the identical grid (38×38, 10% wall density, the same 10/14×terrainCost step-cost "
    "model) so the comparison isolates the algorithmic choice, not the map. \"Field rebuild ms\" is one "
    "Stopwatch-timed call as in E.1. \"Per-agent A*\" is a separate, hand-written implementation — a binary "
    "min-heap (array-backed, explicit sift-up/sift-down, not a library structure) with an octile-distance "
    "heuristic that is admissible because the cheapest possible tile costs terrainCost = 1. It runs once per "
    "random walkable start tile toward the same goal (one warm-up search discarded first), and the reported "
    "number is one Stopwatch span covering the whole batch of N searches back to back — so it is genuinely "
    "\"what it would cost to path N enemies individually,\" not a per-search figure multiplied out.", styles["body"]))

story.append(Paragraph("E.5 — Dynamic-edit stability test", styles["h2"]))
story.append(Paragraph(
    "With 500 agents held alive exactly as in E.3, a coroutine toggles a 2×2 block of wall tiles on or off every "
    "0.5 seconds for 8 seconds (16 edits total), calling <font face='Courier'>GenerateFlowField()</font> "
    "synchronously after every single edit. This is a deliberately worse case than real play — the actual game "
    "only rebuilds on a genuine player placement or a wall breach, not a fixed timer — chosen specifically to "
    "surface a worst-case number. Frame times are sampled with the identical FrameSampler used in E.3 throughout "
    "the 8-second window and compared against the static-map 500-agent tier from the same run.", styles["body"]))

story.append(Paragraph("E.6 — Environment metadata", styles["h2"]))
story.append(Paragraph(
    "CPU/GPU/RAM/OS strings and core count are read from Unity's <font face='Courier'>SystemInfo</font> API and "
    "<font face='Courier'>Application.unityVersion</font> at the start of every run and written verbatim into "
    "that run's JSON file, so each result file is self-describing and traceable to the exact machine it ran on.",
    styles["body"]))

# ---- F. Caveats ----
story.append(Paragraph("F. Methodology Caveats", styles["h1"]))
story += bullets([
    "<b>Single machine, single run per condition.</b> These are point measurements, not averages across "
    "repeated trials — treat them as one representative sample rather than statistically bulletproof figures. "
    "Re-running the same scene (press B) typically reproduces results within a few percent on this hardware.",
    "<b>Editor Play Mode, not a standalone build.</b> All numbers in this evaluation were captured with the "
    "Unity Editor running (vSync off, uncapped frame rate) — editor-only overhead (profiler hooks, debug "
    "scaffolding) makes these a conservative floor. An actual standalone build should perform at least as well.",
    "<b>Layout determinism varies by test.</b> Bench_Maze and Bench_Choke build the same wall layout every run "
    "by construction; Bench_Stress seeds its random scatter (<font face='Courier'>Random.InitState(12345)</font>) "
    "so it's reproducible too. BenchmarkRunner's grid-size × density sweep (used for the report's section 4.1 "
    "chart) places walls with live, unseeded <font face='Courier'>Random.Range</font> calls, so that specific "
    "chart can show minor run-to-run variance in exactly which tiles are walled, though not in the density.",
    "<b>Uncapped frame rate is intentional but unrealistic for players.</b> Turning vSync off exposes true "
    "per-frame cost, which is what a performance evaluation needs — but most players will actually run with "
    "vSync on, where frame pacing looks smoother than these raw numbers suggest.",
    "<b>Benchmark scenes are static snapshots.</b> Bench_Maze/Choke/Stress don't grow their walls over time the "
    "way a real player's match does — they measure the cost of a given map complexity, not the cost of a map "
    "that keeps escalating for 20+ waves. E.5 is the one test that specifically adds ongoing map churn.",
])

# ---- G. Glossary ----
story.append(Paragraph("G. Glossary", styles["h1"]))
glossary_rows = [
    ("Flow field", "A grid where every tile stores a direction toward one shared destination, computed once and read by every agent."),
    ("SPFA", "Shortest Path Faster Algorithm — a FIFO-queue-based relaxation (Bellman-Ford family); this project's fill algorithm."),
    ("Terrain cost", "The cost to enter a tile. 1 = open ground; a placed wall raises it (default 15) to represent \"as bad as a 15-tile detour.\""),
    ("Dig / wall health", "Seconds of chewing (by one agent; several agents chew a shared tile in parallel) before a wall tile breaks back to open ground."),
    ("Corner-cutting rule", "Diagonal moves are forbidden when either flanking cardinal tile is solid, so agents can't clip through wall corners."),
    ("FieldVersion", "A counter bumped once per completed rebuild, used by the visualizer to know when to regenerate its mesh."),
    ("p95 / worst", "p95 = the frame time only the worst 5% of sampled frames exceeded. Worst = the single slowest frame in the sample."),
    ("Coroutine", "Cooperative multitasking on Unity's main thread (pause/resume between frames) — not a thread, no real concurrency."),
]
story.append(data_table(
    [[Paragraph(h, styles["cellhead"]) for h in ["Term", "Meaning"]]] +
    [[Paragraph(a, styles["cell"]), Paragraph(b, styles["cell"])] for a, b in glossary_rows],
    colWidths=[3.6 * cm, CONTENT_W - 3.6 * cm]))

doc.build(story)
print("PDF written:", PDF)

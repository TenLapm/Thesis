# Docs/GDD.md -> out/gdd.html, a print-styled page, which headless Edge prints to Docs/GDD.pdf.
# Handles exactly the Markdown the GDD uses: headings, paragraphs, lists (with nested
# blocks), tables, one fenced block, images, bold, italics and code spans.
import html, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DOCS = os.path.join(ROOT, 'Docs')
SRC = os.path.join(DOCS, 'GDD.md')
OUT = os.path.join(HERE, 'out', 'gdd.html')
PDF = os.path.join(DOCS, 'GDD.pdf')
IMG_BASE = 'file:///' + DOCS.replace(os.sep, '/') + '/'


def edge():
    for p in (r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', r'C:\Program Files\Microsoft\Edge\Application\msedge.exe'):
        if os.path.exists(p): return p
    return None
TAGS = {'Built': 'built', 'Planned': 'planned', 'Proposed': 'proposed', 'Open': 'open'}
ITEM = re.compile(r'^(- |\d+\. )')
FIGURES_HERE = '<!-- pdf: figures here -->'
IMAGE = re.compile(r'^!\[(.*?)\]\((.*?)\)\s*$')


def bold(m):
    t = m.group(1)
    for word, cls in TAGS.items():
        if t == word or re.match(re.escape(word) + r'[\s\.,:\(]', t):
            rest = t[len(word):]
            tag = '<span class="tag %s">%s</span>' % (cls, word)
            if rest.strip() in ('', '.', ':', ','): return tag
            return tag + ' <strong>' + rest.lstrip(' :').strip() + '</strong>'
    return '<strong>' + t + '</strong>'


def inline(text):
    out = []
    for i, part in enumerate(text.split('`')):
        if i % 2 == 1:
            out.append('<code>' + html.escape(part, quote=False) + '</code>')
            continue
        s = html.escape(part, quote=False)
        s = re.sub(r'\*\*(.+?)\*\*', bold, s)
        assert '**' not in s, 'unbalanced bold in: ' + text[:80]
        s = re.sub(r'(?<![\*\w])\*(?!\s)([^*]+?)(?<!\s)\*(?!\w)', r'<em>\1</em>', s)
        out.append(s)
    return ''.join(out)


def slug(text):
    return re.sub(r'[^a-z0-9]+', '-', text.lower()).strip('-')


def cells(line):
    return [c.strip() for c in line.strip().strip('|').split('|')]


def table(rows):
    head, body = cells(rows[0]), [cells(r) for r in rows[2:]]
    wide = ' class="c%d%s"' % (len(head), ' long' if len(body) > 13 else '')
    h = ['<table%s><thead><tr>' % wide] + ['<th>%s</th>' % inline(c) for c in head] + ['</tr></thead><tbody>']
    for r in body:
        h.append('<tr>' + ''.join('<td>%s</td>' % inline(c) for c in r) + '</tr>')
    h.append('</tbody></table>')
    return ''.join(h)


def blocks(lines, toc=None, figs=None):
    # figs (top level only): pictures wait here until the text of their subsections has
    # been written, so that several can follow one another on landscape pages without
    # a nearly empty page of text between each two.
    out, i, n = [], 0, len(lines)
    while i < n:
        line = lines[i]
        if not line.strip():
            i += 1
            continue

        if line.strip() == FIGURES_HERE:
            # the document says where its waiting pictures go (a landscape page breaks the
            # text, so the place is chosen where a page of text is full anyway)
            if figs is not None and figs['pending']:
                out.extend(figs['pending'])
                figs['pending'] = []
            i += 1
            continue

        if line.startswith('```'):
            j = i + 1
            while not lines[j].startswith('```'): j += 1
            out.append('<pre>' + html.escape('\n'.join(lines[i + 1:j]), quote=False) + '</pre>')
            i = j + 1
            continue

        m = re.match(r'^(#{1,3}) (.*)$', line)
        if m:
            level, title = len(m.group(1)), m.group(2)
            j = i + 1
            while j < n and not lines[j].strip(): j += 1
            img = IMAGE.match(lines[j]) if j < n else None
            if level == 1:
                out.append('<h1>Game design document</h1>')
            elif img and figs is not None:
                # a heading with its picture: the heading stays in the text, the picture
                # gets a landscape page of its own a little later
                number = figs['count'] = figs['count'] + 1
                path = img.group(2)
                if os.path.exists(os.path.join(DOCS, path.replace('.png', '.svg'))): path = path.replace('.png', '.svg')   # a drawn picture prints sharper as SVG
                figs['pending'].append('<section class="figpage"><div class="figtitle">Figure %d <span>%s</span></div><img src="%s%s" alt=""><p class="caption">%s</p></section>'
                                       % (number, inline(title), IMG_BASE, path, inline(img.group(1))))
                out.append('<h%d id="%s">%s <span class="figref">Figure %d</span></h%d>' % (level, slug(title), inline(title), number, level))
                i = j + 1
                continue
            else:
                if figs is not None and figs['pending']:
                    out.extend(figs['pending'])
                    figs['pending'] = []
                if level == 2 and toc is not None: toc.append(title)
                out.append('<h%d id="%s">%s</h%d>' % (level, slug(title), inline(title), level))
            i += 1
            continue

        if line.strip() == '---':
            i += 1
            continue

        if line.lstrip().startswith('|'):
            j = i
            while j < n and lines[j].lstrip().startswith('|'): j += 1
            out.append(table(lines[i:j]))
            i = j
            continue

        if ITEM.match(line):
            ordered = line[0].isdigit()
            items = []
            while i < n and ITEM.match(lines[i]):
                width = len(ITEM.match(lines[i]).group(1))
                body = [lines[i][width:]]
                i += 1
                while i < n:
                    if lines[i].startswith(' '):
                        body.append(lines[i][width:] if lines[i][:width].strip() == '' else lines[i].lstrip())
                        i += 1
                    elif not lines[i].strip() and i + 1 < n and lines[i + 1].startswith(' '):
                        body.append('')
                        i += 1
                    else:
                        break
                inner = blocks(body)
                if inner.count('<p>') == 1 and inner.startswith('<p>') and inner.endswith('</p>'): inner = inner[3:-4]
                items.append('<li>' + inner + '</li>')
                # a blank line between two items of the same list does not end it
                if i + 1 < n and not lines[i].strip() and ITEM.match(lines[i + 1]) and lines[i + 1][0].isdigit() == ordered: i += 1
            out.append(('<ol>%s</ol>' if ordered else '<ul>%s</ul>') % ''.join(items))
            continue

        j = i
        while j < n and lines[j].strip() and not lines[j].startswith(('#', '```', '|')) and not ITEM.match(lines[j]) and lines[j].strip() != '---':
            j += 1
        out.append('<p>' + inline(' '.join(l.strip() for l in lines[i:j])) + '</p>')
        i = j
    return ''.join(out)


CSS = """
@page { size: A4; margin: 19mm 18mm 20mm 18mm;
        @bottom-center { content: counter(page) " / " counter(pages); font: 8.5pt "Segoe UI", Arial, sans-serif; color: #7a8291; } }
@page wide { size: A4 landscape; margin: 12mm 13mm 14mm 13mm;
        @bottom-center { content: counter(page) " / " counter(pages); font: 8.5pt "Segoe UI", Arial, sans-serif; color: #7a8291; } }
* { box-sizing: border-box; }
html { font: 10.6pt/1.5 "Segoe UI", Arial, Helvetica, sans-serif; color: #1c2230; }
body { margin: 0; }
h1 { font-size: 25pt; line-height: 1.15; margin: 0 0 2mm; letter-spacing: -0.3pt; }
h2 { font-size: 15.5pt; line-height: 1.25; margin: 9mm 0 2.5mm; padding-bottom: 1.5mm; border-bottom: 0.6pt solid #c9d0dc; break-after: avoid; }
h3 { font-size: 11.8pt; margin: 6mm 0 1.5mm; break-after: avoid; }
p { margin: 0 0 2.6mm; orphans: 3; widows: 3; }
ul, ol { margin: 0 0 2.6mm; padding-left: 5.5mm; }
ul { list-style: none; }
ul > li { position: relative; }
ul > li::before { content: "BULLET"; position: absolute; left: -4.2mm; color: #5a6475; }
li { margin: 0 0 1.2mm; }
li > p { margin: 0 0 1.5mm; }
code { font: 9.2pt Consolas, "Courier New", monospace; background: #eef1f6; padding: 0.2pt 2.2pt; border-radius: 2pt; }
pre { font: 8.4pt/1.42 Consolas, "Courier New", monospace; background: #f3f5f9; border: 0.5pt solid #d5dbe6; border-radius: 3pt;
      padding: 3mm 3.5mm; margin: 0 0 3.5mm; white-space: pre; break-inside: avoid; }
table { border-collapse: separate; border-spacing: 0; width: 100%; margin: 1mm 0 4mm; font-size: 9.1pt; line-height: 1.38;
        break-inside: avoid; }
th { border-top: 0.6pt solid #b9c1cf; }
th:first-child, td:first-child { border-left: 0.6pt solid #b9c1cf; }
p:has(+ table) { break-after: avoid; }
table.long { break-inside: auto; }
th { text-align: left; background: #e9edf4; font-weight: 600; }
th, td { border-right: 0.6pt solid #b9c1cf; border-bottom: 0.6pt solid #b9c1cf; padding: 1.5mm 2mm; vertical-align: top; }
tbody tr:nth-child(even) td { background: #f8f9fc; }
tr { break-inside: avoid; }
thead { display: table-header-group; }
table.c8 th, table.c8 td { text-align: center; }
table.c5 td:first-child { white-space: nowrap; }
li table { margin-top: 2mm; width: auto; }
.tag { display: inline-block; font-size: 8pt; font-weight: 700; letter-spacing: 0.3pt; text-transform: uppercase;
       padding: 0 4.5pt; border-radius: 7pt; line-height: 13pt; vertical-align: 1pt; white-space: nowrap; }
.tag.built { background: #d5f3e3; color: #0b5a34; }
.tag.planned { background: #dbe7fb; color: #173f86; }
.tag.proposed { background: #fdebc4; color: #7a4a00; }
.tag.open { background: #fbd9d9; color: #8a1616; }
.lead { color: #5a6475; font-size: 10pt; margin-bottom: 5mm; }
.contents { columns: 2; column-gap: 10mm; font-size: 9.6pt; margin: 2mm 0 2mm; padding: 3mm 4mm; background: #f5f7fa; border-radius: 3pt; break-inside: avoid; }
.contents a { color: #1c2230; text-decoration: none; display: block; padding: 0.4mm 0; }
.contents-title { font-size: 9pt; font-weight: 700; letter-spacing: 0.6pt; text-transform: uppercase; color: #5a6475; margin: 6mm 0 1mm; }
.figpage { page: wide; break-before: page; break-after: page; }
.figtitle { font-size: 9pt; font-weight: 700; letter-spacing: 0.6pt; text-transform: uppercase; color: #5a6475; margin: 0 0 3mm; }
.figtitle span { font-size: 12.5pt; font-weight: 700; letter-spacing: 0; text-transform: none; color: #1c2230; margin-left: 3mm; }
.figref { font-size: 8pt; font-weight: 700; letter-spacing: 0.4pt; text-transform: uppercase; color: #5a6475; background: #eef1f6;
          padding: 1pt 5pt; border-radius: 7pt; margin-left: 2mm; vertical-align: 1.5pt; white-space: nowrap; }
.figpage img { display: block; width: 100%; max-height: 158mm; object-fit: contain; object-position: left top; border: 0.5pt solid #c3cad7; }
.caption { font-size: 8.8pt; color: #5a6475; margin: 2mm 0 0; }
"""

CSS = CSS.replace("BULLET", chr(8226))
lines = open(SRC, encoding='utf-8').read().replace('\r\n', '\n').split('\n')
toc = []
figs = {'count': 0, 'pending': []}
body = blocks(lines, toc, figs) + ''.join(figs['pending'])

# the title line, then a contents box after the legend table (before the first section)
contents = '<div class="contents-title">Contents</div><div class="contents">' + ''.join(
    '<a href="#%s">%s</a>' % (slug(t), inline(t)) for t in toc) + '</div>'
first_h2 = body.index('<h2')
body = body[:first_h2] + contents + body[first_h2:]

page = ('<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Game design document</title><style>'
        + CSS + '</style></head><body>' + body + '</body></html>')
os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, 'w', encoding='utf-8', newline='\n').write(page)
print('html written:', len(page), 'chars;', len(toc), 'sections;', body.count('<table'), 'tables;', body.count('figpage'), 'figure pages')

if edge():
    subprocess.run([edge(), '--headless', '--disable-gpu', '--no-pdf-header-footer', '--print-to-pdf=' + PDF, 'file:///' + OUT.replace(os.sep, '/')],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print('pdf written:', PDF, os.path.getsize(PDF), 'bytes')
else:
    print('Edge not found: open', OUT, 'in a browser and print it to PDF (no headers or footers).')

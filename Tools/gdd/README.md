# Tools/gdd: the pictures and the PDF of the game design document

`Docs/GDD.md` is the source. Everything here is made from it, or for it. Python 3 and
Microsoft Edge are needed (Edge prints the PDF and turns the SVG pictures into PNG);
`routetool` also needs the .NET SDK that `Tools/dotnet` uses.

| To do this | Run this (from anywhere) |
|---|---|
| Make `Docs/GDD.pdf` again after `GDD.md` changed | `python Tools/gdd/pdf.py` |
| Make the three pictures in `Docs/gdd/` again | `python Tools/gdd/pictures.py` |
| Work out the routes again after the sample board changed | see below |

## The files

- `board.py`: the sample board of the pictures: rocks, walls and towers, tile by tile.
- `routes.txt`: the walkers' and the sappers' route on that board. **Not drawn by hand:**
  it is the output of `routetool`, which runs the game's own flow field (`Thesis.Sim`).
  This is why the GDD may say that the routes in its pictures are what the game would do.
- `pictures.py`: draws `routes.svg`, `screen-build.svg` and `screen-wave.svg` from the two
  files above. The flyers' line is a straight line and needs no tool.
- `pdf.py`: turns the Markdown that `GDD.md` uses into a page for printing. It knows only
  that Markdown: headings, paragraphs, lists, tables, one fenced block, images, bold,
  italics and code spans. A heading that is followed directly by an image becomes a
  numbered figure on a landscape page of its own. The line `<!-- pdf: figures here -->` in
  `GDD.md` says where the waiting figure pages go (pick a place where a page of text is
  full anyway, or the page before the figure stays half empty).
- `out/`: scratch files (not in git).

## After changing the sample board

```
python Tools/gdd/board.py
dotnet run -c Release --project Tools/gdd/routetool -- Tools/gdd/out/board.txt Tools/gdd/out/towers.txt Tools/gdd/routes.txt
python Tools/gdd/pictures.py
python Tools/gdd/pdf.py
```

`routetool` prints the board with the walkers' route on it. Read it before drawing: the
route is the cheapest one, which is not always the one that was meant. While the first
board was being made, the walkers went round the whole map through a gap in a corner,
and later chewed through a thin wall because one baffle had made the detour too long.
`pictures.py` stops if the routes are not the kind it draws (walkers that chew nothing,
sappers that go through exactly two wall tiles), and the numbers in the picture's text
(71, 50 and 44 tiles; 9, 5 and 1 towers) are written out by hand, so check them against
the tool's output when the board changes.

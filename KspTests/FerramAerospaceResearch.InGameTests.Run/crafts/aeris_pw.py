"""The Aeris 4A with its main wings (structuralWing, deltaWing) as B9 procedural panels of the planform FAR gives the
stock parts: same place, rotation, symmetry and children.
    python aeris_pw.py STOCK.craft B9TEMPLATE.craft OUT.craft
STOCK the game's Ships/SPH/Aeris 4A.craft; B9TEMPLATE any craft with a B9 TypeA panel (its WingProcedural and FAR
modules are the template; the rest of its fields are set here).
FAR's stock numbers (FerramAerospaceResearch.cfg): MAC 2.25, taper 0.091 for both; b_2 1.875 sweep 44.99 (structural),
b_2 3.75 sweep 26.57 (delta). B9 TypeA with zero-width edges: chord = base width, mid-chord sweep = atan(offsetTip/length),
root mid-chord at the part origin (FAR's stock wings have no rootMidChordOffsetFromOrig)."""
import math, re, sys

stock_path, template_path, out_path = sys.argv[1:4]
B9 = "B9.Aero.Wing.Procedural.TypeA"
PLANFORM = {"structuralWing": (1.875, 44.99), "deltaWing": (3.75, 26.57)}
MAC, TAPER = 2.25, 0.091


def blocks(text):
    """(start, end) of each top-level PART { ... } block."""
    out, i = [], 0
    while True:
        m = re.search(r"(?m)^PART\r?\n\{", text[i:])
        if not m:
            return out
        s = i + m.start()
        depth, j = 0, s + m.end() - m.start() - 1
        while True:
            c = text[j]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    out.append((s, j + 1))
                    break
            j += 1
        i = j + 1


def top_lines(block):
    """The block's own key = value lines (depth 1), in order."""
    lines, depth = [], 0
    for ln in block.splitlines():
        t = ln.strip()
        if t == "{":
            depth += 1
            continue
        if t == "}":
            depth -= 1
            continue
        if depth == 1 and "=" in t:
            lines.append(t)
    return lines


def sub_nodes(block, keep):
    """The block's child nodes (MODULE etc.) whose name is in keep, verbatim."""
    out, lines, i, depth = [], block.splitlines(), 0, 0
    while i < len(lines):
        t = lines[i].strip()
        if t == "{":
            depth += 1
        elif t == "}":
            depth -= 1
        elif depth == 1 and t in ("MODULE", "RESOURCE") and lines[i + 1].strip() == "{":
            start, d, j = i, 0, i + 1
            while True:
                tj = lines[j].strip()
                d += tj == "{"
                d -= tj == "}"
                if d == 0:
                    break
                j += 1
            node = "\n".join(lines[start:j + 1])
            name = re.search(r"name = (\S+)", node).group(1)
            if t == "MODULE" and name in keep:
                out.append(node)
            i = j
        i += 1
    return out


def flip_y(line):
    """rot = x,y,z,w turned 180 deg about the part's own y axis: q * (0, 1, 0, 0)."""
    x, y, z, w = (float(v) for v in line.split("=", 1)[1].split(","))
    # Hamilton product (w, x, y, z) of q and j = (0, 0, 1, 0): (-y, -z, w, x)
    nw, nx, ny, nz = -y, -z, w, x
    return "rot = %.9g,%.9g,%.9g,%.9g" % (nx, ny, nz, nw)


stock = open(stock_path, encoding="utf-8-sig").read()
tmpl_text = open(template_path, encoding="utf-8-sig").read()
tmpl = next(tmpl_text[s:e] for s, e in blocks(tmpl_text) if re.search(r"part = " + re.escape(B9) + "_", tmpl_text[s:e]))
modules = sub_nodes(tmpl, {"WingProcedural", "FARWingAerodynamicModel", "GeometryPartModule", "FARAeroPartModule"})

renames, pieces, last = {}, [], 0
for s, e in blocks(stock):
    block = stock[s:e]
    m = re.search(r"part = (\S+)_(\d+)\s", block)
    kind, pid = m.group(1), m.group(2)
    if kind not in PLANFORM:
        continue
    semispan, sweep = PLANFORM[kind]
    root = 2 * MAC / (1 + TAPER)
    values = {"sharedBaseLength": semispan, "sharedBaseWidthRoot": root, "sharedBaseWidthTip": TAPER * root,
              "sharedBaseOffsetRoot": 0, "sharedBaseOffsetTip": semispan * math.tan(math.radians(sweep)),
              "sharedBaseThicknessRoot": 0.12, "sharedBaseThicknessTip": 0.04,
              "sharedEdgeWidthLeadingRoot": 0, "sharedEdgeWidthLeadingTip": 0,
              "sharedEdgeWidthTrailingRoot": 0, "sharedEdgeWidthTrailingTip": 0}
    mods = []
    for node in modules:
        if "name = WingProcedural" in node:
            for k, v in values.items():
                node, n = re.subn(r"(?m)^(\s*%s = ).*$" % k, lambda mm: mm.group(1) + ("%.6g" % v), node)
                assert n == 1, k
        mods.append(node)
    keys = [ln for ln in top_lines(block) if not ln.startswith("part =")]
    # B9 builds its panel along the other side of the part's span axis from the stock model: turned 180 deg about the
    # chord (local y) it reaches outboard as the stock wing does, its leading edge still forward
    keys = [flip_y(ln) if ln.startswith("rot =") else ln for ln in keys]
    new = "PART\n{\n\tpart = %s_%s\n" % (B9, pid) + "".join("\t%s\n" % ln for ln in keys) + "\n".join(mods) + "\n}"
    renames["%s_%s" % (kind, pid)] = "%s_%s" % (B9, pid)
    pieces.append((s, e, new))

out = ""
for s, e, new in pieces:
    out += stock[last:s] + new
    last = e
out += stock[last:]
for old, new in renames.items():
    out = re.sub(r"\b%s\b" % re.escape(old), new, out)
out = re.sub(r"(?m)^ship = .*$", "ship = Aeris 4A PW", out, count=1)
out = re.sub(r"(?m)^description = .*$", "description = The Aeris 4A with its main wings as B9 procedural panels of the stock parts' planform", out, count=1)
open(out_path, "w", encoding="utf-8", newline="\n").write(out)
print("replaced", ", ".join(sorted(renames)))

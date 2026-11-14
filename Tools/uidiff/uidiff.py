"""UI pixel diff: our build vs New Era reference screenshots.

For every case in cases.json:
  ours = Builds/ContraCity/uitest/compare_cases/<name>.png   (made by  ContraCity.exe -uicompare)
  ref  = W:/Contra City Extract/Shop_Screens/<ref>
  crop the case area, blank the masks (dynamic content: chat lines, fighter pose, posters, other players),
  mark pixels whose colour differs by more than THRESH, group them into regions on a CELL grid,
  and for every region search a local shift (±SHIFT px) to tell "moved by dx,dy" from "different content".

Output (Builds/ContraCity/uitest/uidiff/):
  <name>.png   ours | New Era | diff (red = differs, boxes numbered)
  <name>.gif   blink ours / New Era (best way to see small shifts)
  report.md    per case: % differing pixels, regions with their verdict
  summary.png  all cases, worst first

Usage: python -I uidiff.py [case-name ...]
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
OURS = os.path.join(ROOT, 'Builds', 'ContraCity', 'uitest', 'compare_cases')
REFS = 'W:/Contra City Extract/Shop_Screens'
OUT = os.path.join(ROOT, 'Builds', 'ContraCity', 'uitest', 'uidiff')
THRESH = 40        # max channel difference treated as "same" (antialiasing noise)
CELL = 6           # grid cell (px)
CELL_RATIO = 0.12  # a cell differs when this share of its pixels differs
SHIFT = 8          # local shift search (px)
FONT = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'gui', 'fonts', 'tahomabd.ttf')


def load(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.int16)


def regions(flag):
    """4-connected components of a boolean cell grid -> list of (y0, x0, y1, x1) in cells."""
    h, w = flag.shape
    seen = np.zeros_like(flag, dtype=bool)
    out = []
    for y in range(h):
        for x in range(w):
            if not flag[y, x] or seen[y, x]:
                continue
            stack = [(y, x)]; seen[y, x] = True
            y0 = y1 = y; x0 = x1 = x; n = 0
            while stack:
                cy, cx = stack.pop(); n += 1
                y0, y1, x0, x1 = min(y0, cy), max(y1, cy), min(x0, cx), max(x1, cx)
                for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                    if 0 <= ny < h and 0 <= nx < w and flag[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True; stack.append((ny, nx))
            out.append((y0, x0, y1 + 1, x1 + 1, n))
    return out


def best_shift(a, b, box, valid):
    """Shift of our region (a) that best matches the reference (b): returns (dx, dy, err0, err)."""
    y0, x0, y1, x1 = box
    H, W = b.shape[:2]
    ref = b[y0:y1, x0:x1]; vm = valid[y0:y1, x0:x1]
    if vm.sum() == 0:
        return 0, 0, 0.0, 0.0
    def err(dx, dy):
        ys, xs = y0 + dy, x0 + dx
        if ys < 0 or xs < 0 or ys + (y1 - y0) > H or xs + (x1 - x0) > W:
            return 1e9
        d = np.abs(a[ys:ys + (y1 - y0), xs:xs + (x1 - x0)] - ref).max(axis=2)
        return float((d[vm] > THRESH).mean())
    e0 = err(0, 0); best = (0, 0, e0)
    for dy in range(-SHIFT, SHIFT + 1):
        for dx in range(-SHIFT, SHIFT + 1):
            e = err(dx, dy)
            if e < best[2] - 1e-9 or (abs(e - best[2]) < 1e-9 and abs(dx) + abs(dy) < abs(best[0]) + abs(best[1])):
                best = (dx, dy, e)
    return best[0], best[1], e0, best[2]


def run_case(c, font):
    name = c['name']
    po, pr = os.path.join(OURS, c.get('ours', name) + '.png'), os.path.join(REFS, c['ref'])
    if not os.path.exists(po):
        return dict(name=name, missing='ours'), None
    if not os.path.exists(pr):
        return dict(name=name, missing='ref'), None
    x0, y0, x1, y1 = c['crop']
    a = load(po)[y0:y1, x0:x1].copy(); b = load(pr)[y0:y1, x0:x1].copy()
    valid = np.ones(b.shape[:2], dtype=bool)
    for m in c.get('masks', []):
        mx0, my0, mx1, my1 = m[0] - x0, m[1] - y0, m[2] - x0, m[3] - y0
        valid[max(0, my0):max(0, my1), max(0, mx0):max(0, mx1)] = False
    d = np.abs(a - b).max(axis=2)
    diff = (d > THRESH) & valid
    H, W = diff.shape
    gh, gw = (H + CELL - 1) // CELL, (W + CELL - 1) // CELL
    pad = np.zeros((gh * CELL, gw * CELL), dtype=bool); pad[:H, :W] = diff
    cell = pad.reshape(gh, CELL, gw, CELL).mean(axis=(1, 3)) > CELL_RATIO
    regs = [r for r in regions(cell) if r[4] >= 2]
    regs.sort(key=lambda r: -r[4])
    items = []
    for i, (cy0, cx0, cy1, cx1, n) in enumerate(regs[:25]):
        box = (max(0, cy0 * CELL - 4), max(0, cx0 * CELL - 4), min(H, cy1 * CELL + 4), min(W, cx1 * CELL + 4))
        dx, dy, e0, e = best_shift(a, b, box, valid)
        if (dx or dy) and e < e0 * 0.5:
            verdict = f'сдвиг: наше нужно сдвинуть на ({-dx:+d}, {-dy:+d}) px' + ('' if e < 0.05 else f', после сдвига ещё {e*100:.0f}% отличий')
        else:
            verdict = 'другое содержимое (шрифт / текст / картинка / цвет)'
        items.append(dict(n=i + 1, x=box[1] + x0, y=box[0] + y0, w=box[3] - box[1], h=box[2] - box[0], verdict=verdict, err=e0))
    score = float(diff.sum()) / max(1, valid.sum()) * 100
    # images
    ia, ib = Image.fromarray(a.astype(np.uint8)), Image.fromarray(b.astype(np.uint8))
    heat = (b.astype(np.float32) * 0.35).astype(np.uint8)
    heat[diff] = [255, 40, 40]
    heat[~valid] = (heat[~valid] * 0.3 + np.array([0, 60, 120]) * 0.7).astype(np.uint8)
    ih = Image.fromarray(heat)
    dr = ImageDraw.Draw(ih)
    for it in items:
        bx, by = it['x'] - x0, it['y'] - y0
        dr.rectangle([bx, by, bx + it['w'], by + it['h']], outline=(255, 220, 0), width=2)
        dr.text((bx + 3, by + 2), str(it['n']), fill=(255, 255, 0), font=font)
    sheet = Image.new('RGB', (W * 3 + 20, H + 26), (30, 30, 30))
    sd = ImageDraw.Draw(sheet)
    for k, (im, t) in enumerate(((ia, 'наше'), (ib, 'Новая Эра'), (ih, f'отличия {score:.1f}%'))):
        sheet.paste(im, (k * (W + 10), 26)); sd.text((k * (W + 10) + 4, 4), t, fill=(255, 200, 60), font=font)
    sheet.save(os.path.join(OUT, name + '.png'))
    ia.save(os.path.join(OUT, name + '.gif'), save_all=True, append_images=[ib], duration=700, loop=0)
    return dict(name=name, ref=c['ref'], score=score, items=items, note=c.get('note', '')), sheet


def main():
    os.makedirs(OUT, exist_ok=True)
    cases = json.load(open(os.path.join(HERE, 'cases.json'), encoding='utf-8'))
    only = set(sys.argv[1:])
    font = ImageFont.truetype(FONT, 14) if os.path.exists(FONT) else ImageFont.load_default()
    results = []
    for c in cases:
        if only and c['name'] not in only:
            continue
        r, sheet = run_case(c, font)
        results.append((r, sheet))
        print(f"{r['name']:28s} " + (f"{r['score']:5.1f}%  regions {len(r['items'])}" if 'score' in r else 'MISSING ' + r['missing']))
    ok = [r for r in results if 'score' in r[0]]
    ok.sort(key=lambda r: -r[0]['score'])
    lines = ['# UI diff: наше vs Новая Эра', '', f'Порог цвета {THRESH}, ячейка {CELL}px. Синим закрашены маски (динамика), красным — отличия.', '']
    for r, _ in ok:
        lines.append(f"## {r['name']} — {r['score']:.1f}% отличий  (эталон {r['ref']})")
        if r['note']:
            lines.append(r['note'])
        for it in r['items']:
            lines.append(f"- {it['n']}. x{it['x']} y{it['y']} {it['w']}x{it['h']}: {it['verdict']}")
        lines.append('')
    for r, _ in results:
        if 'missing' in r:
            lines.append(f"- {r['name']}: нет файла ({r['missing']})")
    open(os.path.join(OUT, 'report.md'), 'w', encoding='utf-8').write('\n'.join(lines))
    # summary sheet: scaled rows, worst first
    rows = [s for _, s in ok if s is not None]
    if rows:
        wmax = 1800
        scaled = [s.resize((wmax, int(s.height * wmax / s.width))) if s.width > wmax else s for s in rows]
        tot = sum(s.height for s in scaled) + 6 * len(scaled)
        summ = Image.new('RGB', (wmax, tot), (15, 15, 15)); y = 0
        for s in scaled:
            summ.paste(s, (0, y)); y += s.height + 6
        summ.save(os.path.join(OUT, 'summary.png'))


main()

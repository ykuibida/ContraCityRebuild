# Relaunch (contra.city devlog) -> New Era scale. All relaunch numbers are BODY damage near/mid/far at one moment:
# T1 = before patch 1.0.182 (11.10.2026), the "было" side of «Баланс подробно 1.0.182» (devlog lines 391-425),
# or, for a weapon not changed on 11.10, the "было" side of 1.0.183 (it then had that value at T1 too).
import json, statistics as st
S = 'C:/Users/Fallen/AppData/Local/Temp/claude/9b063d734a9a821c/074943bf-c6a6-42d7-8380-5337ef12e2c7/scratchpad/arsenal/'
ref = {r['sname']: r for r in json.load(open(S + 'data.json', encoding='utf-8'))['ref']}
def ne(sn):  # NE body damage per range = mean of min/max
    d = [float(x) for x in ref[sn]['dmg']]
    return [(d[0] + d[1]) / 2, (d[2] + d[3]) / 2, (d[4] + d[5]) / 2]
# shared weapons, relaunch T1 body damage (None = not published or a broken value), class, devlog line
shared = [
    ('OHCA_Crowbar', 'Лом', 'melee', [42, None, None], 397),          # melee: only the near value is a real hit
    ('HG_SIGSauerP226_B', 'Дружинник', 'pistol', [31, 30, 19], 401),
    ('HG_Glock_S', 'Политрук', 'pistol', [19, 15, 8], 402),
    ('HG_Desert', 'Сокол', 'pistol', [67, 62.5, 43], 424),
    ('HG_GlockB01_S', 'Спекулянт', 'pistol', [9, 8, 4], 423),
    ('MG_AK47B06', 'Засад', 'rifle', [26.5, 25, 14.5], 418),
    ('MG_AK47B07', 'Смертобой', 'rifle', [20, None, None], 420),      # mid/far 70 / 150: broken values in the relaunch
    ('MG_AK47B08', 'Звездочет', 'rifle', [20, None, None], 425),      # same
    ('SR_Arctic', 'Писец', 'sniper', [55, 102, None], 415),
    ('SR_M110_B', 'Клык', 'sniper', [None, 37.5, 43.5], 407),
    ('RL_RPG7B02', 'Троллебузина', 'explosive', [70, 70, 70], 419),
]
ratios, rows = {}, []
for sn, name, cls, rl, line in shared:
    n = ne(sn)
    r = [round(n[i] / rl[i], 3) if rl[i] else None for i in range(3)]
    if cls == 'melee': r = [r[0], None, None]
    if cls == 'explosive': r = [r[0], None, None]
    rows.append({'sname': sn, 'name': name, 'cls': cls, 'relaunch': rl, 'ne': [round(x, 1) for x in n], 'ratio': r, 'line': line})
    ratios.setdefault(cls, []).extend([x for x in r if x])
allr = [x for v in ratios.values() for x in v]
k = {c: round(st.median(v), 3) for c, v in ratios.items()}
k['all'] = round(st.median(allr), 3)
# rate of fire: NE interval (ms) / relaunch 60000/rpm at T1
rate_pairs = [('HG_Desert', 107), ('SR_Arctic', 28), ('MG_AK47B06', 545), ('HG_GlockB01_S', 429), ('HG_SIGSauerP226_B', 222), ('HG_Glock_S', 261), ('OHCA_Crowbar', 118), ('MG_AK47B07', 462)]
rr = [round(float(ref[s]['as']) / (60000 / rpm), 3) for s, rpm in rate_pairs]
k['rate'] = round(st.median(rr), 3)
print('ratios per class', {c: sorted(v) for c, v in ratios.items()})
print('factors', k, 'rate ratios', sorted(rr))
json.dump({'shared': rows, 'factors': k, 'rateRatios': dict(zip([p[0] for p in rate_pairs], rr))}, open(S + 'scale.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

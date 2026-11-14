# Proposals (NOT approved) for missing weapons: relaunch body damage at T1 (before 11.10.2026) x the class factor from scale.py;
# rate: 60000 / rpm x the rate factor. NE convention: guns/melee min = max - 2, explosives and machine guns min = max.
import json
S = 'C:/Users/Fallen/AppData/Local/Temp/claude/9b063d734a9a821c/074943bf-c6a6-42d7-8380-5337ef12e2c7/scratchpad/arsenal/'
F = json.load(open(S + 'scale.json', encoding='utf-8'))['factors']
CLS = {'pistol': 'pistol', 'rifle': 'rifle', 'sniper': 'sniper', 'mg': 'all', 'melee': 'melee', 'explosive': 'explosive'}
NAME = {'pistol': 'пистолеты', 'rifle': 'автоматы', 'sniper': 'снайперские', 'all': 'все классы (у пулемётов нет общих стволов)', 'melee': 'ближний бой («Лом»)', 'explosive': 'взрывное («Троллебузина»)'}
# sname: (class, T1 body near/mid/far or None, rpm at T1 or None, crit % at T1 or None, effects, effect dmg, ticks, devlog lines, extra note)
src = {
 'HG_TT': ('pistol', [12, 10, 8], None, 0, [], None, None, '398', ''),
 'HG_Walther_R': ('pistol', [29, 20, 9], 146, None, [], None, None, '400', ''),
 'MG_AK103': ('rifle', [None, 7, 5], 387, 0, [], None, None, '403', 'урон вблизи в патч-ноутах не публиковался'),
 'MG_M16': ('rifle', [None, None, 4], None, 2, [], None, None, '399', 'опубликован только урон вдали'),
 'MG_M4': ('rifle', [15, 12, 6], None, 4, [], None, None, '404', ''),
 'MG_M4_O': ('rifle', [15, 13, 7], 462, 6, [], None, None, '405', ''),
 'MG_UMP45': ('rifle', [15, 12, 4], 462, 7, [], None, None, '417', ''),
 'SR_HK417_D': ('sniper', [28, 65, None], 80, None, [], None, None, '406', 'урон в тело вдали не публиковался'),
 'GG_M249': ('mg', [20, 20, 13], 333, 0, [], None, None, '408, 152', ''),
 'GG_M134B02': ('mg', [15, 15, 13], 600, 0, [], None, None, '421, 161', ''),
 'GG_M134B03': ('mg', [16, 15, 14], 600, None, [], None, None, '422, 162', ''),
 'GG_N2': ('mg', [7, 6, 5], 375, 0, [], None, None, '149', '11.10 не менялся, поэтому это и значение до 11.10'),
 'OHCA_Torch_F': ('melee', [42, None, None], 119, 5, ['fire'], None, None, '409, 154', 'огонь по описанию; тики не публиковались'),
 'THCA_Katana_B': ('melee', [64, None, None], None, None, ['blood'], 4, 5, '410', 'кровотечение 5 тиков по 4'),
 'GL_Milkor_A': ('explosive', [70, 70, 70], None, 8, ['poison', 'slow'], 6, 5, '412-413', 'яд 5 тиков по 6, замедление 4800 мс, пауза между выстрелами 851 мс'),
 'GL_SnowLauncher': ('explosive', [62, 62, 62], 81, 0, ['frost'], None, None, '414', 'заморозка по описанию'),
 'RL_RPG7': ('explosive', [60, 60, 60], None, None, [], None, None, '150', '11.10 не менялся; сравнение с «Троллебузиной» до 11.10'),
 'SR_Steyr': ('sniper', [None, None, None], 27, None, [], None, None, '416', 'урон не публиковался, только скорострельность'),
 'SR_SteyrB01': ('sniper', [None, None, None], 27, None, [], None, None, '164', 'урон не публиковался, только скорострельность'),
}
out = {}
for sn, (c, dmg, rpm, crit, eff, ed, et, lines, extra) in src.items():
    fk = CLS[c]; f = F[fk]
    p = {}
    equal = c in ('mg', 'explosive')
    for i, key in enumerate(['near', 'mid', 'far']):
        if dmg[i] is None: continue
        mx = round(dmg[i] * f)
        p[key + 'Max'] = mx; p[key + 'Min'] = mx if equal else max(1, mx - 2)
    if c == 'melee' and 'nearMax' in p:
        for key in ('mid', 'far'): p[key + 'Max'] = p['nearMax']; p[key + 'Min'] = p['nearMin']
    if rpm: p['interval'] = round(60000 / rpm * F['rate'])
    if crit: p['crit'] = crit
    if ed: p['effDmg'] = ed
    if et: p['effTicks'] = et
    basis = f"Перезапуск до 11.10 (урон в тело {'/'.join('–' if x is None else str(x) for x in dmg)}{', ' + str(rpm) + ' выстр./мин' if rpm else ''}) × {f} ({NAME[fk]}){'; интервал × ' + str(F['rate']) if rpm else ''}. Девлог, строки {lines}." + (f" {extra}." if extra else '')
    out[sn] = {'fields': p, 'effects': eff, 'basis': basis}
# from the owner's words (12.11.2026), compared with the strongest NE weapons
gad = out['GL_Milkor_A']['fields']['nearMax']
gr = round(gad * 0.9)
out['GL_Milkor'] = {'fields': {'nearMin': gr, 'nearMax': gr, 'midMin': gr, 'midMax': gr, 'farMin': gr, 'farMax': gr}, 'effects': ['slow'],
    'basis': f"Со слов игрока: как «Гадюка» или чуть меньше, без яда, с замедлением. Взято 90 % от предложения для «Гадюки» ({gad})."}
out['MG_UMP45VKKS_O'] = {'fields': {'nearMin': 31, 'nearMax': 33, 'midMin': 24, 'midMax': 26, 'farMin': 16, 'farMax': 18, 'interval': 105, 'ammo': 30, 'ammoTot': 90, 'crit': 25},
    'effects': [], 'basis': "Со слов игрока: «Провокатор» (магазин 30/90, крит 25 %, приближение на ПКМ), но сильнее любого автомата. Урон вблизи и на средней выше лучших автоматов НЭ («Смертобой» 29–31 / 23–25, «Рейдер» 28–30 / 23–25), вдали ниже «Смертобоя» (19–21), интервал как у самого быстрого автомата НЭ («Барс», 105 мс)."}
json.dump(out, open(S + 'proposals.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for sn, v in out.items(): print(sn, v['fields'], v['effects'])

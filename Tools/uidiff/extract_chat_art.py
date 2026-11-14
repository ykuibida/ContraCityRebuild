"""Extract the Contra Chat art (base64 PNGs embedded in New Era 嘑.EnsureQuestArt / EnsureCachedStyles)
into Assets/Game/Resources/gui/chat/<name>[_hover|_active].png plus chat_art.json (borders)."""
import base64, json, os, re

SRC = 'W:/Contra City Extract/NE_Scripts_Decrypted/嘑.cs'
DST = 'W:/ContraCityRebuild/Assets/Game/Resources/gui/chat'
os.makedirs(DST, exist_ok=True)
t = open(SRC, encoding='utf-8').read()
i = t.index('private static void EnsureQuestArt()')
j = t.index('\n\t}\n', i)
body = t[i:j]
meta = {}
for m in re.finditer(r'(\w+) = ArtPanel\("([A-Za-z0-9+/=]+)", (\d+)\);', body):
    name, b64, border = m.group(1), m.group(2), int(m.group(3))
    open(os.path.join(DST, name + '.png'), 'wb').write(base64.b64decode(b64))
    meta[name] = {'kind': 'panel', 'border': border}
for m in re.finditer(r'(\w+) = ArtButton\(a, (\d+), "([A-Za-z0-9+/=]+)", "([A-Za-z0-9+/=]+)", "([A-Za-z0-9+/=]+)", (\w+), ([\w.]+)\);', body):
    name, size = m.group(1), int(m.group(2))
    for suffix, b64 in (('', m.group(3)), ('_hover', m.group(4)), ('_active', m.group(5))):
        open(os.path.join(DST, name + suffix + '.png'), 'wb').write(base64.b64decode(b64))
    meta[name] = {'kind': 'button', 'fontSize': size, 'text': m.group(6), 'textHover': m.group(7), 'border': 7}
json.dump(meta, open(os.path.join(DST, 'chat_art.json'), 'w'), indent=1)
print(len(meta), sorted(meta))

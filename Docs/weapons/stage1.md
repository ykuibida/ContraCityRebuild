# Weapons stage 1: models, slots, switching, HUD bar

Base: New Era (`W:/Contra City Extract/NE_Scripts_Decrypted/`); Penta 2016 is the reference.

## What is ported, and from where

| Piece | Our file | Source |
|---|---|---|
| First-person models | `BuildGame.CreateLobbyScene` keeps `Fighter_locomotion/Fighter/MainCamera/Weapon` (inactive) as `WeaponTemplate`; `ShotController.Attach` clones it under the eye camera, keeping the prefab local pose | prefab `gamebundle/prefabs/soldier/Fighter_locomotion.prefab` |
| Weapon camera | `WeaponCamera`: clear Depth, culling layer 8, FOV 60, depth 1, near 0.1, far 300; eye camera depth 0, culling mask 4293637887, far ≥ 1000; two weapon fill lights | prefab MainCamera / WeaponCamera; NE `嗃.cs:39-60, 149-175` |
| Marker classes (original GUIDs) | `WeaponLook`, `ArmClothModel`, `HandTextureLoader`, `RuntimeTextureSwitch`, `Flamer` | NE same-named files |
| Texture loader | `RuntimeTextureLoader`: full ForceLoad (shaders per texture, Resources `Textures/<Folder><id>`, silencer opaque) | NE `RuntimeTextureLoader.cs:19-416` |
| Map textures | `Resources/Textures/Map/` = New Era `Resources/Textures/map` (137 files) | NE Resources |
| Arm clothes | `ArmClothModel.Sync` with the worn shirt / glove | NE `ArmClothModel.cs:76-130`, `PlayerCustomisator.cs:255, 303` |
| Hand texture | `Dresser.HandTextureID` | NE `PlayerCustomisator.SetHandTextureID :308-322`, `CombatPlayer.cs:1561-1578` |
| Battle weapon data | `BattleWeapons.Build` (weapon-info hashtable per slot) | NE `PlayerManager.cs:1666-1880, 2057-2722` |
| Gym effects on weapons | `BattleStats.GetNewValue` / `GetRawValue` (Барахольщик 7, Точность по ГОСТу 10, Шустряк 5) | NE `嚱.cs:251-284` |
| Weapon runtime | `CombatWeapon` + `WeaponType` + `WeaponMode` (whole class) | NE `噔.cs`, `啁.cs`, `啄.cs` |
| Slots and switching | `ShotController`: InitWeapon, SetWeapon, OnChangeWeapon, Change, QuickChange, SetNext / SetPrevWeapon, IsWeaponKeyDown, Hide / ShowWeapon(s) | NE `ShotController.cs:288-378, 1024-1182, 1194-1300, 1718-1758, 2609-2671`, `WeaponController.cs:172-310` |
| Container animations | `ChangeWeaponAnimation` (WeaponChangeAnimation, then ShotgunRun / ShotgunIdle queued), the Run / Idle sway when IsRunning changes, `Reset` on the model | NE `ActorAnimator.cs:861-879, 1087-1109, 1471-1500, 1587-1601` |
| HUD | bar refreshed every frame while the HUD is active; ammo: melee 0/0/0, else loaded / max / reserve; crosshair `crosshair_0<type>` | NE `ShotController.cs:1246-1250`, `GameHUD.cs:545-556, 897-981`, `ShotController.SetCrossHair :733-794` |
| Taunts | shot off, zoom off, `Reset` on the weapon at start and end; the weapon camera follows the eye camera | NE `ShotController.cs:1558-1606, 1668-1716`, `PlayerManager.cs:8126-8143` |

## First-person arms (checked 11.11.2026, test cases fp_*)

- Penta had no first-person sleeves: only the worn glove's texture on the hands mesh (P `PlayerCustomisator.cs:297-310`, per-renderer material).
- New Era added `ArmClothModel`: every first-person model has its own arm meshes "Shirts_<name>" / "Gloves_<name>". A worn item whose name
  token [1] matches shows its mesh and hides the base (`Shirts_torso01` bare arm, `Gloves_hands01` bare hands).
- All 32 shop shirts have a sleeve mesh (Тельник is sleeveless, so the arm stays bare). 7 of 18 gloves have their own mesh (stalker, business,
  santa, santa2, prison, mummy2, clock02); the other 11 (Кожчатки, Байкер, Призрак, Мститель …) put their body texture onto the base hands
  (SetHandTextureID). The textures New Era excludes from the hands (Thanos / santa / business / stalker / mummy) all belong to gloves with their own mesh.
- The default outfit (`Shirts_shirtB06`, `Gloves_hand01`) has no first-person meshes and an empty glove texture: bare arm and bare hand,
  as in the New Era screenshots (`ne_ui2/113`, `115`), where the third-person corpse wears brown sleeves and dark hands.
- Our weapon container lives for the session, so `ShotController.Attach` puts the prefab hand texture back before applying the glove one;
  otherwise a life without gloves kept the last glove texture (New Era loaded the material fresh, Penta used per-renderer copies).

### Decision (owner, 11.11.2026): New Era arms are kept

Not a bug: the two clients are built differently. Penta and the original web bundles (`WebCache/.../character/meshes/weapons`, 87 hands prefabs)
have only `Shirts_torso01` + `Gloves_hands01` / `Gloves_hands_new` in first person; the sleeve and glove meshes and `ArmClothModel` exist only
in the New Era bundle and code. We keep New Era's look. If we ever switch to the Penta variant:

1. `ShotController.Attach`: call `ArmClothModel.Sync(w.transform, "", "")` instead of passing the worn shirt / glove — every arm mesh is
   hidden and the bare bases (`Shirts_torso01`, `Gloves_hands01`) are shown. Keep the `ArmClothModel` class (its GUID holds the prefab data).
2. `Dresser.HandTextureID`: Penta's exclusion list only — `!id.Contains("santa") && id != "CLOTH_shirt_mummy_Color"` (P `PlayerCustomisator.cs:295-310`;
   New Era added Thanos / business / stalker because those got their own meshes). Penta applies only the worn glove's model (the shown one), with no
   separate pass for the default glove.
3. Penta's `HandTextureLoader` has an empty `Start` and gets `ForceLoad()` right after the textureID is set; its `RuntimeTextureLoader.ForceLoad`
   uses `renderer.material` / `renderer.materials[materialID − 1]` (a per-renderer copy) and sets no shaders. With per-renderer copies the
   `prefabHandTexture` restore in `ShotController` is no longer needed.
4. Check with the `fp_*` cases of `-uicompare`: every outfit must show the bare arm; gloves only as the texture on the hands
   (santa / mummy gloves: bare hands).

## Where New Era and Penta differ (New Era kept)

- Switching: New Era hides the old model at once. Penta waited 0.2 s (P `ShotController.cs:1552-1604`).
- Selecting: New Era allows any filled slot. Penta skipped empty-ammo slots except slot 1 and sticky bombs (P `SetWeapon :215`, `SetNext/PrevWeapon :925-963`).
- HUD bar: New Era refreshes it every frame, so it stays visible. Penta showed it for 2 s after a change.
- Weapon data: the New Era balance code is kept (`Normalize*`, exact reload times, sequential shell reload).

## Unity 6 notes

- Built-in shaders are found by their "Legacy Shaders/…" names (`RuntimeTextureLoader.FindShader`).
- A texture missing from Resources is not written. New Era wrote null; Penta applied only on success. `concrete_03_d` and `shotgun` are missing in New Era too.
- `Flamer` keeps its timer only. The legacy ParticleEmitters are converted in stage 4.

## Not in stage 1

- Stage 2: firing, reload, auto-reload of an empty clip, zoom, the 3D crosshair (colour, rotation, CheckWeaponState).
- Stage 4: the gatling stop on change.
- Stage 5: the third-person body's weapon (SendChange → PlayerManager.ChangeWeapon).

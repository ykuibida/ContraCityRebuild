# Decisions after the 2026-11-11 audit

Base: **New Era** (`W:/Contra City Extract/NE_Scripts_Decrypted/`). Penta 2016 (`Penta_Original/Export_WebPlayer_2016/.../Assembly-CSharp/`) is a reference only.
Every ported value carries its source as a comment in the code (`NE File.cs:line`).

## Taken from New Era (owner's choice)

| Item | Now | Source |
|---|---|---|
| Speeds | 15.5 / 13.5 / 7 / 6.5 / 5.5 / 5 / 3.5 / 3.2 × 1.04 × «Бегун» × clothing speed | NE `SoldierController.cs:366-401` (ForceBattleMovementValues) |
| Accelerations, jump | ground 252 / 88, air 10 / 10, jump 2.7 + extra 0.9 | NE `SoldierController.cs:513-518, 399` |
| Input | axis 0.05 per frame (scaled above 250 fps), opposite keys cancel, walk = Shift only while moving | NE `SoldierController.cs:551-641, 721-727` |
| Motor | strafe sustain (90 % after 0.18 s), air speed clamp runSpeed × 1.25 × 2.7, ground normal skipped below 0.5 | NE `CharacterMotor.cs:882-928, 1159-1200` |
| Round lock | the motor stands still in TimeOver | NE `CharacterMotor.cs:264-277, 393-401` |
| Mouse | Normal mode 0.004 without dt, invert Y, aim sensitivity Lerp(dt × 15) ratio Aim / Sens | NE `SoldierCamera.cs:334-432` |
| FOV, sensitivity | option (FOV 74.66 and sensitivity 1.02 = the owner's own values) | NE `喞.cs:30, 55-65` |
| Fall damage | 0.45 s, height ≥ 30 and down speed ≥ 18 → 40, ≥ 60 → 80; «Легкое приземление» ×0.5; armor first; zombies immune; no damage reduction | NE `CharacterMotor.cs:602-710`, `PlayerManager.cs:8467-8513` |
| Under the map | no death: recovery to the last ground point after tunnelling, otherwise the persistent "cheat_msg" warning | NE `CharacterMotor.cs:403-600`, `GameHUD.cs:423-427, 3253` |
| Respawn | 7 s, the digit counts 6…0, click debounce 0.75 s | NE `GameHUD.cs:30, 3470, 3862-3880` |
| Side switch | a spawn request into the other team: instant respawn there, no death; cooldown 90 s from the start of the match (server value, taken from a New Era capture: 61 s left 31 s after joining) | NE `BattleServerHttpClient.cs:3177-3197, 6615` |
| Pickups | small kit (size 1, or value 50) = + half of the maximum (rounded up), big = full; health max = max(100, max health), armor max 100; ammo box always taken; back after 7 s | NE `PlayerManager.cs:4520-4649`, `RemoveItem` 7 s |
| Spawn points | Respawn_T<team> → its RespawnPoint children (DM: neutral T0), sorted, shuffled bag without an immediate repeat, ground snap +0.05, yaw of the Respawn_T root, fallbacks own → T0 → T1 → T2 → (0, 5, 0) | NE `MainNetworkController.cs:985-1065, 1424-1446, 1486-1540, 1613-1645, 1722-1734, 1784-1830` |
| Round end | time limit in every mode (CTF/CP at their 60 min); frag limit: DM a player's kills, TDM a team's kills ≥ Frags (DM/TDM = 100, the control list 50/100/200/300/500 at index 1) | NE `OfflineMatchController.cs:160-229`, `喬.cs:454-475` |
| Time over | the team with more points gets a win, stats are committed, everybody is killed without a frag, the map camera, results 10 s, «Announcer_Winner» | NE `OfflineMatchController.cs:662-693`, `PlayerManager.cs:8165-8216` |
| New round | 8 s after time over: NewGame and an immediate respawn in the same team | NE `OfflineMatchController.cs:695-726` |
| Stats per round | minutes = ceil(round time), ≥ 1, ≤ TimeLimit + 1; kills, deaths; win / loss by team points (DM: 1st place) per map and mode | NE `喡.cs:416-530` |
| Exit | commits the round, back to «Битвы» → room list | NE `GameHUD.cs:4330-4400` |
| Death | controllers off, motor reset, clothes shown, ragdoll (ActorAnimator.Defeat), TPS camera behind the body | NE `PlayerManager.cs:7382-7425, 7889-7925`, `ActorAnimator.cs:1706-1769` |
| First-person arms | New Era sleeves for every shirt and 7 glove meshes (ArmClothModel); other gloves = texture on the hands. Penta / original bundles: bare arm, glove texture on the hands only. Switch steps: `Docs/weapons/stage1.md` → "Decision" | NE `ArmClothModel.cs`, `PlayerCustomisator.cs:255, 303, 308-322`; P `PlayerCustomisator.cs:295-310` |


## Fixed where ours differed from both originals

- Crouch eye height: FPSCamera.Walk = a movement key held (4.7 standing still / 5.2 moving) — `PlayerLocal.cs:118-187` (Penta and NE).
- Box colliders: always the real hit normal (`Type.GetType("BoxCollider")` is null in both) — NE `CharacterMotor.cs:1010`, Penta `:663`.
- Eye camera at the prefab MainCamera position (0.0084, 6.268, 0.6457); the eye-height lerp does not write the final snap — prefab Fighter_locomotion, NE `FPSCamera.cs:312-317`.
- A dead fighter's motor stops — NE `CharacterMotor.cs:259-263`, Penta `:271`.
- Esc always opens the menu — NE `GameHUD.cs:3806-3814`, Penta `:2509-2516`.
- The timer shows 0 during team selection (except TDM) — NE `GameHUD.cs:843-848`.
- TAB only in Play / Dead / DeadReady — NE `GameHUD.cs:3510`.
- No cursor-lock gate on mouse look (none in either client).
- Taunt camera FOV 60 — prefab TPSCamera.
- Body animation key priority right › left › forward › back; footsteps only in the four run states — `PlayerLocal.cs`, `ActorAnimator.cs:813-828`.
- Layer collision matrix = the original one (identical in Penta and NE `ProjectSettings/DynamicsManager.asset`).
- KillY (out-of-map suicide) removed — exists in neither client.

## Not ported (with the reason)

- **Base gates** — PORTED. The working gates are `AutomaticDoor` doors (Penta 2016 `AutomaticDoor.cs`, NE `AutomaticDoor.cs` offline path): "Door_Red / Door_Blue" with a "Scanner" child and the gate "steel_03_b01" (Team 1 / 2) on Angar, ArenaRing, Arena_3lvl, Arena_well, LegoTurnament, TF3_Arena_map, Zombi_2. Every 0.5 s `PlayerManager.TeamScan` (NE `:10143-10165`) looks for a live player near the scanner who is not in the other team → `door_open_Angar` / `door_clouse_Angar` at doorSpeed 2, then a 1 s pause. DM players are team 0, so in DM every gate opens for everybody. The class was missing in the project (the scenes kept its script GUID c8404a65…), now restored under that GUID. The old `InitGates` / `SetupGates` (GatesRed1 / GatesBlue1, layers 12 / 0) is ported too but those objects are inactive in every original map, so it has no visible effect; the collision matrix is the original one.
- Landing crouch of the body (`jumpLandCrouchAmount`): it only weights the aiming layers, which need weapons.
- Spectating team-mates with RMB while dead, the killer's camera after 2 s: need other players.
- Remote-head support, zombie hit stagger / push, the free-space check of spawn points: need other players.
- Sniper zoom FOV / sensitivity: needs weapons.
- The Windows speech of the winner's name (SoundManager.AnnounceWinner): a New Era addition through SAPI.

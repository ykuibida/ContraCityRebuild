#!/usr/bin/env bash
# Bots map lab: one game process per map, all at once (4 maps ~16 min instead of 40+; 8 cores / 16 threads, 32 GB).
# Run from Git Bash:  MAPS=ArenaRing,Bit_map,Arena_3lvl,ShipConteyner [RESEARCH=1] [EXTRA=-labdiag] [SKIPBUILD=1] Tools/lab/lab_par.sh
#   RESEARCH=1  delete those maps' jump caches first (full search);  EXTRA=-labdiag  the owner's jumps repeated, flat speeds
#   SKIPBUILD=1 use the current build;  logs: Builds/ContraCity/labtest_<map>.txt, pictures: Builds/ContraCity/uitest/
set -u
ROOT="W:/ContraCityRebuild"
UNITY="W:/Unity/Установки/6000.3.28f1/Editor/Unity.exe"
cd "$ROOT" || exit 1
if [ "${SKIPBUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod BuildGame.Build -logFile Builds/build_log.txt
  if ! grep -q "\[BuildGame\] Succeeded" Builds/build_log.txt; then
    echo "BUILD FAILED:"; grep -E "error CS" Builds/build_log.txt | sort -u | head -20; exit 2
  fi
fi
cd Builds/ContraCity || exit 1
start=$(date +%s)
pids=()
for m in ${MAPS//,/ }; do
  [ "${RESEARCH:-0}" = "1" ] && rm -f "LabData/$m.links.json"
  timeout 4000 ./ContraCity.exe -labtest "$m" ${EXTRA:-} -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "labtest_$m.txt" > /dev/null &
  pids+=($!)
done
for p in "${pids[@]}"; do wait "$p"; done
echo "all maps took $(( $(date +%s) - start )) s"
for m in ${MAPS//,/ }; do
  echo "----- $m: exceptions $(grep -c Exception "labtest_$m.txt")"
  grep -a "\[labtest\]" "labtest_$m.txt"
  grep -a -B2 -A6 "Exception" "labtest_$m.txt" | head -20
done

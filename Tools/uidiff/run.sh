#!/usr/bin/env bash
# Full UI comparison cycle: build the game, take the -uicompare screenshots, diff them against New Era.
# Stops on a failed build so stale screenshots are never compared.   Usage: bash Tools/uidiff/run.sh [case ...]
set -u
ROOT="W:/ContraCityRebuild"
UNITY="W:/Unity/Установки/6000.3.28f1/Editor/Unity.exe"
cd "$ROOT" || exit 1
"$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod BuildGame.Build -logFile Builds/build_log.txt
if ! grep -q "\[BuildGame\] Succeeded" Builds/build_log.txt; then
  echo "BUILD FAILED:"; grep -E "error CS" Builds/build_log.txt | sort -u | head -20; exit 2
fi
cd Builds/ContraCity || exit 1
rm -f uitest/compare_cases/*.png
timeout 700 ./ContraCity.exe -uicompare -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 -logFile uicompare_log.txt > /dev/null
echo "exceptions: $(grep -c Exception uicompare_log.txt)"
cd "$ROOT/Tools/uidiff" && python -I uidiff.py "$@"

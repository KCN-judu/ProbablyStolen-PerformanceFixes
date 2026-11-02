#!/usr/bin/env bash
# Builds the mod with the Roslyn compiler from Visual Studio Build Tools, against the game's own Mono assemblies.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe ./build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"
mkdir -p out/performance_fixes

refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp Assembly-CSharp-firstpass 0Harmony \
         UnityEngine UnityEngine.CoreModule UnityEngine.UI UnityEngine.UIModule UnityEngine.InputLegacyModule UnityEngine.IMGUIModule UnityEngine.PhysicsModule UnityEngine.Physics2DModule UnityEngine.AudioModule UnityEngine.AnimationModule UnityEngine.TextRenderingModule Unity.TextMeshPro Unity.Localization Unity.ResourceManager DOTween; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done

# The .NET Framework csc cannot create its temp files under long paths, so compile in a short folder.
# It also reads "/" as an option prefix, so sources and output use backslashes.
WORK="${TEMP:-/tmp}/pf_build"
rm -rf "$WORK" && mkdir -p "$WORK/src"
cp src/*.cs "$WORK/src/"
srcs=()
for f in src/*.cs; do srcs+=("src\\$(basename "$f")"); done
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -langversion:latest -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" '-out:PerformanceFixes.dll' "${srcs[@]}" )
cp "$WORK/PerformanceFixes.dll" out/performance_fixes/

cp manifest.xml out/performance_fixes/
cp preview.png out/performance_fixes/
mkdir -p out/performance_fixes/Localization
cp Localization/*.csv out/performance_fixes/Localization/
echo "built out/performance_fixes"

if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/performance_fixes"
  cp -r out/performance_fixes "$GAME/Mods/"
  echo "installed to $GAME/Mods/performance_fixes"
fi

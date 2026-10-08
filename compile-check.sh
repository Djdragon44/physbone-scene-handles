#!/bin/bash
# Compile-check the editor assembly against the real Unity + VRChat SDK DLLs.
# Not part of the package; local verification only.
#
# Unity doesn't expose a headless "just typecheck this assembly" mode, so this drives the
# Roslyn compiler Unity ships with, pointed at the same reference set Unity would use. It
# catches what a compile catches - nothing about layout or runtime behaviour, which still
# needs a real editor.
#
# Override for a different Unity version or project:
#   UNITY_DATA=... SDK_PACKAGES=... bash compile-check.sh
set -u
U="${UNITY_DATA:-/home/duncan/Unity/Hub/Editor/2022.3.22f1/Editor/Data}"
P="${SDK_PACKAGES:-/home/duncan/Documents/VRChat Projects/Ririka/Packages}"

for d in "$U" "$P"; do
  [ -d "$d" ] || { echo "compile-check: not found: $d" >&2; exit 1; }
done
OUT="${PAPERCLIP_RUN_SCRATCH_DIR:-/tmp}/pbh-compile"
mkdir -p "$OUT"

# The netstandard 2.1 facade. The VRChat SDK DLLs are compiled against it, so without this
# every attribute and base type they expose comes back as CS0012.
REFS=( "-r:$U/MonoBleedingEdge/lib/mono/unityjit-linux/Facades/netstandard.dll" )
for d in "$U/Managed/UnityEngine" "$U/Managed" "$U/MonoBleedingEdge/lib/mono/unityjit-linux"; do
  # Skip the monolithic UnityEngine.dll / UnityEditor.dll: they re-export every type that
  # also lives in the per-module assemblies, which makes every single type ambiguous (CS0433).
  while IFS= read -r f; do
    case "$(basename "$f")" in UnityEngine.dll|UnityEditor.dll) continue ;; esac
    REFS+=( "-r:$f" )
  done < <(find "$d" -maxdepth 1 -name '*.dll')
done
for f in "$P/com.vrchat.base/Runtime/VRCSDK/Plugins/Harmony"/0Harmony.dll \
         "$P/com.vrchat.base/Runtime/VRCSDK/Plugins"/*.dll \
         "$P/com.vrchat.base/Editor/VRCSDK/Plugins"/*.dll \
         "$P/com.vrchat.avatars/Runtime/VRCSDK/Plugins"/*.dll; do
  [ -f "$f" ] && REFS+=( "-r:$f" )
done

dotnet "$U/DotNetSdkRoslyn/csc.dll" \
  -target:library -nostdlib+ -noconfig -langversion:9.0 \
  -define:UNITY_EDITOR -define:UNITY_2022_3_OR_NEWER -define:UNITY_2019_4_OR_NEWER -define:PBHANDLES_VRCSDK_PRESENT \
  -out:"$OUT/check.dll" "${REFS[@]}" \
  Packages/com.opensource.physbonehandles/Editor/*.cs

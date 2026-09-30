#!/usr/bin/env bash
# Regenerates src/Plc.Ir.Logix/catalog/logix-services.json from the ladder instruction signature table
# (src/L5x/Parsing/Rll/InstructionSignatures.txt) plus the FBD-style instructions listed in
# tools/logix-fbd-instructions.txt (MNEMONIC:STRUCTURE_TYPE pairs; called from ST with their backing structure).
set -euo pipefail
cd "$(dirname "$0")/.."
out=src/Plc.Ir.Logix/catalog/logix-services.json
tmp=$(mktemp)
awk -f tools/gen-logix-catalog.awk src/L5x/Parsing/Rll/InstructionSignatures.txt | head -n -2 > "$tmp"
awk 'BEGIN{ORS=""} { for (i=1;i<=NF;i++) { split($i,p,":"); if (p[1]=="ALMA"||p[1]=="ALMD") continue; printf ",\n    { \"name\": \"%s\", \"group\": \"fbd\", \"enable\": \"input\", \"variadic\": true, \"params\": [{ \"name\": \"structure\", \"role\": \"ref\", \"type\": \"%s\" } ] }", p[1], p[2] } }' tools/logix-fbd-instructions.txt >> "$tmp"
printf '\n  ]\n}\n' >> "$tmp"
mv "$tmp" "$out"
echo "$(grep -c '"group"' "$out") services written to $out"

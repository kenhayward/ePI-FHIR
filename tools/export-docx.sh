#!/usr/bin/env bash
# Export the canonical Markdown specs/design to Word (.docx) under docs/exports/.
# Requires: pandoc (https://pandoc.org). Run from the repository root.
set -euo pipefail

OUT="docs/exports"
mkdir -p "$OUT"

# specs
pandoc specs/deliverables-definition.md              -f gfm -t docx --toc --toc-depth=3 -o "$OUT/Deliverables Definition.docx"
pandoc specs/D1-solution-overview.md                 -f gfm -t docx --toc --toc-depth=3 -o "$OUT/D1 Solution Overview Specification.docx"
for f in specs/capabilities/*.md; do
  base="$(basename "${f%.md}")"
  pandoc "$f" -f gfm -t docx --toc --toc-depth=3 -o "$OUT/$base.docx"
done

# design
pandoc design/D3-technical-architecture.md           -f gfm -t docx --toc --toc-depth=3 -o "$OUT/D3 Technical Architecture.docx"

echo "Exported .docx files to $OUT/"

# Jot stacked-paper icon — source lock

The user selected **B — folded corner** from the generated compact-stack board on 2026-09-26.

Reference: `stack-concepts.png`, middle column B. The board is exploratory; the other columns are not approved marks.

Locked details:

- Three compact, offset sheets, with the yellow front sheet upright.
- Teal middle sheet and blue rear sheet, matching the selected offset, angle, and corner-radius relationships.
- The front sheet's lower-right folded corner, including its diagonal silhouette.
- No text, pen, surrounding app tile, or added decorative symbol.
- Colored app/taskbar icon; matching black/white tray variants with clear paper separation.

Finalization may clean the crop, transparent background, rendering quality, contrast, and export sizes. It must not introduce a new silhouette or reinterpret the selected fold. Final raster assets are generated from this reference, not reconstructed as SVG/code geometry.

## Final exports for review

- `../../assets/jot-color.png`: selected B color master, generated from the approved board.
- `../../assets/jot-tray-mask.png`: cleaned monochrome master derived from B. Native rendering reuses its alpha identically for light and dark ink; the independently generated white export was rejected for edge artifacts.
- `../../assets/jot.ico`: native 16, 20, 24, 32, 48, 64, 128 and 256px color frames, packaged by `scripts/make-icon.ps1` through the same raster renderer used by the app.
- `../../assets/jot-tray-dark.ico` and `../../assets/jot-tray-light.ico`: prepackaged adaptive tray variants, avoiding runtime source-image rasterization.
- `final-icon-board.png`: actual packaged-size review sheet, not a simulated taskbar screenshot.

Prompt summary: preserve the three offset sheets, front fold, proportions and angle relationships of B; clean transparency and edges, export color and one-color variants without added text, glow or a surrounding tile. The built-in image-generation tool produced the raster masters. The icon packer performs only aspect-preserving image placement, size conversion and native tray tinting—no tracing or replacement geometry.

The v1.1.1 taskbar sizing correction reduces only colored-icon padding from 6% (minimum 1px) to 1.5% rounded to pixels. It does not change the master artwork, aspect ratio, silhouette, or tray padding. Runtime uses these prepackaged ICO files; the export command remains the source-of-truth packer.

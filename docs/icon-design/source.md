# Jot faceted stacked-paper icon — source lock

The user approved the **three-color faceted stack** on 2026-09-27 with “use this logo”. This replaces the original B color master, while keeping its three-paper identity. No app rename was approved.

Approved source: `C:\Users\Admin\.codex\generated_images\01a0cd18-77e2-7191-96d1-bebe88081919\exec-60e9a68d-6956-4182-97b8-48c361616fba.png`. The project master `../../assets/jot-color.png` is a byte-for-byte copy, verified by SHA-256.

The user initially paused after placing the logo in the project, then explicitly resumed on 2026-09-28. The color master remains unchanged. A matching monochrome alpha master has now been generated from this approved source, and the native color/dark-tray/light-tray ICO packages have been regenerated. No installer was built and no running user app was replaced or interrupted.

Locked details:

- Three compact, offset sheets, with the yellow front sheet upright.
- Teal middle sheet and blue rear sheet, matching the selected offset, angle, and corner-radius relationships.
- The front sheet's lower-right folded corner, including its diagonal silhouette.
- The approved angular fold treatment and large diagonal tonal facets, inspired by the supplied VS Code style reference. Preserve the three color families, not an all-blue recoloring.
- No text, pen, surrounding app tile, or added decorative symbol.
- Colored app/taskbar icon; matching black/white tray variants with clear paper separation.

Finalization may clean the crop, transparent background, rendering quality, contrast, and export sizes. It must not introduce a new silhouette or reinterpret the selected fold. Final raster assets are generated from this reference, not reconstructed as SVG/code geometry.

## Source and packaging status

- `../../assets/jot-color.png`: the exact approved faceted three-color master (SHA-256 `0125FCDFAB8CC061D017D12319240694B6333B0D42E3A4ECA2E924FB8AE4B7D2`).
- `../../assets/jot-tray-mask.png`: matching monochrome alpha master, generated from the approved color image with transparent sheet/fold separators. Both light/dark tray inks reuse the same alpha, preserving identical geometry.
- `../../assets/jot.ico`: current color package, with 16, 20, 24, 32, 48, 64, 128 and 256px frames, generated using `scripts/make-icon.ps1 -NoRestore` and the existing raster packer.
- `../../assets/jot-tray-dark.ico` and `../../assets/jot-tray-light.ico`: prepackaged adaptive tray variants, avoiding runtime source-image rasterization.
- `final-icon-board.png`: actual packaged-size review sheet for the current icon, including light/dark color and monochrome samples. This is not a simulated taskbar screenshot.

Prompt summary: edit the existing three-paper mark with VS Code-like angular folds and faceted shading, preserving the yellow front, teal middle, blue rear, stacking order, and lower-right fold. No letters, writing lines, pencil, tile or extra objects. The built-in image-generation tool produced the approved image. The icon packer must perform only aspect-preserving image placement, size conversion and native tray tinting—no tracing or replacement geometry.

Tray source after the user's bottom-right correction: `C:\Users\Admin\.codex\generated_images\01a0cd18-77e2-7191-96d1-bebe88081919\exec-0f75f9c5-1004-4abc-a74b-bd6dae06c73c.png`, SHA-256 `321676C06A08E802FF05C2A8353F7B6AD0AC6336BD46D009673324A4D544CDCA`. Built-in image editing repaired the hollow front fold into a solid flap with a transparent L-shaped separator, preserving the three-sheet layout and diagonal outer corner. The prior mask is retained at `C:\Users\Admin\.codex\generated_images\01a0cd18-77e2-7191-96d1-bebe88081919\exec-52f1bf57-3896-46ad-b633-88f28fd14a62.png` (SHA-256 `F67AAFB987C3C0ED23388E8FD0B477BE1DF4F51E6BC009CE0B1B872DCF304D64`).

Tray ink is now applied **after** resampling so bicubic interpolation cannot brighten/darken the fold's ink. The antialiased alpha is shared unchanged between both theme variants. This export-only change does not add runtime work. The colored PNG and ICO remain byte-identical. The corrected mask and two tray ICOs were applied to the existing `dist/Jot-dev-faceted-icon/assets` folder; its executable was not replaced and the running app was not interrupted. A normal restart loads the new assets.

Previous source history: `stack-concepts.png`, middle column B, was selected on 2026-09-26 and underlies the new style edit. Previous packaged assets remain recoverable through Git history.

The v1.1.1 taskbar sizing correction reduces only colored-icon padding from 6% (minimum 1px) to 1.5% rounded to pixels. It does not change the master artwork, aspect ratio, silhouette, or tray padding. Runtime uses these prepackaged ICO files; the export command remains the source-of-truth packer.

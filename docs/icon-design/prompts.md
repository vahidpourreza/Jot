# Icon generation prompts

## Current approved faceted color master (2026-09-27)

Built-in image editing used the original three-color paper stack as its reference. The user approved this result, and the exact generated PNG is the current `assets/jot-color.png`; it was not regenerated during packaging.

> Restyle the existing three overlapping papers with VS Code-like angular folds and large, restrained diagonal tonal facets. Preserve the upright yellow front, teal middle and blue rear sheet, stacking order, relative rotations and lower-right front fold. Retain all three color families and transparency. No letters, writing lines, pencil, badge, tile, shadow, scene or extra object. One image only.

## Current monochrome tray master (2026-09-28)

Built-in image editing used the approved faceted color master as the edit target and the previous monochrome mask only as a functional reference for transparent gaps.

> Convert the approved three-paper stack to one near-black alpha master. Preserve the source positions, outer silhouette, sheet angles, proportions and angular front fold. Remove color and gradient facets; introduce clean transparent channels at the sheet overlaps and front fold so the layers remain recognizable at small tray sizes. Keep genuine transparency, clean antialiased edges and no new shapes, letters, outlines, tile, shadows or effects. Use this one alpha mask for both tray inks.

The raster packer resamples the shared alpha and then tints it to `(30,30,30)` for light Windows surfaces and `(245,245,245)` for dark surfaces. Tinting after scaling prevents interpolation color fringes. It does not reconstruct vector logo geometry.

### Bottom-right fold correction (2026-09-28)

The user reported an unpleasant bottom-right patch in the monochrome tray icon. Built-in image editing used only the existing tray mask as its edit target; the colored master was not changed.

> Repair only the hollow bottom-right front fold: make the flap a solid opaque triangle in the same near-black ink, separated from the front page by a clean narrow transparent L-shaped channel. Remove the spindly diagonal border while retaining the diagonal outer cut corner. Preserve all three sheet positions, proportions, angles, radii, inter-sheet gaps and crop. Crisp antialiased contours, genuine transparent exterior and gaps, no new objects, text, gradients, shadows or colors. The small-size fold must read clearly in both tray inks.

## Previous B exports (historical)

Generated with the built-in image-generation tool. The user selected candidate B from `stack-concepts.png` before these refinements. Native ICO packaging uses the generated raster artwork, not traced or reconstructed logo geometry.

## Color master — assets/jot-color.png

Reference: middle-column candidate B in `stack-concepts.png`.

> Extract and finalize ONLY the COLOR logo from candidate B, the MIDDLE column in the reference. ONE icon alone, square canvas, genuinely transparent alpha background, no board, no labels, no text, no app tile. Preserve B exactly: three offset sheets, upright yellow front sheet with the distinctive lower-right folded corner, teal middle sheet and blue rear sheet with the same relative tilts, offsets, radii, proportions, fold diagonal and silhouette as the approved reference. Center it and fill about 84 percent of the canvas width/height, equal optical margins. Make fills crisp and opaque with clean anti-aliased edges; NO haze, outer glow, backdrop, drop shadow, paper grain, lighting or texture. No new shape or outline. Use flat warm yellow, teal and sky blue and a darker yellow fold triangle. High quality icon export intended to be downsampled into Windows 16-256px icons. Only the empty area outside the original approved mark must be transparent. Do not change the logo geometry.

## Monochrome master cleanup — assets/jot-tray-mask.png

Reference: the generated black monochrome B export. Transparent-background output was explicitly enabled.

> Cleanup/export pass ONLY for this approved black stacked-paper icon. Keep the exact silhouette, all three sheet positions, relative angles, corner radii, gaps, and folded front lower-right corner. Do not redraw, reinterpret, add, or remove geometric features. Make all shape interiors uniformly opaque black #202020, no gradients or grain. Remove stray edge speckles and retain smooth, crisp antialiased contours. Outside and the inter-sheet gaps must be perfectly transparent alpha; no black background, no white background, no glow, no stroke outline, no shadow. Output one isolated icon on a square transparent canvas at the same crop and scale as the reference. This is a monochrome source asset for Windows system-tray rendering; foreground tint will be applied by the app.

The independently generated white export was rejected for transparency artifacts. Both tray colors use the same accepted alpha master at runtime, preserving identical outlines and gaps.

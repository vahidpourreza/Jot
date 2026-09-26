# Responsiveness assessment — v1.2.1

## Decision

Keep C#/.NET 10 for now. The measured stalls are in the UI/rendering work, not evidence of a programming-language limit. This build improves index updates and image-heavy editing. It does **not** claim Telegram-level window movement, display frame pacing, or startup parity.

The user confirmed that the concern covers moving/resizing, typing/scrolling, and opening/switching pages. All tests ran away from the user's desktop. No live Telegram interaction or foreground side-by-side comparison was performed.

## Changes shipped

- Index rows are keyed by note ID. Updates reuse controls and change only affected text, icons, selection, and order. Actions always reference current note metadata.
- Date/number formatters and group filters are reused. Search rendering is coalesced into one frame. The initial index response is reused instead of immediately loading it again.
- Native visibility is sent to the index. Autosave refreshes are deferred while it is hidden, then combined into one refresh on reveal. This reduces unnecessary work while writing in floating notes.
- Undo snapshot serialization waits for a short typing pause (140ms), rather than serializing embedded original images on every keystroke. Undo, formatting commands, composition boundaries, and Save synchronously capture pending text first. Autosave timing and on-disk format are unchanged. Serialization still has a cost; it was moved away from each key, not eliminated.
- Existing UI styling, menu/header transitions, original images, Persian/English writing, clipboard formats, and recovery behavior remain intact.

## Measurements

Synthetic same-machine workloads, not guarantees. Timing and frame delivery fluctuate with other applications. Measurements cover synchronous JS/layout work, not complete input-to-display latency.

| Workload | Before | v1.2.1 |
| --- | ---: | ---: |
| Refresh one changed row in a 250-note index, median | 119.0ms | 2.0ms |
| Same index workload, p95 | 163.6ms | 3.5ms |
| Unchanged last-row replacements across 16 updates | 16 | 0 |
| Key handler in a 1.81MB image-containing note, median | 12.4ms | 0.3ms |
| Same image-note key handler, p95 | 14.7ms | 0.5ms |

Index baseline: `test-results/index-before/smoothness.json`. Image-note baseline: `test-results/responsive-01/window-and-image-work.json` (before deferred history). Final evidence: `test-results/responsive-final/`. The final 1,800-paragraph input test measured 0.7ms median / 1.1ms p95; the 5.12MB save/load test measured 136.2ms median. These benchmarks do not imply a fixed 60 or 120fps delivery rate.

## Rendering limit and experimental comparison

Jot currently uses a WPF `WebView2CompositionControl`. [Microsoft documents](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.wpf.webview2compositioncontrol) that this control captures browser output into a WPF image and can run at a lower frame rate than standard WebView2. The test machine reported hardware render tier 2. A read-only test inspection found its internal FPS divider already set to 1; no private SDK property is changed in production.

An experimental standard-WebView2/WinForms host loaded the same editor HTML in a separate synthetic profile. In the final run, dispatcher resize/layout work measured 43.5ms median / 55.6ms p95 in the current composition host, and 4.2ms / 6.7ms in the direct-host probe. Previous runs of the composition-host workload ranged around 33–88ms median, so a universal speedup multiplier would be misleading.

This is **not** an equivalent product comparison: the probe lacks Jot's antialiased corners, shadow, full message bridge and window behavior. It uses an opaque, non-activating form outside every monitor, while production-like WPF test windows stay transparent. Its initial attempt to share the composition environment failed with `0x8007139F`; the successful probe uses its own test profile. The prototype never reads or writes real notes and is not enabled in normal launches. Its purpose is to establish that a different host within .NET is a credible direction, not to ship a partially working replacement.

## Next architectural step

The next substantial improvement should remove the WPF capture stage while retaining .NET and the tested storage/editor behavior. A native visual host using the documented [composition-controller root visual target](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2compositioncontroller.rootvisualtarget), or a fully native editor, needs a separate parity pass. The plain direct host above is not ready to replace the production one.

Before switching, verify Windows 10 antialiased corners/shadows, resize edges, multi-monitor DPI, focus and IME, mixed Persian/English text, full-resolution clipboard images, accessibility, keyboard/tray/taskbar behavior, error recovery, and image-viewer lifetime. Only then compare actual foreground frame pacing and input latency when the user is available. A C++/Qt rewrite, like [Telegram Desktop's architecture](https://github.com/telegramdesktop/tdesktop), would also require reimplementing these behaviors; changing language alone is not the demonstrated fix.

## Verification

216/216 offscreen checks passed. New checks cover stable row reuse, updated metadata actions, row reordering/removal, group/search round trips (including a literal `*` group), hidden-index refresh coalescing, immediate Undo/Redo during a typing burst, and formatting after pending typing. Existing clipboard, image, RTL/LTR, save-failure, recovery, tray, theme, and animation checks remain in the suite. The experimental direct host runs after UI regressions to avoid mixing its window/message-loop activity with pointer tests.

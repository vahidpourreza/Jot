# Mixed Persian/English writing

Jot 1.3.0 adds automatic mixed-direction handling without changing the app's English interface or requiring a Persian/English writing-mode switch.

## What changed

The previous implementation relied on the first strong character of a paragraph. That is the usual [`dir=auto` behavior](https://www.w3.org/International/questions/qa-html-dir), but it is not enough for Persian prose that starts with an English identifier.

Jot now recognizes a conservative identifier-led Persian sentence pattern. For example, `Currency enum حذف شد.` is displayed as an RTL sentence, with `Currency enum` retained as an LTR unit. Ordinary English prose still uses LTR. Dotted member names, constants, functions, URLs, email addresses, paths, versions, phone/date/number expressions, command flags, arrays, and generic type names are kept together where recognized.

Inline units use semantic `<bdi dir="ltr">` boundaries, following [W3C's inline bidirectional-text guidance](https://www.w3.org/International/articles/inline-bidi-markup/Overview.en.php?changelang=en). The app changes display boundaries, not the order or spelling of the text. It does not insert hidden Unicode direction characters into note text or plain-text Copy. Source-provided direction marks and Persian joining characters are preserved.

The same interpretation applies to read-only note titles, snippets, and group labels in the index. Direction is independent for separate paragraphs, list items, and table cells. Existing explicit code blocks remain LTR. Plain paste remains unformatted—no code boxes, colors, or fonts are reintroduced.

## Ambiguous paragraphs

Automatic direction is a heuristic, not language understanding. A short fragment, an unusual identifier, or an English sentence ending in a Persian quotation may not contain enough information to infer the writer's intention.

Place the caret in a paragraph (or select multiple paragraphs), then use **More → Direction → Auto / LTR / RTL**. The override affects only those paragraphs, supports Undo/Redo, and survives saving/reloading. Auto removes the override. This is a secondary control in More; the writing area has no language switch.

Use Enter for an independently directed paragraph. Shift+Enter remains a soft line break within the same paragraph. Empty paragraphs follow the active keyboard; once a neutral-only line has content, its initial direction is retained across keyboard switches. A later strong-language character can determine its automatic direction.

Existing explicit Unicode overrides in the source are not silently deleted. They can still affect their contents, and a damaged or deliberately misleading source sequence may require editing the source text. Other applications decide how much HTML direction information they retain on paste; Jot cannot guarantee another app's display of plain clipboard text.

## Editing and data safety

- Only changed blocks are revisited during typing. Already-correct wrappers are reused.
- Text positions and backward selections are preserved across structural changes, including image and line-break boundaries.
- Normalization is deferred during IME composition and applied after completion.
- Original image bytes stay outside text-direction wrappers and remain full-resolution for Copy/viewing.
- Large plain-text runs are built off-DOM in one pass. Existing-wrapper lookup is indexed, and long non-email strings do not cause repeated email backtracking.
- Stored text remains in logical input order. Automatic display markup is not a batch migration of the user's notes. Existing notes are normalized when opened; ordinary save rules still apply.
- Rich HTML Copy/export retains direction markup, while plain-text Copy retains the original characters. Export respects explicit paragraph direction.

## Verification and limits

The release passed **284/284 isolated offscreen checks**, with evidence in `test-results/bidi-final-check/`.

The added matrix includes all eight user-supplied examples; English prose with Persian words; punctuation and quoted URLs; email; Windows, relative and Unix paths; phone numbers including parenthesized area codes; Persian/Arabic/Western digits; dates, versions and percentages; C#/C++/.NET labels; command flags; arrays and generic types; emoji and ZWNJ; list items and table cells; existing code; rich inline formatting; repeated normalization; backward selection; real WebView input/backspace; image-adjacent carets; selection deletion/Undo; IME composition; keyboard changes; paragraph overrides through the actual More buttons; reload, Copy and export; and index previews. The example screenshot is `bidi-user-examples.png` in that output folder.

The final 1,800-paragraph edit test measured 1.9ms median / 2.8ms p95; the 66,000-character write/save check also passed its existing performance limit. These are synthetic workload observations, not universal latency or Telegram-equivalence claims. The user's running app, real clipboard, keyboard layout and foreground windows were not changed. No finite test matrix can prove every possible mixed-language sentence or receiving application's rendering.

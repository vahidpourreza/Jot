# Emoji catalogue

Unicode Emoji 18.0, fully-qualified sequences in Unicode's CLDR order, with the
corresponding minimally-qualified/unqualified sequences accepted on paste.
Skin-tone/hair components alone are not complete note emojis.

- Source: https://www.unicode.org/Public/18.0.0/emoji/emoji-test.txt
- Final-data declaration: https://www.unicode.org/Public/18.0.0/ReadMe.txt
- License: Unicode License V3, retained in `LICENSE.txt`.

`emoji-test.txt` is an unmodified offline source snapshot. Run
`node scripts/generate-emoji-data.mjs` to regenerate browser data and the native
validation allowlist. `--check` verifies both generated assets without network.
The generator downloads only when the pinned source snapshot is absent, and
rejects an unexpected version. No package install is required.

Jot uses the operating system's emoji font; it does not ship vendor emoji art.
Older Windows versions may show newer emojis as boxes or separate characters.
The picker retains searchable names and explains this limitation rather than
silently losing an emoji or replacing the saved value.

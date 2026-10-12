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

Jot bundles local SVG artwork for every offered note emoji: 3,944 Twemoji images
and 19 Noto additions, covering all 3,963 fully-qualified emojis and 5,235 accepted
sequences. Header, tabs, library and picker use these images instead of depending
on Windows emoji-font coverage; stored Unicode strings are unchanged.

See `ARTWORK-NOTICE.txt`, the accompanying licenses and `artwork-manifest.json`
for pinned commits, original paths and checksums. Run
`node scripts/generate-emoji-artwork.mjs --check` to verify the complete local set.
Generation fetches only pinned upstream assets; the running app never downloads
emoji images. The picker uses ranked name/alias search and bounded paging.

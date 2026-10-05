# Editor bundle notices

`scripts/editor-licenses.mjs` reads esbuild's emitted input map and generates
`assets/editor/editor-licenses.txt` for the third-party packages actually included
in the JavaScript/CSS bundle. Ship that file alongside `editor.js` and `editor.css`.
Each package section contains its name, exact installed version, license identifier,
full license text and any installed top-level copyright/notice files. Missing texts
fail the build instead of silently shipping incomplete notices.

The `@editorcn/block-editor@0.3.4` npm archive omits its LICENSE and license metadata.
Its npm `gitHead` is `2b15db504a13140f875a99ac276b9bb1f0e4ce45`. The checked-in
`editorcn-0.3.4-MIT.txt` is the unmodified MIT text from that exact revision:

https://github.com/shadcn-labs/editorcn/blob/2b15db504a13140f875a99ac276b9bb1f0e4ce45/LICENSE

It preserves the upstream copyright notice for Abdullah Mukadam. Review this
fallback when updating editorcn. The adapter's local changes are in
`scripts/build-editor.mjs`: native clipboard delegation and a body-level selection
menu avoid WebView clipboard restrictions and clipping inside the note scroller.

These notices cover the editor bundle only. They are not a replacement for the
licenses of Jot's Windows/.NET dependencies or any other shipped assets.

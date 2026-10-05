import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import { readFile } from 'node:fs/promises';
import { writeEditorLicenses } from './editor-licenses.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const result = await build({
  absWorkingDir: root,
  entryPoints: ['editor-src/entry.tsx'],
  outfile: 'assets/editor/editor.js',
  bundle: true,
  format: 'iife',
  platform: 'browser',
  jsx: 'automatic',
  target: ['chrome120'],
  minify: true,
  sourcemap: false,
  metafile: true,
  define: { 'process.env.NODE_ENV': '"production"' },
  legalComments: 'eof',
  alias: {
    '@editorcn/block-editor/style.css': resolve(root, 'node_modules/@editorcn/block-editor/src/style.css'),
    '@editorcn/block-editor': resolve(root, 'node_modules/@editorcn/block-editor/src/index.ts'),
  },
  plugins: [{
    name: 'editorcn-native-clipboard',
    setup(build) {
      const patched = new Set();
      build.onLoad({filter: /[\\/]block-editor[\\/]src[\\/]bubble-menu[\\/](?:index\.tsx|utils\.ts)$/}, async args => {
        const source = (await readFile(args.path, 'utf8')).replace(/\r\n/g, '\n');
        const original = 'await navigator.clipboard.writeText(text);';
        if (source.split(original).length !== 2) throw new Error('The pinned editorcn clipboard integration changed. Review it before updating.');
        let contents = source.replace(original, 'await copyEditorBlock(editor);');
        if (args.path.endsWith('index.tsx')) {
          const menu = '<TiptapBubbleMenu\n      editor={editor}';
          if (!contents.includes(menu)) throw new Error('Review editorcn bubble-menu positioning after updating.');
          contents = contents.replace(menu, '<TiptapBubbleMenu\n      appendTo={document.body}\n      className="jot-selection-bubble"\n      editor={editor}');
          const editableGuard = 'if (!ed.isEditable) {';
          if (!contents.includes(editableGuard)) throw new Error('Review editorcn bubble-menu focus policy after updating.');
          contents = contents.replace(editableGuard, 'if (!ed.isEditable || (!ed.isFocused && !document.activeElement?.closest(".jot-selection-bubble"))) {');
        }
        patched.add(args.path);
        return {
          contents: `import { copyEditorBlock } from ${JSON.stringify(resolve(root, 'editor-src/editor-ui.tsx'))};\n` + contents,
          loader: args.path.endsWith('.tsx') ? 'tsx' : 'ts',
          resolveDir: dirname(args.path),
        };
      });
      build.onEnd(() => patched.size === 2 ? undefined : {errors: [{text: 'Both editorcn native clipboard adapters must be bundled.'}]});
    },
  }],
  logLevel: 'info',
});
await writeEditorLicenses(result.metafile, resolve(root, 'assets/editor'));

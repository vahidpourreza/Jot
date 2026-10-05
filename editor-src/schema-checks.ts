import { Editor } from '@tiptap/core';
import { closeHistory } from '@tiptap/pm/history';
import { createJotExtensions, importLegacyHtml, validateJotRoundTrip } from './jot-extensions';

/** Only called by the isolated WebView self-test harness; never touches notes. */
export function runSchemaChecks() {
  const results: Array<{ name: string; passed: boolean; detail?: string }> = [];
  const png = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j9woAAAAASUVORK5CYII=';
  const fixtures = [
    ['Persian and Latin typography', '<p data-jot-direction="rtl" dir="rtl" data-jot-align="right" style="text-align:right" data-jot-plain-line="true">Project فارسی می‌شود · انگلیسی English <span style="font-family:Consolas;font-size:19px;color:#f00;background-color:#ff0"><strong>Bold</strong> <em>Italic</em> <u>Underline</u> <s>Strike</s></span></p>'],
    ['Paragraph breaks and whitespace', '<p data-jot-plain-line="true">first  line</p><p><br></p><p>third<br>fourth</p>'],
    ['Code and links', '<p><code dir="ltr">x += 1</code> <a href="https://example.com/path">example</a> <sub>2</sub><sup>3</sup></p><pre dir="ltr"><code>const x = 1;\n  x++;</code></pre>'],
    ['Embedded original image', `<p>before <img src="${png}" alt="original"> after</p>`],
    ['Table merged cells', '<table><thead><tr><th colspan="2">Heading</th></tr></thead><tbody><tr><td>A</td><td>B</td></tr><tr><td rowspan="2">C</td><td>D</td></tr><tr><td>E</td></tr></tbody></table>'],
    ['Legacy containers and captions', `<div><p>outside</p><figure><img src="${png}"><figcaption>caption</figcaption></figure></div><table><caption>Table caption</caption><tbody><tr><td>cell</td></tr></tbody></table>`],
    ['Automatic and manual bidi isolates', '<p dir="rtl">متن <bdi data-jot-bidi="token" dir="ltr">C:\\folder\\file.txt</bdi> و <bdi dir="ltr">manual (unit)</bdi></p>'],
    ['Legacy FONT and styled bold', '<p><font color="#ff0000" face="Consolas" size="4">font</font> <b style="color:#00f">blue bold</b></p>'],
    ['Nested lists and headings', '<h2>Heading</h2><ol start="3"><li>one<ul><li>nested</li></ul></li><li value="8">two</li></ol><blockquote>Quote</blockquote>'],
    ['Highlight', '<p><mark>default</mark> <mark style="background-color:#00ff00">custom</mark></p>'],
  ];
  for (const [name, source] of fixtures) {
    const imported = importLegacyHtml(source);
    let editor: Editor | null = null;
    try {
      if (!imported.safe) throw new Error(imported.unsupported.join('; '));
      editor = new Editor({ element: document.createElement('div'), extensions: createJotExtensions(), content: '<p></p>', parseOptions: { preserveWhitespace: 'full' } });
      // Tiptap's blanket HTML catch-all incorrectly flags TextStyle's
      // non-consuming span rule and table section wrappers. The importer and
      // semantic round-trip audit below are the stricter, compatible gate.
      editor.commands.setContent(imported.html, { emitUpdate: false, errorOnInvalidContent: false, parseOptions: { preserveWhitespace: 'full' } });
      const exported = editor.getHTML();
      const issues = validateJotRoundTrip(imported.html, exported);
      const reopened = importLegacyHtml(exported);
      if (!reopened.safe) issues.push(...reopened.unsupported);
      results.push({ name, passed: issues.length === 0, ...(issues.length ? { detail: `${issues.join('; ')} | ${exported}` } : {}) });
    } catch (error) {
      results.push({ name, passed: false, detail: String(error) });
    } finally { editor?.destroy(); }
  }
  for (const [name, source] of [
    ['Unknown content fails closed', '<p>kept</p><video src="file.mp4"></video>'],
    ['Remote image fails closed', '<p><img src="https://example.com/image.png"></p>'],
    ['Active content fails closed', '<p onclick="alert(1)">unsafe</p>'],
    ['Unsupported CSS fails closed', '<p style="transform:rotate(20deg)">transformed</p>'],
  ]) {
    results.push({ name, passed: !importLegacyHtml(source).safe });
  }
  const functional = (name: string, check: (editor: Editor) => boolean) => {
    const editor = new Editor({ element: document.createElement('div'), extensions: createJotExtensions(), content: '<p></p>', parseOptions: { preserveWhitespace: 'full' } });
    try { results.push({ name, passed: check(editor) }); }
    catch (error) { results.push({ name, passed: false, detail: String(error) }); }
    finally { editor.destroy(); }
  };
  functional('Bidi direction and no inserted Unicode', editor => {
    const text = 'Project فارسی می‌شود · انگلیسی English';
    editor.commands.setContent(`<p>${text}</p>`);
    editor.view.dispatch(editor.state.tr.setMeta('jotBidiRefresh', true));
    return editor.state.doc.firstChild?.attrs.dir === 'rtl' && editor.state.doc.textContent === text && !editor.getHTML().includes('data-jot-bidi');
  });
  functional('Manual direction and code remain LTR', editor => {
    editor.commands.setContent('<p data-jot-direction="ltr">فارسی متن</p><pre><code>فارسی</code></pre>');
    editor.view.dispatch(editor.state.tr.setMeta('jotBidiRefresh', true));
    return editor.state.doc.firstChild?.attrs.dir === 'ltr' && editor.state.doc.lastChild?.attrs.dir === 'ltr';
  });
  functional('Bidi decorations update after code marks and split/undo', editor => {
    editor.commands.setContent('<p>متن انگلیسی English فارسی</p>');
    editor.view.dispatch(editor.state.tr.setMeta('jotBidiRefresh', true));
    if (!editor.view.dom.querySelector('bdi[data-jot-bidi="token"]')) return false;
    const text = editor.state.doc.textContent, from = text.indexOf('English') + 1;
    editor.commands.setTextSelection({ from, to: from + 7 });
    editor.commands.toggleCode();
    if (editor.view.dom.querySelector('code bdi[data-jot-bidi="token"]')) return false;
    editor.commands.toggleCode();
    if (!editor.view.dom.querySelector('bdi[data-jot-bidi="token"]')) return false;
    editor.commands.setTextSelection(from);
    editor.commands.splitBlock();
    editor.commands.undo();
    return editor.state.doc.textContent === text;
  });
  functional('Undo deletion restores rich text and backward selection', editor => {
    editor.commands.setContent('<p><strong>word</strong> suffix</p>');
    editor.commands.setTextSelection({ from: 5, to: 1 });
    editor.view.dispatch(closeHistory(editor.state.tr));
    editor.commands.deleteSelection();
    editor.commands.undo();
    return editor.state.doc.textContent === 'word suffix' && editor.state.selection.anchor === 5 && editor.state.selection.head === 1 && editor.getHTML().includes('<strong>word</strong>');
  });
  functional('Created task list reopens safely', editor => {
    editor.commands.setContent('<p>Task</p>');
    editor.commands.toggleTaskList();
    const html = editor.getHTML(), imported = importLegacyHtml(html);
    if (!imported.safe) throw new Error(imported.unsupported.join('; '));
    editor.commands.setContent(imported.html);
    const issues = validateJotRoundTrip(imported.html, editor.getHTML());
    if (issues.length) throw new Error(issues.join('; ') + ' | ' + editor.getHTML());
    return editor.isActive('taskItem');
  });
  functional('Created table retains rows and column sizes', editor => {
    editor.commands.insertTable({ rows: 2, cols: 2, withHeaderRow: true });
    editor.commands.addRowAfter();
    const html = editor.getHTML(), imported = importLegacyHtml(html);
    if (!imported.safe) throw new Error(imported.unsupported.join('; '));
    editor.commands.setContent(imported.html);
    return validateJotRoundTrip(imported.html, editor.getHTML()).length === 0;
  });
  {
    const text = 'Project فارسی می‌شود · English C:\\project\\file.ts 42';
    const start = performance.now();
    const editor = new Editor({ element: document.createElement('div'), extensions: createJotExtensions(), content: Array.from({ length: 1000 }, () => `<p>${text}</p>`).join('') });
    try {
      editor.view.dispatch(editor.state.tr.setMeta('jotBidiRefresh', true));
      const loaded = performance.now() - start;
      const samples: number[] = [];
      for (let index = 0; index < 20; index++) {
        const tick = performance.now();
        editor.commands.insertContent('x');
        samples.push(performance.now() - tick);
      }
      samples.sort((left, right) => left - right);
      results.push({ name: '1,000-paragraph smoke benchmark', passed: editor.state.doc.childCount === 1000, detail: `Load ${loaded.toFixed(1)}ms; insertion median ${samples[10].toFixed(1)}ms, p95 ${samples[19].toFixed(1)}ms (isolated headless page, not app end-to-end).` });
    } finally { editor.destroy(); }
  }
  return results;
}

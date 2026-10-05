import { Editor } from '@tiptap/core';
import { AllSelection, EditorState, NodeSelection, Selection as PMSelection, TextSelection } from '@tiptap/pm/state';
import { CellSelection } from '@tiptap/pm/tables';
import { DOMSerializer, type Node as DocumentNode } from '@tiptap/pm/model';
import { closeHistory } from '@tiptap/pm/history';
import { runSchemaChecks } from './schema-checks';
import { createJotExtensions } from './jot-extensions';
import { importLegacyHtml, validateJotRoundTrip } from './jot-html-compatibility';
import { mountEditorUi, createSlashCommandExtension, dismissEditorUi } from './editor-ui';
import '@editorcn/block-editor/style.css';
import './editor-ui.css';

interface Host {
  contentHost: HTMLElement;
  toolbarHost: HTMLElement;
  onChange: () => void;
  onSelectionChange: () => void;
  onImage: () => void;
  onError: (error: Error) => void;
  clipboard: (payload: {html: string; text: string; image?: string}) => Promise<unknown>;
  getInputDirection: () => 'ltr' | 'rtl';
  plainText: (html: string) => string;
}
interface NoteState { state: EditorState; originalDoc: DocumentNode; original: string; lastExport: string; changed: boolean; blocked: string[] }
type SavedSelection = {anchor: number; head: number; all?: boolean; json?: ReturnType<PMSelection['toJSON']>};

function create(host: Host) {
  const states = new Map<string, NoteState>();
  let currentId: string | null = null;
  let loading = true;
  const warning = document.createElement('p');
  warning.className = 'editor-compatibility-warning'; warning.role = 'alert'; warning.hidden = true;
  host.contentHost.before(warning);
  const callbacks = {onImage: host.onImage, onError: host.onError};
  const tip = new Editor({
    element: document.createElement('div'),
    injectCSS: false,
    content: '<p></p>',
    parseOptions: {preserveWhitespace: 'full'},
    extensions: [...createJotExtensions({getInputDirection: host.getInputDirection}), createSlashCommandExtension(callbacks)],
    editorProps: {
      attributes: {id: 'editor', class: 'editor', role: 'textbox', 'aria-label': 'Note text · Persian and English', 'aria-multiline': 'true', spellcheck: 'false'},
      // handleDOMEvents returning true bypasses editor keymaps without
      // preventing the browser's native AltGr text insertion.
      handleDOMEvents: {keydown: (_view, event) => event.getModifierState('AltGraph')},
      handleClickOn: (_view, _pos, node) => node.type.name === 'image',
    },
    onUpdate() {
      if (loading || !currentId) return;
      const current = states.get(currentId);
      if (!current || current.blocked.length) return;
      current.changed = true; current.state = tip.state;
      host.onChange();
    },
    onSelectionUpdate() { if (!loading) host.onSelectionChange(); },
  });
  const disposeUi = mountEditorUi({
    editor: tip, contentHost: host.contentHost, toolbarHost: host.toolbarHost,
    ...callbacks,
    onCopyBlock: async (payload: {html: string; text: string; image?: string}) => {
      if (await host.clipboard(payload) !== true) throw new Error('Copy was interrupted. Your note is unchanged.');
    },
  });
  loading = false;
  const current = () => currentId ? states.get(currentId) : undefined;
  const getHTML = () => {
    const entry = current();
    return entry && (!entry.changed || tip.state.doc.eq(entry.originalDoc)) ? entry.original : tip.getHTML();
  };
  function refreshUi() {
    tip.emit('transaction', {editor: tip, transaction: tip.state.tr, appendedTransactions: []});
    tip.emit('selectionUpdate', {editor: tip, transaction: tip.state.tr});
  }
  function flush() {
    // WebView can ask for the final draft before a pending DOM/IME observer
    // delivery. Flush ProseMirror's observer; never serialize its decorated DOM.
    const view = tip.view as typeof tip.view & {domObserver: {forceFlush(): void; flush(): void}};
    view.domObserver.forceFlush(); view.domObserver.flush();
  }
  function loadNote(id: string, html: string, reset = false) {
    dismissEditorUi(tip);
    flush();
    if (currentId && current()) {
      current()!.state = tip.state; current()!.lastExport = getHTML();
    }
    loading = true;
    try {
      let entry = reset ? undefined : states.get(id);
      if (entry && entry.lastExport !== html) entry = undefined;
      currentId = id;
      if (!entry) {
        const imported = importLegacyHtml(html);
        const blocked = [...imported.unsupported];
        if (!imported.safe && !blocked.length) blocked.push('Unsupported document formatting');
        if (!blocked.length) {
          // Tiptap's HTML catch-all rejects valid consuming:false style marks
          // and table section wrappers. Our allowlist + semantic round-trip
          // check is the loss-prevention gate for legacy HTML instead.
          tip.commands.setContent(imported.html, {emitUpdate: false, errorOnInvalidContent: false, parseOptions: {preserveWhitespace: 'full'}});
          blocked.push(...validateJotRoundTrip(imported.html, tip.getHTML()));
        }
        if (blocked.length) {
          const original = new DOMParser().parseFromString(html, 'text/html');
          const paragraph = document.createElement('p'); paragraph.textContent = original.body.textContent || 'This note is preserved, but cannot be edited safely by this editor.';
          tip.commands.setContent(paragraph.outerHTML, {emitUpdate: false});
        }
        const doc = tip.state.doc;
        const fresh = EditorState.create({schema: tip.schema, doc, plugins: tip.state.plugins, selection: TextSelection.atStart(doc)});
        entry = {state: fresh, originalDoc: doc, original: html, lastExport: html, changed: false, blocked};
        states.set(id, entry);
      }
      tip.view.updateState(entry.state);
      tip.setEditable(!entry.blocked.length, false);
      warning.hidden = !entry.blocked.length;
      warning.textContent = entry.blocked.length ? 'Read-only: this note contains formatting the new editor cannot safely convert. The original is unchanged. You can still save/export the original. ' + entry.blocked.join('; ') : '';
      refreshUi();
      return !entry.blocked.length;
    } finally { loading = false; }
  }
  function selection(): SavedSelection {
    const value = tip.state.selection;
    return {anchor: value.anchor, head: value.head, all: value instanceof AllSelection, json: value.toJSON()};
  }
  function restoreSelection(value: SavedSelection | null, focus = true) {
    if (!value || value.anchor < 0 || value.head < 0 || Math.max(value.anchor, value.head) > tip.state.doc.content.size) return false;
    const next = value.json ? PMSelection.fromJSON(tip.state.doc, value.json) : value.all ? new AllSelection(tip.state.doc) : TextSelection.between(tip.state.doc.resolve(value.anchor), tip.state.doc.resolve(value.head));
    if (focus) tip.view.focus();
    tip.view.dispatch(tip.state.tr.setSelection(next));
    return true;
  }
  function syncSelection() {
    const selected = getSelection(), dom = tip.view.dom;
    if (!selected?.rangeCount || document.activeElement !== dom || !dom.contains(selected.anchorNode) || !dom.contains(selected.focusNode)) return;
    if (tip.state.selection instanceof CellSelection) return;
    try {
      const anchor = tip.view.posAtDOM(selected.anchorNode!, selected.anchorOffset), head = tip.view.posAtDOM(selected.focusNode!, selected.focusOffset);
      if (anchor === tip.state.selection.anchor && head === tip.state.selection.head) return;
      const range = selected.getRangeAt(0);
      if (range.startContainer === range.endContainer && range.endOffset === range.startOffset + 1 && range.startContainer.childNodes[range.startOffset]?.nodeName === 'IMG') {
        tip.view.dispatch(tip.state.tr.setSelection(NodeSelection.create(tip.state.doc, Math.min(anchor, head)))); return;
      }
      restoreSelection({anchor, head, all: Math.min(anchor, head) === 0 && Math.max(anchor, head) === tip.state.doc.content.size}, false);
    } catch { /* A detached UI selection never replaces the document selection. */ }
  }
  function command(name: string, value?: string) {
    if (!tip.isEditable || current()?.blocked.length) return false;
    syncSelection(); tip.view.dispatch(closeHistory(tip.state.tr));
    const chain = tip.chain().focus();
    let result = false;
    switch (name) {
      case 'bold': result = chain.toggleBold().run(); break;
      case 'italic': result = chain.toggleItalic().run(); break;
      case 'underline': result = chain.toggleUnderline().run(); break;
      case 'strikeThrough': result = chain.toggleStrike().run(); break;
      case 'insertUnorderedList': result = chain.toggleBulletList().run(); break;
      case 'insertOrderedList': result = chain.toggleOrderedList().run(); break;
      case 'foreColor': result = !value || value === 'inherit' || value === 'currentColor' ? chain.unsetColor().run() : chain.setColor(value).run(); break;
      case 'removeFormat': result = chain.unsetAllMarks().clearNodes().run(); break;
      case 'delete': result = chain.deleteSelection().run(); break;
      case 'insertHTML': result = chain.insertContent(value || '', {parseOptions: {preserveWhitespace: 'full'}}).run(); break;
      case 'insertText': result = chain.command(({tr}) => {tr.insertText(value || ''); return true;}).run(); break;
      case 'formatBlock': {
        const tag = (value || 'p').replace(/[<>]/g, '').toLowerCase();
        if (/^h[1-6]$/.test(tag)) result = chain.setHeading({level: Number(tag[1]) as 1|2|3|4|5|6}).run();
        else if (tag === 'blockquote') result = chain.toggleBlockquote().run();
        else if (tag === 'pre') result = chain.setCodeBlock().run();
        else result = chain.setParagraph().run();
        break;
      }
      default: throw new Error('Unsupported editor command: ' + name);
    }
    tip.view.dispatch(closeHistory(tip.state.tr));
    return result;
  }
  function selectedHTML(all = false) {
    if (all) return getHTML();
    syncSelection();
    const container = document.createElement('div');
    container.append(DOMSerializer.fromSchema(tip.schema).serializeFragment(tip.state.selection.content().content));
    return container.innerHTML;
  }
  const api = {
    editor: tip, ready: true,
    getHTML, getText: () => host.plainText(getHTML()), selectedHTML,
    loadNote,
    setContent(html: string) {
      if (!currentId) throw new Error('No note is open.');
      if (!loadNote(currentId, html, true)) throw new Error('The fixture contains unsupported formatting.');
      host.onChange();
    },
    flush, selection, restoreSelection, syncSelection, command,
    undo(redo = false) { if (tip.isEditable) (redo ? tip.commands.redo() : tip.commands.undo()); },
    canUndo: () => tip.can().undo(), canRedo: () => tip.can().redo(),
    setDirection(value: 'auto'|'ltr'|'rtl') { if (tip.isEditable) tip.chain().focus().setJotDirection(value === 'auto' ? null : value).run(); },
    setAlignment(value: string) { if (tip.isEditable) value === 'auto' ? tip.chain().focus().unsetTextAlign().run() : tip.chain().focus().setTextAlign(value).run(); },
    refreshDirection() {
      if (!loading && currentId) {
        loading = true;
        const entry = current()!, unchanged = !entry.changed || tip.state.doc.eq(entry.originalDoc);
        try {
          tip.view.dispatch(tip.state.tr.setMeta('jotBidiRefresh', true).setMeta('addToHistory', false));
          if (unchanged) entry.originalDoc = tip.state.doc;
          entry.state = tip.state;
        } finally { loading = false; }
      }
    },
    setEditable(value: boolean) { tip.setEditable(value && !current()?.blocked.length, false); },
    dismissUi: () => dismissEditorUi(tip),
    prune(ids: string[]) { const valid = new Set(ids); for (const id of states.keys()) if (id !== currentId && !valid.has(id)) states.delete(id); },
    get blocked() { return !!current()?.blocked.length; },
    runSchemaChecks,
    destroy() {disposeUi(); tip.destroy(); states.clear(); warning.remove();},
  };
  return api;
}

Object.assign(window, {JotEditorFactory: {create}});

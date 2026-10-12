import { Extension, type Editor } from '@tiptap/core';
import { Plugin, PluginKey, type EditorState, type SelectionBookmark, type Transaction } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import './find-in-note.css';

type Match = {from: number; to: number};
interface FindState {query: string; caseSensitive: boolean; matches: Match[]; active: number}
const findKey = new PluginKey<FindState>('jotFind');
const empty: FindState = {query: '', caseSensitive: false, matches: [], active: 0};
function fold(value: string, matchCase: boolean) {
  const normalized = value.normalize('NFKD').replace(/\p{M}/gu, '').replace(/[يى]/g, 'ی').replace(/ك/g, 'ک').replace(/[\u200c\u200d\u0640\uFE0E\uFE0F]/g, '').replace(/[۰-۹]/g,char=>String(char.charCodeAt(0)-0x6f0)).replace(/[٠-٩]/g,char=>String(char.charCodeAt(0)-0x660));
  return matchCase ? normalized : normalized.toLocaleLowerCase();
}
function matches(state: EditorState, query: string, matchCase: boolean): Match[] {
  const term = fold(query, matchCase), result: Match[] = [];
  if (!term) return result;
  state.doc.descendants((block, blockPos) => {
    if (!block.isTextblock) return true;
    let text = '';
    const starts: number[] = [], ends: number[] = [];
    block.descendants((node, offset) => {
      if (!node.isText) {
        if (node.isLeaf) {text += '\n'; starts.push(blockPos + 1 + offset); ends.push(blockPos + 2 + offset);}
        return;
      }
      let index = 0;
      for (const char of node.text || '') {
        const value = fold(char, matchCase), start = blockPos + 1 + offset + index;
        for (let n = 0; n < value.length; n++) {starts.push(start); ends.push(start + char.length);}
        if (!value && ends.length) ends[ends.length - 1] = start + char.length;
        text += value; index += char.length;
      }
    });
    for (let at = text.indexOf(term); at !== -1; at = text.indexOf(term, at + Math.max(1, term.length)))
      result.push({from: starts[at], to: ends[at + term.length - 1]});
    return false;
  });
  return result;
}
export const FindInNote = Extension.create({
  name: 'jotFind',
  addProseMirrorPlugins() {
    return [new Plugin<FindState>({
      key: findKey,
      state: {
        init: () => ({...empty}),
        apply(tr, previous, _old, state) {
          const change = tr.getMeta(findKey) as Partial<FindState> | undefined;
          if (!change && !tr.docChanged) return previous;
          const next = {...previous, ...change};
          if (tr.docChanged || change?.query !== undefined || change?.caseSensitive !== undefined)
            next.matches = matches(state, next.query, next.caseSensitive);
          next.active = Math.max(0, Math.min(next.active, next.matches.length - 1));
          return next;
        },
      },
      props: {
        decorations(state) {
          const value = findKey.getState(state);
          if (!value?.matches.length) return DecorationSet.empty;
          // Keep the full match index for navigation, but bound painted ranges
          // around the active result so a common character cannot flood the DOM.
          const start = Math.max(0, value.active - 150);
          return DecorationSet.create(state.doc, value.matches.slice(start, start + 301).map((match, index) =>
            Decoration.inline(match.from, match.to, {class: index + start === value.active ? 'jot-find-match jot-find-current' : 'jot-find-match', 'data-jot-find-current': String(index + start === value.active)})));
        },
      },
    })];
  },
});

interface FindHost {editor: Editor; before: HTMLElement; available: () => boolean; shortcut: (event: KeyboardEvent) => string | null; shortcutLabel?: (action:string)=>string}
export function mountFindInNote({editor, before, available, shortcut, shortcutLabel}: FindHost) {
  const bar = document.createElement('section'); bar.id = 'noteFind'; bar.className = 'jot-note-find'; bar.role = 'search'; bar.ariaLabel = 'Find in this note'; bar.hidden = true;
  const input = document.createElement('input'); input.type = 'search'; input.id = 'noteFindInput'; input.placeholder = 'Find in this note'; input.ariaLabel = 'Find in this note'; input.autocomplete = 'off'; input.spellcheck = false; input.dir = 'auto';
  const count = document.createElement('output'); count.id = 'noteFindCount'; count.ariaLive = 'polite';
  function button(label: string, text: string) {const value = document.createElement('button'); value.type = 'button'; value.className = 'icon-button'; value.ariaLabel = value.title = label; value.textContent = text; return value;}
  const matchCase = button('Match case', 'Aa'); matchCase.ariaPressed = 'false';
  const previous = button('Previous match · Shift+F3', '↑'), next = button('Next match · F3', '↓'), closeButton = button('Close find · Escape', '×');
  function labels(){for(const [control,label,action]of [[previous,'Previous match','find-previous'],[next,'Next match','find-next']] as const){const key=shortcutLabel?.(action);control.title=control.ariaLabel=label+(key?' · '+key:'');}}
  labels();window.addEventListener('jot-shortcuts-changed',labels);
  bar.append(input, count, matchCase, previous, next, closeButton); before.before(bar);
  let saved: SelectionBookmark | null = null, timer: ReturnType<typeof setTimeout> | undefined;
  const state = () => findKey.getState(editor.state) || empty;
  function render() {const value = state(); count.textContent = value.matches.length ? `${value.active + 1} / ${value.matches.length}` : input.value ? 'No matches' : '0 / 0'; previous.disabled = next.disabled = !value.matches.length;}
  function update(value: Partial<FindState>) {editor.view.dispatch(editor.state.tr.setMeta(findKey, value).setMeta('addToHistory', false)); render();}
  function scroll() {editor.view.dom.querySelector('[data-jot-find-current=true]')?.scrollIntoView({block: 'center', inline: 'nearest'});}
  function query() {clearTimeout(timer); update({query: input.value, caseSensitive: matchCase.ariaPressed === 'true', active: 0}); scroll();}
  function open() {
    if (!available() || document.querySelector('dialog[open]')) return;
    if (bar.hidden) {
      saved = editor.state.selection.getBookmark();
      const selected = editor.state.doc.textBetween(editor.state.selection.from, editor.state.selection.to, '\n');
      if (selected && selected.length <= 150 && !selected.includes('\n')) input.value = selected;
    }
    bar.hidden = false; query(); input.focus({preventScroll: true}); input.select();
  }
  function close(restore = true) {
    clearTimeout(timer); bar.hidden = true; update({query: '', matches: [], active: 0});
    if (restore && available()) {
      editor.view.focus();
      if (saved) try {editor.view.dispatch(editor.state.tr.setSelection(saved.resolve(editor.state.doc)));} catch { /* Content may have changed while finding. */ }
    }
    saved = null;
  }
  function step(direction: number) {if (bar.hidden) open(); queryPending(); const value = state(); if (!value.matches.length) return; update({active: (value.active + direction + value.matches.length) % value.matches.length}); scroll();}
  function queryPending() {if (timer) {query(); timer = undefined;}}
  input.addEventListener('input', () => {clearTimeout(timer); timer = setTimeout(() => {timer = undefined; query();}, 70);});
  input.addEventListener('keydown', event => {if (event.isComposing) return; if (event.key === 'Enter') {event.preventDefault(); step(event.shiftKey ? -1 : 1);} });
  matchCase.onclick = () => {matchCase.ariaPressed = String(matchCase.ariaPressed !== 'true'); query(); input.focus();};
  previous.onclick = () => step(-1); next.onclick = () => step(1); closeButton.onclick = () => close();
  function keydown(event: KeyboardEvent) {
    if (event.defaultPrevented || event.isComposing || event.getModifierState('AltGraph') || document.querySelector('dialog[open]')) return;
    if (!bar.hidden && event.key === 'Escape') {event.preventDefault(); event.stopImmediatePropagation(); close(); return;}
    const action = shortcut(event);
    if (!['find-note', 'find-next', 'find-previous'].includes(action || '')) return;
    if (!available()) {
      const homeSearch = document.querySelector<HTMLInputElement>('#workspaceHome:not([hidden]) #homeSearch');
      if (action === 'find-note' && homeSearch) {event.preventDefault(); event.stopImmediatePropagation(); homeSearch.focus(); homeSearch.select();}
      return;
    }
    event.preventDefault(); event.stopImmediatePropagation();
    if (action === 'find-note') open(); else step(action === 'find-next' ? 1 : -1);
  }
  document.addEventListener('keydown', keydown, true);
  const changed = ({transaction}: {transaction: Transaction}) => {
    if(saved&&transaction.docChanged)saved=saved.map(transaction.mapping);
    if(!bar.hidden&&editor.view.hasFocus()&&transaction.selectionSet)saved=transaction.selection.getBookmark();
    render();
  };
  editor.on('transaction', changed);
  return {
    open, close, next: () => step(1), previous: () => step(-1),
    setQuery(value: string) {if (bar.hidden) open(); input.value = value; query();},
    get count() {return state().matches.length;}, get activeIndex() {return state().active;}, get isOpen() {return !bar.hidden;},
    destroy() {document.removeEventListener('keydown', keydown, true);window.removeEventListener('jot-shortcuts-changed',labels); editor.off('transaction', changed); clearTimeout(timer); bar.remove();},
  };
}

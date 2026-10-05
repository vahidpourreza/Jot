import { Extension, Mark, Node, type Extensions } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Image from '@tiptap/extension-image';
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import { TextStyleKit } from '@tiptap/extension-text-style';
import TextAlign from '@tiptap/extension-text-align';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import Highlight from '@tiptap/extension-highlight';
import Subscript from '@tiptap/extension-subscript';
import Superscript from '@tiptap/extension-superscript';
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight';
import { common, createLowlight } from 'lowlight';
import { Plugin, PluginKey, type EditorState, type Transaction } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';

type Direction = 'ltr' | 'rtl';
interface BidiApi {
  paragraphDirection(text: string, fallback: Direction): Direction;
  tokens(text: string, direction?: Direction): Array<{ start: number; end: number; text: string }>;
}
export interface JotExtensionOptions {
  getInputDirection?: () => Direction;
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    jotDirection: {
      setJotDirection: (direction: Direction | null) => ReturnType;
    };
  }
}

const directionTypes = ['paragraph', 'heading', 'blockquote', 'listItem', 'taskItem', 'table', 'tableCell', 'tableHeader', 'codeBlock', 'legacyBlock'];
const styledTypes = [...directionTypes, 'bulletList', 'orderedList', 'taskList', 'tableRow'];
const bidiKey = new PluginKey<DecorationSet>('jotBidi');
const tokenCache = new WeakMap<ProseMirrorNode, { direction: Direction; tokens: Array<{ start: number; end: number; text: string }> }>();
const directionCache = new WeakMap<ProseMirrorNode, { fallback: Direction; direction: Direction; neutral: Direction | null }>();
const isDirection = (value: unknown): value is Direction => value === 'ltr' || value === 'rtl';
const readDirection = (element: HTMLElement, attribute: string) => {
  const value = element.getAttribute(attribute);
  return isDirection(value) ? value : null;
};
const bidiApi = () => (window as unknown as { JotBidi?: BidiApi }).JotBidi;

// Manual isolates remain document marks. Jot's automatically generated isolates
// are presentation-only decorations, so typing and undo never fight DOM rewrites.
const BidiIsolate = Mark.create({
  name: 'bidiIsolate',
  inclusive: false,
  addAttributes() {
    return { dir: { default: 'auto', parseHTML: element => element.getAttribute('dir') || 'auto' } };
  },
  parseHTML() { return [{ tag: 'bdi:not([data-jot-bidi="token"])' }]; },
  renderHTML({ HTMLAttributes }) { return ['bdi', HTMLAttributes, 0]; },
});

// Old Jot accepted block containers and figures. Preserve their structure and
// editable children instead of flattening them during the first rich-text save.
const LegacyBlock = Node.create({
  name: 'legacyBlock',
  group: 'block',
  content: 'block+',
  defining: true,
  addAttributes() {
    return { tag: { default: 'div', rendered: false, parseHTML: element => element.tagName.toLowerCase() } };
  },
  parseHTML() { return ['div', 'figure', 'figcaption'].map(tag => ({ tag })); },
  renderHTML({ node, HTMLAttributes }) {
    const tag = ['div', 'figure', 'figcaption'].includes(node.attrs.tag) ? node.attrs.tag : 'div';
    return [tag, HTMLAttributes, 0];
  },
});

const JotImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      width: { default: null, parseHTML: element => element.getAttribute('width') || element.style.width || null },
      height: { default: null, parseHTML: element => element.getAttribute('height') || element.style.height || null },
    };
  },
}).configure({ inline: true, allowBase64: true, HTMLAttributes: { class: 'jot-inline-image' } });

const LegacyAttributes = Extension.create({
  name: 'jotLegacyAttributes',
  addGlobalAttributes() {
    return [{
      types: directionTypes,
      attributes: {
        dir: {
          default: null,
          parseHTML: element => readDirection(element, 'dir'),
          renderHTML: attributes => attributes.dir ? { dir: attributes.dir } : {},
        },
        jotDirection: {
          default: null,
          parseHTML: element => readDirection(element, 'data-jot-direction'),
          renderHTML: attributes => attributes.jotDirection ? { 'data-jot-direction': attributes.jotDirection } : {},
        },
        jotNeutralDirection: {
          default: null,
          parseHTML: element => readDirection(element, 'data-jot-neutral-direction'),
          renderHTML: attributes => attributes.jotNeutralDirection ? { 'data-jot-neutral-direction': attributes.jotNeutralDirection } : {},
        },
        jotPlainLine: {
          default: false,
          parseHTML: element => element.getAttribute('data-jot-plain-line') === 'true',
          renderHTML: attributes => attributes.jotPlainLine ? { 'data-jot-plain-line': 'true' } : {},
        },
      },
    }, {
      types: styledTypes,
      attributes: {
        // Keep block-level styling on its block. TextStyle handles styled spans.
        jotBlockStyle: {
          default: null,
          parseHTML: element => {
            const style = document.createElement('span').style;
            for (const property of ['color', 'background-color', 'font-size', 'font-family', 'font-weight', 'font-style', 'text-decoration-line', 'line-height', 'width', 'height', 'min-width']) {
              const value = element.style.getPropertyValue(property);
              if (value) style.setProperty(property, value);
            }
            return style.cssText || null;
          },
          renderHTML: attributes => attributes.jotBlockStyle ? { style: attributes.jotBlockStyle } : {},
        },
      },
    }, {
      types: ['listItem'],
      attributes: {
        value: { default: null, parseHTML: element => element.getAttribute('value') ? Number(element.getAttribute('value')) : null },
      },
    }];
  },
});

const JotTextAlign = TextAlign.extend({
  addGlobalAttributes() {
    return [{
      types: this.options.types,
      attributes: {
        textAlign: {
          default: this.options.defaultAlignment,
          parseHTML: element => element.getAttribute('data-jot-align') || element.style.textAlign || element.getAttribute('align') || null,
          renderHTML: attributes => attributes.textAlign ? { 'data-jot-align': attributes.textAlign, style: `text-align: ${attributes.textAlign}` } : {},
        },
      },
    }];
  },
}).configure({ types: styledTypes, defaultAlignment: null });

function firstStrongDirection(text: string): Direction | null {
  for (const character of text) {
    if (character === '\u200f') return 'rtl';
    if (character === '\u200e') return 'ltr';
    if (/\p{L}/u.test(character)) return /[\p{Script_Extensions=Arabic}\p{Script=Hebrew}]/u.test(character) ? 'rtl' : 'ltr';
  }
  return null;
}

function nodeDecorations(node: ProseMirrorNode, position: number, fallback: Direction): Decoration[] {
  const api = bidiApi();
  if (!api || !node.isTextblock || node.type.name === 'codeBlock') return [];
  const decorations: Decoration[] = [];
  const direction: Direction = node.attrs.jotDirection || node.attrs.dir || api.paragraphDirection(node.textContent, fallback);
  let cached = tokenCache.get(node);
  if (!cached || cached.direction !== direction) {
    let text = '';
    node.forEach(child => {
      const isolated = child.marks.some(mark => ['code', 'bidiIsolate'].includes(mark.type.name));
      if (child.isText) text += isolated ? '\uFFFC'.repeat(child.nodeSize) : child.text;
      else text += child.type.name === 'hardBreak' ? '\n' : '\uFFFC'.repeat(child.nodeSize);
    });
    cached = { direction, tokens: api.tokens(text, direction) };
    tokenCache.set(node, cached);
  }
  for (const token of cached.tokens) {
    if (token.end > token.start) decorations.push(Decoration.inline(position + 1 + token.start, position + 1 + token.end, {
      nodeName: 'bdi', dir: 'ltr', 'data-jot-bidi': 'token', class: 'jot-bidi-token',
    }));
  }
  return decorations;
}

function bidiDecorations(state: EditorState, fallback: Direction): DecorationSet {
  const decorations: Decoration[] = [];
  state.doc.descendants((node, position) => {
    if (!node.isTextblock) return;
    decorations.push(...nodeDecorations(node, position, fallback));
    return false;
  });
  return DecorationSet.create(state.doc, decorations);
}

function updateBidiDecorations(transaction: Transaction, current: DecorationSet, state: EditorState, fallback: Direction) {
  if (transaction.getMeta('jotBidiRefresh') || transaction.steps.length > 80) return bidiDecorations(state, fallback);
  if (!transaction.docChanged) return current;
  // Mark/attribute-only steps have an empty position map, but toggling inline
  // code or a manual isolate still changes which text must be decorated.
  if (transaction.mapping.maps.some(map => {
    let movesPositions = false;
    map.forEach(() => { movesPositions = true; });
    return !movesPositions;
  })) return bidiDecorations(state, fallback);
  let mapped = current.map(transaction.mapping, state.doc);
  const changed = new Map<number, ProseMirrorNode>();
  const addAt = (position: number) => {
    const resolved = state.doc.resolve(Math.max(0, Math.min(state.doc.content.size, position)));
    if (resolved.parent.isTextblock) changed.set(resolved.before(), resolved.parent);
  };
  transaction.mapping.maps.forEach((map, index) => {
    map.forEach((_oldFrom, _oldTo, newFrom, newTo) => {
      const remaining = transaction.mapping.slice(index + 1);
      const from = Math.max(0, remaining.map(newFrom, -1) - 1);
      const to = Math.min(state.doc.content.size, remaining.map(newTo, 1) + 1);
      addAt(from); addAt(to);
      state.doc.nodesBetween(from, to, (node, position) => {
        if (node.isTextblock) { changed.set(position, node); return false; }
      });
    });
  });
  for (const [position, node] of changed) {
    mapped = mapped.remove(mapped.find(position, position + node.nodeSize));
    mapped = mapped.add(state.doc, nodeDecorations(node, position, fallback));
  }
  return mapped;
}

function JotDirection(options: JotExtensionOptions) {
  const inputDirection = () => options.getInputDirection?.() || 'rtl';
  return Extension.create({
    name: 'jotDirection',
    addCommands() {
      return {
        setJotDirection: direction => ({ tr, state, dispatch }) => {
          if (direction !== null && !isDirection(direction)) return false;
          const { from, to, $from } = state.selection;
          const positions = new Set<number>();
          state.doc.nodesBetween(from, to, (node, position) => { if (node.isTextblock) positions.add(position); });
          if (!positions.size && $from.parent.isTextblock) positions.add($from.before());
          if (dispatch) {
            for (const position of positions) {
              const node = tr.doc.nodeAt(position);
              if (node) tr.setNodeMarkup(position, undefined, { ...node.attrs, jotDirection: direction });
            }
            tr.setMeta('jotBidiRefresh', true);
          }
          return positions.size > 0;
        },
      };
    },
    addProseMirrorPlugins() {
      return [new Plugin<DecorationSet>({
        key: bidiKey,
        state: {
          init: (_, state) => bidiDecorations(state, inputDirection()),
          apply: (transaction, value, _oldState, newState) => updateBidiDecorations(transaction, value, newState, inputDirection()),
        },
        props: { decorations: state => bidiKey.getState(state) || DecorationSet.empty },
        appendTransaction(transactions, _oldState, state) {
          if (!transactions.some(transaction => transaction.docChanged || transaction.getMeta('jotBidiRefresh'))) return null;
          const api = bidiApi(), transaction = state.tr;
          state.doc.descendants((node, position) => {
            if (!directionTypes.includes(node.type.name)) return;
            const fallback = inputDirection();
            let cached = directionCache.get(node);
            if (!cached || cached.fallback !== fallback) {
              const text = node.textContent;
              const empty = !text.replace(/[\u200b\u200c\u200d\ufeff]/g, '').trim();
              const manual = node.attrs.jotDirection;
              let neutral: Direction | null = null;
              if (!empty && !firstStrongDirection(text)) neutral = node.attrs.jotNeutralDirection || node.attrs.dir || fallback;
              const direction = node.type.name === 'codeBlock' ? 'ltr' : manual || neutral || (empty ? fallback : api?.paragraphDirection(text, fallback) || firstStrongDirection(text) || fallback);
              cached = { fallback, direction, neutral };
              directionCache.set(node, cached);
            }
            const { direction, neutral } = cached;
            if (node.attrs.dir !== direction || node.attrs.jotNeutralDirection !== neutral) {
              transaction.setNodeMarkup(position, undefined, { ...node.attrs, dir: direction, jotNeutralDirection: neutral });
            }
          });
          return transaction.docChanged ? transaction.setMeta('addToHistory', false) : null;
        },
      })];
    },
  });
}

export function createJotExtensions(options: JotExtensionOptions = {}): Extensions {
  return [
    StarterKit.configure({
      codeBlock: false,
      trailingNode: false,
      link: { openOnClick: false, autolink: false, linkOnPaste: false, protocols: ['http', 'https'], HTMLAttributes: { rel: 'noopener noreferrer', target: '_blank' } },
    }),
    TextStyleKit,
    Highlight.configure({ multicolor: true }),
    Subscript,
    Superscript,
    JotTextAlign,
    JotImage,
    Table.configure({ resizable: true, allowTableNodeSelection: true }),
    TableRow,
    TableCell,
    TableHeader,
    TaskList,
    TaskItem.configure({ nested: true }),
    CodeBlockLowlight.configure({ lowlight: createLowlight(common), defaultLanguage: 'plaintext', HTMLAttributes: { dir: 'ltr' } }),
    BidiIsolate,
    LegacyBlock,
    LegacyAttributes,
    JotDirection(options),
  ];
}

export { importLegacyHtml, validateJotRoundTrip } from './jot-html-compatibility';

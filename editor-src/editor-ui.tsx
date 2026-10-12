import { forwardRef, useEffect, useImperativeHandle, useRef, useState, type ReactNode } from 'react';
import { createPortal, flushSync } from 'react-dom';
import { createRoot } from 'react-dom/client';
import { Menu } from '@base-ui/react/menu';
import { Popover } from '@base-ui/react/popover';
import {
  BlockEditor, SlashCommand, defaultSlashCommandItems,
  type SlashCommandSuggestionItem,
} from '@editorcn/block-editor';
import { posToDOMRect, type ChainedCommands, type Editor, type Range } from '@tiptap/core';
import { DOMSerializer } from '@tiptap/pm/model';
import { PluginKey } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { exitSuggestion, type SuggestionProps, type SuggestionKeyDownProps } from '@tiptap/suggestion';
import { ReactRenderer, useEditorState } from '@tiptap/react';
import {
  AlignCenter, AlignJustify, AlignLeft, AlignRight, ArrowDownToLine, ArrowUpToLine,
  Bold, Check, ChevronDown, Code, CodeXml, Columns3, Copy, Heading1, Heading2,
  Heading3, Highlighter, ImagePlus, Italic, Link, List, ListChecks, ListOrdered,
  Ellipsis, Minus, Pilcrow, Plus, Quote, Redo2, RemoveFormatting, Rows3, Search, Strikethrough,
  Subscript, Superscript, Table2, TextCursorInput, Trash2, Underline, Undo2,
  Unlink, WrapText, type LucideIcon,
} from 'lucide-react';
import '@editorcn/block-editor/style.css';
import './editor-ui.css';

export interface EditorUiCallbacks {
  onImage: (editor: Editor, range?: Range) => void | Promise<void>;
  onError: (error: Error) => void;
  onCopyBlock?: (payload: { html: string; text: string }) => void | Promise<void>;
  getWritingPreferences?: () => WritingPreferences;
  setWritingPreference?: (key: 'fontSize' | 'lineHeight', value: number | null) => void | Promise<void>;
  onFind?: () => void;
  shortcutLabel?: (action:string)=>string;
}

export interface WritingPreferences {
  fontSize: number;
  lineHeight: number;
  fontSizeOverride: number | null;
  lineHeightOverride: number | null;
}

interface EditorUiOptions extends EditorUiCallbacks {
  editor: Editor;
  toolbarHost: HTMLElement;
  contentHost: HTMLElement;
}

const bindings = new WeakMap<Editor, EditorUiCallbacks>();
const dismissEvents = new WeakMap<Editor, EventTarget>();
const uiGenerations = new WeakMap<Editor, number>();
const jotSlashKey = new PluginKey('jotSlashCommand');
type EditorPanel = 'colors' | 'link' | 'table' | 'writing';
type PanelRequest = { kind: EditorPanel; range?: Range };

export function refreshEditorUi(editor: Editor): void {
  dismissEvents.get(editor)?.dispatchEvent(new Event('preferences'));
}

function openEditorPanel(editor: Editor, kind: EditorPanel, range?: Range) {
  exitSuggestion(editor.view, jotSlashKey);
  const doc = editor.state.doc;
  const generation = uiGenerations.get(editor) ?? 0;
  // Let the originating menu finish its focus restoration first. Otherwise an
  // overflow menu can steal focus back from the newly opened link input.
  requestAnimationFrame(() => {
    if (!editor.isDestroyed && editor.state.doc.eq(doc) && generation === (uiGenerations.get(editor) ?? 0))
      dismissEvents.get(editor)?.dispatchEvent(new CustomEvent<PanelRequest>('panel', { detail: {kind, range} }));
  });
}

function openFind(editor: Editor, callbacks: EditorUiCallbacks) {
  const generation=uiGenerations.get(editor)??0;
  requestAnimationFrame(()=>{
    if(!editor.isDestroyed&&generation===(uiGenerations.get(editor)??0))callbacks.onFind?.();
  });
}

/** Called by our narrowly patched upstream editorcn Copy button. */
export async function copyEditorBlock(editor: Editor): Promise<void> {
  const callbacks = bindings.get(editor);
  if (!callbacks?.onCopyBlock) {
    const error = new Error('Copy is not available. Please try Ctrl+C.');
    callbacks?.onError(error);
    throw error;
  }
  const { selection, schema, doc } = editor.state;
  let { from, to } = selection;
  let codeDepth = 0;
  for (let depth = selection.$from.depth; depth > 0; depth--) {
    if (selection.$from.node(depth).type.name === 'codeBlock') { codeDepth = depth; break; }
  }
  // The code-block Copy action means the complete snippet, even when only a
  // token is selected. Read its range without disturbing the user's selection.
  if (codeDepth) {
    from = selection.$from.before(codeDepth);
    to = selection.$from.after(codeDepth);
  } else if (selection.empty && selection.$from.depth > 0) {
    from = selection.$from.before(selection.$from.depth);
    to = selection.$from.after(selection.$from.depth);
  }
  const fragment = DOMSerializer.fromSchema(schema).serializeFragment(doc.slice(from, to).content);
  const container = document.createElement('div');
  container.append(fragment);
  try {
    await callbacks.onCopyBlock({ html: container.innerHTML, text: doc.textBetween(from, to, '\n', '\n') });
  } catch (error) {
    callbacks.onError(error instanceof Error ? error : new Error('Could not copy this block. Try again.'));
    throw error;
  }
}

export function dismissEditorUi(editor: Editor): void {
  uiGenerations.set(editor, (uiGenerations.get(editor) ?? 0) + 1);
  dismissEvents.get(editor)?.dispatchEvent(new Event('dismiss'));
  // Upstream bubble selectors own their open state. Their public dismiss surface
  // is the overlay; use it on document switches so a menu never follows a note.
  for (const overlay of document.querySelectorAll<HTMLElement>('.block-editor-bubble-overlay')) overlay.click();
  if (!editor.isDestroyed) exitSuggestion(editor.view, jotSlashKey);
}

function safeImage(callbacks: EditorUiCallbacks, editor: Editor, range?: Range) {
  try {
    void Promise.resolve(callbacks.onImage(editor, range)).catch(error =>
      callbacks.onError(error instanceof Error ? error : new Error('Could not insert the image.')),
    );
  } catch (error) {
    callbacks.onError(error instanceof Error ? error : new Error('Could not insert the image.'));
  }
}

export function createSlashCommandExtension(callbacks: EditorUiCallbacks) {
  type Command = ({editor, range}: {editor: Editor; range: Range}) => void;
  const action = (id: string, title: string, icon: LucideIcon, run: (chain: ChainedCommands) => ChainedCommands, keywords: string[] = []): SlashCommandSuggestionItem => {
    const Icon = icon;
    return {id, title, description: 'Apply at the current writing position.', icon: <Icon />, keywords,
      command: ({editor, range}) => { run(editor.chain().focus().deleteRange(range)).run(); }};
  };
  const panel = (id: string, title: string, kind: EditorPanel, icon: LucideIcon, keywords: string[]): SlashCommandSuggestionItem => {
    const Icon = icon;
    return {id, title, description: 'Choose options without losing your place.', icon: <Icon />, keywords,
      command: ({editor, range}) => openEditorPanel(editor, kind, range)};
  };
  const items: SlashCommandSuggestionItem[] = [
    ...defaultSlashCommandItems,
    panel('table', 'Table', 'table', Table2, ['table', 'grid', 'columns', 'جدول']),
    panel('textColor', 'Text color and highlight', 'colors', Highlighter, ['color', 'highlight', 'رنگ']),
    panel('link', 'Add or edit link', 'link', Link, ['link', 'url', 'پیوند']),
    panel('fontSize', 'Font size and line height', 'writing', TextCursorInput, ['font', 'size', 'spacing', 'line', 'height', 'فونت', 'فاصله']),
    {
      id: 'image', title: 'Image', description: 'Choose an image from your computer.',
      keywords: ['image', 'picture', 'photo', 'تصویر'], icon: <ImagePlus />,
      command: ({ editor, range }) => {
        // Native file selection is asynchronous. The host owns cancellation;
        // keep the slash prefix intact until it accepts an image.
        exitSuggestion(editor.view, jotSlashKey);
        safeImage(callbacks, editor, range);
      },
    },
    action('bold', 'Bold', Bold, chain => chain.toggleBold(), ['bold', 'strong']),
    action('italic', 'Italic', Italic, chain => chain.toggleItalic(), ['italic', 'emphasis']),
    action('underline', 'Underline', Underline, chain => chain.toggleUnderline(), ['underline']),
    action('strike', 'Strikethrough', Strikethrough, chain => chain.toggleStrike(), ['strike', 'delete']),
    action('inlineCode', 'Inline code', Code, chain => chain.toggleCode(), ['inline', 'code', 'کد']),
    action('subscript', 'Subscript', Subscript, chain => chain.toggleSubscript(), ['subscript']),
    action('superscript', 'Superscript', Superscript, chain => chain.toggleSuperscript(), ['superscript']),
    action('clearFormatting', 'Clear text formatting', RemoveFormatting, chain => chain.unsetAllMarks(), ['clear', 'format', 'reset']),
    action('unlink', 'Remove link', Unlink, chain => chain.extendMarkRange('link').unsetLink(), ['unlink', 'remove', 'link']),
    action('clearColor', 'Default text color', Highlighter, chain => chain.unsetColor(), ['color', 'reset']),
    action('clearHighlight', 'Remove highlight', Highlighter, chain => chain.unsetHighlight(), ['highlight', 'remove']),
    action('csharp', 'C# code block', CodeXml, chain => chain.toggleCodeBlock({language: 'csharp'}), ['c#', 'csharp', 'dotnet', 'code']),
    action('naturalAlignment', 'Natural alignment', WrapText, chain => chain.unsetTextAlign(), ['natural', 'align']),
    ...(['auto', 'ltr', 'rtl'] as const).map(direction => action(`direction-${direction}`,
      direction === 'auto' ? 'Automatic text direction' : direction === 'ltr' ? 'Left-to-right' : 'Right-to-left', WrapText,
      chain => chain.setJotDirection(direction === 'auto' ? null : direction), ['direction', direction, 'جهت'])),
    ...(['left', 'center', 'right', 'justify'] as const).map((alignment, index) => ({
      id: `align-${alignment}`, title: alignment === 'justify' ? 'Justify' : `Align ${alignment}`,
      description: 'Change paragraph alignment.', keywords: ['align', alignment],
      icon: [<AlignLeft />, <AlignCenter />, <AlignRight />, <AlignJustify />][index],
      command: ({ editor, range }: { editor: Editor; range: Range }) => {
        editor.chain().focus().deleteRange(range).setTextAlign(alignment).run();
      },
    })),
    ...[
      ['rowBefore', 'Add row above', ArrowUpToLine, (chain: ChainedCommands) => chain.addRowBefore()],
      ['rowAfter', 'Add row below', ArrowDownToLine, (chain: ChainedCommands) => chain.addRowAfter()],
      ['columnBefore', 'Add column before', Columns3, (chain: ChainedCommands) => chain.addColumnBefore()],
      ['columnAfter', 'Add column after', Columns3, (chain: ChainedCommands) => chain.addColumnAfter()],
      ['headerRow', 'Toggle header row', Rows3, (chain: ChainedCommands) => chain.toggleHeaderRow()],
      ['headerColumn', 'Toggle header column', Columns3, (chain: ChainedCommands) => chain.toggleHeaderColumn()],
      ['mergeCells', 'Merge cells', Table2, (chain: ChainedCommands) => chain.mergeCells()],
      ['splitCell', 'Split cell', Table2, (chain: ChainedCommands) => chain.splitCell()],
      ['deleteRow', 'Delete row', Rows3, (chain: ChainedCommands) => chain.deleteRow()],
      ['deleteColumn', 'Delete column', Columns3, (chain: ChainedCommands) => chain.deleteColumn()],
      ['deleteTable', 'Delete table', Trash2, (chain: ChainedCommands) => chain.deleteTable()],
    ].map(([id, title, icon, run]) => action(`table-${id}`, title as string, icon as LucideIcon,
      run as (chain: ChainedCommands) => ChainedCommands, ['table', 'row', 'column', 'جدول'])),
    {id: 'copyBlock', title: 'Copy block', description: 'Copy the current block.', icon: <Copy />, keywords: ['copy', 'block'],
      command: (({editor, range}) => {editor.chain().focus().deleteRange(range).run(); void copyEditorBlock(editor).catch(() => {});}) as Command},
    {id: 'find', title: 'Find in note', description: 'Search the current note.', icon: <Search />, keywords: ['find', 'search', 'جستجو'],
      command: (({editor, range}) => {editor.chain().focus().deleteRange(range).run(); openFind(editor,callbacks);}) as Command},
  ];
  return SlashCommand.configure({
    suggestion: {
      pluginKey: jotSlashKey,
      command: ({editor, range, props}) => {
        // A chosen command is one undo step, separate from typing its prefix.
        editor.view.dispatch(closeHistory(editor.state.tr));
        props.command({editor, range});
        editor.view.dispatch(closeHistory(editor.state.tr));
      },
      allow: ({ editor, state }) => editor.isEditable && !editor.view.composing &&
        !editor.isActive('codeBlock') && !editor.isActive('code') && state.selection.empty,
      items: ({ query, editor }) => {
        const term = query.trim().toLocaleLowerCase();
        return items.filter(item => {
          if (item.id.startsWith('table-') && !editor.isActive('table')) return false;
          if (item.id === 'table' && editor.isActive('table')) return false;
          if (item.id === 'table-mergeCells' && !editor.can().mergeCells()) return false;
          if (item.id === 'table-splitCell' && !editor.can().splitCell()) return false;
          if (item.id === 'find' && !callbacks.onFind) return false;
          if (item.id === 'copyBlock' && !bindings.get(editor)?.onCopyBlock) return false;
          return !term || [item.title, ...item.keywords].some(value => value.toLocaleLowerCase().includes(term));
        });
      },
      render: renderSlashCommands,
    },
  });
}

type SlashProps = SuggestionProps<SlashCommandSuggestionItem>;
type SlashHandle = { onKeyDown: (props: SuggestionKeyDownProps) => boolean };

const SlashCommands = forwardRef<SlashHandle, SlashProps>(function SlashCommands(props, ref) {
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState(0);
  const list = useRef<HTMLDivElement>(null);
  const results = props.items.filter(item => [item.title, ...item.keywords].some(text => text.toLocaleLowerCase().includes(search.toLocaleLowerCase())));
  useEffect(() => setSelected(0), [search, props.query]);
  useEffect(() => {
    const element = list.current?.children[selected] as HTMLElement | undefined;
    if (element && list.current) {
      const top = element.offsetTop - list.current.offsetTop;
      if (top < list.current.scrollTop) list.current.scrollTop = top;
      else if (top + element.offsetHeight > list.current.scrollTop + list.current.clientHeight)
        list.current.scrollTop = top + element.offsetHeight - list.current.clientHeight;
    }
  }, [selected]);
  const keyDown = (event: KeyboardEvent | React.KeyboardEvent) => {
    if (event.key === 'Escape') { exitSuggestion(props.editor.view, jotSlashKey); props.editor.commands.focus(); return true; }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      if (results.length) setSelected(value => (value + (event.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length);
      return true;
    }
    if (event.key === 'Enter') { if (results[selected]) props.command(results[selected]); return true; }
    return false;
  };
  useImperativeHandle(ref, () => ({onKeyDown: ({event}) => keyDown(event)}));
  return <div className="block-editor-slash-menu jot-slash-menu" role="dialog" aria-label="Writing commands">
    <div className="block-editor-slash-menu-search"><Search aria-hidden="true" />
      <input className="block-editor-slash-menu-search-input" aria-label="Search writing commands" placeholder="Search commands…" value={search}
        onChange={event => setSearch(event.target.value)} onKeyDown={event => { if (keyDown(event)) event.preventDefault(); }} />
    </div>
    <div className="block-editor-slash-menu-list" role="listbox" ref={list}>
      {results.map((item, index) => <button type="button" key={item.id} role="option" aria-selected={index === selected}
        className="block-editor-slash-menu-item" data-command={item.id} onMouseEnter={() => setSelected(index)}
        onMouseDown={event => event.preventDefault()} onClick={() => props.command(item)}>
        <span className="block-editor-slash-menu-item-icon">{item.icon}</span>
        <span className="block-editor-slash-menu-item-content"><span className="block-editor-slash-menu-item-label">{item.title}</span>
          <span className="block-editor-slash-menu-item-desc">{item.description}</span></span>
      </button>)}
      {!results.length && <div className="block-editor-slash-menu-empty">No matching commands</div>}
    </div>
  </div>;
});

function renderSlashCommands() {
  let component: ReactRenderer<SlashHandle, SlashProps> | undefined;
  let props: SlashProps | undefined;
  let frame = 0;
  let resize: ResizeObserver | undefined;
  const position = () => {
    frame = 0;
    if (!component || !props || props.editor.isDestroyed) return;
    const element = component.element as HTMLElement;
    const rect = posToDOMRect(props.editor.view, props.range.from, props.range.to);
    const writing = props.editor.view.dom.closest('#writingArea')?.getBoundingClientRect();
    const top = Math.max(8, writing?.top ?? 8), bottom = Math.min(innerHeight - 8, writing?.bottom ?? innerHeight - 8);
    if (rect.bottom < top || rect.top > bottom) { exitSuggestion(props.editor.view, jotSlashKey); return; }
    const below = Math.max(0, bottom - rect.bottom - 6), above = Math.max(0, rect.top - top - 6);
    const useAbove = below < 220 && above > below;
    const available = Math.max(44, useAbove ? above : below);
    element.style.setProperty('--jot-slash-height', `${Math.floor(available)}px`);
    element.style.width = `${Math.max(0, Math.min(300, innerWidth - 16))}px`;
    const height = Math.min(element.getBoundingClientRect().height, available);
    element.style.left = `${Math.max(8, Math.min(innerWidth - element.offsetWidth - 8, rect.left))}px`;
    element.style.top = `${Math.max(top, Math.min(bottom - height, useAbove ? rect.top - height - 6 : rect.bottom + 6))}px`;
    element.dataset.placement = useAbove ? 'top' : 'bottom';
  };
  const schedule = () => { if (!frame) frame = requestAnimationFrame(position); };
  const outside = (event: PointerEvent) => {
    if (props && !component?.element.contains(event.target as Node)) exitSuggestion(props.editor.view, jotSlashKey);
  };
  return {
    onStart(next: SlashProps) {
      props = next;
      component = new ReactRenderer(SlashCommands, {editor: next.editor, props: next});
      const element = component.element as HTMLElement;
      element.classList.add('jot-slash-positioner'); element.style.position = 'fixed';
      document.body.append(element); position();
      resize = new ResizeObserver(schedule); resize.observe(element);
      window.addEventListener('resize', schedule);
      document.addEventListener('scroll', schedule, true);
      document.addEventListener('pointerdown', outside, true);
    },
    onUpdate(next: SlashProps) {props = next; component?.updateProps(next); schedule();},
    onKeyDown(event: SuggestionKeyDownProps) { return component?.ref?.onKeyDown(event) ?? false; },
    onExit() {
      cancelAnimationFrame(frame); frame = 0; resize?.disconnect();
      window.removeEventListener('resize', schedule);
      document.removeEventListener('scroll', schedule, true);
      document.removeEventListener('pointerdown', outside, true);
      component?.element.remove(); component?.destroy(); component = undefined; props = undefined;
    },
  };
}

function useDismissableOpen(editor: Editor) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const target = dismissEvents.get(editor);
    const dismiss = () => setOpen(false);
    target?.addEventListener('dismiss', dismiss);
    window.addEventListener('blur', dismiss);
    return () => {
      target?.removeEventListener('dismiss', dismiss);
      window.removeEventListener('blur', dismiss);
    };
  }, [editor]);
  return [open, setOpen] as const;
}

function useEditorPanel(editor: Editor, kind: EditorPanel) {
  const [open, setOpen] = useDismissableOpen(editor);
  const [external, setExternal] = useState(false);
  const saved = useRef<{selection: typeof editor.state.selection; doc: typeof editor.state.doc; range?: Range; generation: number}>(undefined);
  const capture = (range?: Range) => { saved.current = {selection: editor.state.selection, doc: editor.state.doc, range, generation: uiGenerations.get(editor) ?? 0}; };
  useEffect(() => {
    const target = dismissEvents.get(editor);
    const request = (event: Event) => {
      const detail = (event as CustomEvent<PanelRequest>).detail;
      if (detail.kind !== kind) { setOpen(false); return; }
      capture(detail.range); setExternal(true); setOpen(true);
    };
    const dismiss = () => { saved.current = undefined; };
    target?.addEventListener('panel', request);
    target?.addEventListener('dismiss', dismiss);
    return () => { target?.removeEventListener('panel', request); target?.removeEventListener('dismiss', dismiss); };
  }, [editor, kind, setOpen]);
  function change(next: boolean) {
    if (next) {capture(); setExternal(false);}
    else saved.current = undefined;
    setOpen(next);
  }
  function apply(command: (chain: ChainedCommands) => ChainedCommands) {
    const snapshot = saved.current;
    if (!snapshot || snapshot.generation !== (uiGenerations.get(editor) ?? 0) || !editor.state.doc.eq(snapshot.doc)) {setOpen(false); return false;}
    editor.view.dispatch(closeHistory(editor.state.tr.setSelection(snapshot.selection)));
    let chain = editor.chain().focus();
    if (snapshot?.range) chain = chain.deleteRange(snapshot.range);
    const result = command(chain).run();
    editor.view.dispatch(closeHistory(editor.state.tr));
    if (result) { saved.current = undefined; setOpen(false); }
    return result;
  }
  const anchor = external ? {contextElement: editor.view.dom, getBoundingClientRect: () => {
    const position = Math.min(saved.current?.range?.to ?? saved.current?.selection.head ?? editor.state.selection.head, editor.state.doc.content.size);
    return posToDOMRect(editor.view, position, position);
  }} : undefined;
  return {open, change, setOpen, apply, anchor};
}

function ToolButton({ label, icon: Icon, active, disabled, onClick }: {
  label: string; icon: LucideIcon; active?: boolean; disabled?: boolean; onClick: () => void;
}) {
  return <button type="button" className="jot-tool" aria-label={label} title={label}
    aria-pressed={active} disabled={disabled} onMouseDown={event => event.preventDefault()} onClick={onClick}>
    <Icon aria-hidden="true" />
  </button>;
}

function ToolMenu({ editor, label, icon: Icon, text, children, active = false }: {
  editor: Editor; label: string; icon: LucideIcon; text?: string; children: ReactNode; active?: boolean;
}) {
  const [open, setOpen] = useDismissableOpen(editor);
  return <Menu.Root open={open} onOpenChange={setOpen} modal={false} disabled={!editor.isEditable}>
    <Menu.Trigger className={`jot-tool${text ? ' jot-tool-label' : ''}`} aria-label={label}
      title={label} data-active={active || undefined} onMouseDown={event => event.preventDefault()}>
      <Icon aria-hidden="true" />{text && <span>{text}</span>}<ChevronDown className="jot-chevron" aria-hidden="true" />
    </Menu.Trigger>
    <Menu.Portal>
      <Menu.Positioner className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Menu.Popup className="jot-editor-menu" finalFocus={() => document.activeElement instanceof HTMLElement && document.activeElement.closest('#noteFind:not([hidden])') ? document.activeElement : editor.view.dom} aria-label={label}>
          {children}
        </Menu.Popup>
      </Menu.Positioner>
    </Menu.Portal>
  </Menu.Root>;
}

function Action({ label, icon: Icon, run, active, disabled, danger }: {
  label: string; icon: LucideIcon; run: () => void; active?: boolean; disabled?: boolean; danger?: boolean;
}) {
  return <Menu.Item className="jot-editor-menu-item" disabled={disabled} onClick={run} data-danger={danger || undefined}>
    <Icon aria-hidden="true" /><span>{label}</span>{active && <Check className="jot-action-check" aria-hidden="true" />}
  </Menu.Item>;
}

function MenuSeparator() { return <div className="jot-editor-menu-separator" role="separator" />; }

const textColors = ['#e57373', '#ed9b52', '#d6b94b', '#79ae69', '#4cb9b1', '#6aa4db', '#b291df', '#d484b3'];
const highlightColors = ['#f8d568', '#f2ab83', '#ee9dba', '#c3ade7', '#9abde7', '#8bd1cc', '#b7d596', '#c0c0c0'];

function ColorPicker({ editor }: { editor: Editor }) {
  const panel = useEditorPanel(editor, 'colors');
  return <Popover.Root open={panel.open} onOpenChange={panel.change}>
    <Popover.Trigger className="jot-tool" aria-label="Text color and highlight" title="Text color and highlight"
      disabled={!editor.isEditable} onMouseDown={event => event.preventDefault()}><Highlighter aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner anchor={panel.anchor} className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-color-picker" finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Text color</Popover.Title>
          <div className="jot-color-grid" role="group" aria-label="Text colors">
            {textColors.map(color => <button key={color} type="button" className="jot-color-swatch"
              style={{ color }} aria-label={`Text color ${color}`} title={color}
              onClick={() => panel.apply(chain => chain.setColor(color))}>A</button>)}
          </div>
          <button className="jot-editor-text-action" type="button" onClick={() => panel.apply(chain => chain.unsetColor())}>Default text color</button>
          <div className="jot-editor-menu-separator" />
          <p className="jot-editor-popover-title">Highlight</p>
          <div className="jot-color-grid" role="group" aria-label="Highlight colors">
            {highlightColors.map(color => <button key={color} type="button" className="jot-color-swatch jot-highlight-swatch"
              style={{ backgroundColor: color }} aria-label={`Highlight ${color}`} title={color}
              onClick={() => panel.apply(chain => chain.setHighlight({color}))} />)}
          </div>
          <button className="jot-editor-text-action" type="button" onClick={() => panel.apply(chain => chain.unsetHighlight())}>Remove highlight</button>
        </Popover.Popup>
      </Popover.Positioner>
    </Popover.Portal>
  </Popover.Root>;
}

function LinkEditor({ editor }: { editor: Editor }) {
  const panel = useEditorPanel(editor, 'link');
  const [value, setValue] = useState('');
  const [error, setError] = useState('');
  const input = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (panel.open) {setValue(String(editor.getAttributes('link').href || '')); setError('');}
  }, [panel.open, editor]);
  function setLink() {
    const text = value.trim();
    const href = /^https?:\/\//i.test(text) ? text : `https://${text}`;
    try {
      const url = new URL(href);
      if (!text || !['http:', 'https:'].includes(url.protocol) || !url.hostname || /\s/.test(text) ||
          (/^[a-z][a-z\d+.-]*:/i.test(text) && !/^https?:\/\//i.test(text))) throw new Error();
      if (!panel.apply(chain => chain.extendMarkRange('link').setLink({href}))) throw new Error();
    } catch { setError('Enter a valid http or https website link.'); }
  }
  return <Popover.Root open={panel.open} onOpenChange={panel.change}>
    <Popover.Trigger className="jot-tool" aria-label="Edit link" title="Link" disabled={!editor.isEditable}
      data-active={editor.isActive('link') || undefined} onMouseDown={event => event.preventDefault()}><Link aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner anchor={panel.anchor} className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-link-editor" initialFocus={input} finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Link</Popover.Title>
          <form onSubmit={event => { event.preventDefault(); setLink(); }}>
            <input ref={input} type="text" aria-label="Link address" className="jot-editor-input" value={value}
              onChange={event => setValue(event.target.value)} placeholder="https://example.com" spellCheck={false}
              aria-invalid={!!error} aria-describedby={error ? 'jot-link-error' : undefined} />
            {error && <p id="jot-link-error" className="jot-editor-form-error" role="alert">{error}</p>}
            <div className="jot-editor-form-actions">
              {editor.isActive('link') && <button type="button" className="jot-editor-text-action" onClick={() => {
                panel.apply(chain => chain.extendMarkRange('link').unsetLink());
              }}><Unlink aria-hidden="true" />Remove</button>}
              <button className="jot-editor-submit" type="submit"><Check aria-hidden="true" />Apply</button>
            </div>
          </form>
        </Popover.Popup>
      </Popover.Positioner>
    </Popover.Portal>
  </Popover.Root>;
}

function TableInsert({ editor }: { editor: Editor }) {
  const panel = useEditorPanel(editor, 'table');
  const [rows, setRows] = useState('3');
  const [columns, setColumns] = useState('3');
  const [header, setHeader] = useState(true);
  return <Popover.Root open={panel.open} onOpenChange={panel.change}>
    <Popover.Trigger className="jot-tool" aria-label="Insert table" title="Insert table" disabled={!editor.isEditable || editor.isActive('table')}
      onMouseDown={event => event.preventDefault()}><Table2 aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner anchor={panel.anchor} className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-table-insert" finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Insert table</Popover.Title>
          <form onSubmit={event => {
            event.preventDefault();
            panel.apply(chain => chain.insertTable({ rows: Math.max(1, Math.min(30, Number(rows) || 3)), cols: Math.max(1, Math.min(12, Number(columns) || 3)), withHeaderRow: header }));
          }}>
            <div className="jot-table-dimensions">
              <label>Rows<input className="jot-editor-input" type="number" min="1" max="30" value={rows} onChange={event => setRows(event.target.value)} required /></label>
              <label>Columns<input className="jot-editor-input" type="number" min="1" max="12" value={columns} onChange={event => setColumns(event.target.value)} required /></label>
            </div>
            <label className="jot-table-header-option"><input type="checkbox" checked={header} onChange={event => setHeader(event.target.checked)} />Header row</label>
            <button type="submit" className="jot-editor-submit"><Plus aria-hidden="true" />Insert table</button>
          </form>
        </Popover.Popup>
      </Popover.Positioner>
    </Popover.Portal>
  </Popover.Root>;
}

function WritingPreferencesPicker({editor, callbacks}: {editor: Editor; callbacks: EditorUiCallbacks}) {
  const panel = useEditorPanel(editor, 'writing');
  const read = () => callbacks.getWritingPreferences?.() ?? {fontSize: 16, lineHeight: 1.95, fontSizeOverride: null, lineHeightOverride: null};
  const [preferences, setPreferences] = useState(read);
  const [pending, setPending] = useState(false);
  useEffect(() => {
    const refresh = () => setPreferences(previous => {
      const next = read();
      return previous.fontSize === next.fontSize && previous.lineHeight === next.lineHeight &&
        previous.fontSizeOverride === next.fontSizeOverride && previous.lineHeightOverride === next.lineHeightOverride ? previous : next;
    });
    const target = dismissEvents.get(editor);
    target?.addEventListener('preferences', refresh); editor.on('transaction', refresh);
    return () => {target?.removeEventListener('preferences', refresh); editor.off('transaction', refresh);};
  }, [editor, callbacks]);
  useEffect(() => { if (panel.open) setPreferences(read()); }, [panel.open]);
  async function setPreference(key: 'fontSize' | 'lineHeight', value: number | null) {
    if (!callbacks.setWritingPreference || pending) return;
    setPending(true);
    try {
      await callbacks.setWritingPreference(key, value);
      panel.apply(chain => chain); setPreferences(read());
    } catch (error) { callbacks.onError(error instanceof Error ? error : new Error('Could not change note typography.')); }
    finally { setPending(false); }
  }
  return <Popover.Root open={panel.open} onOpenChange={panel.change}>
    <Popover.Trigger className="jot-tool jot-tool-label jot-writing-trigger" aria-label="Font size and line height"
      title="Note font size and line height" disabled={!editor.isEditable || !callbacks.setWritingPreference}
      onMouseDown={event => event.preventDefault()}><TextCursorInput aria-hidden="true" />
      <span>{preferences.fontSize} px</span><ChevronDown className="jot-chevron" aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal><Popover.Positioner anchor={panel.anchor} className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
      <Popover.Popup className="jot-editor-popover jot-writing-preferences" finalFocus={() => editor.view.dom}>
        <Popover.Title className="jot-editor-popover-title">Note typography</Popover.Title>
        <div className="jot-writing-setting"><label htmlFor="jot-note-font-size">Font size</label>
          <select id="jot-note-font-size" className="jot-editor-input" value={preferences.fontSize} disabled={pending}
            onChange={event => void setPreference('fontSize', Number(event.target.value))}>
            {Array.from({length: 12}, (_, index) => 13 + index).map(size => <option key={size} value={size}>{size} px</option>)}
          </select>
          <button type="button" className="jot-editor-text-action" disabled={pending || preferences.fontSizeOverride === null}
            onClick={() => void setPreference('fontSize', null)}>Use default</button>
        </div>
        <div className="jot-writing-setting"><label htmlFor="jot-note-line-height">Line height</label>
          <select id="jot-note-line-height" className="jot-editor-input" value={preferences.lineHeight} disabled={pending}
            onChange={event => void setPreference('lineHeight', Number(event.target.value))}>
            {[...new Set([1.2, 1.5, 1.75, 1.95, 2.2, 2.5, preferences.lineHeight])].sort((a, b) => a - b)
              .map(height => <option key={height} value={height}>{height}</option>)}
          </select>
          <button type="button" className="jot-editor-text-action" disabled={pending || preferences.lineHeightOverride === null}
            onClick={() => void setPreference('lineHeight', null)}>Use default</button>
        </div>
        <p className="jot-writing-hint">This note only. Use default follows your app settings.</p>
        {pending && <p role="status" className="jot-writing-hint">Saving…</p>}
      </Popover.Popup>
    </Popover.Positioner></Popover.Portal>
  </Popover.Root>;
}

function AlignmentActions({editor}: {editor: Editor}) {
  const attributes = editor.getAttributes(editor.isActive('heading') ? 'heading' : 'paragraph');
  return <>
    <Action label="Natural alignment" icon={WrapText} active={!attributes.textAlign} run={() => editor.chain().focus().unsetTextAlign().run()} />
    {(['left', 'center', 'right', 'justify'] as const).map((alignment, index) => <Action key={alignment}
      label={alignment === 'justify' ? 'Justify' : `Align ${alignment}`} icon={[AlignLeft, AlignCenter, AlignRight, AlignJustify][index]}
      active={attributes.textAlign === alignment} run={() => editor.chain().focus().setTextAlign(alignment).run()} />)}
    <MenuSeparator />
    <Action label="Automatic text direction" icon={WrapText} active={!attributes.jotDirection} run={() => editor.chain().focus().setJotDirection(null).run()} />
    <Action label="Left-to-right" icon={AlignLeft} active={attributes.jotDirection === 'ltr'} run={() => editor.chain().focus().setJotDirection('ltr').run()} />
    <Action label="Right-to-left" icon={AlignRight} active={attributes.jotDirection === 'rtl'} run={() => editor.chain().focus().setJotDirection('rtl').run()} />
  </>;
}

function TableActions({editor}: {editor: Editor}) {
  return <>
    <Action label="Add row above" icon={ArrowUpToLine} run={() => editor.chain().focus().addRowBefore().run()} />
    <Action label="Add row below" icon={ArrowDownToLine} run={() => editor.chain().focus().addRowAfter().run()} />
    <Action label="Add column before" icon={Columns3} run={() => editor.chain().focus().addColumnBefore().run()} />
    <Action label="Add column after" icon={Columns3} run={() => editor.chain().focus().addColumnAfter().run()} />
    <MenuSeparator />
    <Action label="Toggle header row" icon={Rows3} run={() => editor.chain().focus().toggleHeaderRow().run()} />
    <Action label="Toggle header column" icon={Columns3} run={() => editor.chain().focus().toggleHeaderColumn().run()} />
    <Action label="Merge cells" icon={Table2} disabled={!editor.can().mergeCells()} run={() => editor.chain().focus().mergeCells().run()} />
    <Action label="Split cell" icon={Table2} disabled={!editor.can().splitCell()} run={() => editor.chain().focus().splitCell().run()} />
    <MenuSeparator />
    <Action label="Delete row" icon={Rows3} danger run={() => editor.chain().focus().deleteRow().run()} />
    <Action label="Delete column" icon={Columns3} danger run={() => editor.chain().focus().deleteColumn().run()} />
    <Action label="Delete table" icon={Trash2} danger run={() => editor.chain().focus().deleteTable().run()} />
  </>;
}

function EditorToolbar({ editor, callbacks }: { editor: Editor; callbacks: EditorUiCallbacks }) {
  const [,refreshShortcutLabels]=useState(0);
  useEffect(()=>{const update=()=>refreshShortcutLabels(value=>value+1);window.addEventListener('jot-shortcuts-changed',update);return()=>window.removeEventListener('jot-shortcuts-changed',update);},[]);
  const findShortcut=callbacks.shortcutLabel?.('find-note');
  const findLabel='Find in note'+(findShortcut?' · '+findShortcut:'');
  const bar = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(800);
  useEffect(() => {
    if (!bar.current) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)));
    observer.observe(bar.current); return () => observer.disconnect();
  }, []);
  const state = useEditorState({
    editor,
    selector: ({ editor: ed }) => ({
      editable: ed.isEditable,
      bold: ed.isActive('bold'), italic: ed.isActive('italic'), underline: ed.isActive('underline'),
      strike: ed.isActive('strike'), code: ed.isActive('code'), blockquote: ed.isActive('blockquote'),
      codeBlock: ed.isActive('codeBlock'), bulletList: ed.isActive('bulletList'),
      orderedList: ed.isActive('orderedList'), taskList: ed.isActive('taskList'),
      table: ed.isActive('table'), heading: Number(ed.getAttributes('heading').level || 0),
      link: ed.isActive('link'),
      undo: ed.can().undo(), redo: ed.can().redo(),
      align: String(ed.getAttributes(ed.isActive('heading') ? 'heading' : 'paragraph').textAlign || 'start'),
      direction: String(ed.getAttributes(ed.isActive('heading') ? 'heading' : 'paragraph').jotDirection || 'auto'),
    }),
  });
  const blockLabel = state.heading ? `Heading ${state.heading}` : state.codeBlock ? 'Code' :
    state.taskList ? 'Tasks' : state.bulletList ? 'Bullets' : state.orderedList ? 'Numbered' : state.blockquote ? 'Quote' : 'Text';
  const alignIcons = { left: AlignLeft, center: AlignCenter, right: AlignRight, justify: AlignJustify, start: AlignLeft };
  const AlignIcon = alignIcons[state.align as keyof typeof alignIcons] || AlignLeft;
  return <div ref={bar} className="jot-editor-toolbar" role="group" aria-label="Document tools" dir="ltr">
    <ToolButton label="Undo · Ctrl+Z" icon={Undo2} disabled={!state.editable || !state.undo} onClick={() => editor.chain().focus().undo().run()} />
    {width >= 420 && <ToolButton label="Redo · Ctrl+Y" icon={Redo2} disabled={!state.editable || !state.redo} onClick={() => editor.chain().focus().redo().run()} />}
    <ToolMenu editor={editor} label="Text style" icon={Pilcrow} text={blockLabel}>
      <Action label="Text" icon={Pilcrow} active={blockLabel === 'Text'} run={() => editor.chain().focus().setParagraph().run()} />
      <Action label="Heading 1" icon={Heading1} active={state.heading === 1} run={() => editor.chain().focus().toggleHeading({ level: 1 }).run()} />
      <Action label="Heading 2" icon={Heading2} active={state.heading === 2} run={() => editor.chain().focus().toggleHeading({ level: 2 }).run()} />
      <Action label="Heading 3" icon={Heading3} active={state.heading === 3} run={() => editor.chain().focus().toggleHeading({ level: 3 }).run()} />
      <MenuSeparator />
      <Action label="Bullet list" icon={List} active={state.bulletList} run={() => editor.chain().focus().toggleBulletList().run()} />
      <Action label="Numbered list" icon={ListOrdered} active={state.orderedList} run={() => editor.chain().focus().toggleOrderedList().run()} />
      <Action label="Task list" icon={ListChecks} active={state.taskList} run={() => editor.chain().focus().toggleTaskList().run()} />
      <Action label="Quote" icon={Quote} active={state.blockquote} run={() => editor.chain().focus().toggleBlockquote().run()} />
      <Action label="Code block" icon={CodeXml} active={state.codeBlock} run={() => editor.chain().focus().toggleCodeBlock({ language: 'plaintext' }).run()} />
    </ToolMenu>
    <ToolButton label="Bold · Ctrl+B" icon={Bold} active={state.bold} disabled={!state.editable} onClick={() => editor.chain().focus().toggleBold().run()} />
    <ToolButton label="Italic · Ctrl+I" icon={Italic} active={state.italic} disabled={!state.editable} onClick={() => editor.chain().focus().toggleItalic().run()} />
    {width >= 470 && <ToolButton label="Underline · Ctrl+U" icon={Underline} active={state.underline} disabled={!state.editable} onClick={() => editor.chain().focus().toggleUnderline().run()} />}
    {width >= 1050 && <>
      <ToolButton label="Strikethrough" icon={Strikethrough} active={state.strike} disabled={!state.editable} onClick={() => editor.chain().focus().toggleStrike().run()} />
      <ToolButton label="Inline code" icon={Code} active={state.code} disabled={!state.editable} onClick={() => editor.chain().focus().toggleCode().run()} />
    </>}
    <span className="jot-tool-slot" hidden={width < 530}><ColorPicker editor={editor} /></span>
    <LinkEditor editor={editor} />
    <ToolButton label="Remove link" icon={Unlink} disabled={!state.editable || !state.link}
      onClick={() => editor.chain().focus().extendMarkRange('link').unsetLink().run()} />
    <span className="jot-tool-slot" hidden={width < 630}><WritingPreferencesPicker editor={editor} callbacks={callbacks} /></span>
    {width >= 730 && <ToolMenu editor={editor} label="Alignment and direction" icon={AlignIcon}><AlignmentActions editor={editor} /></ToolMenu>}
    <span className="jot-tool-slot" hidden={width < 850}><TableInsert editor={editor} /></span>
    {width >= 850 && state.table && <ToolMenu editor={editor} label="Table options" icon={Table2} active><TableActions editor={editor} /></ToolMenu>}
    {width >= 850 && <ToolButton label="Image from computer" icon={ImagePlus} disabled={!state.editable} onClick={() => safeImage(callbacks, editor)} />}
    {width >= 1050 && <ToolButton label="Divider" icon={Minus} disabled={!state.editable} onClick={() => editor.chain().focus().setHorizontalRule().run()} />}
    {width >= 530 && callbacks.onFind && <ToolButton label={findLabel} icon={Search} onClick={() => openFind(editor,callbacks)} />}
    <span className="jot-toolbar-spacer" />
    <ToolMenu editor={editor} label="More editor tools" icon={Ellipsis}>
      {width < 420 && <Action label="Redo · Ctrl+Y" icon={Redo2} disabled={!state.redo} run={() => editor.chain().focus().redo().run()} />}
      {width < 470 && <Action label="Underline · Ctrl+U" icon={Underline} active={state.underline} run={() => editor.chain().focus().toggleUnderline().run()} />}
      {width < 1050 && <>
      <Action label="Strikethrough" icon={Strikethrough} active={state.strike} run={() => editor.chain().focus().toggleStrike().run()} />
      <Action label="Inline code" icon={Code} active={state.code} run={() => editor.chain().focus().toggleCode().run()} />
      </>}
      <Action label="Subscript" icon={Subscript} active={editor.isActive('subscript')} run={() => editor.chain().focus().toggleSubscript().run()} />
      <Action label="Superscript" icon={Superscript} active={editor.isActive('superscript')} run={() => editor.chain().focus().toggleSuperscript().run()} />
      <MenuSeparator />
      <Action label="Clear text formatting" icon={RemoveFormatting} run={() => editor.chain().focus().unsetAllMarks().run()} />
      {width < 530 && <Action label="Text color and highlight" icon={Highlighter} run={() => openEditorPanel(editor, 'colors')} />}
      {width < 630 && <Action label="Font size and line height" icon={TextCursorInput} run={() => openEditorPanel(editor, 'writing')} />}
      {width < 730 && <><MenuSeparator /><AlignmentActions editor={editor} /></>}
      <MenuSeparator />
      {width < 850 && <>
        {!state.table && <Action label="Insert table" icon={Table2} run={() => openEditorPanel(editor, 'table')} />}
        <Action label="Image from computer" icon={ImagePlus} run={() => safeImage(callbacks, editor)} />
      </>}
      {width < 1050 && <Action label="Divider" icon={Minus} run={() => editor.chain().focus().setHorizontalRule().run()} />}
      {width < 850 && state.table && <><MenuSeparator /><TableActions editor={editor} /></>}
      <MenuSeparator />
      <Action label="Copy block" icon={Copy} disabled={!callbacks.onCopyBlock} run={() => { void copyEditorBlock(editor).catch(() => {}); }} />
      {width < 530 && callbacks.onFind && <Action label={findLabel} icon={Search} run={() => openFind(editor,callbacks)} />}
    </ToolMenu>
  </div>;
}

/** Keeps React confined to the editing surface; the native Jot shell stays intact. */
export function mountEditorUi({ editor, toolbarHost, contentHost, ...callbacks }: EditorUiOptions): () => void {
  bindings.set(editor, callbacks);
  dismissEvents.set(editor, new EventTarget());
  uiGenerations.set(editor, 0);
  contentHost.dataset.jotEditorcn = 'true';
  const root = createRoot(contentHost);
  flushSync(() => root.render(
    <BlockEditor editor={editor} className="jot-block-editor">
      <BlockEditor.Content />
      {createPortal(<EditorToolbar editor={editor} callbacks={callbacks} />, toolbarHost)}
    </BlockEditor>,
  ));
  const dismissUpstreamMenu = (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || event.isComposing || !document.querySelector('.block-editor-bubble-overlay')) return;
    event.preventDefault();
    dismissEditorUi(editor);
    editor.commands.focus();
  };
  const dismissOnBlur = () => dismissEditorUi(editor);
  document.addEventListener('keydown', dismissUpstreamMenu, true);
  window.addEventListener('blur', dismissOnBlur);
  return () => {
    document.removeEventListener('keydown', dismissUpstreamMenu, true);
    window.removeEventListener('blur', dismissOnBlur);
    dismissEditorUi(editor);
    root.unmount();
    dismissEvents.delete(editor);
    uiGenerations.delete(editor);
    bindings.delete(editor);
    delete contentHost.dataset.jotEditorcn;
  };
}

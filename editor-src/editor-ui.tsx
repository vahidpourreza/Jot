import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal, flushSync } from 'react-dom';
import { createRoot } from 'react-dom/client';
import { Menu } from '@base-ui/react/menu';
import { Popover } from '@base-ui/react/popover';
import {
  BlockEditor, SlashCommand, defaultSlashCommandItems, getSlashCommandSuggestion,
  type SlashCommandSuggestionItem,
} from '@editorcn/block-editor';
import type { Editor, Range } from '@tiptap/core';
import { DOMSerializer } from '@tiptap/pm/model';
import { PluginKey } from '@tiptap/pm/state';
import { exitSuggestion } from '@tiptap/suggestion';
import { useEditorState } from '@tiptap/react';
import {
  AlignCenter, AlignJustify, AlignLeft, AlignRight, ArrowDownToLine, ArrowUpToLine,
  Bold, Check, ChevronDown, Code, CodeXml, Columns3, Copy, Heading1, Heading2,
  Heading3, Highlighter, ImagePlus, Italic, Link, List, ListChecks, ListOrdered,
  Minus, Pilcrow, Plus, Quote, Redo2, RemoveFormatting, Rows3, Strikethrough,
  Subscript, Superscript, Table2, TextCursorInput, Trash2, Underline, Undo2,
  Unlink, WrapText, type LucideIcon,
} from 'lucide-react';
import '@editorcn/block-editor/style.css';
import './editor-ui.css';

export interface EditorUiCallbacks {
  onImage: (editor: Editor, range?: Range) => void | Promise<void>;
  onError: (error: Error) => void;
  onCopyBlock?: (payload: { html: string; text: string }) => void | Promise<void>;
}

interface EditorUiOptions extends EditorUiCallbacks {
  editor: Editor;
  toolbarHost: HTMLElement;
  contentHost: HTMLElement;
}

const bindings = new WeakMap<Editor, EditorUiCallbacks>();
const dismissEvents = new WeakMap<Editor, EventTarget>();
const jotSlashKey = new PluginKey('jotSlashCommand');

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
  const items: SlashCommandSuggestionItem[] = [
    ...defaultSlashCommandItems,
    {
      id: 'table', title: 'Table', description: 'A table with a header row.',
      keywords: ['table', 'grid', 'columns', 'جدول'], icon: <Table2 />,
      command: ({ editor, range }) => {
        editor.chain().focus().deleteRange(range).insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run();
      },
    },
    {
      id: 'image', title: 'Image', description: 'Choose an image from your computer.',
      keywords: ['image', 'picture', 'photo', 'تصویر'], icon: <ImagePlus />,
      command: ({ editor, range }) => {
        editor.chain().focus().deleteRange(range).run();
        safeImage(callbacks, editor);
      },
    },
    {
      id: 'inlineCode', title: 'Inline code', description: 'Format a short code fragment.',
      keywords: ['inline', 'code', 'کد'], icon: <Code />,
      command: ({ editor, range }) => { editor.chain().focus().deleteRange(range).toggleCode().run(); },
    },
    ...(['left', 'center', 'right', 'justify'] as const).map((alignment, index) => ({
      id: `align-${alignment}`, title: alignment === 'justify' ? 'Justify' : `Align ${alignment}`,
      description: 'Change paragraph alignment.', keywords: ['align', alignment],
      icon: [<AlignLeft />, <AlignCenter />, <AlignRight />, <AlignJustify />][index],
      command: ({ editor, range }: { editor: Editor; range: Range }) => {
        editor.chain().focus().deleteRange(range).setTextAlign(alignment).run();
      },
    })),
  ];
  const suggestion = getSlashCommandSuggestion(items);
  const originalRender = suggestion.render;
  return SlashCommand.configure({
    suggestion: {
      ...suggestion,
      pluginKey: jotSlashKey,
      allow: ({ editor, state }) => editor.isEditable && !editor.view.composing &&
        !editor.isActive('codeBlock') && !editor.isActive('code') && state.selection.empty,
      items: ({ query }) => {
        const term = query.trim().toLocaleLowerCase();
        return items.filter(item => !term || [item.title, ...item.keywords]
          .some(value => value.toLocaleLowerCase().includes(term)));
      },
      render: () => {
        const renderer = originalRender?.() ?? {};
        return {
          ...renderer,
          onKeyDown(props) {
            if (props.event.key === 'Escape') {
              exitSuggestion(props.view, jotSlashKey);
              return true;
            }
            return renderer.onKeyDown?.(props) ?? false;
          },
        };
      },
    },
  });
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

function ToolButton({ label, icon: Icon, active, disabled, onClick }: {
  label: string; icon: LucideIcon; active?: boolean; disabled?: boolean; onClick: () => void;
}) {
  return <button type="button" className="jot-tool" aria-label={label} title={label}
    aria-pressed={active} disabled={disabled} onMouseDown={event => event.preventDefault()} onClick={onClick}>
    <Icon aria-hidden="true" />
  </button>;
}

function Separator() { return <span className="jot-tool-separator" aria-hidden="true" />; }

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
        <Menu.Popup className="jot-editor-menu" finalFocus={() => editor.view.dom} aria-label={label}>
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
  const [open, setOpen] = useDismissableOpen(editor);
  return <Popover.Root open={open} onOpenChange={setOpen}>
    <Popover.Trigger className="jot-tool" aria-label="Text color and highlight" title="Text color and highlight"
      disabled={!editor.isEditable} onMouseDown={event => event.preventDefault()}><Highlighter aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-color-picker" finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Text color</Popover.Title>
          <div className="jot-color-grid" role="group" aria-label="Text colors">
            {textColors.map(color => <button key={color} type="button" className="jot-color-swatch"
              style={{ color }} aria-label={`Text color ${color}`} title={color}
              onClick={() => { editor.chain().focus().setColor(color).run(); setOpen(false); }}>A</button>)}
          </div>
          <button className="jot-editor-text-action" type="button" onClick={() => { editor.chain().focus().unsetColor().run(); setOpen(false); }}>Default text color</button>
          <div className="jot-editor-menu-separator" />
          <p className="jot-editor-popover-title">Highlight</p>
          <div className="jot-color-grid" role="group" aria-label="Highlight colors">
            {highlightColors.map(color => <button key={color} type="button" className="jot-color-swatch jot-highlight-swatch"
              style={{ backgroundColor: color }} aria-label={`Highlight ${color}`} title={color}
              onClick={() => { editor.chain().focus().setHighlight({ color }).run(); setOpen(false); }} />)}
          </div>
          <button className="jot-editor-text-action" type="button" onClick={() => { editor.chain().focus().unsetHighlight().run(); setOpen(false); }}>Remove highlight</button>
        </Popover.Popup>
      </Popover.Positioner>
    </Popover.Portal>
  </Popover.Root>;
}

function LinkEditor({ editor }: { editor: Editor }) {
  const [open, setOpen] = useDismissableOpen(editor);
  const [value, setValue] = useState('');
  const [error, setError] = useState('');
  const input = useRef<HTMLInputElement>(null);
  function setLink() {
    const text = value.trim();
    const href = /^https?:\/\//i.test(text) ? text : `https://${text}`;
    try {
      const url = new URL(href);
      if (!text || !['http:', 'https:'].includes(url.protocol) || !url.hostname || /\s/.test(text) ||
          (/^[a-z][a-z\d+.-]*:/i.test(text) && !/^https?:\/\//i.test(text))) throw new Error();
      if (!editor.chain().focus().extendMarkRange('link').setLink({ href }).run()) throw new Error();
      setOpen(false);
    } catch { setError('Enter a valid http or https website link.'); }
  }
  return <Popover.Root open={open} onOpenChange={next => {
    if (next) { setValue(String(editor.getAttributes('link').href || '')); setError(''); }
    setOpen(next);
  }}>
    <Popover.Trigger className="jot-tool" aria-label="Edit link" title="Link" disabled={!editor.isEditable}
      data-active={editor.isActive('link') || undefined} onMouseDown={event => event.preventDefault()}><Link aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-link-editor" initialFocus={input} finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Link</Popover.Title>
          <form onSubmit={event => { event.preventDefault(); setLink(); }}>
            <input ref={input} type="text" aria-label="Link address" className="jot-editor-input" value={value}
              onChange={event => setValue(event.target.value)} placeholder="https://example.com" spellCheck={false}
              aria-invalid={!!error} aria-describedby={error ? 'jot-link-error' : undefined} />
            {error && <p id="jot-link-error" className="jot-editor-form-error" role="alert">{error}</p>}
            <div className="jot-editor-form-actions">
              {editor.isActive('link') && <button type="button" className="jot-editor-text-action" onClick={() => {
                editor.chain().focus().extendMarkRange('link').unsetLink().run(); setOpen(false);
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
  const [open, setOpen] = useDismissableOpen(editor);
  const [rows, setRows] = useState('3');
  const [columns, setColumns] = useState('3');
  const [header, setHeader] = useState(true);
  return <Popover.Root open={open} onOpenChange={setOpen}>
    <Popover.Trigger className="jot-tool" aria-label="Insert table" title="Insert table" disabled={!editor.isEditable || editor.isActive('table')}
      onMouseDown={event => event.preventDefault()}><Table2 aria-hidden="true" /></Popover.Trigger>
    <Popover.Portal>
      <Popover.Positioner className="jot-editor-positioner" sideOffset={6} align="start" collisionPadding={8}>
        <Popover.Popup className="jot-editor-popover jot-table-insert" finalFocus={() => editor.view.dom}>
          <Popover.Title className="jot-editor-popover-title">Insert table</Popover.Title>
          <form onSubmit={event => {
            event.preventDefault();
            editor.chain().focus().insertTable({ rows: Math.max(1, Math.min(30, Number(rows) || 3)), cols: Math.max(1, Math.min(12, Number(columns) || 3)), withHeaderRow: header }).run();
            setOpen(false);
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

function EditorToolbar({ editor, callbacks }: { editor: Editor; callbacks: EditorUiCallbacks }) {
  const state = useEditorState({
    editor,
    selector: ({ editor: ed }) => ({
      editable: ed.isEditable,
      bold: ed.isActive('bold'), italic: ed.isActive('italic'), underline: ed.isActive('underline'),
      strike: ed.isActive('strike'), code: ed.isActive('code'), blockquote: ed.isActive('blockquote'),
      codeBlock: ed.isActive('codeBlock'), bulletList: ed.isActive('bulletList'),
      orderedList: ed.isActive('orderedList'), taskList: ed.isActive('taskList'),
      table: ed.isActive('table'), heading: Number(ed.getAttributes('heading').level || 0),
      undo: ed.can().undo(), redo: ed.can().redo(),
      align: String(ed.getAttributes(ed.isActive('heading') ? 'heading' : 'paragraph').textAlign || 'start'),
      direction: String(ed.getAttributes(ed.isActive('heading') ? 'heading' : 'paragraph').jotDirection || 'auto'),
    }),
  });
  const blockLabel = state.heading ? `Heading ${state.heading}` : state.codeBlock ? 'Code' :
    state.taskList ? 'Tasks' : state.bulletList ? 'Bullets' : state.orderedList ? 'Numbered' : state.blockquote ? 'Quote' : 'Text';
  const alignIcons = { left: AlignLeft, center: AlignCenter, right: AlignRight, justify: AlignJustify, start: AlignLeft };
  const AlignIcon = alignIcons[state.align as keyof typeof alignIcons] || AlignLeft;
  return <div className="jot-editor-toolbar" role="group" aria-label="Document tools" dir="ltr">
    <ToolButton label="Undo · Ctrl+Z" icon={Undo2} disabled={!state.editable || !state.undo} onClick={() => editor.chain().focus().undo().run()} />
    <ToolButton label="Redo · Ctrl+Y" icon={Redo2} disabled={!state.editable || !state.redo} onClick={() => editor.chain().focus().redo().run()} />
    <Separator />
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
    <Separator />
    <ToolButton label="Bold · Ctrl+B" icon={Bold} active={state.bold} disabled={!state.editable} onClick={() => editor.chain().focus().toggleBold().run()} />
    <ToolButton label="Italic · Ctrl+I" icon={Italic} active={state.italic} disabled={!state.editable} onClick={() => editor.chain().focus().toggleItalic().run()} />
    <ToolButton label="Underline · Ctrl+U" icon={Underline} active={state.underline} disabled={!state.editable} onClick={() => editor.chain().focus().toggleUnderline().run()} />
    <ToolMenu editor={editor} label="More text formatting" icon={TextCursorInput}>
      <Action label="Strikethrough" icon={Strikethrough} active={state.strike} run={() => editor.chain().focus().toggleStrike().run()} />
      <Action label="Inline code" icon={Code} active={state.code} run={() => editor.chain().focus().toggleCode().run()} />
      <Action label="Subscript" icon={Subscript} active={editor.isActive('subscript')} run={() => editor.chain().focus().toggleSubscript().run()} />
      <Action label="Superscript" icon={Superscript} active={editor.isActive('superscript')} run={() => editor.chain().focus().toggleSuperscript().run()} />
      <MenuSeparator />
      <Action label="Clear text formatting" icon={RemoveFormatting} run={() => editor.chain().focus().unsetAllMarks().run()} />
    </ToolMenu>
    <ColorPicker editor={editor} />
    <LinkEditor editor={editor} />
    <Separator />
    <ToolMenu editor={editor} label="Alignment and direction" icon={AlignIcon}>
      <Action label="Natural alignment" icon={WrapText} active={state.align === 'start'} run={() => editor.chain().focus().unsetTextAlign().run()} />
      <Action label="Align left" icon={AlignLeft} active={state.align === 'left'} run={() => editor.chain().focus().setTextAlign('left').run()} />
      <Action label="Align center" icon={AlignCenter} active={state.align === 'center'} run={() => editor.chain().focus().setTextAlign('center').run()} />
      <Action label="Align right" icon={AlignRight} active={state.align === 'right'} run={() => editor.chain().focus().setTextAlign('right').run()} />
      <Action label="Justify" icon={AlignJustify} active={state.align === 'justify'} run={() => editor.chain().focus().setTextAlign('justify').run()} />
      <MenuSeparator />
      <Action label="Automatic text direction" icon={WrapText} active={state.direction === 'auto'} run={() => editor.chain().focus().setJotDirection(null).run()} />
      <Action label="Left-to-right" icon={AlignLeft} active={state.direction === 'ltr'} run={() => editor.chain().focus().setJotDirection('ltr').run()} />
      <Action label="Right-to-left" icon={AlignRight} active={state.direction === 'rtl'} run={() => editor.chain().focus().setJotDirection('rtl').run()} />
    </ToolMenu>
    <TableInsert editor={editor} />
    {state.table && <ToolMenu editor={editor} label="Table options" icon={Table2} text="Table" active>
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
    </ToolMenu>}
    <ToolMenu editor={editor} label="Insert and block actions" icon={Plus}>
      <Action label="Image from computer" icon={ImagePlus} run={() => safeImage(callbacks, editor)} />
      <Action label="Divider" icon={Minus} run={() => editor.chain().focus().setHorizontalRule().run()} />
      <MenuSeparator />
      <Action label="Copy block" icon={Copy} disabled={!callbacks.onCopyBlock} run={() => { void copyEditorBlock(editor).catch(() => {}); }} />
    </ToolMenu>
  </div>;
}

/** Keeps React confined to the editing surface; the native Jot shell stays intact. */
export function mountEditorUi({ editor, toolbarHost, contentHost, ...callbacks }: EditorUiOptions): () => void {
  bindings.set(editor, callbacks);
  dismissEvents.set(editor, new EventTarget());
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
    bindings.delete(editor);
    delete contentHost.dataset.jotEditorcn;
  };
}

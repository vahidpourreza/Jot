export interface LegacyImportResult {
  html: string;
  warnings: string[];
  unsupported: string[];
  safe: boolean;
}

const supportedTags = new Set('P DIV BR B BDI STRONG I EM U S STRIKE SPAN H1 H2 H3 H4 H5 H6 UL OL LI BLOCKQUOTE PRE CODE IMG A FONT TABLE THEAD TBODY TFOOT TR TH TD CAPTION HR MARK SUB SUP FIGURE FIGCAPTION COLGROUP COL LABEL INPUT'.split(' '));
const blockTags = 'p,div,h1,h2,h3,h4,h5,h6,ul,ol,li,blockquote,pre,table,figure,figcaption,hr';
const supportedStyles = new Set(['color', 'background-color', 'font-size', 'font-family', 'font-weight', 'font-style', 'text-decoration', 'text-decoration-line', 'text-align', 'line-height', 'width', 'height', 'min-width', 'white-space', 'direction', 'unicode-bidi', 'border-collapse', 'table-layout']);
const supportedAttributes = new Set(['dir', 'style', 'color', 'face', 'size', 'src', 'alt', 'title', 'href', 'rel', 'target', 'colspan', 'rowspan', 'start', 'value', 'align', 'width', 'height', 'colwidth', 'data-colwidth', 'class', 'data-type', 'data-checked', 'type', 'checked', 'contenteditable', 'aria-label', 'data-color', 'data-language', 'data-jot-direction', 'data-jot-neutral-direction', 'data-jot-align', 'data-jot-plain-line', 'data-jot-bidi', 'data-empty-block']);
const imagePattern = /^data:image\/(?:png|jpeg|webp|gif);base64,[a-z0-9+/=\s]+$/i;

function replaceTag(element: Element, tag: string): HTMLElement {
  const replacement = document.createElement(tag);
  for (const attribute of element.attributes) replacement.setAttribute(attribute.name, attribute.value);
  replacement.append(...element.childNodes);
  element.replaceWith(replacement);
  return replacement;
}

/** Validate before importing: never silently discard note content to fit a schema. */
export function importLegacyHtml(html: string): LegacyImportResult {
  const documentHtml = new DOMParser().parseFromString(String(html || '<p></p>'), 'text/html');
  const warnings = new Set<string>(), unsupported = new Set<string>();
  for (const element of documentHtml.head.children) unsupported.add(`Unsupported document header: ${element.tagName.toLowerCase()}`);
  for (const element of documentHtml.body.querySelectorAll<HTMLElement>('*')) {
    const tag = element.tagName;
    if (!supportedTags.has(tag)) unsupported.add(`Unsupported element: ${tag.toLowerCase()}`);
    for (const attribute of element.attributes) {
      if (!supportedAttributes.has(attribute.name) || /^on/i.test(attribute.name)) unsupported.add(`Unsupported ${tag.toLowerCase()} attribute: ${attribute.name}`);
    }
    if (tag === 'IMG' && (!imagePattern.test(element.getAttribute('src') || '') || (element.getAttribute('src') || '').length > 12 * 1024 * 1024)) unsupported.add('Image must be an embedded PNG, JPEG, WebP or GIF no larger than 12 MB.');
    if (tag === 'A' && element.getAttribute('href') && !/^https?:\/\//i.test(element.getAttribute('href') || '')) unsupported.add('Unsupported link protocol.');
    for (const property of Array.from(element.style)) {
      const value = element.style.getPropertyValue(property);
      if (!supportedStyles.has(property) || /url\s*\(|var\s*\(|expression\s*\(/i.test(value)) unsupported.add(`Unsupported style: ${property}`);
    }
    // A checkbox is allowed only as the generated non-document part of a task.
    if (tag === 'INPUT' && !(element.getAttribute('type') === 'checkbox' && element.closest('li[data-type="taskItem"]'))) unsupported.add('Unsupported form control.');
    if (tag === 'LABEL' && !element.closest('li[data-type="taskItem"]')) unsupported.add('Unsupported label outside a task item.');
    if (tag === 'PRE' && element.querySelector('img,table,b,strong,i,em,u,s,strike,mark,sub,sup')) unsupported.add('Rich content inside a code block needs explicit conversion.');
    if (tag === 'PRE' && [...element.querySelectorAll<HTMLElement>('*')].some(child => child.style.cssText)) unsupported.add('Styled text inside a code block needs explicit conversion.');
  }

  for (const element of [...documentHtml.body.querySelectorAll<HTMLElement>('font')]) {
    const replacement = replaceTag(element, 'span');
    if (element.getAttribute('color') && !replacement.style.color) replacement.style.color = element.getAttribute('color')!;
    if (element.getAttribute('face') && !replacement.style.fontFamily) replacement.style.fontFamily = element.getAttribute('face')!;
    if (element.hasAttribute('size')) {
      const sizes = ['8', '10', '12', '14', '18', '24', '36'];
      const value = Number(element.getAttribute('size'));
      if (Number.isInteger(value) && value >= 1 && value <= 7 && !replacement.style.fontSize) replacement.style.fontSize = sizes[value - 1] + 'pt';
      else unsupported.add('Unsupported legacy font size.');
    }
    for (const attribute of ['color', 'face', 'size']) replacement.removeAttribute(attribute);
  }

  // Undo the old display-only wrappers. The exact underlying Unicode text is
  // retained; the editor recreates isolation with ProseMirror decorations.
  for (const isolate of [...documentHtml.body.querySelectorAll('bdi[data-jot-bidi="token"]')]) isolate.replaceWith(...isolate.childNodes);
  for (const element of [...documentHtml.body.querySelectorAll('strike')]) replaceTag(element, 's');
  for (const element of [...documentHtml.body.querySelectorAll<HTMLElement>('div')].reverse()) {
    if (!element.querySelector(blockTags)) replaceTag(element, 'p');
  }
  // Table captions are real document text; preserve them as a paragraph directly
  // before the table (Tiptap tables have rows as their only schema children).
  for (const caption of [...documentHtml.body.querySelectorAll('caption')]) {
    const table = caption.closest('table');
    if (!table) { unsupported.add('A table caption has no table.'); continue; }
    const paragraph = document.createElement('p');
    for (const attribute of caption.attributes) paragraph.setAttribute(attribute.name, attribute.value);
    paragraph.append(...caption.childNodes);
    table.before(paragraph);
    caption.remove();
    warnings.add('Table caption retained as a paragraph immediately before the table.');
  }
  // ProseMirror's block containers require paragraphs around loose inline
  // children. Do that explicitly, before comparing the migration round trip.
  // In particular a figure's image and caption remain separate visible lines.
  for (const container of documentHtml.body.querySelectorAll('div,figure,figcaption,blockquote,td,th,li')) {
    let paragraph: HTMLParagraphElement | null = null;
    for (const child of [...container.childNodes]) {
      if (child instanceof Element && (child.matches(blockTags) || child.matches('label,input'))) { paragraph = null; continue; }
      if (child.nodeType === 3 && !child.textContent?.trim() && !paragraph) continue;
      if (!paragraph) {
        paragraph = document.createElement('p');
        for (const attribute of ['dir', 'data-jot-direction', 'data-jot-neutral-direction', 'data-jot-align']) {
          if (container.hasAttribute(attribute)) paragraph.setAttribute(attribute, container.getAttribute(attribute)!);
        }
        if (container instanceof HTMLElement && container.style.textAlign) paragraph.style.textAlign = container.style.textAlign;
        container.insertBefore(paragraph, child);
      }
      paragraph.append(child);
    }
  }
  for (const element of documentHtml.body.querySelectorAll<HTMLElement>('*')) {
    if (element.hasAttribute('align') && !element.style.textAlign) element.style.textAlign = element.getAttribute('align')!;
    if (element.style.direction && !element.getAttribute('data-jot-direction')) element.setAttribute('data-jot-direction', element.style.direction);
    // TextStyle parses spans, whereas old execCommand could put the same style
    // on a bold/link/underline element. Preserve that layer independently.
    if (!element.matches(`${blockTags},td,th,tr,span,img,br,col,colgroup`) && element.style.cssText && !element.closest('pre')) {
      const span = document.createElement('span');
      span.style.cssText = element.style.cssText;
      span.append(...element.childNodes);
      element.append(span);
      element.removeAttribute('style');
    }
  }
  return { html: documentHtml.body.innerHTML || '<p></p>', warnings: [...warnings], unsupported: [...unsupported], safe: unsupported.size === 0 };
}

function logicalText(root: Element): string {
  const blocks = new Set('P DIV H1 H2 H3 H4 H5 H6 UL OL LI BLOCKQUOTE PRE TABLE THEAD TBODY TFOOT TR FIGURE FIGCAPTION HR'.split(' '));
  function content(node: globalThis.Node): string {
    if (node.nodeType === 3) return node.textContent || '';
    if (node instanceof Element) {
      if (node.tagName === 'IMG' || node.tagName === 'COLGROUP') return '';
      if (node.tagName === 'BR') return '\n';
      if (node.tagName === 'TR') return [...node.children].filter(child => child.matches('td,th')).map(content).join('\t');
      if (blocks.has(node.tagName) && node.childNodes.length === 1 && node.firstChild?.nodeName === 'BR') return '';
    }
    let output = '', previousBlock = false, hasContent = false;
    for (const child of node.childNodes) {
      const block = child instanceof Element && blocks.has(child.tagName);
      if (child.nodeType === 3 && !child.textContent?.trim() && [child.previousSibling, child.nextSibling].some(sibling => sibling instanceof Element && blocks.has(sibling.tagName))) continue;
      const value = content(child);
      if (block) { if (hasContent) output += '\n'; output += value; hasContent = true; previousBlock = true; }
      else if (value) { if (previousBlock) output += '\n'; output += value; hasContent = true; previousBlock = false; }
    }
    return output;
  }
  return content(root);
}

function textFormatting(root: Element) {
  const segments: Array<{ text: string; format: Record<string, string> }> = [];
  const walk = (node: globalThis.Node, inherited: Record<string, string>) => {
    if (node.nodeType === 3) {
      const text = node.textContent || '';
      if (!text) return;
      if (!text.trim() && [node.previousSibling, node.nextSibling].some(sibling => sibling instanceof Element && sibling.matches(blockTags))) return;
      const last = segments.at(-1);
      if (last && JSON.stringify(last.format) === JSON.stringify(inherited)) last.text += text;
      else segments.push({ text, format: inherited });
      return;
    }
    if (!(node instanceof HTMLElement)) return;
    const format = { ...inherited }, tag = node.tagName;
    if (['B', 'STRONG'].includes(tag)) format.bold = 'true';
    if (['I', 'EM'].includes(tag)) format.italic = 'true';
    if (tag === 'U') format.underline = 'true';
    if (['S', 'STRIKE'].includes(tag)) format.strike = 'true';
    if (tag === 'CODE' && !node.closest('pre')) format.code = 'true';
    if (tag === 'SUB') format.subscript = 'true';
    if (tag === 'SUP') format.superscript = 'true';
    if (tag === 'MARK') format.highlight = 'true';
    const style = node.style;
    if (/^(bold|[6-9]00)$/.test(style.fontWeight)) format.bold = 'true';
    if (style.fontStyle === 'italic') format.italic = 'true';
    if (style.textDecorationLine.includes('underline')) format.underline = 'true';
    if (style.textDecorationLine.includes('line-through')) format.strike = 'true';
    for (const property of ['color', 'background-color', 'font-size', 'font-family', 'line-height']) {
      const value = style.getPropertyValue(property);
      if (value) format[property] = value;
    }
    if (node.dataset.jotDirection) format.direction = node.dataset.jotDirection;
    if (node.dataset.jotAlign || style.textAlign) format.align = node.dataset.jotAlign || style.textAlign;
    // Canonical key order makes equivalent nesting independent of mark order.
    const canonical = Object.fromEntries(Object.entries(format).sort(([left], [right]) => left.localeCompare(right)));
    for (const child of node.childNodes) walk(child, canonical);
  };
  walk(root, {});
  return segments;
}

function documentSignature(html: string) {
  const root = new DOMParser().parseFromString(html, 'text/html').body;
  for (const item of root.querySelectorAll('li[data-type="taskItem"] > label')) item.remove();
  const text = logicalText(root);
  const images = [...root.querySelectorAll('img')].map(image => [image.getAttribute('src') || '', image.getAttribute('alt') || '', image.title, image.getAttribute('width') || image.style.width || '', image.getAttribute('height') || image.style.height || '']);
  const links = [...root.querySelectorAll('a[href]')].map(link => [link.textContent || '', link.getAttribute('href') || '', link.getAttribute('title') || '']);
  const tables = [...root.querySelectorAll('table')].map(table => [...table.rows].map(row => [...row.cells].map(cell => [cell.tagName, cell.colSpan, cell.rowSpan, cell.textContent || ''])));
  const isolates = [...root.querySelectorAll('bdi:not([data-jot-bidi])')].map(element => [element.getAttribute('dir') || 'auto', element.textContent || '']);
  const formatting = textFormatting(root);
  const blocks = [...root.querySelectorAll('h1,h2,h3,h4,h5,h6,pre,blockquote,ul,ol')].map(element => [element.tagName, element.getAttribute('start') || '1', element.textContent || '']);
  const tasks = [...root.querySelectorAll('li[data-type="taskItem"]')].map(element => [element.getAttribute('data-checked') === 'true', element.textContent || '']);
  const listValues = [...root.querySelectorAll('li[value]')].map(element => [element.getAttribute('value'), element.textContent || '']);
  const plainLines = [...root.querySelectorAll('[data-jot-plain-line="true"]')].map(element => element.textContent || '');
  return { text, images, links, tables, isolates, formatting, blocks, tasks, listValues, plainLines };
}

/** Catch schema drops before enabling edits or persisting a converted note. */
export function validateJotRoundTrip(originalHtml: string, exportedHtml: string): string[] {
  const before = documentSignature(originalHtml), after = documentSignature(exportedHtml);
  const issues: string[] = [];
  for (const key of ['text', 'images', 'links', 'tables', 'isolates', 'formatting', 'blocks', 'tasks', 'listValues', 'plainLines'] as const) {
    if (JSON.stringify(before[key]) !== JSON.stringify(after[key])) issues.push(`The editor could not preserve the note's ${key}.`);
  }
  return issues;
}

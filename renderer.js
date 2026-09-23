'use strict';
const $ = id => document.getElementById(id);
const editor = $('editor'), app = $('app'), request = JotBridge.request;
let model = {version:2,activeId:null,notes:[],prefs:{...JotDesign.defaults}};
let ready=false,revision=0,savedRevision=-1,saveTimer,typingTimer,selectionTimer;
let saveChain=Promise.resolve(),bookmark=null,composing=false,imageBusy=false,pinned=false,keepFormatOpen=true;
let inputDirection='rtl';
const histories=new Map();
const EMPTY='<p dir="auto"><br></p>';
const blockSelector='p,div,h1,h2,h3,h4,h5,h6,li,blockquote,table,td,th';
const imagePattern=/^data:image\/(?:png|jpeg|webp|gif);base64,[a-z0-9+/=\s]+$/i;
const allowedTags=new Set(['P','DIV','BR','B','STRONG','I','EM','U','S','STRIKE','SPAN','H1','H2','H3','H4','H5','H6','UL','OL','LI','BLOCKQUOTE','PRE','CODE','IMG','A','FONT','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD','CAPTION','HR','MARK','SUB','SUP','FIGURE','FIGCAPTION']);
const discardTags=new Set(['SCRIPT','STYLE','IFRAME','OBJECT','SVG','MATH','FORM','INPUT','BUTTON','VIDEO','AUDIO','LINK','META','TEMPLATE']);
JotBridge.on(data=>{
  if(data.event==='flush'){
    saveNow().then(()=>JotBridge.send('flush-complete',data.intent)).catch(error=>{showError(error);JotBridge.send('flush-failed');});
  }
  if(data.event==='focus')editor.focus();
  if(data.event==='input-language')setInputDirection(data.direction);
  if(data.event==='active-window')app.dataset.activeWindow=String(data.active);
  if(data.event==='clipboard-success'&&$('error').dataset.operation?.startsWith('clipboard-'))$('error').hidden=true;
  if(data.event==='preferences'){model.prefs={...JotDesign.defaults,...data.prefs};applyPrefs();refreshToolbar();}
  if(data.event==='warning'||data.event==='quit-failed'){
    if(data.event==='quit-failed'){$('closeAppButton').disabled=false;$('closeAppButton').setAttribute('aria-busy','false');}
    showError(Object.assign(new Error(data.message),{operation:data.event,logged:true}));
  }
});

function setStatus(state, text) {
  app.dataset.saveState = state;
}
function showError(error) {
  const operation=error?.operation||'renderer';
  JotBridge.reportError(error,operation);
  $('error').textContent = JotI18n.text(error?.message || 'خطایی رخ داد؛ یادداشت شما در ویرایشگر باقی مانده است.');
  $('error').dataset.operation=operation;
  $('error').hidden = false;
  if(operation==='save')setStatus('error');
  app.classList.remove('typing');
}
window.addEventListener('error', (event) => showError(event.error));
window.addEventListener('unhandledrejection', (event) => { event.preventDefault(); showError(event.reason); });
const activeNote = () => model.notes.find((note) => note.id === model.activeId);

function escapeHtml(value) {
  const element = document.createElement('span');
  element.textContent = value;
  return element.innerHTML;
}
function plainToHtml(value) {
  return String(value || '').split(/\r?\n/).map((line) => '<p dir="auto">' + (escapeHtml(line) || '<br>') + '</p>').join('');
}
// Rebuild pasted/loaded markup from a small allowlist; no scripts, remote resources or event attributes.
function sanitizeHtml(html) {
  const doc = new DOMParser().parseFromString(String(html), 'text/html');
  function clean(node) {
    if (node.nodeType === Node.TEXT_NODE) return document.createTextNode(node.textContent);
    if (node.nodeType !== Node.ELEMENT_NODE || discardTags.has(node.tagName)) return document.createDocumentFragment();
    const tag = node.tagName;
    if (tag === 'IMG') {
      const src = node.getAttribute('src') || '';
      if (!imagePattern.test(src) || src.length > 12 * 1024 * 1024) return document.createDocumentFragment();
      const img = document.createElement('img');
      img.src = src;
      img.alt = (node.getAttribute('alt') || 'تصویر').slice(0, 200);
      return img;
    }
    const element = allowedTags.has(tag) ? document.createElement(tag === 'FONT' ? 'span' : tag.toLowerCase()) : document.createDocumentFragment();
    for (const child of node.childNodes) element.append(clean(child));
    if (element.nodeType === Node.ELEMENT_NODE) {
      if (element.matches(blockSelector)) element.dir = 'auto';
      if (tag === 'PRE' || tag === 'CODE') element.dir = 'ltr';
      const color = node.style.color || node.getAttribute('color');
      if (color && CSS.supports('color', color) && !/var\(|url\(/i.test(color)) element.style.color = color;
      if (/^(bold|[6-9]00)$/.test(node.style.fontWeight)) element.style.fontWeight = '700';
      if (node.style.fontStyle === 'italic') element.style.fontStyle = 'italic';
      if (/^(underline|line-through)( (underline|line-through))?$/.test(node.style.textDecorationLine)) element.style.textDecorationLine = node.style.textDecorationLine;
      for(const property of ['backgroundColor','fontSize','fontFamily','textAlign']){
        const value=node.style[property];
        if(value&&!/url\(|var\(/i.test(value))element.style[property]=value;
      }
      for(const attribute of ['colspan','rowspan','start','value']){
        const value=node.getAttribute(attribute);
        if(value&&/^\d{1,3}$/.test(value))element.setAttribute(attribute,value);
      }
      if (tag === 'A') {
        const href = node.getAttribute('href') || '';
        if (/^https?:\/\//i.test(href)) element.setAttribute('href', href);
      }
    }
    return element;
  }
  const container = document.createElement('div');
  for (const node of doc.body.childNodes) container.append(clean(node));
  return container.innerHTML || EMPTY;
}
function normalizeDirection() {
  editor.querySelectorAll(blockSelector).forEach((block) => {
    const empty=!block.textContent.replace(/[\u200b\u200c\u200d\ufeff]/g,'').trim()&&!block.querySelector('img');
    block.dir=empty?inputDirection:'auto';
    block.dataset.emptyBlock=String(empty);
  });
  editor.querySelectorAll('pre,code').forEach((block) => { block.dir = 'ltr'; });
}
function setInputDirection(direction) {
  inputDirection=direction==='rtl'?'rtl':'ltr';
  editor.dir=inputDirection;
  normalizeDirection();
}
editor.addEventListener('focus',()=>request('input-language').then(setInputDirection).catch(()=>{}));
function updateEmpty() {
  const empty = !editor.textContent.trim() && !editor.querySelector('img');
  editor.dataset.empty = String(empty);
}
function capture() {
  if (!ready || !activeNote()) return;
  const note = activeNote();
  note.html = editor.innerHTML;
  note.plain = editor.innerText;
  note.updatedAt = Date.now();
}
function historyRecord(kind = 'command') {
  const id = model.activeId;
  if (!histories.has(id)) histories.set(id, { values: [editor.innerHTML], index: 0, time: 0, kind: '' });
  const h = histories.get(id);
  if (h.values[h.index] === editor.innerHTML) return;
  const merge = kind === 'typing' && h.kind === 'typing' && Date.now() - h.time < 650 && h.index > 0;
  h.values = h.values.slice(0, h.index + 1);
  if (merge) h.values[h.index] = editor.innerHTML;
  else { h.values.push(editor.innerHTML); h.index++; }
  if (h.values.length > 60) { h.values.shift(); h.index--; }
  h.time = Date.now(); h.kind = kind;
}
function queueSave() {
  revision++;
  clearTimeout(saveTimer);
  setStatus('saving', 'در حال ذخیره…');
  saveTimer = setTimeout(() => saveNow().catch(showError), 250);
}
function onEdit(kind = 'typing') {
  if (!ready || composing) return;
  normalizeDirection();
  updateEmpty();
  capture();
  historyRecord(kind);
  queueSave();
  app.classList.add('typing');
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => app.classList.remove('typing'), 1800);
}
function saveNow() {
  clearTimeout(saveTimer);
  if (!ready) return Promise.reject(new Error('یادداشت‌ها هنوز آماده نیستند.'));
  capture();
  const current = revision;
  const snapshot = JSON.parse(JSON.stringify(activeNote()));
  saveChain = saveChain.catch(() => {}).then(async () => {
    if (current <= savedRevision) return;
    setStatus('saving', 'در حال ذخیره…');
    try { await request('save', snapshot); }
    catch(error){error.operation='save';setStatus('error');throw error;}
    savedRevision = current;
    if (current === revision) {
      setStatus('saved', 'ذخیره شد');
      if($('error').dataset.operation==='save')$('error').hidden = true;
    }
  });
  return saveChain;
}
function rememberSelection() {
  const selection = window.getSelection();
  if (selection.rangeCount && editor.contains(selection.anchorNode) && editor.contains(selection.focusNode)) bookmark = selection.getRangeAt(0).cloneRange();
}
function restoreSelection() {
  editor.focus({ preventScroll: true });
  const selection = window.getSelection();
  if (bookmark && editor.contains(bookmark.commonAncestorContainer)) {
    selection.removeAllRanges(); selection.addRange(bookmark);
  } else {
    const range = document.createRange();
    range.selectNodeContents(editor); range.collapse(false);
    selection.removeAllRanges(); selection.addRange(range);
  }
}
function command(name, value) {
  restoreSelection();
  document.execCommand('styleWithCSS', false, true);
  document.execCommand(name, false, value);
  rememberSelection();
  onEdit('command');
  refreshToolbar();
}
function undo(redo = false) {
  const history = histories.get(model.activeId);
  if (!history) return;
  const index = history.index + (redo ? 1 : -1);
  if (index < 0 || index >= history.values.length) return;
  history.index = index; history.kind = '';
  editor.innerHTML = history.values[index];
  bookmark = null;
  normalizeDirection(); updateEmpty(); capture(); queueSave(); restoreSelection();
}
function closePanels() {
  closeFormatMenus();
  $('formatBar').hidden = model.prefs.toolbarVisible === false; $('menu').hidden = true;
  $('formatButton').setAttribute('aria-expanded', String(!$('formatBar').hidden));
  $('menuButton').setAttribute('aria-expanded', 'false');
  keepFormatOpen = model.prefs.toolbarVisible !== false;
}
function selectNote(id) {
  if (ready) capture();
  const note = model.notes.find((item) => item.id === id);
  if (!note) return;
  model.activeId = id;
  editor.innerHTML = sanitizeHtml(note.html);
  bookmark = null;
  if (!histories.has(id)) histories.set(id, { values: [editor.innerHTML], index: 0, time: 0, kind: '' });
  normalizeDirection(); updateEmpty(); closePanels();
  $('writingArea').scrollTop = 0;
  if ($('notesDialog').open) $('notesDialog').close();
  editor.focus({ preventScroll: true });
  if (ready) queueSave();
}
async function newNote() {
  if(!ready)return;
  try{await saveNow();await request('new-note');}catch(error){showError(error);}
}
function noteName(note) {
  return (note.plain || '').split(/\n/).find((line) => line.trim())?.trim().slice(0, 90) || note.legacyTitle || (note.html.includes('<img') ? 'یادداشت تصویری' : 'یادداشت بدون متن');
}
function renderList() {
  const query = $('search').value.trim().toLocaleLowerCase();
  const matches = model.notes.filter((note) => [note.plain, note.legacyTitle].join(' ').toLocaleLowerCase().includes(query)).sort((a, b) => b.updatedAt - a.updatedAt);
  $('noteList').replaceChildren();
  for (const note of matches) {
    const button = document.createElement('button');
    button.className = 'note-row';
    button.dataset.noteId = note.id;
    button.setAttribute('aria-current', String(note.id === model.activeId));
    const name = document.createElement('strong'); name.dir = 'auto'; name.textContent = noteName(note);
    const detail = document.createElement('small'); detail.dir = 'auto';
    detail.textContent = (note.plain || '').split('\n').filter((line) => line.trim()).slice(1).join(' ').slice(0, 100) || new Intl.DateTimeFormat('fa-IR', { month: 'short', day: 'numeric' }).format(note.updatedAt);
    button.append(name, detail);
    button.addEventListener('click', () => selectNote(note.id));
    $('noteList').append(button);
  }
  if (!matches.length) {
    const empty = document.createElement('p'); empty.className = 'empty-list'; empty.textContent = 'یادداشتی پیدا نشد.';
    $('noteList').append(empty);
  }
}
function openNotes() {
  saveNow().then(()=>request('home')).catch(showError);
}
function applyPrefs() {
  model.prefs=JotDesign.apply(model.prefs);
  $('fontSize').textContent=model.prefs.fontSize;
  $('smallerButton').disabled=model.prefs.fontSize<=13;
  $('largerButton').disabled=model.prefs.fontSize>=24;
  keepFormatOpen=model.prefs.toolbarVisible!==false;
  $('formatBar').hidden=!keepFormatOpen;
  $('formatButton').setAttribute('aria-expanded',String(keepFormatOpen));
}
async function setPreference(patch) {
  model.prefs={...model.prefs,...patch};applyPrefs();
  try{model.prefs=JotDesign.apply(await request('preferences',patch));applyPrefs();}
  catch(error){showError(error);}
}
function refreshToolbar() {
  document.querySelectorAll('[data-command][aria-pressed]').forEach((button) => button.setAttribute('aria-pressed', String(document.queryCommandState(button.dataset.command))));
  const block=String(document.queryCommandValue('formatBlock')||'p').toLowerCase().replace(/[<>]/g,'');
  $('blockLabel').textContent=JotI18n.text(({p:'متن',div:'متن',h1:'تیتر',h2:'تیتر',h3:'تیتر',blockquote:'نقل‌قول',pre:'کد'})[block]||'متن');
}
function closeFormatMenus() {
  for(const [button,panel] of [['blockMenuButton','blockMenu'],['colorMenuButton','colorMenu'],['formatMoreButton','formatMoreMenu']]){
    $(panel).hidden=true;$(button).setAttribute('aria-expanded','false');
  }
}
function toggleFormatMenu(buttonId,panelId) {
  const open=$(panelId).hidden;closeFormatMenus();
  if(!open)return;
  const rect=$(buttonId).getBoundingClientRect(),container=app.getBoundingClientRect();
  const panel=$(panelId);panel.hidden=false;
  panel.style.top=(rect.bottom-container.top+8)+'px';
  panel.style.left=Math.max(8,Math.min(rect.left-container.left,container.width-panel.offsetWidth-8))+'px';
  $(buttonId).setAttribute('aria-expanded','true');
}
for(const [button,panel] of [['blockMenuButton','blockMenu'],['colorMenuButton','colorMenu'],['formatMoreButton','formatMoreMenu']]){
  $(button).addEventListener('click',()=>toggleFormatMenu(button,panel));
  $(panel).addEventListener('pointerdown',event=>event.preventDefault());
}
function readImage(file) {
  if (!/^image\/(png|jpeg|webp|gif)$/.test(file.type)) throw new Error('فرمت تصویر پشتیبانی نمی‌شود.');
  if (file.size > 8 * 1024 * 1024) throw new Error('اندازه هر تصویر باید کمتر از ۸ مگابایت باشد.');
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error('تصویر خوانده نشد.'));
    reader.readAsDataURL(file);
  });
}
async function insertImages(files) {
  if (!files.length || imageBusy) return;
  rememberSelection();
  const targetId = model.activeId;
  imageBusy = true;
  $('imageButton').disabled = true;
  try {
    const results = [];
    for (const file of files) results.push(await readImage(file));
    if (model.activeId !== targetId) throw new Error('یادداشت تغییر کرد. تصویر را دوباره بچسبانید.');
    const size = results.reduce((sum, src) => sum + src.length, 0) + editor.innerHTML.length;
    if (size > 24 * 1024 * 1024) throw new Error('حجم این یادداشت زیاد است؛ تصویر را در یادداشت جدید قرار دهید.');
    command('insertHTML', results.map((src) => '<img alt="'+JotI18n.text('تصویر')+'" src="' + src + '">').join(' ') + ' ');
  } finally { imageBusy = false; $('imageButton').disabled = false; }
}
async function importHtml(html, files) {
  const doc=new DOMParser().parseFromString(html,'text/html');
  let fileIndex=0,missing=0;
  for(const image of [...doc.querySelectorAll('img')]){
    const src=image.getAttribute('src')||'';
    if(imagePattern.test(src))continue;
    try{
      if(files[fileIndex])image.src=await readImage(files[fileIndex++]);
      else if(/^https:\/\//i.test(src))image.src=await request('import-image',src);
      else throw new Error('unavailable image');
    }catch{
      const placeholder=doc.createElement('span');placeholder.textContent=JotI18n.text('[تصویر دریافت نشد]');
      image.replaceWith(placeholder);missing++;
    }
  }
  return {html:sanitizeHtml(doc.body.innerHTML),missing};
}
async function paste(event) {
  event.preventDefault();
  rememberSelection();
  const targetId=model.activeId;
  const data=event.clipboardData;
  const images=[...data.items].filter(item=>item.kind==='file'&&item.type.startsWith('image/')).map(item=>item.getAsFile()).filter(Boolean);
  const html=data.getData('text/html');
  // HTML carries ordering and structure. A bitmap representation must not replace it.
  if(html){
    const imported=await importHtml(html,images);
    if(model.activeId!==targetId)throw new Error('یادداشت تغییر کرد؛ دوباره بچسبانید.');
    command('insertHTML',imported.html);
    if(imported.missing){$('error').textContent=JotI18n.text('بخشی از تصاویر قابل دریافت نبود؛ جای آن‌ها مشخص شده است.');$('error').hidden=false;}
  }else if(images.length)await insertImages(images);
  else command('insertHTML',plainToHtml(data.getData('text/plain')));
}
function copyPayload(all=false) {
  const container=document.createElement('div');
  const selection=window.getSelection();
  if(all)container.innerHTML=editor.innerHTML;
  else if(selection.rangeCount&&editor.contains(selection.anchorNode)&&editor.contains(selection.focusNode))container.append(selection.getRangeAt(0).cloneContents());
  const html=sanitizeHtml(container.innerHTML);
  const text=all?editor.innerText:selection.toString();
  const images=container.querySelectorAll('img');
  return {html,text,...(images.length===1?{image:images[0].src}:{})};
}
editor.addEventListener('copy',event=>{
  const payload=copyPayload();
  event.preventDefault();
  event.clipboardData.setData('text/html',payload.html);
  event.clipboardData.setData('text/plain',payload.text);
  request('clipboard-write',payload).catch(showError);
});
$('copyButton').addEventListener('click',()=>request('clipboard-write',copyPayload(true)).catch(showError));
$('copyTextButton').addEventListener('click',()=>request('clipboard-text',editor.innerText).catch(showError));
$('appearanceButton').addEventListener('click',()=>saveNow().then(()=>request('home',true)).catch(showError));
editor.addEventListener('click',event=>{
  if(event.target.tagName==='IMG'){event.preventDefault();request('view-image',event.target.src).catch(showError);}
});
function loadIcons() {
  JotDesign.icons();
  const palette = [['currentColor','پیش‌فرض'],['#fb7185','قرمز'],['#fbbf24','طلایی'],['#4ade80','سبز'],['#60a5fa','آبی'],['#c084fc','بنفش']];
  for (const [color, label] of palette) {
    const button = document.createElement('button');
    button.className = 'swatch'; button.style.setProperty('--swatch', color);
    button.title = label; button.setAttribute('aria-label', label); button.dataset.color = color;
    button.addEventListener('click', () => {
      command('foreColor', color === 'currentColor' ? 'inherit' : color);
      $('selectedColor').style.setProperty('--selected-text-color',color==='currentColor'?'var(--foreground)':color);
      document.querySelectorAll('[data-color]').forEach(swatch=>swatch.setAttribute('aria-pressed',String(swatch===button)));
      closeFormatMenus();
    });
    $('colors').append(button);
  }
}
editor.addEventListener('input', () => onEdit());
editor.addEventListener('compositionstart', () => { composing = true; });
editor.addEventListener('compositionend', () => { composing = false; onEdit(); });
editor.addEventListener('beforeinput', (event) => {
  if (event.inputType === 'historyUndo' || event.inputType === 'historyRedo') { event.preventDefault(); undo(event.inputType === 'historyRedo'); }
});
editor.addEventListener('paste', (event) => paste(event).catch(showError));
editor.addEventListener('dragover', (event) => event.preventDefault());
editor.addEventListener('drop', (event) => {
  event.preventDefault();
  const range = document.caretRangeFromPoint(event.clientX, event.clientY);
  if (range && editor.contains(range.startContainer)) { window.getSelection().removeAllRanges(); window.getSelection().addRange(range); }
  insertImages([...event.dataTransfer.files]).catch(showError);
});
editor.addEventListener('click', (event) => { if (event.target.closest('a')) event.preventDefault(); });
$('formatBar').addEventListener('pointerdown', (event) => event.preventDefault());
document.querySelectorAll('[data-command]').forEach((button) => button.addEventListener('click', () => {command(button.dataset.command);closeFormatMenus();}));
document.querySelectorAll('[data-block]').forEach((button) => button.addEventListener('click', () => {command('formatBlock', button.dataset.block);closeFormatMenus();}));
$('formatButton').addEventListener('pointerdown', (event) => { rememberSelection(); event.preventDefault(); });
$('formatButton').addEventListener('click',()=>setPreference({toolbarVisible:!keepFormatOpen}));
document.addEventListener('selectionchange',()=>{rememberSelection();refreshToolbar();});
$('newButton').addEventListener('click', newNote);
$('dialogNewButton').addEventListener('click', newNote);
$('notesButton').addEventListener('click', openNotes);
$('closeNotesButton').addEventListener('click', () => { $('notesDialog').close(); editor.focus(); });
$('search').addEventListener('input', renderList);
$('notesDialog').addEventListener('close', () => editor.focus());
$('menuButton').addEventListener('click', () => {
  const open = $('menu').hidden; closePanels(); $('menu').hidden = !open;
  $('menuButton').setAttribute('aria-expanded', String(open)); app.classList.remove('typing');
});
$('themeButton').addEventListener('click', () => {
  setPreference({theme:model.prefs.theme === 'dark' ? 'light' : 'dark'});
});
for (const [id, delta] of [['smallerButton', -1], ['largerButton', 1]]) $(id).addEventListener('click', () => setPreference({fontSize:Math.min(24,Math.max(13,model.prefs.fontSize+delta))}));
$('pinButton').addEventListener('click', async () => {
  try { pinned = await request('pin', !pinned); $('pinButton').setAttribute('aria-pressed', String(pinned)); } catch (error) { showError(error); }
});
async function leave(action) { try { await saveNow(); await request(action); } catch (error) { showError(error); } }
$('closeAppButton').addEventListener('click',async()=>{
  const button=$('closeAppButton');if(button.disabled)return;
  button.disabled=true;button.setAttribute('aria-busy','true');button.title='Saving notes and closing…';
  try{await saveNow();await request('quit');}
  catch(error){button.disabled=false;button.setAttribute('aria-busy','false');button.title='Save notes and quit Jot';showError(error);}
});
$('hideButton').addEventListener('click', () => leave('hide'));
$('quitButton').addEventListener('click', () => leave('quit'));
$('imageButton').addEventListener('pointerdown', () => rememberSelection());
$('imageButton').addEventListener('click', () => $('imageInput').click());
$('imageInput').addEventListener('change', () => { insertImages([...$('imageInput').files]).catch(showError); $('imageInput').value = ''; });
$('exportButton').addEventListener('click', async () => {
  try { await saveNow(); await request('export', { name: noteName(activeNote()), html: sanitizeHtml(editor.innerHTML) }); } catch (error) { showError(error); }
});
$('handle').addEventListener('pointerdown', (event) => {
  if (event.button === 0 && !event.target.closest('button')) request('drag').catch(showError);
});
document.addEventListener('pointerdown', (event) => {
  if(!event.target.closest('#formatBar,.format-popover'))closeFormatMenus();
  if (event.target.closest('#editor')) { $('menu').hidden = true; $('menuButton').setAttribute('aria-expanded', 'false'); }
});
document.addEventListener('keydown', (event) => {
  if (event.isComposing) return;
  const ctrl = event.ctrlKey || event.metaKey;
  if (ctrl && event.code === 'KeyK') { event.preventDefault(); if (!$('notesDialog').open) openNotes(); }
  else if (ctrl && event.code === 'KeyN') { event.preventDefault(); newNote(); }
  else if (ctrl && event.code === 'KeyS') { event.preventDefault(); saveNow().catch(showError); }
  else if (ctrl && event.shiftKey && event.code === 'KeyF') { event.preventDefault(); $('formatButton').click(); }
  else if (ctrl && editor.contains(document.activeElement) && ['KeyZ','KeyY'].includes(event.code)) { event.preventDefault(); undo(event.shiftKey || event.code === 'KeyY'); }
  else if (ctrl && editor.contains(document.activeElement) && ['KeyB','KeyI','KeyU'].includes(event.code)) { event.preventDefault(); command({KeyB:'bold',KeyI:'italic',KeyU:'underline'}[event.code]); }
  else if (event.key === 'Escape' && !$('notesDialog').open) {
    event.preventDefault();
    if (!$('menu').hidden||document.querySelector('.format-popover:not([hidden])')) closePanels(); else leave('hide');
  }
});
async function boot() {
  editor.contentEditable='false';loadIcons();
  $('handle').after($('formatBar'));
  const context=await request('context');
  app.dataset.activeWindow=String(!!context.active);
  setInputDirection(context.inputDirection);
  const stored=await request('load');
  if(!stored||!stored.notes.some(note=>note.id===context.noteId))throw new Error('یادداشت پیدا نشد.');
  model=stored;model.prefs={...JotDesign.defaults,...stored.prefs};model.activeId=context.noteId;
  selectNote(context.noteId);ready=true;editor.contentEditable='true';
  document.execCommand('defaultParagraphSeparator',false,'p');
  applyPrefs();setStatus('saved','ذخیره شد');editor.focus();window.jotReady=true;
}
boot().catch(showError);

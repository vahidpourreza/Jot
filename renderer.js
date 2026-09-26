'use strict';
const $ = id => document.getElementById(id);
const editor = $('editor'), app = $('app'), request = JotBridge.request;
let model = {version:2,activeId:null,notes:[],prefs:{...JotDesign.defaults}};
let ready=false,revision=0,savedRevision=-1,saveTimer,typingTimer,selectionTimer;
let saveChain=Promise.resolve(),bookmark=null,composing=false,imageBusy=false,pinned=false,keepFormatOpen=true;
let inputDirection='rtl',deleting=false;
let noteMenuAnimation=null,toolbarAnimation=null,colorMenuAnimation=null,toolbarShown=null;
const visibilityAnimations=new Map();
const reducedMotion=matchMedia('(prefers-reduced-motion: reduce)');
const formatMenus=[['colorMenuButton','colorMenu']];
const histories=new Map();
const EMPTY='<p dir="auto"><br></p>';
const blockSelector='p,div,h1,h2,h3,h4,h5,h6,li,blockquote,table,td,th';
const imagePattern=/^data:image\/(?:png|jpeg|webp|gif);base64,[a-z0-9+/=\s]+$/i;
const allowedTags=new Set(['P','DIV','BR','B','STRONG','I','EM','U','S','STRIKE','SPAN','H1','H2','H3','H4','H5','H6','UL','OL','LI','BLOCKQUOTE','PRE','CODE','IMG','A','FONT','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD','CAPTION','HR','MARK','SUB','SUP','FIGURE','FIGCAPTION']);
const discardTags=new Set(['SCRIPT','STYLE','IFRAME','OBJECT','SVG','MATH','FORM','INPUT','BUTTON','VIDEO','AUDIO','LINK','META','TEMPLATE']);
let directionChanges=[];
const directionObserver=new MutationObserver(records=>directionChanges.push(...records));
directionObserver.observe(editor,{subtree:true,childList:true,characterData:true});
JotBridge.on(data=>{
  if(data.event==='flush'){
    saveNow().then(()=>JotBridge.send('flush-complete',data.intent)).catch(error=>{showError(error);JotBridge.send('flush-failed');});
  }
  if(data.event==='focus')editor.focus();
  if(data.event==='input-language')setInputDirection(data.direction);
  if(data.event==='active-window')app.dataset.activeWindow=String(data.active);
  if(data.event==='clipboard-success'&&$('error').dataset.operation?.startsWith('clipboard-'))$('error').hidden=true;
  if(data.event==='preferences'){model.prefs={...JotDesign.defaults,...data.prefs};applyPrefs();refreshToolbar();}
  if(data.event==='note-color'&&activeNote()){activeNote().color=data.color;applyNoteColor();}
  if(data.event==='warning'||data.event==='quit-failed'){
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
function normalizeDirection(full=true) {
  const changes=directionChanges.concat(directionObserver.takeRecords());directionChanges=[];
  const blocks=new Set();
  if(full)editor.querySelectorAll(blockSelector+',pre,code').forEach(block=>blocks.add(block));
  else for(const change of changes){
    let node=change.target.nodeType===Node.ELEMENT_NODE?change.target:change.target.parentElement;
    while(node&&node!==editor){if(node.matches(blockSelector+',pre,code'))blocks.add(node);node=node.parentElement;}
    for(const added of change.addedNodes||[]){
      if(added.nodeType!==Node.ELEMENT_NODE)continue;
      if(added.matches(blockSelector+',pre,code'))blocks.add(added);
      added.querySelectorAll(blockSelector+',pre,code').forEach(block=>blocks.add(block));
    }
  }
  for(const block of blocks){
    if(!editor.contains(block))continue;
    if(block.matches('pre,code')){if(block.dir!=='ltr')block.dir='ltr';continue;}
    const empty=!block.textContent.replace(/[\u200b\u200c\u200d\ufeff]/g,'').trim()&&!block.querySelector('img');
    const dir=empty?inputDirection:'auto';
    if(block.dir!==dir)block.dir=dir;
    if(block.dataset.emptyBlock!==String(empty))block.dataset.emptyBlock=String(empty);
  }
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
  const html=editor.innerHTML;
  if (!histories.has(id)) histories.set(id, { values: [html], index: 0, time: 0, kind: '' });
  const h = histories.get(id);
  if (h.values[h.index] === html) return;
  const merge = kind === 'typing' && h.kind === 'typing' && Date.now() - h.time < 650 && h.index > 0;
  h.values = h.values.slice(0, h.index + 1);
  if (merge) h.values[h.index] = html;
  else { h.values.push(html); h.index++; }
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
  normalizeDirection(false);
  updateEmpty();
  // Capture plain text (innerText forces layout) only at the save boundary.
  historyRecord(kind);
  queueSave();
  app.classList.add('typing');
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => app.classList.remove('typing'), 1800);
}
function saveNow() {
  clearTimeout(saveTimer);
  if (!ready) return Promise.reject(new Error('یادداشت‌ها هنوز آماده نیستند.'));
  if(revision<=savedRevision)return saveChain;
  capture();
  const current = revision;
  const {id,html,plain,updatedAt}=activeNote();
  const snapshot = {id,html,plain,updatedAt};
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
  setToolbarVisible(model.prefs.toolbarVisible !== false); setNoteMenuVisible(false);
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
  closePanels();
  saveNow().then(()=>request('home')).catch(showError);
}
function applyPrefs() {
  model.prefs=JotDesign.apply(model.prefs);
  applyNoteColor();
  $('fontSize').textContent=model.prefs.fontSize+' px';
  $('smallerButton').disabled=model.prefs.fontSize<=13;
  $('largerButton').disabled=model.prefs.fontSize>=24;
  $('smallerButton').title=$('smallerButton').disabled?'Minimum font size (13 px)':'Decrease font size';
  $('largerButton').title=$('largerButton').disabled?'Maximum font size (24 px)':'Increase font size';
  $('lineHeight').textContent=model.prefs.lineHeight+'×';
  $('tighterLinesButton').disabled=model.prefs.lineHeight<=1.2;
  $('looserLinesButton').disabled=model.prefs.lineHeight>=2.5;
  $('tighterLinesButton').title=$('tighterLinesButton').disabled?'Minimum line height (1.2×)':'Decrease line height';
  $('looserLinesButton').title=$('looserLinesButton').disabled?'Maximum line height (2.5×)':'Increase line height';
  const nextMode=model.prefs.theme==='dark'?'light':'dark';
  $('themeLabel').textContent=nextMode==='light'?'Light mode':'Dark mode';
  $('themeButton').title='Switch all Jot windows to '+nextMode+' mode';
  keepFormatOpen=model.prefs.toolbarVisible!==false;
  setToolbarVisible(keepFormatOpen);
}
function applyNoteColor() {
  const color=JotDesign.noteColor(activeNote()?.color);
  app.dataset.noteColor=color.slug;
  app.style.setProperty('--note-accent',color.primary);
  app.style.setProperty('--note-accent-foreground',color.foreground);
  $('noteColors').querySelectorAll('button[data-note-color]').forEach(button=>{
    const swatch=JotDesign.noteColor(button.dataset.noteColor);
    button.style.setProperty('--swatch',swatch.primary);button.style.color=swatch.foreground;
    button.setAttribute('aria-pressed',String(swatch.slug===color.slug));
    button.tabIndex=swatch.slug===color.slug?0:-1;
  });
}
async function setNoteColor(color) {
  const buttons=[...$('noteColors').querySelectorAll('button')];
  buttons.forEach(button=>button.disabled=true);
  const chosen=buttons.find(button=>button.dataset.noteColor===color);
  chosen?.setAttribute('aria-busy','true');
  try { await request('note-metadata',{id:model.activeId,color});activeNote().color=color;applyNoteColor(); }
  catch(error){showError(error);}
  finally{buttons.forEach(button=>button.disabled=false);chosen?.removeAttribute('aria-busy');}
}
async function setPreference(patch) {
  model.prefs={...model.prefs,...patch};applyPrefs();
  try{model.prefs=JotDesign.apply(await request('preferences',patch));applyPrefs();}
  catch(error){showError(error);}
}
function refreshToolbar() {
  document.querySelectorAll('[data-command][aria-pressed]').forEach((button) => button.setAttribute('aria-pressed', String(document.queryCommandState(button.dataset.command))));
  const selection=getSelection();
  if(selection.rangeCount&&editor.contains(selection.anchorNode)&&editor.contains(selection.focusNode)){
    const color=document.queryCommandValue('foreColor');
    if(color&&CSS.supports('color',color))$('colorMenuButton').style.setProperty('--selected-text-color',color);
  }
}
function syncColorChoices() {
  const selected=JotDesign.hexColor(getComputedStyle($('colorMenuButton')).color);
  const normal=JotDesign.hexColor(getComputedStyle(editor).color);
  $('colors').querySelectorAll('[data-color]').forEach(button=>button.setAttribute('aria-pressed',String((button.dataset.color==='currentColor'?normal:button.dataset.color)===selected)));
}
function closeFormatMenus() {
  setColorMenuVisible(false);
}
function toggleFormatMenu(buttonId,panelId) {
  const open=$(panelId).hidden;
  if(open)syncColorChoices();
  setColorMenuVisible(open);
  if(open)positionFormatMenu(buttonId,panelId);
}
function positionFormatMenu(buttonId,panelId) {
  if($(panelId).hidden)return;
  const rect=$(buttonId).getBoundingClientRect(),container=app.getBoundingClientRect();
  const panel=$(panelId),above=rect.top-container.top-8;
  panel.style.maxHeight=Math.max(0,above-8)+'px';
  panel.style.top=Math.max(8,above-panel.offsetHeight)+'px';
  panel.style.left=Math.max(8,Math.min(rect.left-container.left,container.width-panel.offsetWidth-8))+'px';
}
for(const [button,panel] of formatMenus){
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
$('copyButton').addEventListener('click',()=>{closePanels();request('clipboard-write',copyPayload(true)).catch(showError);});
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
    button.append(JotDesign.icon(color==='currentColor'?'color-circle':'check'));
    button.addEventListener('click', () => {
      command('foreColor', color === 'currentColor' ? 'inherit' : color);
      $('colorMenuButton').style.setProperty('--selected-text-color',color==='currentColor'?'var(--foreground)':color);
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
$('formatButton').addEventListener('pointerdown', (event) => { rememberSelection(); event.preventDefault(); });
$('formatButton').addEventListener('click',()=>setPreference({toolbarVisible:!keepFormatOpen}));
document.addEventListener('selectionchange',()=>{
  rememberSelection();
  if(selectionTimer)return;
  selectionTimer=requestAnimationFrame(()=>{selectionTimer=0;refreshToolbar();});
});
$('newButton').addEventListener('click', newNote);
$('dialogNewButton').addEventListener('click', newNote);
$('menuNotesButton').addEventListener('click', openNotes);
$('tighterLinesButton').addEventListener('click',()=>setPreference({lineHeight:JotDesign.stepLineHeight(model.prefs.lineHeight,-1)}));
$('looserLinesButton').addEventListener('click',()=>setPreference({lineHeight:JotDesign.stepLineHeight(model.prefs.lineHeight,1)}));
$('closeNotesButton').addEventListener('click', () => { $('notesDialog').close(); editor.focus(); });
$('search').addEventListener('input', renderList);
$('notesDialog').addEventListener('close', () => editor.focus());
function positionNoteMenu() {
  if($('menu').hidden)return;
  $('menu').style.maxHeight=Math.max(0,app.clientHeight-24)+'px';
}
function transitionVisibility(panel,open,closedFrame,shownFrame,duration,settled,animate=true) {
  const previous=visibilityAnimations.get(panel);
  const style=previous?getComputedStyle(panel):null;
  const from=style?Object.fromEntries(Object.keys(closedFrame).map(key=>[key,style[key]])):(open?closedFrame:shownFrame);
  previous?.cancel();visibilityAnimations.delete(panel);panel.classList.remove('is-exiting');
  panel.hidden=!open;panel.inert=!open;panel.setAttribute('aria-hidden',String(!open));
  if(!animate||reducedMotion.matches)return null;
  if(!open)panel.classList.add('is-exiting');
  const animation=panel.animate([from,open?shownFrame:closedFrame],{duration,easing:'cubic-bezier(.22,.61,.36,1)',fill:'both'});
  visibilityAnimations.set(panel,animation);
  animation.onfinish=()=>{
    if(visibilityAnimations.get(panel)!==animation)return;
    visibilityAnimations.delete(panel);panel.classList.remove('is-exiting');animation.cancel();settled();
  };
  return animation;
}
function setNoteMenuVisible(open) {
  const panel=$('menu');if(!open&&panel.hidden)return;
  $('menuButton').setAttribute('aria-expanded',String(open));
  noteMenuAnimation=transitionVisibility(panel,open,{opacity:1,clipPath:'inset(0% 0% 100% 0%)'},{opacity:1,clipPath:'inset(0% 0% 0% 0%)'},open?260:200,()=>noteMenuAnimation=null);
}
function setToolbarVisible(open) {
  $('formatButton').setAttribute('aria-expanded',String(open));
  $('formatButton').title=(open?'Hide':'Show')+' formatting tools · Ctrl+Shift+F';
  if(toolbarShown===open)return;
  const initialized=toolbarShown!==null;toolbarShown=open;
  if(!open)closeFormatMenus();
  toolbarAnimation=transitionVisibility($('formatBar'),open,{opacity:0,transform:'translateY(10px)'},{opacity:1,transform:'translateY(0px)'},open?220:180,()=>toolbarAnimation=null,initialized);
}
function setColorMenuVisible(open) {
  const panel=$('colorMenu');if(!open&&panel.hidden)return;
  $('colorMenuButton').setAttribute('aria-expanded',String(open));
  colorMenuAnimation=transitionVisibility(panel,open,{opacity:0,transform:'translateY(6px) scale(.96)'},{opacity:1,transform:'translateY(0px) scale(1)'},open?160:120,()=>colorMenuAnimation=null);
}
reducedMotion.addEventListener('change',()=>{
  if(!reducedMotion.matches)return;
  for(const [panel,animation] of visibilityAnimations){animation.cancel();panel.classList.remove('is-exiting');}
  visibilityAnimations.clear();noteMenuAnimation=toolbarAnimation=colorMenuAnimation=null;
});
$('menuButton').addEventListener('click', () => {
  const open = $('menu').hidden; closePanels(); if(open)setNoteMenuVisible(true);
  $('menuButton').setAttribute('aria-expanded', String(open)); app.classList.remove('typing');
  if(open){
    $('menuActions').scrollTop=0;
    positionNoteMenu();
    const selected=$('noteColors').querySelector('[aria-pressed=true]');
    selected?.focus({preventScroll:true});
  }
});
window.addEventListener('resize',()=>{
  positionNoteMenu();
  for(const [button,panel] of formatMenus)positionFormatMenu(button,panel);
});
$('noteColors').addEventListener('keydown',event=>{
  if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;
  const buttons=[...$('noteColors').querySelectorAll('button')],index=buttons.indexOf(event.target);
  if(index<0)return;
  event.preventDefault();
  const step={ArrowLeft:-1,ArrowRight:1}[event.key]||0;
  const next=event.key==='Home'?0:event.key==='End'?buttons.length-1:Math.max(0,Math.min(buttons.length-1,index+step));
  buttons.forEach((button,i)=>button.tabIndex=i===next?0:-1);buttons[next].focus({preventScroll:true});
});
$('themeButton').addEventListener('click',()=>setPreference({theme:model.prefs.theme==='dark'?'light':'dark'}));
for (const [id, delta] of [['smallerButton', -1], ['largerButton', 1]]) $(id).addEventListener('click', () => setPreference({fontSize:Math.min(24,Math.max(13,model.prefs.fontSize+delta))}));
$('pinButton').addEventListener('click', async () => {
  try {
    pinned = await request('pin', !pinned);
    $('pinButton').setAttribute('aria-pressed', String(pinned));
    const glyph=JotDesign.icon('pin');
    if(pinned)glyph.querySelector('path:last-child').setAttribute('fill','currentColor');
    $('pinButton').replaceChildren(glyph);
    $('pinButton').title=pinned?'Unpin note':'Keep note on top';
    $('pinButton').setAttribute('aria-label',$('pinButton').title);
  } catch (error) { showError(error); }
});
async function leave(action) { try { await saveNow(); await request(action); } catch (error) { showError(error); } }
$('hideButton').addEventListener('click', () => leave('hide'));
$('deleteButton').onclick=()=>{closePanels();$('deleteError').hidden=true;$('deleteDialog').showModal();$('deleteCancel').focus();};
$('deleteCancel').onclick=()=>$('deleteDialog').close();
$('deleteDialog').addEventListener('cancel',event=>{if(deleting)event.preventDefault();});
$('deleteDialog').addEventListener('close',()=>{if(!deleting)editor.focus();});
$('deleteConfirm').onclick=async()=>{
  if(deleting)return;
  if(imageBusy){$('deleteError').textContent='Wait for the image to finish inserting, then try again.';$('deleteError').hidden=false;return;}
  deleting=true;editor.contentEditable='false';$('deleteError').hidden=true;
  $('deleteConfirm').disabled=$('deleteCancel').disabled=true;
  $('deleteConfirm').setAttribute('aria-busy','true');$('deleteConfirm').textContent='Deleting…';
  try{await saveNow();await request('note-delete',model.activeId);}
  catch(error){
    JotBridge.reportError(error,'note-delete');$('deleteError').textContent='The note could not be deleted. Your note is still available. '+JotI18n.text(error.message);$('deleteError').hidden=false;
    deleting=false;editor.contentEditable='true';$('deleteConfirm').disabled=$('deleteCancel').disabled=false;
    $('deleteConfirm').setAttribute('aria-busy','false');$('deleteConfirm').textContent='Delete note';
  }
};
$('imageButton').addEventListener('pointerdown', () => rememberSelection());
$('imageButton').addEventListener('click', () => $('imageInput').click());
$('imageInput').addEventListener('change', () => { insertImages([...$('imageInput').files]).catch(showError); $('imageInput').value = ''; });
$('exportButton').addEventListener('click', async () => {
  closePanels();
  try { await saveNow(); await request('export', { name: noteName(activeNote()), html: sanitizeHtml(editor.innerHTML) }); } catch (error) { showError(error); }
});
$('handle').addEventListener('pointerdown', (event) => {
  if (event.button === 0 && !event.target.closest('button')) request('drag').catch(showError);
});
document.addEventListener('pointerdown', (event) => {
  if(!event.target.closest('#formatBar,.format-popover'))closeFormatMenus();
  if (!event.target.closest('#menu,#menuButton')) setNoteMenuVisible(false);
});
document.addEventListener('keydown', (event) => {
  if($('deleteDialog').open)return;
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
    if(!$('menu').hidden){closePanels();$('menuButton').focus({preventScroll:true});}
    else if(document.querySelector('.format-popover:not([hidden])'))closePanels();
    else leave('hide');
  }
});
async function boot() {
  editor.contentEditable='false';loadIcons();
  const colorOrder=['crimson','red','orange','amber','yellow','lime','green','emerald','teal','cyan','sky','blue','indigo','violet','purple','fuchsia','pink','rose','neutral'];
  const rank=slug=>{const index=colorOrder.indexOf(slug);return index<0?colorOrder.length:index;};
  const accents=[...JotAccents].sort((a,b)=>rank(a.slug)-rank(b.slug));
  $('noteColors').style.setProperty('--note-color-count',accents.length);
  for(const accent of accents){
    const button=document.createElement('button');button.className='note-color';button.dataset.noteColor=accent.slug;
    button.title=button.ariaLabel=accent.slug[0].toUpperCase()+accent.slug.slice(1);button.append(JotDesign.icon('check'));
    button.onclick=()=>setNoteColor(accent.slug);$('noteColors').append(button);
  }
  const {context,model:stored}=await request('note-load');
  app.dataset.activeWindow=String(!!context.active);
  setInputDirection(context.inputDirection);
  if(!stored||!stored.notes.some(note=>note.id===context.noteId))throw new Error('یادداشت پیدا نشد.');
  model=stored;model.prefs={...JotDesign.defaults,...stored.prefs};model.activeId=context.noteId;
  selectNote(context.noteId);ready=true;editor.contentEditable='true';
  document.execCommand('defaultParagraphSeparator',false,'p');
  applyPrefs();setStatus('saved','ذخیره شد');savedRevision=revision;editor.focus();window.jotReady=true;
  // Resolve the real header color with initial transitions disabled before
  // revealing the native window. No neutral/white intermediate frame is visible.
  window.jotInitialHeader=getComputedStyle($('handle'),'::before').backgroundColor;
  JotBridge.send('editor-ready');
  requestAnimationFrame(()=>{app.dataset.paintReady='true';});
}
boot().catch(error=>{showError(error);JotBridge.send('editor-failed');});

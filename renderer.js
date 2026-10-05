'use strict';
const $ = id => document.getElementById(id);
let editor = $('editor');
const app = $('app'), request = JotBridge.request;
let model = {version:2,activeId:null,notes:[],prefs:{...JotDesign.defaults}};
let appPreferences={...JotDesign.defaults},notePreferenceChain=Promise.resolve(),notePreferenceRevision=0,confirmedNoteView={};
let ready=false,revision=0,savedRevision=-1,saveTimer,typingTimer,selectionTimer;
let historyTimer=0,typingHistoryPending=false;
let saveChain=Promise.resolve(),bookmark=null,bookmarkDirection=null,pmBookmark=null,composing=false,imageBusy=false,pinned=false,keepFormatOpen=true;
let cutting=false;
let inputDirection='rtl',deleting=false;
let noteFullscreen=false,fullscreenBusy=false;
let workspace=false,tabbed=false,tabHeaders=[],tabBusy=false,tabPendingId=null,tabPendingAction=null;
let workspaceOptionsOwner=null;
const tabUi=new Map();
let editorLockedByHost=false;
const pendingEdits=new Set();
function trackEdit(operation){pendingEdits.add(operation);operation.then(()=>pendingEdits.delete(operation),()=>pendingEdits.delete(operation));return operation;}
function resumeEditing(){editorLockedByHost=false;app.inert=false;}
for(const type of ['pointerdown','keydown'])document.addEventListener(type,event=>{
  if(editorLockedByHost){event.preventDefault();event.stopImmediatePropagation();}
},true);
let noteMenuAnimation=null,toolbarAnimation=null,colorMenuAnimation=null,toolbarShown=null;
const visibilityAnimations=new Map();
const reducedMotion=matchMedia('(prefers-reduced-motion: reduce)');
const formatMenus=[['colorMenuButton','colorMenu']];
const EMPTY='<p dir="auto"><br></p>';
const blockSelector='p,div,h1,h2,h3,h4,h5,h6,li,blockquote,table,td,th';
const imagePattern=/^data:image\/(?:png|jpeg|webp|gif);base64,[a-z0-9+/=\s]+$/i;
const allowedTags=new Set(['P','DIV','BR','B','BDI','STRONG','I','EM','U','S','STRIKE','SPAN','H1','H2','H3','H4','H5','H6','UL','OL','LI','BLOCKQUOTE','PRE','CODE','IMG','A','FONT','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD','CAPTION','HR','MARK','SUB','SUP','FIGURE','FIGCAPTION']);
const discardTags=new Set(['SCRIPT','STYLE','IFRAME','OBJECT','SVG','MATH','FORM','INPUT','BUTTON','VIDEO','AUDIO','LINK','META','TEMPLATE']);
JotBridge.on(data=>{
  if(data.event==='flush'){
    rememberTabView();
    flushFinal(data.holdEditing).then(()=>JotBridge.send('flush-complete',data.intent)).catch(error=>{showError(error);JotBridge.send('flush-failed',data.intent);});
  }
  if(data.event==='prepare-quit'){editorLockedByHost=true;app.inert=pendingEdits.size===0;window.JotEditorMenu?.close();window.JotMenus?.close();}
  if(data.event==='resume-editing')resumeEditing();
  if(data.event==='focus'&&(!window.JotWorkspace||JotWorkspace.view==='note')){resumeEditing();editor.focus();}
  if(data.event==='input-language')setInputDirection(data.direction);
  if(data.event==='active-window')app.dataset.activeWindow=String(data.active);
  if(data.event==='note-fullscreen'){noteFullscreen=!!data.enabled;updateFullscreenButton();}
  if(data.event==='window-fullscreen'&&workspace){noteFullscreen=!!data.enabled;updateFullscreenButton();}
  if(data.event==='tab-content')installTabContent(data);
  if(data.event==='tabs-changed'&&workspace&&!window.JotWorkspace){tabHeaders=data.tabs;renderNoteTabs();}
  if(data.event==='clipboard-success'&&$('error').dataset.operation?.startsWith('clipboard-')){$('error').hidden=true;window.JotToast?.dismiss('note-error');}
  if(data.event==='preferences'){appPreferences={...JotDesign.defaults,...data.prefs};if(ready){applyPrefs();refreshToolbar();}if(tabbed&&!window.JotWorkspace)renderNoteTabs();}
  if(data.event==='note-color'&&activeNote()){activeNote().color=data.color;applyNoteColor();}
  if((data.event==='warning'||data.event==='quit-failed')&&!window.JotWorkspace){
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
  $('error').hidden = true;
  JotToast.error($('error').textContent,{id:'note-error'});
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
  return String(value || '').split(/\r\n?|\n/).map((line) => '<p dir="auto" data-jot-plain-line="true">' + (escapeHtml(line) || '<br>') + '</p>').join('');
}
function editorPlainText(root=editor,{image=()=>'',fromHtml=false}={}) {
  const blocks=new Set('ADDRESS ARTICLE ASIDE BLOCKQUOTE CAPTION DIV DL DT DD FIGURE FIGCAPTION FOOTER H1 H2 H3 H4 H5 H6 HEADER HR LI MAIN OL P PRE SECTION TABLE THEAD TBODY TFOOT TR UL'.split(' '));
  function content(node,preserve=!fromHtml){
    if(node.nodeType===Node.TEXT_NODE)return preserve?node.data.replace(/\r\n?/g,'\n'):node.data.replace(/[\t\r\n ]+/g,' ');
    if(node.nodeType===Node.ELEMENT_NODE){
      if(discardTags.has(node.tagName))return '';
      if(node.tagName==='IMG')return image(node);
      if(node.tagName==='BR')return '\n';
      preserve||=node.matches('pre,code')||/^(pre|pre-wrap|break-spaces)$/.test(node.style.whiteSpace);
      if(node.tagName==='TR')return [...node.children].filter(child=>child.matches('td,th')).map(child=>content(child,preserve)).join('\t');
      // A paragraph containing only its editing placeholder is one empty line.
      if(blocks.has(node.tagName)&&node.childNodes.length===1&&node.firstChild.nodeName==='BR')return '';
    }
    let output='',previousBlock=false,hasContent=false;
    for(const child of node.childNodes){
      const block=child.nodeType===Node.ELEMENT_NODE&&blocks.has(child.tagName);
      if(child.nodeType===Node.TEXT_NODE&&!child.data.trim()&&[child.previousSibling,child.nextSibling].some(sibling=>sibling?.nodeType===Node.ELEMENT_NODE&&blocks.has(sibling.tagName)))continue;
      const value=content(child,preserve);
      if(block){if(hasContent)output+='\n';output+=value;hasContent=true;previousBlock=true;}
      else if(value){if(previousBlock)output+='\n';output+=value;hasContent=true;previousBlock=false;}
    }
    if(fromHtml&&node.nodeName==='LI'){
      const list=node.parentElement,number=(Number(list?.getAttribute('start'))||1)+[...list.children].indexOf(node);
      return (list?.tagName==='OL'?number+'. ':'• ')+output;
    }
    return output;
  }
  return content(root);
}
// HTML is only a fallback for text, or a map of where embedded images belong.
// Never import its colors, fonts, code containers, backgrounds or active content.
function unformattedClipboardHtml(root) {
  const images=[],token='\uE000'+crypto.randomUUID()+'\uE001';
  const plain=editorPlainText(root,{fromHtml:true,image:node=>{
    const src=node.getAttribute('src')||'';if(!imagePattern.test(src)||src.length>12*1024*1024)return '';
    const image=document.createElement('img');image.src=src;image.alt=(node.getAttribute('alt')||JotI18n.text('تصویر')).slice(0,200);
    return token+(images.push(image.outerHTML)-1)+token;
  }});
  if(!plain)return '';
  return plainToHtml(plain).replace(new RegExp(token+'(\\d+)'+token,'g'),(_,index)=>images[Number(index)]||'');
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
      if(element.matches('p,div')&&node.dataset.jotPlainLine==='true')element.dataset.jotPlainLine='true';
      if(element.matches(blockSelector)&&['ltr','rtl'].includes(node.dataset.jotDirection)){element.dataset.jotDirection=node.dataset.jotDirection;element.dir=node.dataset.jotDirection;}
      if(element.matches(blockSelector)&&['ltr','rtl'].includes(node.dataset.jotNeutralDirection)){element.dataset.jotNeutralDirection=node.dataset.jotNeutralDirection;if(!element.dataset.jotDirection)element.dir=node.dataset.jotNeutralDirection;}
      if(tag==='BDI'){
        element.dir=['ltr','rtl','auto'].includes(node.dir)?node.dir:'auto';
        if(node.dataset.jotBidi==='token')element.dataset.jotBidi='token';
      }
      if (tag === 'PRE' || tag === 'CODE') element.dir = 'ltr';
      const color = node.style.color || node.getAttribute('color');
      if (color && CSS.supports('color', color) && !/var\(|url\(/i.test(color)) element.style.color = color;
      if (/^(bold|[6-9]00)$/.test(node.style.fontWeight)) element.style.fontWeight = '700';
      if (node.style.fontStyle === 'italic') element.style.fontStyle = 'italic';
      if (/^(underline|line-through)( (underline|line-through))?$/.test(node.style.textDecorationLine)) element.style.textDecorationLine = node.style.textDecorationLine;
      if(element.matches(blockSelector)){
        const alignment=node.dataset.jotAlign||node.style.textAlign||node.getAttribute('align');
        if(['left','center','right','justify'].includes(alignment)){element.dataset.jotAlign=alignment;element.style.textAlign=alignment;}
      }
      for(const property of ['backgroundColor','fontSize','fontFamily']){
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
// editorcn/Tiptap is the only editing engine. The host retains window, file,
// preference and clipboard ownership; ProseMirror owns document/history state.
const editorHost=document.createElement('div');editorHost.id='richEditorHost';editor.replaceWith(editorHost);
const legacyControls=document.createElement('div');legacyControls.hidden=true;legacyControls.inert=true;
legacyControls.id='legacyFormattingControls';legacyControls.append(...$('formatBar').childNodes);$('formatBar').after(legacyControls);
const dock=document.querySelector('.quiet-footer');$('writingArea').before(dock);dock.classList.add('document-toolbar');dock.ariaLabel='Document tools';app.dataset.document='true';
const rich=window.JotRichEditor=JotEditorFactory.create({
  contentHost:editorHost,toolbarHost:$('formatBar'),onChange:()=>onEdit(),
  onSelectionChange:()=>{if(ready){pmBookmark=rich.selection();refreshToolbar();}},
  onImage:()=>{rememberSelection();$('imageInput').click();},onError:showError,
  clipboard:payload=>request('clipboard-write',payload),getInputDirection:()=>inputDirection,
  plainText:html=>{const holder=document.createElement('div');holder.innerHTML=html;return editorPlainText(holder);}
});
editor=rich.editor.view.dom;
function normalizeDirection(){if(!composing)rich.refreshDirection();}
function setInputDirection(direction) {
  inputDirection=direction==='rtl'?'rtl':'ltr';
  if(composing)return;
  editor.dir=inputDirection;
  normalizeDirection();
}
editor.addEventListener('focus',()=>request('input-language').then(setInputDirection).catch(()=>{}));
function updateEmpty() {
  const empty = !editor.textContent.trim() && !editor.querySelector('img');
  editor.dataset.empty = String(empty);
}
function capture(html=rich.getHTML()) {
  if (!ready || !activeNote()) return;
  const note = activeNote();
  note.html = html;
  note.plain = rich.getText();
  note.updatedAt = Date.now();
}
function editorSelectionState(){rich.syncSelection();return rich.selection();}
function restoreEditorSelection(state) {
  if(!rich.restoreSelection(state))return false;rememberSelection();return true;
}
function sameEditorSelection(state) {
  const current=editorSelectionState();
  return !!current&&!!state&&current.anchor===state.anchor&&current.head===state.head&&current.all===state.all&&JSON.stringify(current.json)===JSON.stringify(state.json);
}
function rememberHistorySelection() {
  rich.syncSelection();
}
function flushTypingHistory(html) {
  clearTimeout(historyTimer);historyTimer=0;
  typingHistoryPending=false;rich.flush();
}
function queueSave() {
  revision++;
  window.JotNoteFiles?.edited(model.activeId);
  clearTimeout(saveTimer);
  setStatus('saving', 'در حال ذخیره…');
  saveTimer = setTimeout(() => saveNow().catch(showError), 250);
}
function onEdit(kind = 'typing') {
  if (!ready || rich.blocked) return;
  updateEmpty();
  // Tiptap transactions own undo and selection. Serialize only at save boundaries.
  queueSave();
  app.classList.add('typing');
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => app.classList.remove('typing'), 1800);
}
async function flushFinal(holdEditing=false){
  const previousFocus=document.activeElement;
  rich.dismissUi();
  editorLockedByHost||=holdEditing;app.inert=pendingEdits.size===0;
  try{
    while(pendingEdits.size)await Promise.all([...pendingEdits]);
    app.inert=true;
    // Capture the DOM even if the last input event/IME revision has not arrived.
    // Recheck after an in-flight save so its earlier snapshot cannot win.
    do{await saveNow(true);}while(revision>savedRevision||rich.getHTML()!==activeNote().html);
  }finally{
    if(!editorLockedByHost){
      app.inert=false;
      if(!app.hidden&&previousFocus?.isConnected&&app.contains(previousFocus)&&!document.querySelector('dialog[open]')&&[document.body,previousFocus].includes(document.activeElement)){
        if(previousFocus===editor)rich.editor.view.focus();else previousFocus.focus({preventScroll:true});
      }
    }
  }
}
async function saveNow(forceCapture=false) {
  await window.JotDocumentHeading?.flush();
  await notePreferenceChain;
  clearTimeout(saveTimer);
  if (!ready) return Promise.reject(new Error('یادداشت‌ها هنوز آماده نیستند.'));
  rich.flush();
  if(rich.blocked)return saveChain;
  let captured=false;
  if(forceCapture){
    composing=false;
    const html=rich.getHTML();
    if(html!==activeNote().html){capture(html);revision++;captured=true;}
  }
  if(revision<=savedRevision)return saveChain;
  if(!captured)capture();
  flushTypingHistory(activeNote().html);
  const current = revision;
  const {id,html,plain,updatedAt}=activeNote();
  const snapshot = {id,html,plain,updatedAt};
  saveChain = saveChain.catch(() => {}).then(async () => {
    if (current <= savedRevision) return;
    setStatus('saving', 'در حال ذخیره…');
    try { await request('save', snapshot); }
    catch(error){error.operation='save';setStatus('error');throw error;}
    savedRevision = current;
    window.JotNoteFiles?.refresh();
    updateActiveTabLabel();
    if (current === revision) {
      setStatus('saved', 'ذخیره شد');
      if($('error').dataset.operation==='save'){$('error').hidden = true;JotToast.dismiss('note-error');}
    }
  });
  return saveChain;
}
function rememberSelection() {
  const selection = window.getSelection();
  if (selection.rangeCount && editor.contains(selection.anchorNode) && editor.contains(selection.focusNode)) {
    bookmark = selection.getRangeAt(0).cloneRange();
    bookmarkDirection={range:bookmark,backward:!selection.isCollapsed&&selection.anchorNode===bookmark.endContainer&&selection.anchorOffset===bookmark.endOffset};
    rich.syncSelection();pmBookmark=rich.selection();
  }
}
function restoreSelection() {
  if(!rich.restoreSelection(pmBookmark))rich.editor.commands.focus('end');
}
function command(name, value) {
  restoreSelection();
  rich.command(name,value);
  rememberSelection();
  refreshToolbar();
}
function undo(redo = false) {
  if(!ready||composing||deleting)return;
  rich.undo(redo);rememberSelection();updateEmpty();refreshToolbar();
}
function closePanels() {
  rich.dismissUi();
  window.JotWritingTools?.hide();
  window.JotEditorMenu?.close();
  closeFormatMenus();
  setToolbarVisible(model.prefs.toolbarVisible !== false); setNoteMenuVisible(false);
  $('menuButton').setAttribute('aria-expanded', 'false');
  keepFormatOpen = model.prefs.toolbarVisible !== false;
}
function selectNote(id) {
  flushTypingHistory();
  if (ready) capture();
  const note = model.notes.find((item) => item.id === id);
  if (!note) return;
  model.activeId = id;
  rich.loadNote(id,note.html);
  window.JotDocumentHeading?.render();
  bookmark = null;pmBookmark=rich.selection();
  normalizeDirection(); updateEmpty(); closePanels();
  $('writingArea').scrollTop = 0;
  if ($('notesDialog').open) $('notesDialog').close();
  editor.focus({ preventScroll: true });
  // Opening/canonicalizing a legacy note is not an edit or a file-dirty change.
}
async function newNote() {
  if(!ready)return;
  const button=$('newButton');if(button.disabled)return;
  button.disabled=true;button.ariaBusy='true';
  try{await saveNow();await request('new-note');}catch(error){showError(error);}
  finally{button.disabled=false;button.ariaBusy='false';}
}
function rememberTabView(){
  if(!workspace||!ready||!activeNote())return;
  tabUi.set(model.activeId,{scroll:$('writingArea').scrollTop,selection:editorSelectionState()});
}
function restoreTabView(){
  const view=tabUi.get(model.activeId);if(!view)return;
  if(!restoreEditorSelection(view.selection))bookmark=null;
  $('writingArea').scrollTop=view.scroll;
}
function pruneTabHistories(){
  const openIds=new Set(tabHeaders.map(note=>note.id));rich.prune([...openIds]);for(const id of tabUi.keys())if(id!==model.activeId&&!openIds.has(id))tabUi.delete(id);
}
function renderNoteTabs(){
  if(!workspace)return;
  pruneTabHistories();
  if(window.JotWorkspace)JotWorkspace.updateTabs(tabHeaders);
}
function updateActiveTabLabel(){
  if(!workspace)return;
  const n=activeNote(),header=tabHeaders.find(t=>t.id===n?.id);if(!header)return;
  const plain=n.plain.slice(0,160);if(header.plain===plain)return;
  header.plain=plain;renderNoteTabs();
}
async function runTabAction(action,id=null){
  if(window.JotWorkspace)return JotWorkspace.runAction(action,id);
  if(!workspace||tabBusy||!ready||action==='tab-switch'&&id===model.activeId)return;
  tabBusy=true;tabPendingId=id;tabPendingAction=action;rememberTabView();closePanels();renderNoteTabs();
  try{await request(action,id);}catch(error){showError(error);}finally{tabBusy=false;tabPendingId=null;tabPendingAction=null;renderNoteTabs();}
}
function installTabContent(data){
  if(!workspace)return;
  // The native host has flushed the old note and serialized tab operations.
  // Keep per-note undo histories, but reset save revisions for the new identity.
  clearTimeout(saveTimer);clearTimeout(historyTimer);typingHistoryPending=false;historyTimer=0;
  ready=false;deleting=false;imageBusy=false;composing=false;bookmark=null;pmBookmark=null;revision=0;savedRevision=0;saveChain=Promise.resolve();
  if($('deleteDialog').open)$('deleteDialog').close();$('deleteConfirm').disabled=$('deleteCancel').disabled=false;$('deleteConfirm').ariaBusy='false';$('deleteConfirm').textContent='Move to Trash';
  model.notes=[data.note];model.activeId=data.note.id;confirmedNoteView={...data.note.view};
  appPreferences={...JotDesign.defaults,...data.prefs};model.prefs={...appPreferences};tabHeaders=data.tabs;tabbed=data.tabbed;
  selectNote(data.note.id);applyPrefs();ready=true;rich.setEditable(true);resumeEditing();$('error').hidden=true;JotToast.dismiss('note-error');
  restoreTabView();if(!window.JotWorkspace){renderNoteTabs();JotBridge.send('tab-ready',data.intent);}
}
function noteName(note) {
  return note.title || (note.plain || '').split(/\n/).find((line) => line.trim())?.trim().slice(0, 90) || note.legacyTitle || (note.html.includes('<img') ? 'یادداشت تصویری' : 'یادداشت بدون متن');
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
  const view=activeNote()?.view??{},overrides={};
  for(const key of ['fontSize','lineHeight','toolbarVisible','pinned'])if(view[key]!==undefined&&view[key]!==null)overrides[key]=view[key];
  model.prefs=JotDesign.apply({...appPreferences,...overrides});
  applyNoteColor();
  $('fontSize').textContent=model.prefs.fontSize+' px';
  $('smallerButton').disabled=model.prefs.fontSize<=13;
  $('largerButton').disabled=model.prefs.fontSize>=24;
  $('smallerButton').title=$('smallerButton').disabled?'Minimum font size (13 px)':'Decrease font size';
  $('largerButton').title=$('largerButton').disabled?'Maximum font size (24 px)':'Increase font size';
  $('lineHeight').textContent=model.prefs.lineHeight+'×';
  $('tighterLinesButton').disabled=model.prefs.lineHeight<=1.2;
  $('looserLinesButton').disabled=model.prefs.lineHeight>=2.5;
  for(const [key,id] of [['fontSize','defaultFontSize'],['lineHeight','defaultLineHeight']]){
    const button=$(id),inherited=view[key]===undefined||view[key]===null;
    const busy=button.getAttribute('aria-busy')==='true';button.disabled=busy||inherited;button.textContent=busy?'Applying…':inherited?'Default':'Use default';button.title=inherited?'Using the app writing default':'Use the app writing default for this note';
  }
  $('tighterLinesButton').title=$('tighterLinesButton').disabled?'Minimum line height (1.2×)':'Decrease line height';
  $('looserLinesButton').title=$('looserLinesButton').disabled?'Maximum line height (2.5×)':'Increase line height';
  const nextMode=model.prefs.theme==='dark'?'light':'dark';
  $('themeLabel').textContent=nextMode==='light'?'Light mode':'Dark mode';
  $('themeButton').title='Switch all of Jot to '+nextMode+' mode';
  keepFormatOpen=model.prefs.toolbarVisible!==false;
  setToolbarVisible(keepFormatOpen);
  applyPin(!!model.prefs.pinned);
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
function setAppTheme(theme) {
  // Keep writes ordered with note changes, but only the shared preference
  // broadcast applies theme. A stale per-note view can never override it.
  notePreferenceChain=notePreferenceChain.then(()=>request('app-theme',theme)).catch(showError);
  return notePreferenceChain;
}
function setPreference(patch) {
  const target=activeNote(),sequence=++notePreferenceRevision;
  target.view={...target.view};
  for(const [key,value] of Object.entries(patch)){if(value===null&&['fontSize','lineHeight'].includes(key))delete target.view[key];else target.view[key]=value;}
  applyPrefs();
  notePreferenceChain=notePreferenceChain.then(async()=>{
    try{
      const view=await request('note-preferences',{id:target.id,settings:patch});confirmedNoteView={...view};
      if(sequence===notePreferenceRevision){target.view={...view};applyPrefs();}
    }catch(error){
      if(sequence===notePreferenceRevision){target.view={...confirmedNoteView};applyPrefs();}
      showError(error);
    }
  });
  return notePreferenceChain;
}
function refreshToolbar() {
  // editorcn observes ProseMirror state for all visible formatting controls.
  refreshDirectionControls();
  refreshAlignmentControls();
}
function selectedDirectionBlocks(){
  const selection=getSelection(),range=bookmark||(selection.rangeCount?selection.getRangeAt(0):null);
  if(!range||!editor.contains(range.commonAncestorContainer))return [];
  if(range.collapsed){
    const node=range.startContainer.nodeType===Node.ELEMENT_NODE?range.startContainer:range.startContainer.parentElement;
    if(node===editor){
      const atEnd=range.startOffset>=editor.childNodes.length,item=editor.childNodes[range.startOffset]||editor.lastElementChild;
      if(!item||item.nodeType!==Node.ELEMENT_NODE||item.closest('pre,code'))return [];
      if(item.matches(blockSelector)&&!item.querySelector(blockSelector))return [item];
      const leaves=[...item.querySelectorAll(blockSelector)].filter(block=>!block.closest('pre,code')&&!block.querySelector(blockSelector));
      const block=atEnd?leaves.at(-1):leaves[0];return block?[block]:[];
    }
    const block=node.closest(blockSelector);return block&&block!==editor&&editor.contains(block)&&!block.closest('pre,code')?[block]:[];
  }
  const candidates=[...editor.querySelectorAll(blockSelector)].filter(block=>!block.closest('pre,code')&&!block.querySelector(blockSelector));
  return candidates.filter(block=>range.intersectsNode(block));
}
function refreshDirectionControls(){
  if($('menu').hidden)return;
  const blocks=selectedDirectionBlocks(),modes=new Set(blocks.map(block=>block.dataset.jotDirection||'auto'));
  document.querySelectorAll('[data-paragraph-direction]').forEach(button=>{button.disabled=!blocks.length;button.setAttribute('aria-pressed',String(modes.size===1&&modes.has(button.dataset.paragraphDirection)));});
}
function setParagraphDirection(direction){
  if(!['auto','ltr','rtl'].includes(direction)||composing)return;
  restoreSelection();rich.setDirection(direction);rememberSelection();refreshDirectionControls();closePanels();
}
document.querySelectorAll('[data-paragraph-direction]').forEach(button=>{
  button.addEventListener('pointerdown',event=>event.preventDefault());button.onclick=()=>setParagraphDirection(button.dataset.paragraphDirection);
});
function paragraphAlignment(block){return ['left','center','right','justify'].includes(block.dataset.jotAlign||block.style.textAlign)?block.dataset.jotAlign||block.style.textAlign:'auto';}
function selectedAlignmentBlocks(){
  const selection=getSelection(),range=bookmark||(selection.rangeCount?selection.getRangeAt(0):null);
  return selectedDirectionBlocks().filter(block=>{
    if(!range||range.collapsed)return true;
    if(!block.textContent&&!block.querySelector('br,img,hr')){
      const whole=document.createRange();whole.selectNode(block);
      if(range.compareBoundaryPoints(Range.START_TO_START,whole)<=0&&range.compareBoundaryPoints(Range.END_TO_END,whole)>=0)return true;
    }
    const overlap=range.cloneRange(),contents=document.createRange();contents.selectNodeContents(block);
    if(overlap.compareBoundaryPoints(Range.START_TO_START,contents)<0)overlap.setStart(block,0);
    if(overlap.compareBoundaryPoints(Range.END_TO_END,contents)>0)overlap.setEnd(block,block.childNodes.length);
    // A selection ending at the start of the next paragraph must not format it.
    return !overlap.collapsed&&(overlap.toString().length>0||!!overlap.cloneContents().querySelector('br,img,hr'));
  });
}
function refreshAlignmentControls(){
  if($('menu').hidden)return;
  const blocks=selectedAlignmentBlocks(),modes=new Set(blocks.map(paragraphAlignment));
  document.querySelectorAll('[data-paragraph-alignment]').forEach(button=>{button.disabled=!blocks.length;button.ariaPressed=String(modes.size===1&&modes.has(button.dataset.paragraphAlignment));});
}
function setParagraphAlignment(alignment){
  if(!['auto','left','center','right','justify'].includes(alignment)||!ready||composing||deleting)return;
  restoreSelection();rich.setAlignment(alignment);rememberSelection();refreshAlignmentControls();closePanels();
}
document.querySelectorAll('[data-paragraph-alignment]').forEach(button=>{
  button.addEventListener('pointerdown',event=>event.preventDefault());button.onclick=()=>setParagraphAlignment(button.dataset.paragraphAlignment);
});
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
  if(app.dataset.document==='true'){
    const below=rect.bottom-container.top+6;panel.style.top=below+'px';panel.style.bottom='auto';
    panel.style.maxHeight=Math.max(40,container.height-below-8)+'px';
    panel.style.left=Math.max(8,Math.min(rect.left-container.left,container.width-panel.offsetWidth-8))+'px';return;
  }
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
function insertImages(...args){return trackEdit(insertImagesCore(...args));}
async function insertImagesCore(files,isCurrent=()=>true) {
  if (!files.length || imageBusy) return;
  rememberSelection();
  const targetId = model.activeId,targetRevision=revision,targetSelection=editorSelectionState();
  imageBusy = true;
  $('imageButton').disabled = true;
  try {
    const results = [];
    for (const file of files) results.push(await readImage(file));
    if (model.activeId !== targetId||revision!==targetRevision||composing||deleting||!isCurrent()) throw new Error('یادداشت تغییر کرد. تصویر را دوباره بچسبانید.');
    const size = results.reduce((sum, src) => sum + src.length, 0) + rich.getHTML().length;
    if (size > 24 * 1024 * 1024) throw new Error('حجم این یادداشت زیاد است؛ تصویر را در یادداشت جدید قرار دهید.');
    restoreEditorSelection(targetSelection);
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
  return {html:unformattedClipboardHtml(doc.body),missing};
}
function paste(...args){return trackEdit(pasteCore(...args));}
function pasteWithParagraphAlignment(html,alignment){
  if(!['left','center','right','justify'].includes(alignment))return html;
  const content=document.createElement('div');content.innerHTML=html;
  for(const block of content.querySelectorAll('p,div,li')){block.dataset.jotAlign=alignment;block.style.textAlign=alignment;}
  return content.innerHTML;
}
async function pasteCore(event,isCurrent=()=>true) {
  event.preventDefault();
  rememberSelection();
  const targetId=model.activeId,targetRevision=revision,targetSelection=editorSelectionState();
  const targetAlignment=selectedAlignmentBlocks().map(paragraphAlignment)[0]||'auto';
  const data=event.clipboardData;
  if(!data)return;
  const images=[...data.items].filter(item=>item.kind==='file'&&item.type.startsWith('image/')).map(item=>item.getAsFile()).filter(Boolean);
  const html=data.getData('text/html');
  const plain=data.getData('text/plain');
  const containsImages=html&&/<img\b/i.test(html)&&new DOMParser().parseFromString(html,'text/html').body.querySelector('img');
  // Mixed text/images keep their ordering, but not source text styling. A bitmap
  // offered as an alternate representation must not replace clipboard text.
  if(containsImages){
    const imported=await importHtml(html,images);
    if(model.activeId!==targetId||revision!==targetRevision||composing||deleting||!isCurrent())throw new Error('یادداشت تغییر کرد؛ دوباره بچسبانید.');
    restoreEditorSelection(targetSelection);
    if(imported.html)command('insertHTML',pasteWithParagraphAlignment(imported.html,targetAlignment));
    if(imported.missing){$('error').textContent='Some images could not be imported. Their places are marked in the note.';$('error').hidden=true;JotToast.warning($('error').textContent,{id:'image-import-warning'});}
  }else if(plain)command('insertHTML',pasteWithParagraphAlignment(plainToHtml(plain),targetAlignment));
  else if(html){
    const content=unformattedClipboardHtml(new DOMParser().parseFromString(html,'text/html').body);
    if(content)command('insertHTML',pasteWithParagraphAlignment(content,targetAlignment));else if(images.length)await insertImages(images,isCurrent);
  }else if(images.length)await insertImages(images,isCurrent);
}
function copyPayload(all=false) {
  const container=document.createElement('div');
  container.innerHTML=rich.selectedHTML(all);
  const html=rich.blocked?sanitizeHtml(container.innerHTML):container.innerHTML;
  const text=editorPlainText(container);
  const images=container.querySelectorAll('img');
  return {html,text,...(images.length===1?{image:images[0].src}:{})};
}
function cutSelection(isCurrent=()=>true) {
  if(cutting||!ready||composing||deleting||!editor.isContentEditable)return Promise.resolve(false);
  rich.syncSelection();if(rich.editor.state.selection.empty)return Promise.resolve(false);
  const selection=editorSelectionState();if(!selection)return Promise.resolve(false);
  const noteId=model.activeId,noteRevision=revision,payload=copyPayload();
  cutting=true;
  const operation=(async()=>{
    try{
      const copied=await request('clipboard-write',payload);
      if(!isCurrent())return false;
      if(copied!==true)throw new Error('Copy was interrupted. Your note is unchanged.');
      if(!ready||composing||deleting||!editor.isContentEditable||model.activeId!==noteId||revision!==noteRevision||!sameEditorSelection(selection)||window.JotWorkspace&&JotWorkspace.view!=='note')
        throw new Error('Copied, but the note or selection changed. Nothing was cut.');
      if(!restoreEditorSelection(selection))return false;
      command('delete');return true;
    }finally{cutting=false;}
  })();
  // Closing/switching waits for a possible deletion, but a rejected clipboard
  // write did not change the note and must not become a failed-save result.
  trackEdit(operation.catch(()=>false));return operation;
}
editor.addEventListener('cut',event=>{
  event.preventDefault();event.stopImmediatePropagation();cutSelection().catch(showError);
},true);
editor.addEventListener('copy',event=>{
  const payload=copyPayload();
  event.preventDefault();
  event.stopImmediatePropagation();
  event.clipboardData.setData('text/html',payload.html);
  event.clipboardData.setData('text/plain',payload.text);
  request('clipboard-write',payload).catch(showError);
},true);
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
editor.addEventListener('compositionstart', () => {
  composing = true;
});
editor.addEventListener('compositionend', () => { composing = false; });
editor.addEventListener('paste', (event) => {event.stopImmediatePropagation();paste(event).catch(showError);},true);
editor.addEventListener('dragover', (event) => {if(event.dataTransfer.types.includes('Files'))event.preventDefault();});
editor.addEventListener('drop', (event) => {
  if(!event.dataTransfer.files.length)return;
  event.preventDefault();
  event.stopImmediatePropagation();
  const range = document.caretRangeFromPoint(event.clientX, event.clientY);
  if (range && editor.contains(range.startContainer)) { window.getSelection().removeAllRanges(); window.getSelection().addRange(range); }
  insertImages([...event.dataTransfer.files]).catch(showError);
},true);
editor.addEventListener('click', (event) => { if (event.target.closest('a')) event.preventDefault(); });
legacyControls.querySelectorAll('[data-command]').forEach((button) => button.addEventListener('click', () => {command(button.dataset.command);closeFormatMenus();}));
$('formatButton').addEventListener('pointerdown', (event) => { rememberSelection(); event.preventDefault(); });
$('formatButton').addEventListener('click',()=>setPreference({toolbarVisible:!keepFormatOpen}));
document.addEventListener('selectionchange',()=>{
  rememberSelection();
  if(selectionTimer)return;
  selectionTimer=requestAnimationFrame(()=>{selectionTimer=0;refreshToolbar();});
});
$('newButton').addEventListener('click',()=>newNote());
$('dialogNewButton').addEventListener('click', newNote);
$('menuNotesButton').addEventListener('click', openNotes);
$('tighterLinesButton').addEventListener('click',()=>setPreference({lineHeight:JotDesign.stepLineHeight(model.prefs.lineHeight,-1)}));
$('looserLinesButton').addEventListener('click',()=>setPreference({lineHeight:JotDesign.stepLineHeight(model.prefs.lineHeight,1)}));
for(const [id,key] of [['defaultFontSize','fontSize'],['defaultLineHeight','lineHeight']])$(id).addEventListener('click',async()=>{
  const button=$(id);button.ariaBusy='true';button.disabled=true;
  try{await setPreference({[key]:null});}finally{button.removeAttribute('aria-busy');applyPrefs();}
});
$('closeNotesButton').addEventListener('click', () => { $('notesDialog').close(); editor.focus(); });
$('search').addEventListener('input', renderList);
$('notesDialog').addEventListener('close', () => editor.focus());
function positionNoteMenu() {
  if($('menu').hidden)return;
  $('menu').style.maxHeight=Math.max(0,app.clientHeight-24-(window.JotWorkspace?0:($('workspaceHandle')?.offsetHeight||0)))+'px';
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
  $('formatButton').title=(open?'Hide':'Show')+' formatting tools';
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
    refreshDirectionControls();
    refreshAlignmentControls();
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
$('themeButton').addEventListener('click',()=>setAppTheme(model.prefs.theme==='dark'?'light':'dark'));
for (const [id, delta] of [['smallerButton', -1], ['largerButton', 1]]) $(id).addEventListener('click', () => setPreference({fontSize:Math.min(24,Math.max(13,model.prefs.fontSize+delta))}));
function applyPin(value) {
    const changed=pinned!==value;pinned=value;
    $('pinButton').setAttribute('aria-pressed', String(pinned));
    const glyph=JotDesign.icon('pin');
    if(pinned)glyph.querySelector('path:last-child').setAttribute('fill','currentColor');
    $('pinButton').replaceChildren(glyph);
    $('pinButton').title=pinned?'Unpin note':'Keep note on top';
    $('pinButton').setAttribute('aria-label',$('pinButton').title);
    if(changed)request('pin',pinned).catch(showError);
}
$('pinButton').addEventListener('click',()=>setPreference({pinned:!pinned}));
function updateFullscreenButton(){
  for(const button of [$('fullscreenButton'),$('workspaceMaximize')].filter(Boolean)){
    button.ariaPressed=String(noteFullscreen);button.title=button.ariaLabel=noteFullscreen?'Restore':'Maximize';
    button.replaceChildren(JotDesign.icon(noteFullscreen?'window-restore':'window-maximize'));
  }
}
async function toggleNoteFullscreen(){
  if(fullscreenBusy)return;fullscreenBusy=true;const button=workspace?$('workspaceMaximize'):$('fullscreenButton');
  rememberSelection();closePanels();button.disabled=true;button.ariaBusy='true';button.title='Changing view…';
  try{noteFullscreen=await request('note-fullscreen',{enabled:!noteFullscreen});restoreSelection();}
  catch(error){showError(error);}
  finally{fullscreenBusy=false;button.disabled=false;button.ariaBusy='false';updateFullscreenButton();}
}
$('fullscreenButton').onclick=toggleNoteFullscreen;
async function leave(action) { try { if(action!=='hide')await saveNow();await request(action); } catch (error) { showError(error); } }
$('minimizeButton').onclick=()=>leave('minimize');
$('hideButton').addEventListener('click', () => leave('hide'));
$('deleteButton').onclick=()=>{closePanels();$('deleteError').hidden=true;$('deleteDialog').showModal();$('deleteCancel').focus();};
$('deleteCancel').onclick=()=>$('deleteDialog').close();
$('deleteDialog').addEventListener('cancel',event=>{if(deleting)event.preventDefault();});
$('deleteDialog').addEventListener('close',()=>{if(!deleting)editor.focus();});
$('deleteConfirm').onclick=async()=>{
  if(deleting)return;
  if(imageBusy){$('deleteError').textContent='Wait for the image to finish inserting, then try again.';$('deleteError').hidden=false;return;}
  deleting=true;rich.setEditable(false);$('deleteError').hidden=true;
  $('deleteConfirm').disabled=$('deleteCancel').disabled=true;
  $('deleteConfirm').setAttribute('aria-busy','true');$('deleteConfirm').textContent='Moving…';
  try{await saveNow();await request('note-delete',model.activeId);}
  catch(error){
    JotBridge.reportError(error,'note-delete');$('deleteError').textContent='The note could not be deleted. Your note is still available. '+JotI18n.text(error.message);$('deleteError').hidden=false;
    deleting=false;rich.setEditable(true);$('deleteConfirm').disabled=$('deleteCancel').disabled=false;
    $('deleteConfirm').setAttribute('aria-busy','false');$('deleteConfirm').textContent='Move to Trash';
  }
};
$('imageButton').addEventListener('pointerdown', () => rememberSelection());
$('imageButton').addEventListener('click', () => $('imageInput').click());
$('imageInput').addEventListener('change', () => { insertImages([...$('imageInput').files]).catch(showError); $('imageInput').value = ''; });
$('exportButton').addEventListener('click', async () => {
  closePanels();
  try { await saveNow(); await request('export', { name: noteName(activeNote()), html: rich.blocked?sanitizeHtml(rich.getHTML()):rich.getHTML() }); } catch (error) { showError(error); }
});
let headerDrag=null;
function cancelHeaderDrag(){
  const drag=headerDrag;headerDrag=null;
  if(drag&&$('handle').hasPointerCapture(drag.id))$('handle').releasePointerCapture(drag.id);
}
$('handle').addEventListener('pointerdown',event=>{
  if(event.button!==0||event.isPrimary===false||event.target.closest('button')||fullscreenBusy)return;
  if(!noteFullscreen){request('drag').catch(showError);return;}
  event.preventDefault();
  const rect=$('handle').getBoundingClientRect();
  headerDrag={id:event.pointerId,startY:event.clientY,anchorX:(event.clientX-rect.left)/rect.width,anchorY:event.clientY-rect.top};
  if(event.isTrusted)$('handle').setPointerCapture(event.pointerId);
});
// Track an active header gesture across the document as well as pointer
// capture: WebView can deliver a fast downward move to the editor underneath.
document.addEventListener('pointermove',event=>{
  if(!headerDrag||event.pointerId!==headerDrag.id)return;
  if(!(event.buttons&1)){cancelHeaderDrag();return;}
  if(event.clientY-headerDrag.startY<8)return;
  const drag=headerDrag;cancelHeaderDrag();
  if(!noteFullscreen||fullscreenBusy)return;
  closePanels();
  request('drag',{restore:true,anchorX:drag.anchorX,anchorY:drag.anchorY}).catch(showError);
});
for(const type of ['pointerup','pointercancel'])document.addEventListener(type,cancelHeaderDrag);
$('handle').addEventListener('lostpointercapture',cancelHeaderDrag);
window.addEventListener('blur',cancelHeaderDrag);
document.addEventListener('pointerdown', (event) => {
  if(!event.target.closest('#formatBar,.format-popover'))closeFormatMenus();
  if (!event.target.closest('#menu,#menuButton')) setNoteMenuVisible(false);
});
document.addEventListener('keydown', (event) => {
  if(event.defaultPrevented)return;
  if(window.JotWorkspace&&JotWorkspace.view!=='note')return;
  if(document.querySelector('dialog[open]'))return;
  if (event.isComposing) return;
  if(event.altKey||event.getModifierState?.('AltGraph'))return;
  const ctrl = event.ctrlKey || event.metaKey;
  if (ctrl && editor.contains(document.activeElement) && ['KeyZ','KeyY'].includes(event.code)) { event.preventDefault(); undo(event.shiftKey || event.code === 'KeyY'); }
  else if (ctrl && editor.contains(document.activeElement) && ['KeyB','KeyI','KeyU'].includes(event.code)) { event.preventDefault(); command({KeyB:'bold',KeyI:'italic',KeyU:'underline'}[event.code]); }
  else if (event.key === 'Escape' && !$('notesDialog').open) {
    event.preventDefault();
    if(!$('menu').hidden){closePanels();(window.JotWorkspace?(workspaceOptionsOwner?.isConnected?workspaceOptionsOwner:editor):$('menuButton')).focus({preventScroll:true});}
    else if(document.querySelector('.format-popover:not([hidden])'))closePanels();
  }
});
async function boot() {
  rich.setEditable(false);loadIcons();
  await window.JotShortcutBindings?.ready;
  const colorOrder=['crimson','red','orange','amber','yellow','lime','green','emerald','teal','cyan','sky','blue','indigo','violet','purple','fuchsia','pink','rose','neutral'];
  const rank=slug=>{const index=colorOrder.indexOf(slug);return index<0?colorOrder.length:index;};
  const accents=[...JotAccents].sort((a,b)=>rank(a.slug)-rank(b.slug));
  $('noteColors').style.setProperty('--note-color-count',accents.length);
  for(const accent of accents){
    const button=document.createElement('button');button.className='note-color';button.dataset.noteColor=accent.slug;
    button.title=button.ariaLabel=accent.slug[0].toUpperCase()+accent.slug.slice(1);button.append(JotDesign.icon('check'));
    button.onclick=()=>setNoteColor(accent.slug);$('noteColors').append(button);
  }
  if(window.JotWorkspace){
    workspace=true;tabbed=true;app.dataset.workspace='true';app.dataset.activeWindow='true';app.dataset.paintReady='true';
    noteFullscreen=JotWorkspace.maximized;
    return;
  }
  const {context,model:stored}=await request('note-load');
  noteFullscreen=!!context.fullscreen;updateFullscreenButton();
  workspace=false;tabbed=false;tabHeaders=[];
  app.dataset.activeWindow=String(!!context.active);
  setInputDirection(context.inputDirection);
  if(!stored||!stored.notes.some(note=>note.id===context.noteId))throw new Error('یادداشت پیدا نشد.');
  model=stored;appPreferences={...JotDesign.defaults,...stored.prefs};model.prefs={...appPreferences};model.activeId=context.noteId;
  confirmedNoteView={...activeNote().view};
  selectNote(context.noteId);ready=true;rich.setEditable(true);
  renderNoteTabs();
  applyPrefs();setStatus('saved','ذخیره شد');savedRevision=revision;editor.focus();window.jotReady=true;
  restoreTabView();
  // Resolve the real header color with initial transitions disabled before
  // revealing the native window. No neutral/white intermediate frame is visible.
  window.jotInitialHeader=getComputedStyle($('handle'),'::before').backgroundColor;
  JotBridge.send('editor-ready');
  requestAnimationFrame(()=>{app.dataset.paintReady='true';});
}
window.JotNoteEditor={show:installTabContent,
  suspend(){if(ready){rememberTabView();flushTypingHistory();}closePanels();rich.setEditable(false);resumeEditing();ready=false;},
  setHeaders(headers){tabHeaders=headers;pruneTabHistories();},
  openOptions(owner){if(ready){workspaceOptionsOwner=owner;$('menuButton').click();positionNoteMenu();}},
  save:saveNow
};
JotNoteEditor.ready=boot();
if(!window.JotWorkspace)JotNoteEditor.ready.catch(error=>{showError(error);JotBridge.send('editor-failed');});

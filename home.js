'use strict';
(()=>{
const embedded=!!window.JotWorkspace;
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let homeData={version:2,notes:[],folders:[],prefs:{...JotDesign.defaults}},refreshVersion=0,selectedId=null,editingId=null;
let folder=null,query='',viewMode='grid',metadataMode='note',confirmAction=null,confirmBusy=false,fullscreen=false;
let workspaceTabs=[],tabBusy=false,tabPendingId=null,tabPendingAction=null;
const cardViews=new Map(),dateFormatter=new Intl.DateTimeFormat(JotI18n.locale,{month:'short',day:'numeric'});
let indexVisible=true,indexDirty=false,refreshScheduled=false,searchTimer;
let contentRenderScheduled=false,contentRenderPending=false;
const contentUpdates=new Map();
let pinnedOnly=false,selectionMode=false,bulkBusy=false;
const selectedNotes=new Set();
let trashMode=false,trashItems=[],trashCount=0,trashLoading=false,trashError=null;
const selectedTrash=new Set();
const indexViewKey='jot-index-view';
try{const previous=JSON.parse(sessionStorage.getItem(indexViewKey)||'null');if(previous){selectedId=previous.selectedId??null;folder=previous.folder??null;query=previous.query||'';pinnedOnly=previous.pinnedOnly===true;trashMode=previous.trashMode===true;}}catch{}
function saveIndexView(){try{sessionStorage.setItem(indexViewKey,JSON.stringify({selectedId,folder,query,pinnedOnly,trashMode}));}catch{}}
function error(e){
  JotBridge.reportError(e,'home');const alert=document.querySelector('dialog[open] .dialog-error');
  if(alert){alert.textContent=t(e.message);alert.hidden=false;}else{$('homeError').textContent=t(e.message);$('homeError').hidden=true;JotToast.error(t(e.message),{id:'home-error'});}
}
function displayMixedText(element,text){element.textContent=text;JotBidi.normalize(element,new Set([element]),'ltr');}
function displayPreview(element,text){element.replaceChildren();for(const line of text.split(/\n/).filter(s=>s.trim())){const span=document.createElement('span');span.dir='auto';displayMixedText(span,line);element.append(span);}}
function noteName(note){return note.title||note.legacyTitle||note.noteFile?.path?.split(/[\\/]/).pop()||'';}
function searchText(value){return String(value||'').normalize('NFKC').replace(/\u064a/g,'\u06cc').replace(/\u0643/g,'\u06a9').replace(/[\u06f0-\u06f9\u0660-\u0669]/g,c=>String(c.charCodeAt(0)&15)).replace(/[\u200c\u200d\u200e\u200f\u064b-\u065f]/g,'').toLocaleLowerCase();}
function noteMatches(note){return (!pinnedOnly||note.libraryPinned===true)&&(folder===null||note.group===folder)&&(!query||searchText([noteName(note),note.noteFile?.path,note.title,note.legacyTitle,note.plain,note.group].join('\n')).includes(searchText(query)));}
function visibleNotes(){return homeData.notes.filter(noteMatches).sort((a,b)=>Number(b.libraryPinned===true)-Number(a.libraryPinned===true)||b.updatedAt-a.updatedAt);}
function selectCard(id){selectedId=id;saveIndexView();updateSelectedCards();}
function openNote(note,tabs=false){selectCard(note.id);return request(tabs?'open-note-tab':'open-note-default',note.id);}
function createCard(note){
  const card=document.createElement('article');card.className='note-card';card.dataset.noteId=note.id;
  const open=document.createElement('button');open.className='card-open';open.type='button';
  const title=document.createElement('strong');title.className='card-title';title.dir='auto';
  const snippet=document.createElement('p');snippet.className='card-snippet';snippet.dir='auto';
  const text=document.createElement('span');text.className='card-text';
  const heading=document.createElement('span');heading.className='card-heading';
  const icon=document.createElement('span');icon.className='card-note-icon';icon.ariaHidden='true';
  const dirty=document.createElement('span');dirty.className='card-file-dirty';dirty.textContent='●';dirty.title=dirty.ariaLabel='Unsaved file changes';dirty.hidden=true;
  heading.append(icon,title,dirty);text.append(heading,snippet);
  const foot=document.createElement('span');foot.className='card-footer';const group=document.createElement('span');group.className='card-folder';group.dir='auto';const time=document.createElement('small');foot.append(group,time);
  const pin=document.createElement('span');pin.className='card-pin';pin.title='Pinned to top';pin.ariaLabel='Pinned to top';pin.append(JotDesign.icon('pin'));pin.hidden=true;foot.prepend(pin);
  open.append(text,foot);card.append(open);
  const select=document.createElement('input');select.type='checkbox';select.className='card-select';select.ariaLabel='Select note';card.append(select);
  const view={card,open,title,snippet,time,group,pin,select,icon,dirty,note,previous:{}};
  select.onclick=event=>{event.stopPropagation();selectionMode=true;toggleSelected(view.note.id,select.checked);};
  open.onclick=event=>{if(bulkBusy)return;if(selectionMode||event.shiftKey){selectionMode=true;toggleSelected(view.note.id);return;}openNote(view.note,event.ctrlKey||event.metaKey).catch(error);};
  open.onmousedown=event=>{if(event.button===1)event.preventDefault();};
  open.onauxclick=event=>{if(event.button===1){event.preventDefault();openNote(view.note,true).catch(error);}};
  card.oncontextmenu=e=>{e.preventDefault();selectCard(view.note.id);openNoteMenu(view.note,e.clientX,e.clientY,open);};
  open.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){e.preventDefault();const r=open.getBoundingClientRect();openNoteMenu(view.note,r.left+12,r.top+20,open);}};
  return view;
}
function updateSelectedCards(){for(const [id,view] of cardViews){view.card.dataset.selected=String(id===selectedId);view.card.dataset.checked=String(selectedNotes.has(id));view.select.checked=selectedNotes.has(id);view.select.disabled=bulkBusy;view.open.setAttribute('aria-current',String(id===selectedId));}}
function toggleSelected(id,value=!selectedNotes.has(id)){value?selectedNotes.add(id):selectedNotes.delete(id);updateSelectedCards();renderSelection();}
function clearSelection(){selectedNotes.clear();selectedTrash.clear();updateSelectedCards();renderSelection();}
function renderSelection(){
  $('cards').dataset.selecting=String(selectionMode);$('bulkActions').hidden=!selectionMode;$('selectNotes').ariaPressed=String(selectionMode);$('selectNotes').textContent=selectionMode?'Done':'Select';
  const visible=trashMode?visibleTrash():visibleNotes(),selected=trashMode?trashItems.filter(n=>selectedTrash.has(n.key)):homeData.notes.filter(n=>selectedNotes.has(n.id));
  $('selectionCount').textContent=selected.length+' selected';$('selectAllNotes').checked=visible.length>0&&visible.every(n=>trashMode?selectedTrash.has(n.key):selectedNotes.has(n.id));$('selectAllNotes').indeterminate=selected.length>0&&!$('selectAllNotes').checked;
  $('selectAllNotes').disabled=bulkBusy||visible.length===0;$('selectNotes').disabled=bulkBusy;
  for(const id of ['bulkPin','bulkMove','bulkDelete'])$(id).disabled=bulkBusy||selected.length===0;
  $('bulkPin').hidden=$('bulkMove').hidden=trashMode;$('bulkRestore').hidden=!trashMode;
  $('bulkRestore').disabled=bulkBusy||!selected.length||selected.some(item=>!item.canRestore);
  $('bulkDelete').querySelector('span:last-child').textContent=trashMode?'Delete forever':'Delete';
  $('trashCards').dataset.selecting=String(selectionMode);
  for(const row of $('trashCards').children){const checkbox=row.querySelector('input');checkbox.checked=selectedTrash.has(row.dataset.trashKey);checkbox.disabled=bulkBusy;row.dataset.checked=String(checkbox.checked);row.querySelectorAll('button').forEach(button=>button.disabled=bulkBusy||button.dataset.action==='restore'&&button.dataset.canRestore==='false');}
  if(!bulkBusy)$('bulkPin').querySelector('span:last-child').textContent=selected.length>0&&selected.every(n=>n.libraryPinned)?'Unpin':'Pin';
}
async function bulkAction(action,extra={}){
  const ids=[...selectedNotes];if(bulkBusy||!ids.length)return;bulkBusy=true;renderSelection();updateSelectedCards();
  try{await request('library-bulk',{ids,action,...extra});if(action==='delete')selectedNotes.clear();await refresh();}
  finally{bulkBusy=false;renderSelection();updateSelectedCards();}
}
function visibleTrash(){return trashItems.filter(item=>!query||searchText([item.title,item.legacyTitle,item.plain,item.group].join('\n')).includes(searchText(query)));}
function renderTrash(){
  const items=visibleTrash(),container=$('trashCards'),visible=new Set(items.map(item=>item.key));
  for(const key of selectedTrash)if(!visible.has(key))selectedTrash.delete(key);
  $('trashStatus').hidden=!trashLoading&&!trashError;$('trashStatus').ariaBusy=String(trashLoading);
  $('trashStatusText').textContent=trashLoading?'Loading Trash…':trashError||'';$('trashRetry').hidden=!trashError;
  $('homeEmpty').hidden=trashLoading||!!trashError||items.length>0;
  $('emptyTitle').textContent=query?'No matching deleted notes':'Trash is empty';
  $('emptyDescription').textContent=query?'Try a different search.':'Deleted notes appear here. You can restore them whenever you need.';
  $('emptyNew').hidden=true;$('noteCount').textContent=items.length+' '+(items.length===1?'item':'items');
  container.replaceChildren();
  if(!trashLoading&&!trashError)for(const item of items){
    const row=document.createElement('article');row.className='library-trash-item';row.dataset.trashKey=item.key;
    const select=document.createElement('input');select.type='checkbox';select.className='trash-select';
    const name=item.title||item.legacyTitle||item.plain?.trim().slice(0,70)||'Untitled note';select.ariaLabel='Select '+name;
    select.onchange=()=>{selectionMode=true;select.checked?selectedTrash.add(item.key):selectedTrash.delete(item.key);renderSelection();};
    const icon=document.createElement('span');icon.className='trash-note-icon';icon.ariaHidden='true';icon.append(JotNoteIcons.render(item.icon));
    const content=document.createElement('div');content.className='trash-item-content';
    const title=document.createElement('strong');title.dir='auto';displayMixedText(title,name);
    const preview=document.createElement('p');preview.dir='auto';displayMixedText(preview,item.plain||'Empty note');
    const meta=document.createElement('small');meta.textContent=item.canRestore?'Deleted '+dateFormatter.format(item.deletedAt)+(item.group?' · '+item.group:''):'Recovery copy unavailable';
    content.append(title,preview,meta);
    const actions=document.createElement('div');actions.className='trash-item-actions';
    const restore=document.createElement('button');restore.type='button';restore.className='library-secondary';restore.dataset.action='restore';restore.dataset.canRestore=String(item.canRestore);restore.ariaLabel='Restore '+name;
    const restoreLabel=document.createElement('span');restoreLabel.textContent='Restore';restore.append(JotDesign.icon('undo-2'),restoreLabel);
    restore.onclick=()=>trashAction('restore',[item.key],restore).catch(error);
    const remove=document.createElement('button');remove.type='button';remove.className='icon-button library-trash-delete';remove.dataset.action='delete';remove.title='Delete forever';remove.ariaLabel='Permanently delete '+name;remove.append(JotDesign.icon('trash-2'));
    remove.onclick=()=>confirmTrashDelete([item.key]);actions.append(restore,remove);row.append(select,icon,content,actions);
    row.oncontextmenu=event=>{event.preventDefault();JotMenus.open({x:event.clientX,y:event.clientY,owner:restore,error,items:[
      {id:'restore',label:'Restore',icon:'undo-2',disabled:!item.canRestore,pending:'Restoring…'},
      {id:'delete',label:'Delete forever',icon:'trash-2'}],run:action=>action==='restore'?trashAction('restore',[item.key]):confirmTrashDelete([item.key])});};
    container.append(row);
  }
  renderSelection();
}
async function trashAction(action,keys,button){
  if(bulkBusy||!keys.length)return;bulkBusy=true;renderSelection();
  const label=button?.querySelector('span');if(button)button.ariaBusy='true';if(label)label.textContent='Restoring…';
  try{
    const count=await request('trash-update',{action,keys,confirmed:action==='delete'});selectedTrash.clear();
    JotToast.success(action==='restore'?(count===1?'Note restored':'Notes restored'):(count===1?'Item permanently deleted':'Items permanently deleted'),{id:'trash-result'});
  }finally{
    try{await refresh();}finally{bulkBusy=false;if(button)button.ariaBusy='false';if(label)label.textContent='Restore';renderSelection();}
  }
}
function confirmTrashDelete(keys){
  if(bulkBusy||!keys.length)return;
  confirm('Delete forever?',keys.length===1?'This recovery copy will be permanently removed. This cannot be undone. Saved files are not affected.':keys.length+' recovery copies will be permanently removed. This cannot be undone. Saved files are not affected.',
    keys.length===1?'Delete forever':'Delete '+keys.length+' forever',()=>trashAction('delete',keys));
}
function renderCards(){
  $('cards').hidden=trashMode;$('trashCards').hidden=!trashMode;$('trashHelp').hidden=!trashMode;
  $('gridView').parentElement.hidden=trashMode;$('homeSearch').placeholder=trashMode?'Search Trash':'Search notes';
  if(trashMode){renderTrash();return;}
  $('trashStatus').hidden=true;
  const notes=visibleNotes(),container=$('cards'),focus=document.activeElement;
  container.dataset.view=viewMode;
  const allIds=new Set(homeData.notes.map(n=>n.id)),visibleIds=new Set(notes.map(n=>n.id));
  for(const [id,v] of cardViews)if(!allIds.has(id)){v.card.remove();cardViews.delete(id);}
  for(const id of selectedNotes)if(!visibleIds.has(id))selectedNotes.delete(id);
  for(const card of [...container.children])if(!visibleIds.has(card.dataset.noteId))card.remove();
  $('homeEmpty').hidden=notes.length>0;
  $('emptyTitle').textContent=query?'No matching notes':pinnedOnly?'No pinned notes':folder!==null?'This folder is empty':'No notes yet';
  $('emptyDescription').textContent=query?'Try a different word or folder.':pinnedOnly?'Right-click a note and choose Pin to top.':'Create a note to get started.';
  $('emptyNew').hidden=!!query||pinnedOnly;$('noteCount').textContent=notes.length+' '+(notes.length===1?'note':'notes');$('noteCount').ariaLabel=notes.length+' notes';
  let cursor=container.firstElementChild;
  for(const note of notes){
    let v=cardViews.get(note.id);if(!v){v=createCard(note);cardViews.set(note.id,v);}v.note=note;
    const name=noteName(note);let preview=note.plain.trim();
    if(name&&preview.split(/\r?\n/,1)[0].trim()===name.trim())preview=preview.split(/\r?\n/).slice(1).join('\n').trim();
    preview=preview.slice(0,320);
    if(v.previous.name!==name){displayMixedText(v.title,name);v.title.hidden=!name;}
    if(v.previous.icon!==note.icon)v.icon.replaceChildren(window.JotNoteIcons?JotNoteIcons.render(note.icon):JotDesign.icon('notepad-text'));
    v.dirty.hidden=!note.noteFile?.path||!note.fileDirty;v.open.title=note.noteFile?.path||name;
    if(v.previous.preview!==preview)displayPreview(v.snippet,preview);
    v.open.ariaLabel='Open '+(name||note.plain.trim().slice(0,70)||'note');
    v.select.ariaLabel='Select '+(name||note.plain.trim().slice(0,70)||'note');v.pin.hidden=!note.libraryPinned;v.card.dataset.pinned=String(!!note.libraryPinned);
    const accent=JotDesign.noteColor(note.color||'neutral').primary;v.card.style.setProperty('--card-accent',accent);
    if(v.previous.group!==note.group){displayMixedText(v.group,note.group||'');v.group.hidden=!note.group||folder!==null;}
    v.group.hidden=!note.group||folder!==null;
    if(v.previous.updatedAt!==note.updatedAt)v.time.textContent=dateFormatter.format(note.updatedAt);
    v.previous={name,preview,group:note.group,updatedAt:note.updatedAt,icon:note.icon};
    if(v.card!==cursor)container.insertBefore(v.card,cursor);else cursor=cursor.nextElementSibling;
  }
  updateSelectedCards();renderSelection();
  if(focus!==document.activeElement&&focus?.isConnected&&container.contains(focus))focus.focus({preventScroll:true});
  $('gridView').ariaPressed=String(viewMode==='grid');$('listView').ariaPressed=String(viewMode==='list');
}
function renderFolders(){
  if(folder!==null&&folder!==''&&!homeData.folders.includes(folder))folder=null;
  displayMixedText($('folderLabel'),trashMode?'Trash':pinnedOnly?'Pinned':folder===null?'All notes':folder||'Unfiled');
  for(const target of [$('folderChoices'),$('sidebarChoices')]){
  target.replaceChildren();
  for(const entry of [{name:null,label:'All notes',icon:'notepad-text'},{name:null,pinned:true,label:'Pinned',icon:'pin'},{name:null,trash:true,label:'Trash',icon:'trash-2'},{name:'',label:'Unfiled',icon:'notepad-text'},...homeData.folders.map(name=>({name,label:name,icon:'folder'}))]){
    const {name}=entry;
    if(name===''){
      const heading=document.createElement('div');heading.className='library-folders-heading';heading.textContent='Folders';
      const add=document.createElement('button');add.className='icon-button';add.type='button';add.title=add.ariaLabel='New folder';add.append(JotDesign.icon('plus'));add.onclick=newFolder;if(target.id==='sidebarChoices')heading.append(add);target.append(heading);
    }
    const button=document.createElement('button');button.className='library-folder-choice';button.type='button';button.ariaPressed=String(name===folder);
    button.ariaPressed=String(entry.trash?trashMode:!trashMode&&!!entry.pinned===pinnedOnly&&(entry.pinned||name===folder));button.dataset.folder=name??'';button.dataset.scope=entry.trash?'trash':entry.pinned?'pinned':name===null?'all':name===''?'unfiled':'folder';
    const label=document.createElement('span');label.dir='auto';displayMixedText(label,entry.label);
    const count=document.createElement('small');count.textContent=entry.trash?(trashCount??'—'):homeData.notes.filter(n=>entry.pinned?n.libraryPinned:name===null||n.group===name).length;if(entry.trash&&trashCount===null)count.title='Trash is unavailable';button.append(JotDesign.icon(entry.icon),label,count);
    button.onclick=()=>{if(bulkBusy)return;folder=name;pinnedOnly=!!entry.pinned;trashMode=!!entry.trash;clearSelection();closeFolders();saveIndexView();renderFolders();renderCards();if(trashMode)refresh().catch(error);};
    if(name)button.oncontextmenu=e=>{e.preventDefault();openFolderMenu(name,e.clientX,e.clientY,button);};
    if(name)button.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){e.preventDefault();const r=button.getBoundingClientRect();openFolderMenu(name,r.left,r.bottom,button);}};
    target.append(button);
  }
  }
}
async function refresh(initialData){
  indexDirty=false;if(!initialData)contentUpdates.clear();
  const version=++refreshVersion,loadingTrash=trashMode;
  if(loadingTrash){trashLoading=true;trashError=null;renderCards();}
  let data,trash;
  try{[data,trash]=await Promise.all([initialData??request('index-load'),loadingTrash?request('trash-load').then(items=>({items}),failure=>({error:failure.message})):request('trash-count').catch(()=>null)]);}
  catch(e){if(version===refreshVersion&&loadingTrash){trashLoading=false;trashError=e.message;renderCards();}throw e;}
  if(version!==refreshVersion)return;
  if(loadingTrash){trashItems=trash.items||[];trashLoading=false;trashError=trash.error||null;trashCount=trash.items?.length??null;}else trashCount=trash;
  if(data)homeData=data;homeData.folders??=[];
  for(let i=0;i<homeData.notes.length;i++){
    const current=homeData.notes[i],updated=contentUpdates.get(current.id);
    if(updated&&updated.updatedAt>current.updatedAt)homeData.notes[i]={...current,plain:updated.plain,updatedAt:updated.updatedAt,hasImage:updated.hasImage,fileDirty:updated.fileDirty};
  }
  contentUpdates.clear();contentRenderPending=false;
  if(!embedded||JotWorkspace.view==='home')homeData.prefs=JotDesign.apply(homeData.prefs);
  viewMode=homeData.prefs.libraryView==='list'?'list':'grid';renderFolders();renderCards();
}
function scheduleRefresh(){
  indexDirty=true;if(!indexVisible||refreshScheduled)return;refreshScheduled=true;
  setTimeout(async()=>{try{while(indexDirty&&indexVisible)await refresh();}catch(e){error(e);}finally{refreshScheduled=false;}},40);
}
function contentChanged(note){
  const position=homeData.notes.findIndex(item=>item.id===note.id);
  if(position<0){scheduleRefresh();return;}
  const current=homeData.notes[position];
  homeData.notes[position]={...current,plain:note.plain,updatedAt:note.updatedAt,hasImage:note.hasImage,fileDirty:note.fileDirty};
  contentUpdates.set(note.id,note);contentRenderPending=true;
  if(!indexVisible||contentRenderScheduled)return;
  contentRenderScheduled=true;
  setTimeout(()=>{contentRenderScheduled=false;if(indexVisible&&contentRenderPending){contentRenderPending=false;renderCards();}},40);
}
function closeFolders(){$('folderPanel').hidden=true;$('folderButton').ariaExpanded='false';}
function openFolders(){
  JotMenus.close();renderFolders();const r=$('folderButton').getBoundingClientRect(),panel=$('folderPanel');
  panel.hidden=false;panel.style.top=r.bottom+5+'px';panel.style.left=Math.max(8,Math.min(r.left,innerWidth-276))+'px';panel.style.maxHeight=Math.max(100,innerHeight-r.bottom-14)+'px';$('folderButton').ariaExpanded='true';panel.querySelector('[aria-pressed=true]')?.focus({preventScroll:true});
}
function dialogReady(id){document.querySelectorAll('#'+id+' .dialog-error').forEach(e=>e.hidden=true);$(id).showModal();}
function editNote(note){metadataMode='note';editingId=note.id;showMetadata('Rename note','Title',note.title||'',140,'Optional title');}
function showMetadata(title,label,value,max,placeholder){closeFolders();$('metadataTitle').textContent=title;$('metadataLabel').textContent=label;const input=$('noteTitleInput');input.value=value;input.maxLength=max;input.placeholder=placeholder;dialogReady('metadataDialog');input.focus();input.select();}
function newFolder(){metadataMode='folder-new';editingId=null;showMetadata('New folder','Name','',64,'Folder name');}
function renameFolder(name){metadataMode='folder-rename';editingId=name;showMetadata('Rename folder','Name',name,64,'Folder name');}
function confirm(title,description,label,action){closeFolders();$('confirmTitle').textContent=title;$('confirmDescription').textContent=description;$('confirmApply').textContent=label;confirmAction=action;dialogReady('libraryConfirm');$('confirmCancel').focus();}
function deleteNote(note){confirm('Move note to Trash?','You can restore this note from Trash. Saved files stay on disk.','Move to Trash',()=>request('library-delete',note.id));}
function removeFolder(name){confirm('Remove folder?','The notes will be kept in Unfiled. Only the folder is removed.','Remove folder',async()=>{await request('folder-remove',name);if(folder===name)folder=null;});}
function moveNote(note){moveNotes([note]);}
function moveNotes(notes){
  $('moveTitle').textContent=notes.length===1?'Move to folder':'Move '+notes.length+' notes';
  const list=$('moveFolders');list.replaceChildren();
  for(const name of ['',...homeData.folders]){
    const b=document.createElement('button');b.className='library-folder-choice';b.type='button';b.ariaPressed=String(notes.every(note=>note.group===name));
    const label=document.createElement('span');label.dir='auto';displayMixedText(label,name||'Unfiled');b.append(label);
    b.onclick=async()=>{list.querySelectorAll('button').forEach(n=>n.disabled=true);$('moveClose').disabled=true;b.classList.add('pending');b.ariaBusy='true';label.textContent='Moving…';try{await request('library-bulk',{ids:notes.map(note=>note.id),action:'move',folder:name});$('moveDialog').close();await refresh();}catch(e){error(e);}finally{list.querySelectorAll('button').forEach(n=>n.disabled=false);$('moveClose').disabled=false;b.classList.remove('pending');b.ariaBusy='false';displayMixedText(label,name||'Unfiled');}};
    list.append(b);
  }
  dialogReady('moveDialog');
}
function openNoteMenu(note,x,y,owner){
  closeFolders();JotMenus.open({x,y,owner,restore:()=>owner.focus({preventScroll:true}),error,items:[
    {id:'open',label:'Open',icon:'notepad-text'},{id:'tab',label:'Open in new tab',icon:'panels-top-left'},
    {separator:true},{id:'pin-library',label:note.libraryPinned?'Unpin':'Pin to top',icon:'pin',pending:'Updating…'},{id:'select-note',label:'Select',icon:'check'},
    {id:'rename',label:'Rename',icon:'pencil'},{id:'note-icon',label:'Note emoji',icon:'notepad-text'},{id:'move',label:'Move to folder',icon:'folder'},
    {separator:true},{id:'copy',label:'Copy',icon:'copy',pending:'Copying…'},{id:'export',label:'Export',icon:'download',pending:'Exporting…'},
    {separator:true},{id:'delete',label:'Move to Trash',icon:'trash-2'}
  ],async run(action){
    if(action==='open'||action==='tab')await openNote(note,action==='tab');
    else if(action==='pin-library'){await request('library-pin',{id:note.id,pinned:!note.libraryPinned});await refresh();}
    else if(action==='select-note'){selectionMode=true;toggleSelected(note.id,true);}
    else if(action==='rename')editNote(note);else if(action==='note-icon')JotNoteIcons.open(note,{owner});else if(action==='move')moveNote(note);
    else if(action==='copy'){if(await request('library-copy',note.id)!==true)throw new Error('Copy was interrupted. Try again.');}
    else if(action==='export')await request('library-export',note.id);else if(action==='delete')deleteNote(note);
  }});
}
function openFolderMenu(name,x,y,owner){JotMenus.open({x,y,owner,error,items:[{id:'new-folder',label:'New folder',icon:'folder-plus'},{separator:true},{id:'rename-folder',label:'Rename folder',icon:'pencil'},{id:'remove-folder',label:'Remove folder',icon:'trash-2'}],run:async action=>{if(action==='new-folder')newFolder();else if(action==='rename-folder')renameFolder(name);else removeFolder(name);}});}
$('metadataForm').onsubmit=async event=>{
  event.preventDefault();if($('metadataSave').disabled)return;
  const button=$('metadataSave');button.disabled=true;button.ariaBusy='true';button.textContent='Saving…';
  $('metadataClose').disabled=true;
  try{
    const value=$('noteTitleInput').value;
    if(metadataMode==='note')await request('note-metadata',{id:editingId,title:value});
    else if(metadataMode==='folder-new'){await request('folder-create',value);folder=value.trim();pinnedOnly=false;trashMode=false;}
    else{await request('folder-rename',{name:editingId,replacement:value});if(folder===editingId)folder=value.trim();}
    $('metadataDialog').close();await refresh();saveIndexView();
  }catch(e){error(e);}finally{button.disabled=false;button.ariaBusy='false';button.textContent='Save';$('metadataClose').disabled=false;}
};
$('metadataClose').onclick=()=>$('metadataDialog').close();
$('metadataDialog').addEventListener('cancel',e=>{if($('metadataSave').disabled)e.preventDefault();});
$('moveClose').onclick=()=>$('moveDialog').close();
$('moveDialog').addEventListener('cancel',event=>{if($('moveClose').disabled)event.preventDefault();});
$('confirmCancel').onclick=()=>$('libraryConfirm').close();
$('libraryConfirm').addEventListener('cancel',e=>{if(confirmBusy)e.preventDefault();});
$('confirmApply').onclick=async()=>{
  if(confirmBusy||!confirmAction)return;confirmBusy=true;const b=$('confirmApply'),label=b.textContent;b.disabled=$('confirmCancel').disabled=true;b.ariaBusy='true';b.textContent='Removing…';
  $('libraryConfirm').querySelector('.dialog-error').hidden=true;
  try{await confirmAction();$('libraryConfirm').close();await refresh();saveIndexView();}catch(e){error(e);}finally{confirmBusy=false;b.disabled=$('confirmCancel').disabled=false;b.ariaBusy='false';b.textContent=label;}
};
async function create(){
  if($('homeNew').disabled)return;closeFolders();
  for(const id of ['homeNew','emptyNew']){$(id).disabled=true;$(id).ariaBusy='true';}
  $('homeNew').querySelector('span:last-child').textContent='Creating…';$('emptyNew').textContent='Creating…';
  try{await request('new-note',folder?{folder}:null);await refresh();}catch(e){error(e);}
  finally{for(const id of ['homeNew','emptyNew']){$(id).disabled=false;$(id).ariaBusy='false';}$('homeNew').querySelector('span:last-child').textContent='New note';$('emptyNew').textContent='New note';}
}
for(const id of ['homeNew','emptyNew'])$(id).onclick=create;
$('folderButton').onclick=()=>{$('folderPanel').hidden?openFolders():closeFolders();};$('newFolder').onclick=newFolder;
$('closeFolders').onclick=()=>{closeFolders();$('folderButton').focus();};
$('selectNotes').onclick=()=>{selectionMode=!selectionMode;if(!selectionMode){selectedNotes.clear();selectedTrash.clear();}updateSelectedCards();renderSelection();};
$('selectAllNotes').onchange=()=>{if(trashMode){for(const item of visibleTrash())$('selectAllNotes').checked?selectedTrash.add(item.key):selectedTrash.delete(item.key);}else for(const note of visibleNotes())$('selectAllNotes').checked?selectedNotes.add(note.id):selectedNotes.delete(note.id);updateSelectedCards();renderSelection();};
$('bulkRestore').onclick=()=>trashAction('restore',[...selectedTrash],$('bulkRestore')).catch(error);
$('trashRetry').onclick=()=>refresh().catch(error);
$('bulkPin').onclick=async()=>{
  const pinned=!homeData.notes.filter(note=>selectedNotes.has(note.id)).every(note=>note.libraryPinned);
  const button=$('bulkPin');button.ariaBusy='true';button.querySelector('span:last-child').textContent=pinned?'Pinning…':'Unpinning…';
  try{await bulkAction('pin',{pinned});}catch(e){error(e);}finally{button.ariaBusy='false';renderSelection();}
};
$('bulkMove').onclick=()=>moveNotes(homeData.notes.filter(note=>selectedNotes.has(note.id)));
$('bulkDelete').onclick=()=>{
  if(trashMode){confirmTrashDelete([...selectedTrash]);return;}
  const count=selectedNotes.size;if(!count)return;
  confirm('Move '+count+' '+(count===1?'note to Trash?':'notes to Trash?'),'You can restore the selected notes from Trash. Saved files stay on disk.','Move to Trash',()=>bulkAction('delete'));
};
for(const [id,value] of [['gridView','grid'],['listView','list']])$(id).onclick=async()=>{const previous=viewMode;viewMode=value;renderCards();try{await request('preferences',{libraryView:value});homeData.prefs.libraryView=value;}catch(e){viewMode=previous;renderCards();error(e);}};
$('homeSearch').value=query;
function search(){query=$('homeSearch').value.trim();$('clearSearch').hidden=!$('homeSearch').value;clearSelection();saveIndexView();renderCards();}
$('homeSearch').oninput=()=>{clearTimeout(searchTimer);searchTimer=setTimeout(search,60);};
$('clearSearch').onclick=()=>{$('homeSearch').value='';search();$('homeSearch').focus();};
if(!embedded){$('homeClose').onclick=()=>request('hide').catch(error);$('homeMinimize').onclick=()=>request('minimize').catch(error);}
$('settingsButton').onclick=()=>{saveIndexView();request('settings').catch(error);};
function renderWorkspaceTabs(){if(!embedded)JotWorkspaceTabs.render($('homeTabs'),{tabs:workspaceTabs,activeId:'home',busy:tabBusy,pendingId:tabPendingId,pendingAction:tabPendingAction,onAction:workspaceAction});}
async function workspaceAction(action,id){
  if(tabBusy||action==='tab-switch'&&id==='home')return;tabBusy=true;tabPendingId=id;tabPendingAction=action;closeFolders();saveIndexView();renderWorkspaceTabs();
  try{await request(action,action==='new-tab'&&folder?{folder}:id);}
  catch(e){error(e);}finally{tabBusy=false;tabPendingId=null;tabPendingAction=null;renderWorkspaceTabs();}
}
function fullscreenState(value){fullscreen=!!value;if(embedded)return;const b=$('homeFullscreen');b.ariaPressed=String(fullscreen);b.title=b.ariaLabel=fullscreen?'Restore':'Maximize';b.replaceChildren(JotDesign.icon(fullscreen?'window-restore':'window-maximize'));}
if(!embedded)$('homeFullscreen').onclick=async()=>{const b=$('homeFullscreen');b.disabled=true;try{fullscreenState(await request('window-fullscreen',!fullscreen));}catch(e){error(e);}finally{b.disabled=false;}};
document.addEventListener('pointerdown',e=>{if(!e.target.closest('#folderButton,#folderPanel,#contentContextMenu'))closeFolders();});
document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!$('folderPanel').hidden){closeFolders();$('folderButton').focus();}else if(e.key==='Escape'&&selectionMode&&!bulkBusy&&!document.querySelector('dialog[open]')){selectionMode=false;clearSelection();$('selectNotes').focus();}});
window.addEventListener('resize',closeFolders);window.addEventListener('pagehide',saveIndexView);
JotBridge.on(data=>{
  if(data.event==='file-state')scheduleRefresh();
  if(data.event==='notes-changed')scheduleRefresh();
  if(data.event==='note-content')contentChanged(data.note);
  if(data.event==='tabs-changed'){workspaceTabs=data.tabs||[];renderWorkspaceTabs();}
  if(data.event==='window-visibility'){indexVisible=data.visible&&(!embedded||JotWorkspace.view==='home');if(indexVisible&&indexDirty)scheduleRefresh();else if(indexVisible&&contentRenderPending){contentRenderPending=false;renderCards();}}
  if(data.event==='preferences'){homeData.prefs=(!embedded||JotWorkspace.view==='home')?JotDesign.apply(data.prefs):data.prefs;viewMode=homeData.prefs.libraryView==='list'?'list':'grid';renderCards();renderWorkspaceTabs();}
  if(data.event==='window-fullscreen')fullscreenState(data.enabled);
  if(data.event==='warning'&&!embedded)error(new Error(data.message));
});
async function boot(){
  JotDesign.icons();if(!embedded)JotDesign.drag($('homeHandle'));
  const [initial,context]=await Promise.all([request('index-load'),request('context')]);indexVisible=context.visible&&(!embedded||JotWorkspace.view==='home');fullscreenState(context.fullscreen);workspaceTabs=context.tabs||[];renderWorkspaceTabs();
  if(!initial){
    const legacy=JSON.parse(localStorage.getItem('vanz-notes')||'[]');if(!Array.isArray(legacy))throw new Error('Could not read the previous notes.');
    const escape=value=>{const element=document.createElement('span');element.textContent=value;return element.innerHTML;};
    homeData.notes=legacy.map(note=>({id:crypto.randomUUID(),html:String(note.content||'').split(/\r?\n/).map(line=>'<p dir="auto">'+(escape(line)||'<br>')+'</p>').join(''),plain:String(note.content||''),legacyTitle:String(note.title||''),updatedAt:Number(note.updatedAt)||Date.now()}));
    await request('import',homeData);
  }
  await refresh(initial);$('clearSearch').hidden=!query;if(!embedded)window.jotReady=true;
  if(context.warning)error(Object.assign(new Error(context.warning),{logged:true}));
  if(context.startupWarning)error(new Error('Could not restore your note windows. Your notes are unchanged; open them from Home.'));
}
window.JotHome={editNote,deleteNote,closePanels:closeFolders,
  show(prefs){indexVisible=true;if(prefs)homeData.prefs=prefs;JotDesign.apply(homeData.prefs);if(indexDirty)refresh().catch(error);else if(contentRenderPending){contentRenderPending=false;renderCards();}},
  hide(){indexVisible=false;closeFolders();saveIndexView();},get folder(){return folder;}};
Object.defineProperties(window,{
  homeData:{configurable:true,get:()=>homeData,set:value=>homeData=value},folder:{configurable:true,get:()=>folder,set:value=>folder=value},
  query:{configurable:true,get:()=>query,set:value=>query=value},viewMode:{configurable:true,get:()=>viewMode,set:value=>viewMode=value},
  selectedId:{configurable:true,get:()=>selectedId,set:value=>selectedId=value},
  indexVisible:{configurable:true,get:()=>indexVisible},indexDirty:{configurable:true,get:()=>indexDirty},refreshScheduled:{configurable:true,get:()=>refreshScheduled}
});
Object.assign(window,{search,searchText,renderCards,renderFolders,removeFolder,newFolder,renameFolder,editNote,refresh,saveIndexView,cardViews});
JotHome.ready=boot().catch(error=>{window.JotHome.error=error;throw error;});
if(!embedded)JotHome.ready.catch(error);
})();

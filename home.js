'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let homeData={version:2,notes:[],prefs:{...JotDesign.defaults}},refreshVersion=0,selectedId=null,editingId=null;
const cardViews=new Map(),dateFormatter=new Intl.DateTimeFormat(JotI18n.locale,{month:'short',day:'numeric'});
let indexVisible=true,indexDirty=false,refreshScheduled=false;
const indexViewKey='jot-index-view';
try {
  const previous=JSON.parse(sessionStorage.getItem(indexViewKey)||'null');
  if(previous)selectedId=previous.selectedId??null;
} catch { /* An unavailable cache must not block the notes index. */ }
function saveIndexView(){
  try{sessionStorage.setItem(indexViewKey,JSON.stringify({selectedId}));}catch{}
}
function error(e){JotBridge.reportError(e,'home');$('homeError').textContent=t(e.message);$('homeError').hidden=false;}
function noteName(note){return note.title||note.plain.split(/\n/).find(line=>line.trim())?.trim().slice(0,90)||note.legacyTitle||t(note.hasImage?'یادداشت تصویری':'یادداشت تازه');}
function displayMixedText(element,text){element.textContent=text;JotBidi.normalize(element,new Set([element]),'ltr');}
function createCard(note){
  const card=document.createElement('article');card.className='note-card sticky-card';card.dataset.noteId=note.id;
  const open=document.createElement('button');open.className='card-open';
  const title=document.createElement('strong');title.dir='auto';
  const snippet=document.createElement('p');snippet.dir='auto';
  const text=document.createElement('span');text.className='card-text';text.append(title,snippet);
  open.append(JotDesign.icon(note.hasImage?'image':'notepad-text'),text);
  const foot=document.createElement('div');foot.className='card-footer';
  const time=document.createElement('small');
  const edit=document.createElement('button');edit.className='card-edit icon-button';edit.title='Rename note';edit.setAttribute('aria-label','Rename note');edit.append(JotDesign.icon('pencil'));
  foot.append(time,edit);card.append(open,foot);
  const view={card,open,title,snippet,time,note,previous:{},hasImage:!!note.hasImage};
  open.onclick=()=>{selectedId=view.note.id;saveIndexView();updateSelectedCards();request('open-note',view.note.id).catch(error);};
  edit.onclick=()=>editNote(view.note);return view;
}
function updateSelectedCards(){
  const id=selectedId||homeData.activeId;
  for(const [noteId,view] of cardViews){const selected=String(noteId===id);if(view.card.dataset.selected!==selected)view.card.dataset.selected=selected;}
}
function renderCards(){
  const notes=[...homeData.notes].sort((a,b)=>b.updatedAt-a.updatedAt);
  const container=$('cards'),focus=document.activeElement;
  const allIds=new Set(homeData.notes.map(note=>note.id)),visibleIds=new Set(notes.map(note=>note.id));
  for(const [id,view] of cardViews)if(!allIds.has(id)){view.card.remove();cardViews.delete(id);}
  for(const card of [...container.children])if(!visibleIds.has(card.dataset.noteId))card.remove();
  $('homeEmpty').hidden=homeData.notes.length>0;
  let cursor=container.firstElementChild;
  for(const note of notes){
    let view=cardViews.get(note.id);if(!view){view=createCard(note);cardViews.set(note.id,view);}
    const previous=view.previous;view.note=note;
    if(previous.plain!==note.plain||previous.title!==note.title||previous.legacyTitle!==note.legacyTitle||view.hasImage!==!!note.hasImage){
      const name=noteName(note);if(view.title.textContent!==name)displayMixedText(view.title,name);
      view.open.setAttribute('aria-label',t('باز کردن')+' '+name);
      const snippet=note.plain.split('\n').filter(s=>s.trim()).slice(note.title?0:1).join(' ').slice(0,180)||t('برای نوشتن باز کن…');
      if(view.snippet.textContent!==snippet)displayMixedText(view.snippet,snippet);
    }
    if(previous.updatedAt!==note.updatedAt)view.time.textContent=dateFormatter.format(note.updatedAt);
    if(view.hasImage!==!!note.hasImage){view.open.firstElementChild.replaceWith(JotDesign.icon(note.hasImage?'image':'notepad-text'));view.hasImage=!!note.hasImage;}
    view.previous={plain:note.plain,title:note.title,legacyTitle:note.legacyTitle,updatedAt:note.updatedAt};
    if(view.card!==cursor)container.insertBefore(view.card,cursor);else cursor=cursor.nextElementSibling;
  }
  updateSelectedCards();
  if(focus!==document.activeElement&&focus?.isConnected&&container.contains(focus))focus.focus({preventScroll:true});
}
async function refresh(initialData){
  indexDirty=false;
  const version=++refreshVersion,data=initialData??await request('index-load');if(version!==refreshVersion)return;
  if(data)homeData=data;homeData.prefs=JotDesign.apply(homeData.prefs);renderCards();
}
function scheduleRefresh(){
  indexDirty=true;
  if(!indexVisible||refreshScheduled)return;
  refreshScheduled=true;
  setTimeout(async()=>{
    try{while(indexDirty&&indexVisible)await refresh();}catch(e){error(e);}finally{refreshScheduled=false;}
  },40);
}
function editNote(note){
  editingId=note.id;$('noteTitleInput').value=note.title||'';
  $('metadataDialog').showModal();$('noteTitleInput').focus();
}
$('metadataForm').onsubmit=async event=>{
  event.preventDefault();if($('metadataSave').disabled)return;$('metadataSave').disabled=true;$('metadataSave').ariaBusy='true';$('metadataSave').textContent='Saving…';
  try{await request('note-metadata',{id:editingId,title:$('noteTitleInput').value});$('metadataDialog').close();await refresh();}
  catch(e){error(e);}finally{$('metadataSave').disabled=false;$('metadataSave').ariaBusy='false';$('metadataSave').textContent='Save';}
};
$('metadataClose').onclick=()=>$('metadataDialog').close();
async function create(){
  if($('homeNew').disabled)return;
  for(const id of ['homeNew','emptyNew']){$(id).disabled=true;$(id).ariaBusy='true';$(id).title='Creating note…';$(id).ariaLabel='Creating note…';}
  $('emptyNew').textContent='Creating…';
  try{await request('new-note');await refresh();}catch(e){error(e);}
  finally{for(const id of ['homeNew','emptyNew']){$(id).disabled=false;$(id).ariaBusy='false';$(id).title='New note';$(id).ariaLabel='New note';}$('emptyNew').textContent='New note';}
}
for(const id of ['homeNew','emptyNew'])$(id).onclick=create;
$('homeClose').onclick=()=>request('hide').catch(error);
$('homeMinimize').onclick=()=>request('minimize').catch(error);
$('settingsButton').onclick=()=>{saveIndexView();request('settings').catch(error);};
window.addEventListener('pagehide',saveIndexView);
JotBridge.on(data=>{
  if(data.event==='notes-changed')scheduleRefresh();
  if(data.event==='window-visibility'){indexVisible=data.visible;if(indexVisible&&indexDirty)scheduleRefresh();}
  if(data.event==='preferences')homeData.prefs=JotDesign.apply(data.prefs);
  if(data.event==='warning')error(new Error(data.message));
});
async function boot(){
  JotDesign.icons();JotDesign.drag($('homeHandle'));
  const [initial,context]=await Promise.all([request('index-load'),request('context')]);indexVisible=context.visible;
  if(!initial){
    const legacy=JSON.parse(localStorage.getItem('vanz-notes')||'[]');
    if(!Array.isArray(legacy))throw new Error('یادداشت‌های قبلی قابل خواندن نیستند.');
    const escape=s=>{const element=document.createElement('span');element.textContent=s;return element.innerHTML;};
    homeData.notes=legacy.map(note=>({id:crypto.randomUUID(),html:String(note.content||'').split(/\r?\n/).map(line=>'<p dir="auto">'+(escape(line)||'<br>')+'</p>').join(''),plain:String(note.content||''),legacyTitle:String(note.title||''),updatedAt:Number(note.updatedAt)||Date.now()}));
    await request('import',homeData);
  }
  await refresh(initial);window.jotReady=true;
  if(context.startupWarning)error(new Error('Could not restore your note windows. Your notes are unchanged; open them from Home.'));
}
boot().catch(error);

'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let homeData={version:2,notes:[],prefs:{...JotDesign.defaults}},refreshVersion=0,currentGroup=null,selectedId=null,editingId=null;
const cardViews=new Map(),dateFormatter=new Intl.DateTimeFormat(JotI18n.locale,{month:'short',day:'numeric'}),countFormatter=new Intl.NumberFormat(JotI18n.locale);
let groupSignature='',indexVisible=true,indexDirty=false,refreshScheduled=false,searchFrame=0;
const indexViewKey='jot-index-view';
try {
  const previous=JSON.parse(sessionStorage.getItem(indexViewKey)||'null');
  if(previous){currentGroup=previous.group??null;selectedId=previous.selectedId??null;$('homeSearch').value=previous.search||'';}
} catch { /* An unavailable cache must not block the notes index. */ }
function saveIndexView(){
  try{sessionStorage.setItem(indexViewKey,JSON.stringify({group:currentGroup,selectedId,search:$('homeSearch').value}));}catch{}
}
function error(e){JotBridge.reportError(e,'home');$('homeError').textContent=t(e.message);$('homeError').hidden=false;}
function noteName(note){return note.title||note.plain.split(/\n/).find(line=>line.trim())?.trim().slice(0,90)||note.legacyTitle||t(note.hasImage?'یادداشت تصویری':'یادداشت تازه');}
function displayMixedText(element,text){element.textContent=text;JotBidi.normalize(element,new Set([element]),'ltr');}
function groupNames(){return [...new Set(homeData.notes.map(note=>note.group||'').filter(Boolean))].sort((a,b)=>a.localeCompare(b,JotI18n.locale));}
function renderGroups(){
  const groups=[null,'',...groupNames()],signature=JSON.stringify(groups);
  if(signature!==groupSignature){
    groupSignature=signature;$('groupFilters').replaceChildren();
    for(const group of groups){
      const button=document.createElement('button');button.className='group-chip';button.dataset.group=group??'*';
      button.jotGroup=group;
      displayMixedText(button,group===null?t('همه یادداشت‌ها'):group===''?t('بدون گروه'):group);
      button.onclick=()=>{currentGroup=group;saveIndexView();renderCards();renderGroups();};$('groupFilters').append(button);
    }
  }
  for(const button of $('groupFilters').children){const pressed=String(button.jotGroup===currentGroup);if(button.getAttribute('aria-pressed')!==pressed)button.setAttribute('aria-pressed',pressed);}
}
function createCard(note){
  const card=document.createElement('article');card.className='note-card sticky-card';card.dataset.noteId=note.id;
  const open=document.createElement('button');open.className='card-open';
  const title=document.createElement('strong');title.dir='auto';
  const snippet=document.createElement('p');snippet.dir='auto';
  const text=document.createElement('span');text.className='card-text';text.append(title,snippet);
  const group=document.createElement('span');group.className='card-group';group.dir='auto';displayMixedText(group,note.group||t('بدون گروه'));
  open.append(JotDesign.icon(note.hasImage?'image':'notepad-text'),text,group);
  const foot=document.createElement('div');foot.className='card-footer';
  const time=document.createElement('small');
  const edit=document.createElement('button');edit.className='card-edit';edit.title=t('عنوان و گروه');edit.setAttribute('aria-label',t('عنوان و گروه'));edit.append(JotDesign.icon('settings-2'),document.createTextNode(t('عنوان و گروه')));
  foot.append(time,edit);card.append(open,foot);
  const view={card,open,title,snippet,group,time,note,previous:{},hasImage:!!note.hasImage};
  open.onclick=()=>{selectedId=view.note.id;saveIndexView();updateSelectedCards();request('open-note',view.note.id).catch(error);};
  edit.onclick=()=>editNote(view.note);return view;
}
function updateSelectedCards(){
  const id=selectedId||homeData.activeId;
  for(const [noteId,view] of cardViews){const selected=String(noteId===id);if(view.card.dataset.selected!==selected)view.card.dataset.selected=selected;}
}
function renderCards(){
  const query=$('homeSearch').value.trim().toLocaleLowerCase();
  const notes=homeData.notes.filter(note=>(currentGroup===null||(note.group||'')===currentGroup)&&[note.title,note.plain,note.legacyTitle,note.group].join(' ').toLocaleLowerCase().includes(query)).sort((a,b)=>b.updatedAt-a.updatedAt);
  const container=$('cards'),focus=document.activeElement;
  const allIds=new Set(homeData.notes.map(note=>note.id)),visibleIds=new Set(notes.map(note=>note.id));
  for(const [id,view] of cardViews)if(!allIds.has(id)){view.card.remove();cardViews.delete(id);}
  for(const card of [...container.children])if(!visibleIds.has(card.dataset.noteId))card.remove();
  $('homeEmpty').hidden=homeData.notes.length>0;
  const count=countFormatter.format(homeData.notes.length)+' notes';
  if($('noteCount').textContent!==count)$('noteCount').textContent=count;
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
    if(previous.group!==note.group)displayMixedText(view.group,note.group||t('بدون گروه'));
    if(previous.updatedAt!==note.updatedAt)view.time.textContent=dateFormatter.format(note.updatedAt);
    if(view.hasImage!==!!note.hasImage){view.open.firstElementChild.replaceWith(JotDesign.icon(note.hasImage?'image':'notepad-text'));view.hasImage=!!note.hasImage;}
    view.previous={plain:note.plain,title:note.title,legacyTitle:note.legacyTitle,group:note.group,updatedAt:note.updatedAt};
    if(view.card!==cursor)container.insertBefore(view.card,cursor);else cursor=cursor.nextElementSibling;
  }
  updateSelectedCards();
  if(!notes.length&&homeData.notes.length){const empty=document.createElement('p');empty.className='empty-list';empty.textContent=t('یادداشتی پیدا نشد.');container.append(empty);}
  if(focus!==document.activeElement&&focus?.isConnected&&container.contains(focus))focus.focus({preventScroll:true});
}
async function refresh(initialData){
  indexDirty=false;
  const version=++refreshVersion,data=initialData??await request('index-load');if(version!==refreshVersion)return;
  if(data)homeData=data;homeData.prefs=JotDesign.apply(homeData.prefs);renderGroups();renderCards();
  $('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');
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
  editingId=note.id;$('noteTitleInput').value=note.title||'';$('noteGroupInput').value=note.group||'';
  $('groupSuggestions').replaceChildren();
  for(const group of groupNames()){
    const button=document.createElement('button');button.type='button';button.textContent=group;button.dir='auto';button.className='group-chip';
    button.onclick=()=>{$('noteGroupInput').value=group;};$('groupSuggestions').append(button);
  }
  $('metadataDialog').showModal();$('noteTitleInput').focus();
}
$('metadataForm').onsubmit=async event=>{
  event.preventDefault();$('metadataSave').disabled=true;$('metadataSave').textContent=t('در حال ذخیره…');
  try{await request('note-metadata',{id:editingId,title:$('noteTitleInput').value,group:$('noteGroupInput').value});$('metadataDialog').close();await refresh();}
  catch(e){error(e);}finally{$('metadataSave').disabled=false;$('metadataSave').textContent=t('ذخیره');}
};
$('metadataClose').onclick=()=>$('metadataDialog').close();
async function create(){
  $('homeNew').disabled=true;$('homeNew').lastElementChild.textContent=t('در حال ساخت…');
  try{await request('new-note');await refresh();}catch(e){error(e);}
  finally{$('homeNew').disabled=false;$('homeNew').lastElementChild.textContent=t('یادداشت جدید');}
}
for(const id of ['homeNew','emptyNew'])$(id).onclick=create;
$('homeHide').onclick=()=>request('hide').catch(error);
$('homeQuit').onclick=async()=>{
  if($('homeQuit').disabled)return;
  $('homeQuit').disabled=true;$('homeQuit').setAttribute('aria-busy','true');$('homeError').hidden=true;
  try{await request('quit');}
  catch(e){$('homeQuit').disabled=false;$('homeQuit').setAttribute('aria-busy','false');error(e);}
};
$('settingsButton').onclick=()=>{saveIndexView();request('settings').catch(error);};
$('homeSearch').oninput=()=>{saveIndexView();cancelAnimationFrame(searchFrame);searchFrame=requestAnimationFrame(renderCards);};
window.addEventListener('pagehide',saveIndexView);
JotBridge.on(data=>{
  if(data.event==='notes-changed')scheduleRefresh();
  if(data.event==='window-visibility'){indexVisible=data.visible;if(indexVisible&&indexDirty)scheduleRefresh();}
  if(data.event==='preferences'){homeData.prefs=JotDesign.apply(data.prefs);$('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');}
  if(data.event==='warning')error(new Error(data.message));
  if(data.event==='quit-failed'){$('homeQuit').disabled=false;$('homeQuit').setAttribute('aria-busy','false');error(new Error(data.message));}
});
document.addEventListener('keydown',event=>{
  if(event.ctrlKey&&event.code==='KeyN'){event.preventDefault();create();}
  if(event.ctrlKey&&event.code==='KeyK'){event.preventDefault();$('homeSearch').focus();}
  if(event.key==='Escape'&&!$('metadataDialog').open)request('hide').catch(error);
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
}
boot().catch(error);

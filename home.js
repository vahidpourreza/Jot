'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let homeData={version:2,notes:[],prefs:{...JotDesign.defaults}},refreshVersion=0,currentGroup=null,selectedId=null,editingId=null;
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
function groupNames(){return [...new Set(homeData.notes.map(note=>note.group||'').filter(Boolean))].sort((a,b)=>a.localeCompare(b,JotI18n.locale));}
function renderGroups(){
  $('groupFilters').replaceChildren();
  for(const group of [null,'',...groupNames()]){
    const button=document.createElement('button');button.className='group-chip';button.dataset.group=group??'*';
    button.textContent=group===null?t('همه یادداشت‌ها'):group===''?t('بدون گروه'):group;
    button.setAttribute('aria-pressed',String(currentGroup===group));
    button.onclick=()=>{currentGroup=group;saveIndexView();renderCards();renderGroups();};$('groupFilters').append(button);
  }
}
function renderCards(){
  const query=$('homeSearch').value.trim().toLocaleLowerCase();
  const notes=homeData.notes.filter(note=>(currentGroup===null||(note.group||'')===currentGroup)&&[note.title,note.plain,note.legacyTitle,note.group].join(' ').toLocaleLowerCase().includes(query)).sort((a,b)=>b.updatedAt-a.updatedAt);
  $('cards').replaceChildren();$('homeEmpty').hidden=homeData.notes.length>0;
  const count=new Intl.NumberFormat(JotI18n.locale).format(homeData.notes.length);
  $('noteCount').textContent=count+(JotI18n.language==='en'?' notes':' یادداشت');
  for(const note of notes){
    const card=document.createElement('article');card.className='note-card sticky-card';card.dataset.noteId=note.id;card.dataset.selected=String(note.id===(selectedId||homeData.activeId));
    const open=document.createElement('button');open.className='card-open';open.setAttribute('aria-label',t('باز کردن')+' '+noteName(note));
    const title=document.createElement('strong');title.dir='auto';title.textContent=noteName(note);
    const snippet=document.createElement('p');snippet.dir='auto';snippet.textContent=note.plain.split('\n').filter(s=>s.trim()).slice(note.title?0:1).join(' ').slice(0,180)||t('برای نوشتن باز کن…');
    const text=document.createElement('span');text.className='card-text';text.append(title,snippet);
    const group=document.createElement('span');group.className='card-group';group.dir='auto';group.textContent=note.group||t('بدون گروه');
    open.append(JotDesign.icon(note.hasImage?'image':'notepad-text'),text,group);
    open.onclick=()=>{selectedId=note.id;saveIndexView();renderCards();request('open-note',note.id).catch(error);};
    const foot=document.createElement('div');foot.className='card-footer';
    const time=document.createElement('small');time.textContent=new Intl.DateTimeFormat(JotI18n.locale,{month:'short',day:'numeric'}).format(note.updatedAt);
    const edit=document.createElement('button');edit.className='card-edit';edit.title=t('عنوان و گروه');edit.setAttribute('aria-label',t('عنوان و گروه'));edit.append(JotDesign.icon('settings-2'),document.createTextNode(t('عنوان و گروه')));edit.onclick=()=>editNote(note);
    foot.append(time,edit);card.append(open,foot);$('cards').append(card);
  }
  if(!notes.length&&homeData.notes.length){const empty=document.createElement('p');empty.className='empty-list';empty.textContent=t('یادداشتی پیدا نشد.');$('cards').append(empty);}
}
async function refresh(){
  const version=++refreshVersion,data=await request('index-load');if(version!==refreshVersion)return;
  if(data)homeData=data;homeData.prefs=JotDesign.apply(homeData.prefs);renderGroups();renderCards();
  $('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');
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
$('homeSearch').oninput=()=>{saveIndexView();renderCards();};
window.addEventListener('pagehide',saveIndexView);
JotBridge.on(data=>{
  if(data.event==='notes-changed')refresh().catch(error);
  if(data.event==='preferences'){homeData.prefs=JotDesign.apply(data.prefs);renderGroups();renderCards();$('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');}
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
  if(!await request('index-load')){
    const legacy=JSON.parse(localStorage.getItem('vanz-notes')||'[]');
    if(!Array.isArray(legacy))throw new Error('یادداشت‌های قبلی قابل خواندن نیستند.');
    const escape=s=>{const element=document.createElement('span');element.textContent=s;return element.innerHTML;};
    homeData.notes=legacy.map(note=>({id:crypto.randomUUID(),html:String(note.content||'').split(/\r?\n/).map(line=>'<p dir="auto">'+(escape(line)||'<br>')+'</p>').join(''),plain:String(note.content||''),legacyTitle:String(note.title||''),updatedAt:Number(note.updatedAt)||Date.now()}));
    await request('import',homeData);
  }
  await refresh();window.jotReady=true;
}
boot().catch(error);

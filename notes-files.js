'use strict';
(()=>{
  let busy=false,loadedId=null,identity,lastRendered='';
  const states=new Map();
  function currentNote(){return typeof model!=='undefined'&&(!window.JotWorkspace||JotWorkspace.view==='note')?activeNote():null;}
  function currentId(){return currentNote()?.id;}
  const filename=path=>String(path||'').split(/[\\/]/).pop();
  const shortcut=action=>window.JotShortcutBindings?.label(action)||'';
  function name(note){return note?.title||note?.legacyTitle||filename(note?.noteFile?.path)||'';}
  function state(note){return states.get(note?.id)||{id:note?.id,path:note?.noteFile?.path,name:filename(note?.noteFile?.path),format:note?.noteFile?.format,dirty:!!note?.fileDirty};}
  function isDirty(note){const value=state(note);return !!value.path&&(value.dirty||value.format==='jot'&&window.JotDocumentHeading?.hasPending(note?.id)||note?.id===currentId()&&(revision>savedRevision||composing||pendingEdits.size>0));}
  function error(e){JotBridge.reportError(e,'note-file');const alert=document.getElementById(window.JotWorkspace?'workspaceError':'error');if(alert){alert.textContent=e.message;alert.hidden=true;}JotToast.error(e.message,{id:'file-command-error'});}
  function items(id){return [
    {id:'file-open',label:'Open file…',icon:'folder-open',shortcut:shortcut('file-open')},
    {id:'file-save',label:'Save',icon:'save',shortcut:shortcut('file-save'),disabled:!id,pending:'Saving…'},
    {id:'file-save-as',label:'Save as…',icon:'save',shortcut:shortcut('file-save-as'),disabled:!id,pending:'Saving…'}
  ];}
  function apply(value,extra={}){
    if(!value)return;const previous=states.get(value.id),next={...value,...extra};states.set(value.id,next);
    const note=currentNote();
    if(note?.id===value.id){note.fileDirty=value.dirty;if(value.path)note.noteFile={...note.noteFile,path:value.path,format:value.format};}
    render(!!(previous?.path||next.path)&&JSON.stringify(previous)!==JSON.stringify(next));
  }
  function render(forceTabs=false){
    const note=currentNote(),value=state(note),dirty=isDirty(note);
    const signature=JSON.stringify([note?.id,note?.title,value.path,value.name,value.error,!!value.saving,dirty,shortcut('file-save')]);
    if(lastRendered===signature){if(forceTabs)window.JotWorkspace?.renderTabs();return;}
    lastRendered=signature;
    if(!identity&&document.getElementById('writingArea')){
      identity=document.createElement('div');identity.id='fileIdentity';identity.className='file-identity';identity.hidden=true;
      const label=document.createElement('span');label.className='file-name';label.dir='auto';
      const status=document.createElement('span');status.className='file-status';status.setAttribute('role','status');
      identity.append(label,status);const heading=document.getElementById('documentHeading');if(heading)heading.append(identity);else document.getElementById('writingArea').before(identity);
      identity.oncontextmenu=event=>{event.preventDefault();event.stopPropagation();JotHomeMenu.open(identity,event.clientX,event.clientY);};
    }
    if(identity){
      identity.hidden=!value.path;
      identity.querySelector('.file-name').textContent=value.name||'';
      const label=value.error?'Auto-save paused':value.saving?'Saving…':dirty?'● Unsaved':'Saved';
      identity.querySelector('.file-status').textContent=label;
      identity.dataset.dirty=String(dirty);identity.dataset.error=String(!!value.error);
      identity.title=value.path?(value.path+'\n'+(value.error||label)+(dirty&&shortcut('file-save')?' · '+shortcut('file-save'):'')):'';
    }
    if(note&&value.path)document.title=(dirty?'● ':'')+name(note)+(name(note)!==value.name?' · '+value.name:'')+' — Jot';
    window.JotWorkspace?.renderTabs();
  }
  async function loadState(id){
    const before=states.get(id),value=await JotBridge.request('note-file-state',id);
    // File events can overtake a pending state read. Never replace a newer
    // dirty/saving/error broadcast with an older hydration or Save reply.
    if(states.get(id)===before)apply(value,{saving:before?.saving,error:before?.error});
  }
  async function refresh(){
    const id=currentId();render();
    if(id&&id!==loadedId){loadedId=id;if(!states.has(id))try{await loadState(id);}catch(e){loadedId=null;error(e);}}
    if(!id)loadedId=null;
  }
  async function run(action,id=currentId()){
    if(busy)return;busy=true;
    try{
      if(action==='file-open')return await JotBridge.request('open-note-file');
      if(!id)throw new Error('Open a note before saving a file.');
      const result=await JotBridge.request('save-note-file',{id,saveAs:action==='file-save-as'});
      await loadState(id);
      if(result){JotToast.dismiss('file-command-error');JotToast.success('File saved',{id:'file-saved',description:filename(result)});}
      // Manual file commands return typing focus to the same note, but never
      // move focus to a different note or change background auto-save behavior.
      if(currentId()===id)editor.focus({preventScroll:true});
      return result;
    }finally{busy=false;}
  }
  window.JotNoteFiles={editorMenuItems:()=>items(currentId()),run,name,filename,state,isDirty,refresh,shortcut,edited:()=>render()};
  window.JotHomeMenu={open(owner,x,y){
    const id=currentId();
    const pin=window.JotWorkspace?[{id:'workspace-pin',label:JotWorkspace.pinned?'Stop keeping on top':'Keep Jot on top',icon:'pin'}]:[];
    JotMenus.open({x,y,owner,restore:()=>owner.focus({preventScroll:true}),error,items:[...items(id),{separator:true},...pin,{id:'app-settings',label:'Settings',icon:'settings'},{id:'app-shortcuts',label:'Keyboard shortcuts',icon:'keyboard'}],
      run:action=>action==='app-settings'?JotBridge.request('settings'):action==='app-shortcuts'?JotBridge.request('shortcuts'):action==='workspace-pin'?JotBridge.request('workspace-pin',!JotWorkspace.pinned):run(action,id)});
  }};
  JotBridge.on(data=>{
    if(data.event==='file-state'){
      apply(data.state,{saving:!!data.saving,error:data.error});
      const toastId='file-auto-'+data.state.id;
      if(data.error&&(currentId()===data.state.id||window.JotWorkspace))JotToast.error('Auto-save paused',{id:toastId,description:data.error,action:{label:'Save as…',pendingLabel:'Opening…',onClick:()=>run('file-save-as',data.state.id)}});
      else if(!data.error)JotToast.dismiss(toastId);
    }
    if(data.event==='note-identity'){
      const note=currentNote();if(note?.id===data.note.id){for(const key of ['title','icon','color','noteFile','fileDirty'])note[key]=data.note[key];window.JotNoteIcons?.refreshEditor();render(true);}
    }
    if(data.event==='workspace-view'||data.event==='preferences')queueMicrotask(refresh);
  });
  document.addEventListener('keydown',event=>{
    if(event.isComposing||document.querySelector('dialog[open]'))return;
    const action=window.JotShortcutBindings?.match(event,'file');if(!action)return;
    event.preventDefault();event.stopImmediatePropagation();
    if(!event.repeat&&(action==='file-open'||currentId()))run(action).catch(error);
  },true);
  window.addEventListener('jot-shortcuts-changed',()=>render());
  const ready=window.JotNoteEditor?.ready;
  if(ready)ready.then(refresh).catch(error);else setTimeout(refresh,0);
})();

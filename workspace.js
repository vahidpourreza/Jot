'use strict';
(()=>{
  const byId=id=>document.getElementById(id),request=JotBridge.request;
  const state={view:'home',tabs:[],settingsOpen:false,canReopenTab:false,activeId:'home',maximized:false,busy:false,modeBusy:false,pending:null,operation:Promise.resolve(),booted:false};
  let tabActions,tabSwitcher;
  function error(value){JotBridge.reportError(value,'workspace');const target=byId('workspaceError');target.textContent=value?.message||'The action could not be completed.';target.hidden=true;JotToast.error(target.textContent,{id:'workspace-error'});}
  function maximizeState(value){
    state.maximized=!!value;const button=byId('workspaceMaximize');button.ariaPressed=String(state.maximized);
    button.title=button.ariaLabel=state.maximized?'Restore':'Maximize';button.replaceChildren(JotDesign.icon(state.maximized?'window-restore':'window-maximize'));
  }
  function pinState(value){const button=byId('workspacePin');button.ariaPressed=String(!!value);button.title=button.ariaLabel=value?'Stop keeping Jot on top':'Keep Jot on top';}
  function renderTabs(){
    JotWorkspaceTabs.render(byId('workspaceTabs'),{tabs:state.tabs,settingsOpen:state.settingsOpen,canReopenTab:state.canReopenTab,activeId:state.activeId,busy:state.busy||state.modeBusy,
      pendingId:state.pending?.id,pendingAction:state.pending?.action,onAction:runAction,onMenu:tabActions?.open,onRename:tabActions?.rename,onHomeMenu:(owner,x,y)=>window.JotHomeMenu?.open(owner,x,y)});
    tabSwitcher?.sync();
  }
  async function runAction(action,id){
    if(state.busy||state.modeBusy||action==='tab-switch'&&id===state.activeId)return;
    let previousOrder,optimisticOrder;
    if(action==='tab-reorder'&&Array.isArray(id?.ids)){
      const byId=new Map(state.tabs.map(note=>[note.id,note]));
      if(id.ids.length===byId.size&&new Set(id.ids).size===byId.size&&id.ids.every(noteId=>byId.has(noteId))){
        previousOrder=state.tabs;optimisticOrder=id.ids.map(noteId=>byId.get(noteId));state.tabs=optimisticOrder;JotNoteEditor.setHeaders(state.tabs);
      }
    }
    state.busy=true;state.pending={action,id:typeof id==='string'?id:id?.id||state.activeId};renderTabs();JotMenus.close();byId('workspaceError').hidden=true;JotToast.dismiss('workspace-error');
    const payload=action==='new-tab'&&state.view==='home'&&JotHome.folder?{folder:JotHome.folder}:id;
    const operation=request(action,payload);state.operation=operation;
    try{return await operation;}catch(e){if(previousOrder&&state.tabs===optimisticOrder){state.tabs=previousOrder;JotNoteEditor.setHeaders(state.tabs);}error(e);throw e;}
    finally{state.busy=false;state.pending=null;renderTabs();}
  }
  function showView(data){
    const view=data.view||'home';JotMenus.close();byId('workspaceError').hidden=true;
    JotHome.hide();JotNoteEditor.suspend();
    state.tabs=data.tabs||state.tabs;state.settingsOpen=data.settingsOpen??state.settingsOpen;state.canReopenTab=data.canReopenTab??state.canReopenTab;state.activeId=view==='note'?data.note.id:view==='settings'?'settings':'home';state.view=view;
    for(const [name,id] of [['home','workspaceHome'],['settings','workspaceSettings'],['note','app']])byId(id).hidden=name!==view;
    if(view==='note')JotNoteEditor.show(data);
    else if(view==='settings')JotSettings.show(data.prefs);
    else JotHome.show(data.prefs);
    JotNoteEditor.setHeaders(state.tabs);maximizeState(data.fullscreen??state.maximized);renderTabs();
    window.JotNoteIcons?.refreshEditor();window.JotNoteFiles?.refresh();
    if(view!=='note')byId('workspaceTabs').querySelector('[aria-selected=true]')?.focus({preventScroll:true});
    document.body.dataset.view=view;
  }
  window.JotWorkspace={get view(){return state.view;},get pinned(){return byId('workspacePin').ariaPressed==='true';},get maximized(){return state.maximized;},get tabs(){return state.tabs;},get canReopenTab(){return state.canReopenTab;},get tabSwitcher(){return tabSwitcher;},renderTabs,
    setOpeningModeBusy(value){state.modeBusy=!!value;renderTabs();},
    updateTabs(tabs){state.tabs=tabs;renderTabs();},get pending(){return state.operation;},runAction,showView};
  Object.defineProperty(window,'fullscreen',{configurable:true,get:()=>state.maximized});
  JotBridge.on(data=>{
    if(data.event==='workspace-view'){
      try{showView(data);JotBridge.send('workspace-view-ready',{intent:data.intent});}
      catch(e){error(e);JotBridge.send('workspace-view-failed',{intent:data.intent});}
    }
    if(data.event==='tabs-changed'){state.tabs=data.tabs||[];state.settingsOpen=data.settingsOpen??state.settingsOpen;state.canReopenTab=data.canReopenTab??state.canReopenTab;window.JotNoteEditor?.setHeaders(state.tabs);if(state.booted)renderTabs();}
    if(data.event==='window-fullscreen')maximizeState(data.enabled);
    if(data.event==='workspace-pin')pinState(data.pinned);
    if(data.event==='warning'||data.event==='quit-failed')error(new Error(data.message));
    if(data.event==='preferences'){pinState(data.prefs.workspacePinned);if(state.booted)queueMicrotask(renderTabs);}
  });
  function loadScript(name){return new Promise((resolve,reject)=>{const script=document.createElement('script');script.src=name;script.onload=resolve;script.onerror=()=>reject(new Error('Could not load the '+name+' view.'));document.body.append(script);});}
  function importView(html,id){
    const documentView=new DOMParser().parseFromString(html,'text/html'),main=documentView.querySelector('main');
    if(!main)throw new Error('A workspace view could not be loaded.');
    const view=document.importNode(main,true);view.classList.add('workspace-view');view.id=id;view.hidden=true;
    if(id!=='app')view.querySelector('header.index-header')?.remove();
    if(id==='workspaceSettings')view.querySelector('#homeError').id='settingsError';
    if(id==='workspaceHome')for(const dialog of view.querySelectorAll('dialog'))byId('workspaceDialogs').append(dialog);
    byId('workspaceViews').append(view);
  }
  async function boot(){
    JotDesign.icons();JotDesign.drag(byId('workspaceHandle'),{isMaximized:()=>state.maximized,onToggle:()=>request('window-toggle-maximize')});
    byId('workspaceMinimize').onclick=()=>request('minimize').catch(error);
    byId('workspaceClose').onclick=()=>request('hide').catch(error);
    byId('workspaceMaximize').onclick=()=>request('window-toggle-maximize').catch(error);
    byId('workspacePin').onclick=async()=>{const button=byId('workspacePin');if(button.disabled)return;button.disabled=true;try{pinState(await request('workspace-pin',button.ariaPressed!=='true'));}catch(e){error(e);}finally{button.disabled=false;}};
    const [layout,context]=await Promise.all([request('workspace-layout'),request('context')]);
    state.view=context.view||'home';state.tabs=context.tabs||[];maximizeState(context.fullscreen);
    importView(layout.home,'workspaceHome');importView(layout.settings,'workspaceSettings');importView(layout.editor,'app');
    await loadScript('home.js');await loadScript('shortcuts.js');await loadScript('settings.js');await loadScript('assets/editor/editor.js');await loadScript('renderer.js');await loadScript('editor-menu.js');await loadScript('notes-files.js');await loadScript('document-heading.js');await loadScript('note-text-size.js');await loadScript('app-shortcuts.js');
    await Promise.all([JotHome.ready,JotSettings.ready,JotNoteEditor.ready,JotShortcutBindings.ready]);
    tabSwitcher=JotTabSwitcher.create({snapshot:()=>({tabs:state.tabs,settingsOpen:state.settingsOpen,activeId:state.activeId,busy:state.busy||state.modeBusy}),onSwitch:id=>runAction('tab-switch',id)});
    tabActions=JotWorkspaceTabActions.create({request,onAction:runAction,onError:error,
      beforeAction:()=>state.operation.catch(()=>{}),onRename:note=>JotHome.editNote(note),onDelete:note=>JotHome.deleteNote(note),
      async onOptions(note,owner){if(state.activeId!==note.id||state.view!=='note')await runAction('tab-switch',note.id);JotNoteEditor.openOptions(owner);}});
    const initial={...context,prefs:await request('preferences-load')};
    pinState(initial.prefs.workspacePinned);
    if(context.view==='note'){const loaded=await request('note-load');initial.note=loaded.model.notes[0];initial.prefs=loaded.model.prefs;}
    showView(initial);state.booted=true;window.jotReady=true;
    await document.fonts.ready;JotBridge.send('workspace-ready');
  }
  boot().catch(e=>{error(e);JotBridge.send('workspace-failed');});
})();

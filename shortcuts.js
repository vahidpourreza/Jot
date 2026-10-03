'use strict';
window.JotShortcutsPage=(()=>{
  const writing=[['Undo','Ctrl+Z'],['Redo','Ctrl+Y'],['Redo (alternative)','Ctrl+Shift+Z'],['Cut','Ctrl+X'],['Copy','Ctrl+C'],['Paste','Ctrl+V','Text is unformatted by default; images are preserved.'],['Context menu','Shift+F10'],['Select all','Ctrl+A'],['Bold','Ctrl+B'],['Italic','Ctrl+I'],['Underline','Ctrl+U'],['Close menu','Esc']];
  let mounted=false,prefs={},status=null,onPreference,busy=false,editing=null,pendingChord=null,confirmAction=null,returnFocus=null;
  const byId=id=>document.getElementById(id),bindings=()=>JotShortcutBindings.config;
  function keys(value){
    const fragment=document.createDocumentFragment();
    if(!value){const empty=document.createElement('span');empty.className='shortcut-unassigned';empty.textContent='Not set';fragment.append(empty);return fragment;}
    value.split('+').forEach((key,index)=>{if(index){const plus=document.createElement('span');plus.textContent='+';fragment.append(plus);}const kbd=document.createElement('kbd');kbd.textContent=key;fragment.append(kbd);});return fragment;
  }
  function filter(){
    const query=byId('shortcutSearch').value.trim().toLowerCase();let total=0;
    for(const section of byId('shortcutGroups').children){let count=0;for(const row of section.querySelectorAll('.shortcut-row')){const visible=!query||row.dataset.search.includes(query);row.hidden=!visible;if(visible)count++;}section.hidden=count===0;total+=count;}
    byId('shortcutEmpty').hidden=total>0;
  }
  function render(nextPrefs,nextStatus){
    if(nextPrefs)prefs=nextPrefs;if(nextStatus)status=nextStatus;if(!mounted)return;
    byId('globalShortcuts').setAttribute('aria-checked',String(!!prefs.globalShortcuts));
    const message=byId('windowsShortcutStatus');message.dataset.error=String(!!status?.error);
    const hasWindowsKeys=bindings().actions.some(action=>action.scope==='global'&&bindings().bindings[action.id]);
    message.textContent=status?.error||(!prefs.globalShortcuts?'Off. Your choices take effect when enabled.':!hasWindowsKeys?'On — assign a key combination to use Windows shortcuts.':status?.testing?'Enabled for this test session; no Windows keys are reserved.':status?.active?'On — available while Jot is running.':'Not active. Turn this off and on to retry.');
    for(const action of bindings().actions){
      const row=byId('shortcutsPage').querySelector('[data-shortcut-id="'+action.id+'"]');if(!row)continue;
      const value=JotShortcutBindings.label(action.id),custom=Object.hasOwn(bindings().overrides,action.id);
      row.querySelector('.shortcut-binding').replaceChildren(keys(value));
      row.querySelector('.shortcut-clear').disabled=busy||!bindings().bindings[action.id];
      row.querySelector('.shortcut-reset').disabled=busy||!custom;
      row.querySelector('.shortcut-binding').disabled=busy;
      row.dataset.custom=String(custom);row.dataset.search=(action.label+' '+value+' '+(action.note||'')).toLowerCase();
    }
    byId('shortcutsResetAll').disabled=busy||Object.keys(bindings().overrides).length===0;filter();
  }
  function rowFor(action){
    const row=document.createElement('div');row.className='shortcut-row';row.dataset.shortcutId=action.id;
    const term=document.createElement('dt');term.textContent=action.label;
    if(action.note){const hint=document.createElement('small');hint.textContent=action.note;term.append(hint);}
    const value=document.createElement('dd'),change=document.createElement('button');change.type='button';change.className='shortcut-binding';change.ariaLabel='Change '+action.label+' shortcut';change.title='Change shortcut';change.onclick=()=>openRecorder(action,change);
    const clear=document.createElement('button');clear.type='button';clear.className='shortcut-clear';clear.ariaLabel='Clear '+action.label+' shortcut';clear.title='Clear shortcut';clear.append(JotDesign.icon('x'));clear.onclick=()=>confirm('Clear shortcut?','Remove the key combination for '+action.label+'? The action remains available in Jot.','Clear',()=>save({...bindings().overrides,[action.id]:null}),clear);
    const reset=document.createElement('button');reset.type='button';reset.className='shortcut-reset';reset.ariaLabel='Reset '+action.label+' shortcut';reset.title='Reset to '+JotShortcutBindings.format(action.defaultChord);reset.append(JotDesign.icon('undo-2'));reset.onclick=()=>confirm('Reset shortcut?','Restore '+action.label+' to '+JotShortcutBindings.format(action.defaultChord)+'?','Reset',()=>{const next={...bindings().overrides};delete next[action.id];return save(next);},reset);
    value.append(change,clear,reset);row.append(term,value);return row;
  }
  function buildGroups(){
    const groups=new Map();
    for(const action of bindings().actions){if(action.scope==='global'){byId('windowsShortcutList').append(rowFor(action));continue;}if(!groups.has(action.group))groups.set(action.group,[]);groups.get(action.group).push(action);}
    for(const [title,actions]of groups){const section=document.createElement('section');section.className='shortcut-group';const heading=document.createElement('h2');heading.textContent=title;const list=document.createElement('dl');list.className='shortcut-list';for(const action of actions)list.append(rowFor(action));section.append(heading,list);byId('shortcutGroups').append(section);}
    const section=document.createElement('section');section.className='shortcut-group';const title=document.createElement('h2');title.textContent='Writing · standard keys';const list=document.createElement('dl');list.className='shortcut-list';
    for(const [label,shortcut,note]of writing){const row=document.createElement('div');row.className='shortcut-row';row.dataset.search=(label+' '+shortcut+' '+(note||'')).toLowerCase();const term=document.createElement('dt');term.textContent=label;if(note){const hint=document.createElement('small');hint.textContent=note;term.append(hint);}const value=document.createElement('dd');value.append(keys(shortcut));row.append(term,value);list.append(row);}section.append(title,list);byId('shortcutGroups').append(section);
  }
  function dialogError(dialog,error){const target=dialog.querySelector('.dialog-error');target.textContent=error.message||String(error);target.hidden=false;}
  function openRecorder(action,owner){
    if(busy)return;editing=action;pendingChord=null;returnFocus=owner;
    byId('shortcutRecordTitle').textContent='Change '+action.label;byId('shortcutRecordHint').textContent=action.scope==='global'?'Press Ctrl or Alt with a key. Windows shortcuts work outside Jot while enabled.':'Press Ctrl or Alt with a key, or F1–F11. Standard typing keys stay unchanged.';
    byId('shortcutRecordValue').textContent='Press a key combination';byId('shortcutRecordSave').disabled=true;byId('shortcutRecordDialog').querySelector('.dialog-error').hidden=true;
    byId('shortcutRecordDialog').showModal();byId('shortcutRecordValue').focus();
  }
  function record(event){
    if(!editing||busy||event.key==='Escape'||event.key==='Tab'&&!event.ctrlKey&&!event.altKey)return;
    if(event.key==='Enter'&&!event.ctrlKey&&!event.altKey&&!event.shiftKey){if(pendingChord&&!byId('shortcutRecordSave').disabled){event.preventDefault();void applyRecorded();}return;}
    if(['Control','Shift','Alt','Meta'].includes(event.key))return;
    event.preventDefault();event.stopImmediatePropagation();
    if(event.repeat)return;
    const chord=JotShortcutBindings.chordFor(event),error=chord?JotShortcutBindings.validate(editing.id,chord):'This key is reserved for typing or Windows controls.';
    byId('shortcutRecordValue').replaceChildren(chord?keys(JotShortcutBindings.format(chord)):document.createTextNode('Choose another combination'));
    const hint=byId('shortcutRecordDialog').querySelector('.dialog-error');hint.textContent=error;hint.hidden=!error;
    pendingChord=error?null:chord;byId('shortcutRecordSave').disabled=!!error;
  }
  function confirm(title,description,label,action,owner){
    if(busy)return;returnFocus=owner;confirmAction=action;byId('shortcutConfirmTitle').textContent=title;byId('shortcutConfirmDescription').textContent=description;byId('shortcutConfirmApply').textContent=label;byId('shortcutConfirmDialog').querySelector('.dialog-error').hidden=true;byId('shortcutConfirmDialog').showModal();byId('shortcutConfirmCancel').focus();
  }
  async function save(overrides){
    const updated=await JotBridge.request('preferences',{shortcutBindings:overrides});prefs=updated;JotShortcutBindings.configure(updated);render(updated);window.JotToast?.success('Shortcuts updated',{id:'shortcut-saved'});
  }
  async function applyDialog(dialog,button,operation){
    if(busy)return;busy=true;const label=button.textContent;dialog.querySelectorAll('button').forEach(b=>b.disabled=true);button.ariaBusy='true';button.textContent='Saving…';dialog.querySelector('.dialog-error').hidden=true;render();
    try{await operation();dialog.close();}catch(error){JotBridge.reportError(error,'shortcut-setting');dialogError(dialog,error);}finally{busy=false;button.removeAttribute('aria-busy');button.textContent=label;dialog.querySelectorAll('button').forEach(b=>b.disabled=false);render();if(!dialog.open)returnFocus?.focus({preventScroll:true});}
  }
  async function applyRecorded(){if(!editing||!pendingChord)return;const error=JotShortcutBindings.validate(editing.id,pendingChord);if(error){dialogError(byId('shortcutRecordDialog'),error);return;}await applyDialog(byId('shortcutRecordDialog'),byId('shortcutRecordSave'),()=>save({...bindings().overrides,[editing.id]:pendingChord}));}
  async function mount(container,preference){
    if(mounted)return;onPreference=preference;await JotShortcutBindings.ready;
    const html=await JotBridge.request('shortcuts-layout'),doc=new DOMParser().parseFromString(html,'text/html'),page=doc.getElementById('shortcutsPage');if(!page)throw new Error('The shortcuts page could not be loaded.');container.insertBefore(document.importNode(page,true),container.querySelector('.home-footer'));
    for(const dialog of doc.querySelectorAll('dialog'))document.body.append(document.importNode(dialog,true));
    buildGroups();byId('shortcutSearch').oninput=filter;byId('globalShortcuts').onclick=()=>{if(!busy)Promise.resolve(onPreference({globalShortcuts:!prefs.globalShortcuts})).catch(error=>{JotBridge.reportError(error,'shortcut-setting');window.JotToast?.error(error.message,{id:'shortcut-setting'});});};
    byId('shortcutsResetAll').onclick=()=>confirm('Reset all shortcuts?','Restore the default app and Windows key combinations. The Windows shortcuts on/off setting is kept.','Reset all',()=>save({}),byId('shortcutsResetAll'));
    byId('shortcutRecordDialog').addEventListener('keydown',record,true);byId('shortcutRecordSave').onclick=applyRecorded;
    byId('shortcutRecordCancel').onclick=()=>byId('shortcutRecordDialog').close();byId('shortcutConfirmCancel').onclick=()=>byId('shortcutConfirmDialog').close();
    byId('shortcutConfirmApply').onclick=()=>applyDialog(byId('shortcutConfirmDialog'),byId('shortcutConfirmApply'),confirmAction);
    for(const id of ['shortcutRecordDialog','shortcutConfirmDialog']){byId(id).addEventListener('cancel',e=>{if(busy)e.preventDefault();});byId(id).addEventListener('close',()=>{editing=null;pendingChord=null;returnFocus?.focus({preventScroll:true});});}
    mounted=true;JotDesign.icons();status=await JotBridge.request('shortcuts-status');render();
  }
  function show(){byId('shortcutsPage').hidden=false;byId('shortcutSearch').focus({preventScroll:true});}
  function hide(){if(mounted)byId('shortcutsPage').hidden=true;}
  JotBridge.on(data=>{if(data.event==='shortcuts-status')render(null,data.status);});
  window.addEventListener('jot-shortcuts-changed',()=>{if(mounted)render();});
  return{mount,show,hide,render,get mounted(){return mounted;},get busy(){return busy;}};
})();

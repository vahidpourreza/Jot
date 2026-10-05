'use strict';
// Change a note preference, never WebView/page zoom or the selected rich text.
window.JotTextSize=(()=>{
  let wheelTotal=0,wheelTime=0,wheelNote=null;
  const actions={'note-text-larger':1,'note-text-smaller':-1,'note-text-default':0};
  function available(){return ready&&!composing&&!deleting&&!editorLockedByHost&&editor.isContentEditable&&!document.querySelector('dialog[open]')&&(!window.JotWorkspace||JotWorkspace.view==='note');}
  function announce(reset=false){
    const size=model.prefs.fontSize;
    JotToast.info((reset?'Default text size: ':'Text size: ')+size+' px',{id:'note-text-size',duration:1500});
  }
  function change(delta){
    if(!available())return false;
    const size=Math.max(13,Math.min(24,model.prefs.fontSize+delta));
    if(size!==model.prefs.fontSize)void setPreference({fontSize:size});
    announce();return true;
  }
  function reset(){
    if(!available())return false;
    if(activeNote().view?.fontSize!=null)void setPreference({fontSize:null});
    announce(true);return true;
  }
  function clear(){wheelTotal=0;wheelTime=0;wheelNote=null;JotToast.dismiss('note-text-size');}
  function keyAction(event){
    let action=JotShortcutBindings.match(event,'writing');
    if(action)return action;
    // Accept the actual '+' character (Shift+=) and numeric keypad variants
    // while these actions retain their default binding. An assigned app/file
    // shortcut always owns its key, even if it used this chord before upgrade.
    if(!event.ctrlKey||event.altKey||event.metaKey||JotShortcutBindings.match(event,'app')||JotShortcutBindings.match(event,'file'))return null;
    const bindings=JotShortcutBindings.config?.bindings;
    if((event.code==='NumpadAdd'||event.shiftKey&&event.code==='Equal')&&bindings?.['note-text-larger']==='Ctrl+Equal')action='note-text-larger';
    if(event.code==='NumpadSubtract'&&bindings?.['note-text-smaller']==='Ctrl+Minus')action='note-text-smaller';
    if(event.code==='Numpad0'&&!event.shiftKey&&bindings?.['note-text-default']==='Ctrl+Digit0')action='note-text-default';
    return action;
  }
  document.addEventListener('keydown',event=>{
    if(event.defaultPrevented||event.isComposing||!available())return;
    // Leave title/search fields and dialogs in charge of their own typing.
    if(event.target.closest?.('input,textarea,[contenteditable=true]:not(#editor)')&&!editor.contains(event.target))return;
    const action=keyAction(event);if(!Object.hasOwn(actions,action))return;
    event.preventDefault();event.stopImmediatePropagation();
    if(actions[action])change(actions[action]);else reset();
  },true);
  document.getElementById('writingArea').addEventListener('wheel',event=>{
    if(!event.ctrlKey||event.altKey||event.metaKey||event.defaultPrevented||!available()||!event.deltaY)return;
    event.preventDefault();
    const now=performance.now(),id=model.activeId;
    if(id!==wheelNote||now-wheelTime>350||Math.sign(wheelTotal)!==Math.sign(event.deltaY))wheelTotal=0;
    wheelNote=id;wheelTime=now;
    const amount=event.deltaY*(event.deltaMode===1?20:event.deltaMode===2?100:1);
    let steps;
    if(Math.abs(amount)>=80){steps=Math.sign(amount);wheelTotal=0;}
    else{wheelTotal+=amount;steps=Math.trunc(wheelTotal/60);wheelTotal-=steps*60;}
    if(steps)change(-Math.max(-3,Math.min(3,steps)));
  },{passive:false});
  function describe(){
    for(const [id,action,hint] of [['smallerButton','note-text-smaller','Decrease font size'],['largerButton','note-text-larger','Increase font size'],['defaultFontSize','note-text-default','Use the app writing default']]){
      const shortcut=JotShortcutBindings.label(action);
      document.getElementById(id).setAttribute('aria-description',hint+(shortcut?' · '+shortcut:'')+(action==='note-text-default'?'':' · Ctrl+mouse wheel'));
    }
  }
  void JotShortcutBindings.ready.then(describe);
  window.addEventListener('jot-shortcuts-changed',describe);
  window.addEventListener('blur',clear);
  JotBridge.on(data=>{if(['workspace-view','prepare-quit','flush'].includes(data.event))clear();});
  return {change,reset,keyAction};
})();

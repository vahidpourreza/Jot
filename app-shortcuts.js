'use strict';
(()=>{
  let busy=false;
  function commandFor(event){
    return window.JotShortcutBindings?.match(event,'app')||null;
  }
  function report(error){
    JotBridge.reportError(error,'keyboard-shortcut');
    window.JotToast?.error(error.message,{id:'keyboard-shortcut'});
  }
  async function run(command){
    if(busy)return;busy=true;
    try{await JotBridge.request('app-shortcut',command);}catch(error){report(error);}finally{busy=false;}
  }
  document.addEventListener('keydown',event=>{
    const command=commandFor(event);
    if(!command||event.defaultPrevented||document.querySelector('dialog[open]'))return;
    if(window.JotWorkspace?.tabSwitcher?.handleKeyDown(event,command))return;
    // Preserve AltGr typing, composition, native editor/file commands, and
    // modal keyboard ownership. App actions use physical keys in RTL layouts.
    event.preventDefault();event.stopPropagation();
    if(!event.repeat)void run(command);
  },true);
  window.JotAppShortcuts={commandFor,run,get busy(){return busy||!!window.JotWorkspace?.tabSwitcher?.busy;}};
})();

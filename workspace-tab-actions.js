'use strict';
// The Home workspace owns tab actions. Floating notes keep their own compact
// controls and never load this module.
window.JotWorkspaceTabActions=(()=>{
  function create({request=JotBridge.request,onAction,onOptions,onRename,onDelete,onError,beforeAction}){
    let busy=false;
    async function run(action,note,owner){
      if(busy)return;busy=true;
      try{
        await beforeAction?.(action,note);
        if(action==='rename')await onRename(note,owner);
        else if(action==='options')await onOptions(note,owner);
        else if(action==='note-icon')JotNoteIcons.open(note,{owner});
        else if(action==='workspace-pin')await request('workspace-pin',!JotWorkspace.pinned);
        else if(action==='tab-pin')await onAction('tab-pin',{id:note.id,pinned:!note.tabPinned});
        else if(action==='tab-reopen')await onAction('tab-reopen');
        else if(action==='open-window')await request('open-note',note.id);
        else if(action==='copy'){
          if(await request('library-copy',note.id)!==true)throw new Error('Copy was interrupted. Try again.');
        }else if(action==='export')await request('library-export',note.id);
        else if(action==='close')await onAction('tab-close',note.id);
        else if(action==='delete')await onDelete(note,owner);
      }finally{busy=false;}
    }
    function open(note,x,y,owner){
      if(busy)return;
      JotMenus.open({x,y,owner,restore:()=>owner?.isConnected&&owner.focus({preventScroll:true}),error:onError,items:[
        {id:'rename',label:'Rename',icon:'pencil'},
        {id:'note-icon',label:'Note emoji',icon:'notepad-text'},
        {id:'options',label:'More settings',icon:'ellipsis'},
        {id:'tab-pin',label:note.tabPinned?'Unpin tab':'Pin tab',icon:'pin'},
        {id:'workspace-pin',label:JotWorkspace.pinned?'Stop keeping on top':'Keep Jot on top',icon:'pin'},
        {separator:true},{id:'open-window',label:'Open in window',icon:'external-link'},
        {separator:true},{id:'copy',label:'Copy',icon:'copy',pending:'Copying…'},
        {id:'export',label:'Export',icon:'download',pending:'Exporting…'},
        {separator:true},{id:'close',label:'Close tab',icon:'window-close'},
        {id:'tab-reopen',label:'Reopen closed tab',icon:'undo-2',shortcut:window.JotShortcutBindings?.label('reopen-tab'),disabled:!JotWorkspace.canReopenTab},
        {id:'delete',label:'Move to Trash',icon:'trash-2'}
      ],run:action=>run(action,note,owner)});
    }
    function rename(note,owner){
      JotMenus.close();
      run('rename',note,owner).catch(error=>{
        JotBridge.reportError(error,'tab-rename');onError?.(error);
      });
    }
    return {open,rename};
  }
  return {create};
})();

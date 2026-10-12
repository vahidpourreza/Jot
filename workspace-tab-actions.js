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
        else if(action==='tab-pin')await onAction('tab-pin',{id:note.id,pinned:!note.tabPinned});
        else if(action==='open-window')await request('open-note',note.id);
        else if(action==='copy'){
          if(await request('library-copy',note.id)!==true)throw new Error('Copy was interrupted. Try again.');
        }else if(action==='export')await request('library-export',note.id);
        else if(action==='delete')await onDelete(note,owner);
      }finally{busy=false;}
    }
    function open(note,x,y,owner){
      if(busy)return;
      JotMenus.open({x,y,owner,restore:()=>owner?.isConnected&&owner.focus({preventScroll:true}),error:onError,items:[
        {id:'options',label:'More settings',icon:'ellipsis'},
        {id:'tab-pin',label:note.tabPinned?'Unpin tab':'Pin tab',icon:'pin'},
        {separator:true},{id:'open-window',label:'Open in window',icon:'external-link'},
        {separator:true},{id:'copy',label:'Copy',icon:'copy',pending:'Copying…'},
        {id:'export',label:'Export',icon:'download',pending:'Exporting…'},
        {separator:true},{id:'delete',label:'Move to Trash',icon:'trash-2'}
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

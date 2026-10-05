'use strict';
window.JotTabSwitcher=(()=>{
  function create({snapshot,onSwitch}){
    let recent=[],lastActive=null,gesture=null,pending=false,resumeFocus=null;
    const overlay=document.createElement('div');overlay.id='tabSwitcher';overlay.className='tab-switcher';overlay.hidden=true;
    const panel=document.createElement('section');panel.className='tab-switcher-panel';panel.setAttribute('role','dialog');panel.setAttribute('aria-modal','true');panel.setAttribute('aria-labelledby','tabSwitcherTitle');
    const header=document.createElement('div');header.className='tab-switcher-header';
    const title=document.createElement('h2');title.id='tabSwitcherTitle';title.textContent='Switch tabs';
    const count=document.createElement('span');count.className='tab-switcher-count';header.append(title,count);
    const list=document.createElement('div');list.id='tabSwitcherList';list.className='tab-switcher-list';list.tabIndex=-1;list.setAttribute('role','listbox');list.setAttribute('aria-label','Recently used tabs');list.setAttribute('aria-describedby','tabSwitcherHint');
    const hint=document.createElement('p');hint.id='tabSwitcherHint';hint.className='tab-switcher-hint';panel.append(header,list,hint);overlay.append(panel);document.body.append(overlay);
    const modalObserver=new MutationObserver(()=>{if(gesture&&document.querySelector('dialog[open]'))cancel(false);});

    function entries(state=snapshot()){
      return [{id:'home',title:'Home',kind:'home'},...state.tabs.map((note,index)=>({id:note.id,title:note.title||note.legacyTitle||(note.plain||'').split(/\n/).find(line=>line.trim())?.trim().slice(0,50)||note.noteFile?.path?.split(/[\\/]/).pop()||'Note '+(index+1),kind:'note',note})),...(state.settingsOpen?[{id:'settings',title:'Settings',kind:'settings'}]:[])];
    }
    function sync(){
      const state=snapshot(),available=entries(state),ids=available.map(item=>item.id),valid=new Set(ids);
      recent=recent.filter(id=>valid.has(id));for(const id of ids)if(!recent.includes(id))recent.push(id);
      if(lastActive!==state.activeId){recent=[state.activeId,...recent.filter(id=>id!==state.activeId)];lastActive=state.activeId;resumeFocus=null;}
      if(!gesture)return;
      // External tab changes must never activate a now-closed or transferred note.
      if(state.busy||state.activeId!==gesture.origin||document.querySelector('dialog[open]')){cancel(false);return;}
      const selected=gesture.ids[gesture.index];gesture.ids=gesture.ids.filter(id=>valid.has(id));
      if(gesture.ids.length<2||!gesture.ids.includes(selected)){cancel();return;}
      gesture.index=gesture.ids.indexOf(selected);render(available);
    }
    function focusSnapshot(){
      const owner=document.activeElement,selection=getSelection();
      return {owner,start:owner?.selectionStart,end:owner?.selectionEnd,direction:owner?.selectionDirection,
        anchor:selection?.anchorNode,anchorOffset:selection?.anchorOffset,focus:selection?.focusNode,focusOffset:selection?.focusOffset};
    }
    function restore(saved){
      if(!saved?.owner?.isConnected||saved.owner.closest('[hidden],[inert]'))return;
      saved.owner.focus({preventScroll:true});
      if(typeof saved.start==='number')saved.owner.setSelectionRange(saved.start,saved.end,saved.direction);
      else if(saved.anchor?.isConnected&&saved.focus?.isConnected){try{getSelection().setBaseAndExtent(saved.anchor,saved.anchorOffset,saved.focus,saved.focusOffset);}catch{}}
    }
    function close(restoreFocus){
      if(!gesture)return null;
      const previous=gesture;gesture=null;overlay.hidden=true;list.removeAttribute('aria-activedescendant');modalObserver.disconnect();
      if(previous.views)previous.views.inert=previous.inert;
      if(restoreFocus)restore(previous.focus);
      return previous;
    }
    function cancel(restoreFocus=true){resumeFocus=null;close(restoreFocus);}
    function deactivate(){const previous=close(false);if(previous)resumeFocus=previous;}
    function reactivate(){
      const previous=resumeFocus;resumeFocus=null;
      if(previous&&previous.origin===snapshot().activeId&&!gesture&&!pending&&!document.querySelector('dialog[open]')&&[document.body,list].includes(document.activeElement))restore(previous.focus);
    }
    function commit(){
      if(!gesture)return;
      const state=snapshot(),id=gesture.ids[gesture.index];
      if(state.busy||document.querySelector('dialog[open]')||!entries(state).some(item=>item.id===id)){cancel(false);return;}
      const previous=close(false);
      if(id===state.activeId){restore(previous.focus);return;}
      pending=true;
      // Use the normal host transaction: it flushes the current draft and only
      // publishes the new active view after the destination is ready.
      Promise.resolve().then(()=>onSwitch(id)).catch(()=>restore(previous.focus)).finally(()=>{pending=false;});
    }
    function render(available=entries()){
      if(!gesture)return;
      const records=new Map(available.map(item=>[item.id,item]));list.replaceChildren();
      gesture.ids.forEach((id,index)=>{
        const entry=records.get(id);if(!entry)return;
        const option=document.createElement('div');option.id='tabSwitcherOption'+index;option.className='tab-switcher-option';option.dataset.workspaceId=id;option.setAttribute('role','option');option.setAttribute('aria-selected',String(index===gesture.index));
        const marker=document.createElement('span');marker.className='tab-switcher-marker';marker.setAttribute('aria-hidden','true');
        marker.append(entry.kind==='note'?window.JotNoteIcons?.render(entry.note.icon)||JotDesign.icon('notepad-text'):JotDesign.icon(entry.kind==='home'?'jot':'settings'));
        if(entry.note)option.style.setProperty('--switcher-accent',JotDesign.noteColor(entry.note.color||'neutral').primary);
        const copy=document.createElement('span');copy.className='tab-switcher-copy';
        const name=document.createElement('span');name.className='tab-switcher-title';name.dir='auto';name.textContent=entry.title;
        const detail=document.createElement('span');detail.className='tab-switcher-detail';detail.dir='auto';
        const file=entry.note?.noteFile?.path?.split(/[\\/]/).pop();detail.textContent=entry.kind==='home'?'Notes and folders':entry.kind==='settings'?'App preferences':file||'Note';
        copy.append(name,detail);option.append(marker,copy);option.title=entry.title+(file?' · '+file:'');
        if(entry.note?.tabPinned){const pin=JotDesign.icon('pin');pin.classList.add('tab-switcher-pin');pin.setAttribute('aria-hidden','true');option.append(pin);}
        if(id===gesture.origin){const current=document.createElement('span');current.className='tab-switcher-current';current.textContent='Current';option.append(current);}
        option.setAttribute('aria-label',entry.title+(entry.note?.tabPinned?', pinned tab':'')+(id===gesture.origin?', current tab':''));
        option.onpointerdown=event=>event.preventDefault();option.onclick=()=>{if(gesture){gesture.index=index;commit();}};list.append(option);
      });
      count.textContent=gesture.ids.length+' open';
      hint.textContent=(gesture.modifier?'Release '+(gesture.modifier==='Control'?'Ctrl':'Alt'):'Press Enter')+' to switch · Esc to cancel';
      const selected=list.children[gesture.index];if(selected){list.setAttribute('aria-activedescendant',selected.id);selected.scrollIntoView({block:'nearest'});}
    }
    function step(direction){gesture.index=(gesture.index+direction+gesture.ids.length)%gesture.ids.length;render();}
    function start(event,direction){
      sync();resumeFocus=null;const state=snapshot();
      if(pending||state.busy||document.getElementById('workspaceTabs')?.dataset.dragging)return;
      if(recent.length<2)return;
      JotMenus.close();window.JotEditorMenu?.close();window.JotWritingTools?.hide();window.JotWorkspaceTabs?.dismissPicker(document.getElementById('workspaceTabs'));
      const views=document.getElementById('workspaceViews');
      gesture={ids:[...recent],index:0,origin:state.activeId,modifier:event.ctrlKey?'Control':event.altKey?'Alt':null,focus:focusSnapshot(),views,inert:views?.inert};
      if(views)views.inert=true;
      overlay.hidden=false;step(direction);list.focus({preventScroll:true});
      modalObserver.observe(document.body,{subtree:true,attributes:true,attributeFilter:['open']});
    }
    function take(event){event.preventDefault();event.stopImmediatePropagation();return true;}
    function handleKeyDown(event,command){
      if(event.isComposing||event.keyCode===229||event.getModifierState?.('AltGraph')){if(gesture)cancel();return false;}
      if(document.querySelector('dialog[open]')){if(gesture)cancel(false);return false;}
      if(command==='next-tab'||command==='previous-tab'){
        const direction=command==='next-tab'?1:-1;
        if(gesture)step(direction);else start(event,direction);
        return take(event);
      }
      if(!gesture)return false;
      if(event.key==='Escape'){cancel();return take(event);}
      if(event.key==='Enter'){commit();return take(event);}
      if(['ArrowDown','ArrowRight','ArrowUp','ArrowLeft'].includes(event.key)){step(['ArrowDown','ArrowRight'].includes(event.key)?1:-1);return take(event);}
      if(event.key==='Home'||event.key==='End'){gesture.index=event.key==='Home'?0:gesture.ids.length-1;render();return take(event);}
      if(['Control','Shift','Alt','Meta'].includes(event.key))return false;
      if(event.key==='Tab'){step(event.shiftKey?-1:1);return take(event);}
      // Leave unrelated shortcuts working, but first return focus/selection to
      // the note they act on. Bare typing dismisses without accidental input.
      cancel();return command||event.ctrlKey||event.altKey||event.metaKey?false:take(event);
    }
    window.addEventListener('keydown',event=>{if(gesture)handleKeyDown(event,window.JotShortcutBindings?.match(event,'app'));},true);
    window.addEventListener('keyup',event=>{
      if(!gesture||!gesture.modifier)return;
      const held=gesture.modifier==='Control'?event.ctrlKey:event.altKey;
      if(event.key===gesture.modifier||!held){take(event);commit();}
    },true);
    document.addEventListener('pointerdown',event=>{if(gesture&&!panel.contains(event.target))cancel();},true);
    window.addEventListener('blur',deactivate);window.addEventListener('focus',reactivate);
    document.addEventListener('visibilitychange',()=>{if(document.hidden)deactivate();});
    window.addEventListener('jot-shortcuts-changed',()=>cancel());
    JotBridge.on(data=>{if(data.event==='active-window'){if(data.active)reactivate();else deactivate();}else if(['prepare-quit','flush'].includes(data.event))cancel(false);});
    return {sync,cancel,handleKeyDown,get open(){return !!gesture;},get busy(){return pending;},get selectedId(){return gesture?.ids[gesture.index]||null;},get order(){return gesture?[...gesture.ids]:[...recent];}};
  }
  return {create};
})();

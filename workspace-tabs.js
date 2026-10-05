'use strict';
window.JotWorkspaceTabs=(()=>{
  const roots=new WeakMap();
  function title(note,index){return note.title||note.legacyTitle||(note.plain||'').split(/\n/).find(line=>line.trim())?.trim().slice(0,50)||note.noteFile?.path?.split(/[\\/]/).pop()||'Note '+(index+1);}
  function reveal(button){
    const list=button?.closest('.workspace-note-tabs,.workspace-pinned-tabs');if(!list)return;
    stopScroll(roots.get(list.closest('.workspace-tabs')));
    const item=button.closest('.workspace-tab').getBoundingClientRect(),viewport=list.getBoundingClientRect();
    if(item.left<viewport.left)list.scrollLeft-=viewport.left-item.left;
    else if(item.right>viewport.right)list.scrollLeft+=item.right-viewport.right;
  }
  function stopScroll(state){
    if(!state)return;
    cancelAnimationFrame(state.scrollFrame);state.scrollFrame=null;state.scrollTarget=null;delete state.list.dataset.scrolling;
  }
  function scrollTabs(state,direction){
    const list=state.list,start=list.scrollLeft;
    const target=Math.max(0,Math.min(list.scrollWidth-list.clientWidth,(state.scrollTarget??start)+direction*Math.max(96,list.clientWidth-40)));
    stopScroll(state);
    if(matchMedia('(prefers-reduced-motion: reduce)').matches){list.scrollLeft=target;scrollState(state);return;}
    state.scrollTarget=target;list.dataset.scrolling='true';const started=performance.now();
    const frame=now=>{
      const progress=Math.min(1,(now-started)/140),eased=1-Math.pow(1-progress,3);
      list.scrollLeft=start+(target-start)*eased;scrollState(state);
      if(progress<1)state.scrollFrame=requestAnimationFrame(frame);else{list.scrollLeft=target;stopScroll(state);scrollState(state);}
    };
    state.scrollFrame=requestAnimationFrame(frame);
  }
  function closePicker(state,restore=false){
    if(state.picker.hidden)return;
    state.picker.hidden=true;state.all.setAttribute('aria-expanded','false');
    if(restore)(state.all.hidden?state.home.button:state.all).focus({preventScroll:true});
  }
  function updatePicker(state){
    for(const button of state.picker.children){
      const id=button.dataset.workspaceId,record=id==='home'?state.home:state.tabs.get(id);
      if(!record){closePicker(state,true);return;}
      button.querySelector('.tab-title').textContent=record.label;button.title=record.label;
      button.ariaLabel=record.label+(record.note?.tabPinned?', pinned tab':'');button.dataset.pinned=String(!!record.note?.tabPinned);
      button.setAttribute('aria-checked',String(record.item.dataset.active==='true'));
      if(record.note)button.style.setProperty('--tab-accent',JotDesign.noteColor(record.note.color||'neutral').primary);
    }
  }
  function openPicker(root,state,edge){
    if(state.busy||state.all.hidden)return;
    JotMenus.close();window.JotEditorMenu?.close();
    state.picker.replaceChildren();
    for(const record of [state.home,...[...state.pinnedList.children,...state.list.children].map(item=>state.tabs.get(item.querySelector('[role=tab]').dataset.workspaceId))]){
      const button=document.createElement('button');button.type='button';button.setAttribute('role','menuitemradio');button.tabIndex=-1;button.dataset.workspaceId=record.button.dataset.workspaceId;
      const system=record.button.dataset.workspaceId==='settings';
      const marker=record===state.home?JotDesign.icon('jot'):system?JotDesign.icon('settings'):window.JotNoteIcons?.render(record.note?.icon)||JotDesign.icon('notepad-text');
      if(record!==state.home&&!system)marker.classList.add('tab-marker');marker.setAttribute('aria-hidden','true');
      const label=document.createElement('span');label.className='tab-title';label.dir='auto';
      const check=JotDesign.icon('check');check.classList.add('tab-check');button.append(marker,label,check);
      button.onclick=()=>{
        const id=button.dataset.workspaceId;closePicker(state);
        record.button.focus({preventScroll:true});reveal(record.button);scrollState(state);action(state,'tab-switch',id);
      };
      state.picker.append(button);
    }
    updatePicker(state);state.picker.hidden=false;state.all.setAttribute('aria-expanded','true');
    const anchor=state.all.getBoundingClientRect();
    state.picker.style.top=(anchor.bottom+5)+'px';state.picker.style.maxHeight=Math.max(60,innerHeight-anchor.bottom-13)+'px';
    state.picker.style.left=Math.max(6,Math.min(anchor.right-state.picker.offsetWidth,innerWidth-state.picker.offsetWidth-6))+'px';
    const selected=edge==='first'?state.picker.firstElementChild:edge==='last'?state.picker.lastElementChild:state.picker.querySelector('[aria-checked=true]');
    selected?.focus({preventScroll:true});selected?.scrollIntoView({block:'nearest'});
  }
  function scrollState(state){
    const maximum=state.list.scrollWidth-state.list.clientWidth;
    state.previous.setAttribute('aria-disabled',String(state.list.scrollLeft<=1));
    state.next.setAttribute('aria-disabled',String(state.list.scrollLeft>=maximum-1));
  }
  function overflow(root,state){
    // Include the space recovered by hiding the controls. Measuring only the
    // narrowed viewport would leave overflow buttons stuck on after a resize.
    const controls=[state.previous,state.next,state.all],gap=parseFloat(getComputedStyle(root).columnGap)||0;
    const recovered=controls.reduce((width,button)=>width+(!button.hidden&&button.getClientRects().length?button.getBoundingClientRect().width+gap:0),0);
    const needed=state.list.scrollWidth>state.list.clientWidth+recovered+1||state.pinnedList.scrollWidth>state.pinnedList.clientWidth+1;
    root.dataset.overflow=String(needed);
    for(const button of controls)button.hidden=!needed;
    if(!needed)closePicker(state,true);
    scrollState(state);
  }
  function action(state,name,id){
    if(state.busy)return;
    state.requestedAction=name;state.requestedId=id||'new';
    // The workspace action reports its own failure. Do not turn an event
    // handler's rejected promise into a second unhandled-error notification.
    Promise.resolve(state.onAction(name,id)).catch(()=>{});
  }
  function menu(state,record,x,y){
    if(state.busy||!record.note||!state.onMenu)return;
    state.onMenu(record.note,x,y,record.button);
  }
  function cancelMotion(state){
    for(const animation of state.motion){animation.onfinish=animation.oncancel=null;animation.cancel();}
    state.motion.clear();
  }
  function endDrag(state,commit=false){
    const drag=state.drag;if(!drag)return;state.drag=null;cancelAnimationFrame(drag.frame);
    document.removeEventListener('pointermove',drag.move,true);document.removeEventListener('pointerup',drag.up,true);
    document.removeEventListener('pointercancel',drag.cancel,true);document.removeEventListener('keydown',drag.key,true);
    state.root.removeEventListener('lostpointercapture',drag.cancel);
    if(state.root.hasPointerCapture(drag.pointerId))state.root.releasePointerCapture(drag.pointerId);
    if(!drag.started)return;
    state.suppressClickUntil=performance.now()+300;
    // Reorder once, on drop. During the gesture the DOM and focus never move;
    // sibling transforms make a real-sized opening under the dragged tab.
    const visual=commit?new Map(drag.items.map(item=>[item,item.getBoundingClientRect().left])):null;
    if(commit){const next=drag.order[drag.order.indexOf(drag.record.item)+1]||drag.container.querySelector('[data-kind=settings]');drag.container.insertBefore(drag.record.item,next);}
    delete drag.record.item.dataset.dragging;delete state.root.dataset.dragging;
    for(const item of drag.items){item.style.removeProperty('transform');item.style.removeProperty('will-change');}
    // A cancelled gesture relinquishes input and visual state immediately.
    // In particular, do not start another animation after Escape, focus loss
    // or capture loss; only a committed drop gets a settling transition.
    if(!commit){cancelMotion(state);return;}
    const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
    for(const item of drag.items){
      const offset=visual.get(item)-item.getBoundingClientRect().left;
      if(!reduced&&Math.abs(offset)>.5){
        const animation=item.animate([{transform:`translateX(${offset}px)`},{transform:'translateX(0)'}],{duration:150,easing:'cubic-bezier(.2,.8,.2,1)'});
        state.motion.add(animation);animation.onfinish=animation.oncancel=()=>state.motion.delete(animation);
      }
    }
    const ids=[...state.pinnedList.children,...state.list.children].map(item=>item.querySelector('[role=tab]').dataset.workspaceId).filter(id=>id!=='settings');
    if(ids.join()!==drag.ids.join())action(state,'tab-reorder',{ids});
  }
  function startDrag(state,record,event){
    if(state.busy||event.button!==0||event.isPrimary===false||!record.note)return;
    endDrag(state);cancelMotion(state);closePicker(state);stopScroll(state);
    const container=record.item.parentElement;
    const drag={record,container,pointerId:event.pointerId,startX:event.clientX,startY:event.clientY,x:event.clientX,started:false,
      items:[...container.children].filter(item=>item.dataset.kind==='note'),ids:[...state.pinnedList.children,...state.list.children].map(item=>item.querySelector('[role=tab]').dataset.workspaceId).filter(id=>id!=='settings')};
    function position(){
      const source=drag.metrics.get(record.item),delta=drag.x-drag.startX+container.scrollLeft-drag.startScroll;
      const left=Math.max(drag.firstLeft,Math.min(drag.lastRight-source.width,source.left+delta));
      record.item.style.transform=`translateX(${left-source.left}px)`;
      const others=drag.items.filter(item=>item!==record.item),center=left+source.width/2;
      let index=others.findIndex(item=>{const rect=drag.metrics.get(item),middle=rect.left+rect.width/2;return delta<0?center<=middle:center<middle;});if(index<0)index=others.length;
      if(index===drag.index)return;drag.index=index;drag.order=[...others];drag.order.splice(index,0,record.item);
      let target=drag.firstLeft;
      for(const item of drag.order){const rect=drag.metrics.get(item);if(item!==record.item)item.style.transform=`translateX(${target-rect.left}px)`;target+=rect.width+drag.gap;}
    }
    function frame(now){
      drag.frame=null;
      if(state.drag!==drag||!drag.started)return;
      const rect=drag.viewport,elapsed=Math.min(32,now-(drag.lastFrame??now-16.67));drag.lastFrame=now;
      const speed=drag.x<rect.left+28?-Math.min(720,(rect.left+28-drag.x)*24):drag.x>rect.right-28?Math.min(720,(drag.x-rect.right+28)*24):0;
      const before=container.scrollLeft;if(speed)container.scrollLeft+=speed*elapsed/1000;
      position();if(container===state.list)scrollState(state);
      if(container.scrollLeft!==before)drag.frame=requestAnimationFrame(frame);
    }
    drag.move=e=>{
      if(e.pointerId!==drag.pointerId)return;
      if(!(e.buttons&1)){endDrag(state);return;}
      drag.x=e.clientX;
      if(!drag.started){
        if(Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)<6)return;
        drag.started=true;drag.viewport=container.getBoundingClientRect();drag.startScroll=container.scrollLeft;
        drag.gap=parseFloat(getComputedStyle(container).columnGap)||0;
        drag.metrics=new Map(drag.items.map(item=>{const rect=item.getBoundingClientRect();return [item,{left:rect.left-drag.viewport.left+drag.startScroll,width:rect.width}];}));
        drag.firstLeft=drag.metrics.get(drag.items[0]).left;const last=drag.metrics.get(drag.items.at(-1));drag.lastRight=last.left+last.width;
        drag.order=[...drag.items];drag.index=drag.items.indexOf(record.item);
        for(const item of drag.items)item.style.willChange='transform';
        record.item.dataset.dragging='true';state.root.dataset.dragging='true';
        state.root.setPointerCapture(drag.pointerId);
      }
      e.preventDefault();e.stopPropagation();if(!drag.frame)drag.frame=requestAnimationFrame(frame);
    };
    drag.up=e=>{if(e.pointerId!==drag.pointerId)return;if(drag.started){e.preventDefault();e.stopPropagation();drag.x=e.clientX;position();}endDrag(state,true);};
    drag.cancel=e=>{if(e?.pointerId!==undefined&&e.pointerId!==drag.pointerId)return;if(e?.type==='lostpointercapture'&&e.target!==state.root)return;endDrag(state);};
    drag.key=e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();endDrag(state);}};
    state.drag=drag;document.addEventListener('pointermove',drag.move,true);document.addEventListener('pointerup',drag.up,true);
    document.addEventListener('pointercancel',drag.cancel,true);document.addEventListener('keydown',drag.key,true);state.root.addEventListener('lostpointercapture',drag.cancel);
  }
  function createTab(root,state,id){
    const item=document.createElement('div');item.className='workspace-tab';item.dataset.home=String(id==='home');item.dataset.kind=id==='home'||id==='settings'?id:'note';item.setAttribute('role','presentation');
    const button=document.createElement('button');button.type='button';button.setAttribute('role','tab');button.dataset.workspaceId=id;
    let marker;
    if(id==='home'){marker=JotDesign.icon('jot');marker.classList.add('library-logo');marker.alt='Jot';marker.draggable=false;}
    else if(id==='settings'){marker=JotDesign.icon('settings');marker.classList.add('tab-system-icon');}
    else{marker=JotDesign.icon('notepad-text');marker.classList.add('tab-marker');}
    marker.setAttribute('aria-hidden','true');button.append(marker);
    const text=document.createElement('span');text.className='tab-title';text.dir='auto';button.append(text);
    const record={item,button,text,close:null,label:null,note:null};
    if(id!=='home'&&id!=='settings'){record.pin=JotDesign.icon('pin');record.pin.classList.add('tab-pin-marker');record.pin.setAttribute('aria-hidden','true');record.pin.setAttribute('hidden','');button.append(record.pin);}
    if(id!=='home'&&id!=='settings'){record.fileStatus=document.createElement('span');record.fileStatus.className='tab-file-status';record.fileStatus.hidden=true;record.fileStatus.ariaHidden='true';button.append(record.fileStatus);}
    button.onclick=event=>{if(event.detail<2&&performance.now()>(state.suppressClickUntil||0))action(state,'tab-switch',id);};
    if(id==='home')item.oncontextmenu=event=>{event.preventDefault();event.stopPropagation();state.onHomeMenu?.(button,event.clientX,event.clientY);};
    if(id!=='home'&&id!=='settings'){
      button.setAttribute('aria-haspopup','menu');
      button.onpointerdown=event=>startDrag(state,record,event);button.ondragstart=event=>event.preventDefault();
      button.ondblclick=event=>{event.preventDefault();event.stopPropagation();if(record.note&&performance.now()>(state.suppressClickUntil||0))state.onRename?.(record.note,button);};
      item.oncontextmenu=event=>{event.preventDefault();event.stopPropagation();menu(state,record,event.clientX,event.clientY);};
    }
    button.onkeydown=event=>{
      if(event.key==='ContextMenu'||event.shiftKey&&event.key==='F10'){
        event.preventDefault();event.stopPropagation();const rect=button.getBoundingClientRect();
        if(id==='home')state.onHomeMenu?.(button,rect.left,rect.bottom);else menu(state,record,rect.left+8,rect.bottom);
      }else if(id!=='home'&&event.key==='F2'){
        event.preventDefault();event.stopPropagation();if(record.note)state.onRename?.(record.note,button);
      }else if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){
        event.preventDefault();const buttons=[...root.querySelectorAll('[role=tab]')],index=buttons.indexOf(button);
        const next=event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowRight'?1:-1)+buttons.length)%buttons.length;
        buttons[next]?.focus({preventScroll:true});reveal(buttons[next]);
      }else if(event.key==='Delete'&&id!=='home'){event.preventDefault();action(state,'tab-close',id);}
    };
    item.append(button);
    let close;
    if(id!=='home'){
      close=document.createElement('button');close.type='button';close.className='tab-close';close.dataset.workspaceId=id;close.append(JotDesign.icon('window-close'));
      close.onclick=()=>action(state,'tab-close',id);item.append(close);record.close=close;
      item.onmousedown=event=>{if(event.button===1)event.preventDefault();};
      item.onauxclick=event=>{if(event.button===1){event.preventDefault();action(state,'tab-close',id);}};
    }
    return record;
  }
  function initialize(root){
    const state={root,tabs:new Map(),motion:new Set(),busy:false,onAction:null,pendingTimer:null,pendingElement:null,pendingKey:null,initialized:false};
    root.classList.add('workspace-tabs');root.setAttribute('role','tablist');root.setAttribute('aria-label','Workspace tabs');
    state.home=createTab(root,state,'home');
    const pinnedList=document.createElement('div');pinnedList.className='workspace-pinned-tabs';pinnedList.setAttribute('role','presentation');state.pinnedList=pinnedList;
    pinnedList.addEventListener('wheel',event=>{if(Math.abs(event.deltaY)<=Math.abs(event.deltaX)||pinnedList.scrollWidth<=pinnedList.clientWidth)return;event.preventDefault();pinnedList.scrollLeft+=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?pinnedList.clientWidth:1);},{passive:false});
    const list=document.createElement('div');list.className='workspace-note-tabs';list.setAttribute('role','presentation');state.list=list;
    list.addEventListener('scroll',()=>scrollState(state),{passive:true});
    list.addEventListener('wheel',event=>{
      stopScroll(state);
      if(Math.abs(event.deltaY)<=Math.abs(event.deltaX)||list.scrollWidth<=list.clientWidth)return;
      const delta=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?list.clientWidth:1);
      if(delta<0&&list.scrollLeft<=0||delta>0&&list.scrollLeft>=list.scrollWidth-list.clientWidth)return;
      event.preventDefault();list.scrollLeft+=delta;
    },{passive:false});
    const add=document.createElement('button');add.type='button';add.className='icon-button workspace-add-tab';add.dataset.workspaceId='new';add.title=add.ariaLabel='New tab';add.append(JotDesign.icon('plus'));add.onclick=()=>action(state,'new-tab');state.add=add;
    function overflowButton(name,label,icon){
      const button=document.createElement('button');button.type='button';button.className='icon-button workspace-tab-'+name;button.title=button.ariaLabel=label;button.append(JotDesign.icon(icon));button.hidden=true;return button;
    }
    state.previous=overflowButton('previous','Scroll tabs left','chevron-left');
    state.next=overflowButton('next','Scroll tabs right','chevron-left');
    for(const [button,direction] of [[state.previous,-1],[state.next,1]])button.onclick=()=>{
      if(button.getAttribute('aria-disabled')==='true')return;
      scrollTabs(state,direction);
    };
    state.all=overflowButton('all','All tabs','list');state.all.setAttribute('aria-haspopup','menu');state.all.setAttribute('aria-expanded','false');
    state.picker=document.createElement('div');state.picker.id=(root.id||'workspaceTabs')+'Menu';state.picker.className='workspace-tabs-menu';state.picker.setAttribute('role','menu');state.picker.setAttribute('aria-label','All tabs');state.picker.hidden=true;document.body.append(state.picker);
    state.all.setAttribute('aria-controls',state.picker.id);
    state.all.onclick=()=>state.picker.hidden?openPicker(root,state):closePicker(state,true);
    state.all.onkeydown=event=>{if(['ArrowDown','ArrowUp'].includes(event.key)){event.preventDefault();openPicker(root,state,event.key==='ArrowUp'?'last':'first');}};
    state.picker.onkeydown=event=>{
      if(event.isComposing)return;
      if(['Escape','Tab'].includes(event.key)){if(event.key==='Escape')event.preventDefault();event.stopPropagation();closePicker(state,true);return;}
      if(!['ArrowDown','ArrowUp','Home','End'].includes(event.key))return;
      event.preventDefault();event.stopPropagation();
      const buttons=[...state.picker.children],index=buttons.indexOf(document.activeElement);
      const next=event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;
      buttons[next]?.focus({preventScroll:true});buttons[next]?.scrollIntoView({block:'nearest'});
    };
    document.addEventListener('pointerdown',event=>{if(!state.picker.contains(event.target)&&!state.all.contains(event.target))closePicker(state);},true);
    document.addEventListener('contextmenu',()=>closePicker(state),true);
    document.addEventListener('focusin',event=>{if(!state.picker.contains(event.target)&&event.target!==state.all)closePicker(state);});
    window.addEventListener('blur',()=>{closePicker(state);endDrag(state);});
    window.addEventListener('resize',()=>{closePicker(state,true);endDrag(state);});
    JotBridge.on(data=>{if(data.event==='active-window'&&!data.active){closePicker(state);endDrag(state);}});
    root.oncontextmenu=event=>{
      if(event.defaultPrevented||event.target.closest('.workspace-tab'))return;
      event.preventDefault();event.stopPropagation();
      JotMenus.open({x:event.clientX,y:event.clientY,owner:state.add,items:[{id:'tab-reopen',label:'Reopen closed tab',icon:'undo-2',shortcut:window.JotShortcutBindings?.label('reopen-tab'),disabled:!state.canReopenTab}],run:()=>action(state,'tab-reopen')});
    };
    root.replaceChildren(state.home.item,pinnedList,state.previous,list,state.next,add,state.all);
    state.viewportWidth=root.getBoundingClientRect().width;
    state.resizeObserver=new ResizeObserver(entries=>{
      const width=entries[0].contentRect.width,changed=Math.abs(width-state.viewportWidth)>.5;
      state.viewportWidth=width;
      // Refit the selected tab when the window changes size, without resetting
      // deliberate scrolling during title updates, saves, or ordinary renders.
      if(changed&&width>0&&root.isConnected){overflow(root,state);reveal(state.tabs.get(root.dataset.activeId)?.button);scrollState(state);}
    });
    state.resizeObserver.observe(root);
    roots.set(root,state);return state;
  }
  function updateTab(record,{id,label,note,activeId,busy,beforeActive}){
    record.note=note||null;
    record.item.dataset.active=String(id===activeId);record.item.dataset.beforeActive=String(beforeActive);
    record.item.dataset.pinned=String(!!note?.tabPinned);
    record.button.setAttribute('aria-selected',String(id===activeId));record.button.setAttribute('aria-disabled',String(busy));record.button.tabIndex=id===activeId?0:-1;
    if(note){const accent=JotDesign.noteColor(note.color||'neutral');record.item.style.setProperty('--tab-accent',accent.primary);record.item.style.setProperty('--tab-accent-foreground',accent.foreground);}
    if(note&&window.JotNoteIcons&&record.icon!==note.icon){const icon=JotNoteIcons.render(note.icon);icon.classList.add('tab-marker');record.button.querySelector('.tab-marker').replaceWith(icon);record.icon=note.icon;}
    if(record.label!==label){
      record.label=label;record.button.title=record.button.ariaLabel=label;record.text.textContent=id==='home'?'':label;
      if(window.JotBidi)JotBidi.normalize(record.text,new Set([record.text]),'ltr');
      if(record.close)record.close.title=record.close.ariaLabel='Close '+label;
    }
    record.text.hidden=id==='home';
    if(record.pin)record.pin.toggleAttribute('hidden',!note?.tabPinned);
    if(note?.tabPinned){record.button.title='Pinned tab · '+label;record.button.ariaLabel=label+', pinned tab';}
    if(record.close)record.close.setAttribute('aria-disabled',String(busy));
    if(note&&record.fileStatus){
      const file=window.JotNoteFiles?.state(note),dirty=window.JotNoteFiles?.isDirty(note)??note.fileDirty;
      record.fileStatus.hidden=!note.noteFile?.path||!dirty;record.item.dataset.fileError=String(!!file?.error);
      const saveKey=window.JotShortcutBindings?.label('file-save');
      record.button.title=(note.tabPinned?'Pinned tab · ':'')+label+(note.noteFile?.path?'\n'+note.noteFile.path:'')+(file?.error?'\nAuto-save paused: '+file.error:dirty?'\nUnsaved file changes'+(saveKey?' · '+saveKey:''):'');
      record.button.ariaLabel=label+(note.tabPinned?', pinned tab':'')+(dirty?', unsaved file changes':'');
    }
  }
  function pending(root,state,id,name){
    const key=state.busy?name+':'+id:null;
    if(state.pendingKey===key)return;
    clearTimeout(state.pendingTimer);state.pendingTimer=null;state.pendingKey=key;
    if(state.pendingElement){delete state.pendingElement.dataset.pending;state.pendingElement.removeAttribute('aria-busy');state.pendingElement=null;}
    if(!state.busy)return;
    const target=id==='new'?state.add:(id==='home'?state.home:state.tabs.get(id))?.item||state.add;
    target.setAttribute('aria-busy','true');state.pendingElement=target;
    // A normal switch should feel immediate; only slow saves earn a progress mark.
    state.pendingTimer=setTimeout(()=>{if(root.isConnected&&state.pendingKey===key)target.dataset.pending='true';},200);
  }
  function render(root,{tabs=[],settingsOpen=false,canReopenTab=false,activeId='home',busy=false,pendingId,pendingAction,onAction,onMenu,onRename,onHomeMenu}){
    const state=roots.get(root)||initialize(root),focused=document.activeElement;
    const hadFocus=root.contains(focused),activeChanged=root.dataset.activeId!==activeId;
    const allNotes=tabs.filter(note=>note.id!=='home'&&note.id!=='settings');
    const notes=[...allNotes.filter(note=>note.tabPinned),...allNotes.filter(note=>!note.tabPinned)];if(settingsOpen)notes.push({id:'settings',title:'Settings'});
    const ids=new Set(notes.map(note=>note.id));
    if(state.drag&&(busy||state.drag.ids.join()!==notes.filter(note=>note.id!=='settings').map(note=>note.id).join()))endDrag(state);
    state.onAction=onAction;state.onMenu=onMenu;state.onRename=onRename;state.onHomeMenu=onHomeMenu;state.canReopenTab=canReopenTab;state.busy=busy;root.dataset.activeId=activeId;root.setAttribute('aria-busy',String(busy));
    updateTab(state.home,{id:'home',label:'Home',activeId,busy,beforeActive:notes[0]?.id===activeId});
    // Keep controls alive so saves and title changes preserve focus and scroll.
    for(const [id,record] of state.tabs){if(!ids.has(id)){record.item.remove();state.tabs.delete(id);}}
    let pinnedIndex=0,normalIndex=0;
    notes.forEach((note,index)=>{
      let record=state.tabs.get(note.id);
      if(!record){
        record=createTab(root,state,note.id);state.tabs.set(note.id,record);
      }
      updateTab(record,{id:note.id,label:title(note,index),note:note.id==='settings'?null:note,activeId,busy,beforeActive:notes[index+1]?.id===activeId});
      const parent=note.tabPinned?state.pinnedList:state.list,indexInParent=note.tabPinned?pinnedIndex++:normalIndex++;
      if(!state.drag&&parent.children[indexInParent]!==record.item)parent.insertBefore(record.item,parent.children[indexInParent]||null);
    });
    state.pinnedList.hidden=pinnedIndex===0;
    root.dataset.hasPins=String(pinnedIndex>0);
    state.add.setAttribute('aria-disabled',String(busy));
    state.all.setAttribute('aria-disabled',String(busy));
    if(activeChanged||busy)closePicker(state);else if(!state.picker.hidden)updatePicker(state);
    overflow(root,state);
    pending(root,state,pendingId||state.requestedId||activeId,pendingAction||state.requestedAction||'tab-switch');
    if(!busy){state.requestedId=null;state.requestedAction=null;}
    if(activeChanged)reveal(state.tabs.get(activeId)?.button);
    scrollState(state);
    if(hadFocus&&document.activeElement!==focused){
      (focused.isConnected?focused:activeId==='home'?state.home.button:state.tabs.get(activeId)?.button)?.focus({preventScroll:true});
    }
    state.initialized=true;
  }
  return {render,title,dismissPicker(root){const state=roots.get(root);if(state)closePicker(state);}};
})();

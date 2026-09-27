'use strict';
// Suppress browser chrome everywhere. Only editing fields and explicitly registered
// content surfaces get an application-owned menu; ordinary chrome stays quiet.
window.JotMenus=(()=>{
  const menu=document.createElement('div');menu.id='contentContextMenu';menu.className='editor-menu';menu.role='menu';menu.ariaLabel='Actions';menu.hidden=true;document.body.append(menu);
  let context=null,busy=false;
  function close(restore=false){
    if(menu.hidden)return;
    const previous=context;previous.cancelled=true;menu.getAnimations().forEach(a=>a.cancel());menu.hidden=true;
    if(restore)previous.restore?.();
  }
  function open(options){
    if(busy)return;close();window.JotEditorMenu?.close();
    context={...options,cancelled:false,scrolls:new Map()};const current=context;
    for(let node=options.owner;node;node=node.parentElement)current.scrolls.set(node,[node.scrollLeft,node.scrollTop]);
    if(document.scrollingElement)current.scrolls.set(document.scrollingElement,[document.scrollingElement.scrollLeft,document.scrollingElement.scrollTop]);
    (options.owner?.closest('dialog[open]')||document.body).append(menu);menu.replaceChildren();
    for(const item of options.items){
      if(item.separator){const line=document.createElement('div');line.role='separator';menu.append(line);continue;}
      const button=document.createElement('button');button.type='button';button.role='menuitem';button.tabIndex=-1;button.dataset.action=item.id;button.disabled=!!item.disabled;
      const label=document.createElement('span');label.textContent=item.label;const key=document.createElement('kbd');key.textContent=item.shortcut||'';
      button.append(JotDesign.icon(item.icon),label,key);button.onclick=()=>execute(item,current,button);menu.append(button);
    }
    menu.hidden=false;menu.style.left='0px';menu.style.top='0px';menu.style.maxHeight=Math.max(0,innerHeight-12)+'px';
    const rect=menu.getBoundingClientRect();menu.style.left=Math.max(6,Math.min(options.x,innerWidth-rect.width-6))+'px';menu.style.top=Math.max(6,Math.min(options.y,innerHeight-rect.height-6))+'px';menu.scrollTop=0;
    if(!matchMedia('(prefers-reduced-motion: reduce)').matches)menu.animate([{opacity:0,transform:'translateY(-3px)'},{opacity:1,transform:'none'}],{duration:100,easing:'ease-out'});
    menu.querySelector('button:not(:disabled)')?.focus({preventScroll:true});
  }
  async function execute(item,current,button){
    if(busy||button.disabled||current.cancelled)return;
    busy=true;menu.ariaBusy='true';menu.querySelectorAll('button').forEach(b=>b.disabled=true);
    button.classList.add('pending');button.querySelector('span').textContent=item.pending||'Working…';
    try{await current.run(item.id,()=>!current.cancelled);close();}
    catch(error){const cancelled=current.cancelled;close();if(!cancelled){JotBridge.reportError(error,'context-menu');current.error?.(error);}}
    finally{busy=false;menu.removeAttribute('aria-busy');button.classList.remove('pending');button.querySelector('span').textContent=item.label;}
  }
  function field(target){return target?.closest?.('textarea,input:not([type]),input[type=text],input[type=search],input[type=url],input[type=email],input[type=tel]');}
  function openField(input,x,y){
    if(input.disabled||input.type==='email')return; // Email controls have no reliable selection range API.
    const start=input.selectionStart??0,end=input.selectionEnd??0,direction=input.selectionDirection,value=input.value;
    const writable=!input.readOnly,selected=start!==end;if(!writable&&!selected)return;
    const restore=()=>{if(input.isConnected){input.focus({preventScroll:true});input.setSelectionRange(start,end,direction);}};
    const valid=isOpen=>isOpen()&&input.isConnected&&input.getClientRects().length>0&&!input.disabled&&input.value===value;
    open({x,y,owner:input,restore,items:[
      {id:'cut',label:'Cut',icon:'scissors',shortcut:'Ctrl+X',disabled:!writable||!selected,pending:'Copying…'},
      {id:'copy',label:'Copy',icon:'copy',shortcut:'Ctrl+C',disabled:!selected,pending:'Copying…'},
      {id:'paste',label:'Paste',icon:'clipboard',shortcut:'Ctrl+V',disabled:!writable,pending:'Pasting…'},
      {separator:true},{id:'select-all',label:'Select all',icon:'text-select',shortcut:'Ctrl+A',disabled:!value.length}
    ],error(error){
      const alert=document.getElementById('homeError')||document.getElementById('error');if(alert){alert.textContent=error.message;alert.hidden=false;}
    },async run(action,isOpen){
      if(!valid(isOpen))return;
      if(action==='copy'||action==='cut'){
        const written=await JotBridge.request('clipboard-write',{text:value.slice(start,end),html:''});
        if(written!==true)throw new Error('Copy was interrupted. The field is unchanged.');
        if(action==='cut'&&valid(isOpen)&&!input.readOnly){restore();document.execCommand('delete');}
        else if(valid(isOpen))restore();
      }else if(action==='paste'){
        const text=await JotBridge.request('clipboard-read-text');
        if(valid(isOpen)&&!input.readOnly&&text){restore();document.execCommand('insertText',false,text);}
      }else if(action==='select-all'){input.focus({preventScroll:true});input.select();}
    }});
  }
  document.addEventListener('contextmenu',event=>{
    if(event.defaultPrevented)return;event.preventDefault();
    const input=field(event.target);if(input)openField(input,event.clientX,event.clientY);else{close();window.JotEditorMenu?.close();}
  });
  document.addEventListener('pointerdown',event=>{if(!menu.contains(event.target))close();},true);
  document.addEventListener('scroll',event=>{
    if(menu.hidden||menu.contains(event.target))return;
    const target=event.target===document?document.scrollingElement:event.target;
    if(field(target))return;
    const before=context?.scrolls.get(target);
    // Chromium may deliver the preceding Fit/focus scroll event after the menu
    // opens. Dismiss only if an anchor actually moved, not on that stale event.
    if(before&&before[0]===target.scrollLeft&&before[1]===target.scrollTop)return;
    close();
  },true);
  window.addEventListener('resize',()=>close());window.addEventListener('blur',()=>close());
  JotBridge.on(data=>{if(data.event==='active-window'&&!data.active)close();});
  document.addEventListener('keydown',event=>{
    if(event.isComposing)return;
    if(menu.hidden){
      const input=field(event.target);
      if(input&&(event.key==='ContextMenu'||event.shiftKey&&event.key==='F10')){event.preventDefault();event.stopImmediatePropagation();const r=input.getBoundingClientRect();openField(input,r.left,r.bottom);}
      return;
    }
    if(event.key==='Escape'||event.key==='Tab'){event.preventDefault();event.stopImmediatePropagation();close(true);return;}
    if(busy){event.preventDefault();event.stopImmediatePropagation();return;}
    if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
      event.preventDefault();event.stopImmediatePropagation();const buttons=[...menu.querySelectorAll('button:not(:disabled)')],index=buttons.indexOf(document.activeElement);
      buttons[event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();
    }
  },true);
  return {open,close};
})();

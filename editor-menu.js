'use strict';
// Native clipboard access happens only after an explicit action, never on opening.
window.JotEditorMenu=(()=>{
  const menu=document.createElement('div');
  menu.id='editorMenu';menu.className='editor-menu';menu.role='menu';menu.ariaLabel='Editing';menu.hidden=true;app.append(menu);
  let context=null,busy=false;
  function selection(){
    const s=getSelection();
    if(!s.rangeCount||!editor.contains(s.anchorNode)||!editor.contains(s.focusNode))return null;
    return {range:s.getRangeAt(0).cloneRange(),anchor:s.anchorNode,anchorOffset:s.anchorOffset,focus:s.focusNode,focusOffset:s.focusOffset};
  }
  function restore(c){
    if(!c?.selection||!editor.contains(c.selection.range.commonAncestorContainer))return false;
    editor.focus({preventScroll:true});const s=getSelection(),v=c.selection;
    try{s.setBaseAndExtent(v.anchor,v.anchorOffset,v.focus,v.focusOffset);}
    catch{s.removeAllRanges();s.addRange(v.range);}
    rememberSelection();return true;
  }
  function close(focus=false){
    if(menu.hidden)return;
    if(context)context.cancelled=true;
    menu.getAnimations().forEach(a=>a.cancel());menu.hidden=true;if(focus)restore(context);
  }
  function add(action,label,icon,shortcut='',disabled=false){
    const b=document.createElement('button');b.type='button';b.role='menuitem';b.tabIndex=-1;b.dataset.action=action;b.disabled=disabled;
    const text=document.createElement('span');text.textContent=label;const key=document.createElement('kbd');key.textContent=shortcut;
    b.append(JotDesign.icon(icon),text,key);b.onclick=()=>run(action);menu.append(b);
  }
  function separator(){const line=document.createElement('div');line.role='separator';menu.append(line);}
  function directions(){
    const row=document.createElement('div');row.className='context-direction';
    const label=document.createElement('span');label.textContent='Direction';row.append(label);
    const choices=document.createElement('div');choices.className='context-direction-buttons';choices.role='group';choices.ariaLabel='Current or selected paragraph direction';
    const blocks=selectedDirectionBlocks(),modes=new Set(blocks.map(block=>block.dataset.jotDirection||'auto'));
    for(const [mode,text,title] of [['auto','Auto','Automatic paragraph direction'],['ltr','LTR','Left-to-right paragraphs'],['rtl','RTL','Right-to-left paragraphs']]){
      const button=document.createElement('button');button.type='button';button.role='menuitemradio';button.tabIndex=-1;button.dataset.action='direction-'+mode;
      button.title=button.ariaLabel=title;button.ariaChecked=String(modes.size===1&&modes.has(mode));button.disabled=!blocks.length;
      const caption=document.createElement('span');caption.textContent=text;button.append(caption);button.onclick=()=>run('direction-'+mode);choices.append(button);
    }
    row.append(choices);menu.append(row);
  }
  function open(x,y,target,keyboard=false){
    if(busy||!ready||composing||deleting)return;
    window.JotMenus?.close();closePanels();flushTypingHistory();const image=target?.closest?.('img');
    if(image&&editor.contains(image)){
      const range=document.createRange();range.selectNode(image);getSelection().removeAllRanges();getSelection().addRange(range);
    }else if(!keyboard){
      const selected=selection();const inside=selected&&[...selected.range.getClientRects()].some(r=>x>=r.left&&x<=r.right&&y>=r.top&&y<=r.bottom);
      if(!inside){const caret=document.caretRangeFromPoint(x,y);if(caret&&editor.contains(caret.startContainer)){getSelection().removeAllRanges();getSelection().addRange(caret);}}
    }
    rememberSelection();context={selection:selection(),image:image&&editor.contains(image)?image:null,noteId:model.activeId,revision};
    if(!context.selection){restoreSelection();context.selection=selection();}
    const h=histories.get(model.activeId),selected=!context.selection.range.collapsed;menu.replaceChildren();
    if(context.image){add('open-image','Open image','scan');add('copy-image','Copy image','copy');add('remove-image','Remove image','trash-2');separator();}
    add('undo','Undo','undo-2','Ctrl+Z',!h||h.index===0);add('redo','Redo','redo-2','Ctrl+Y',!h||h.index>=h.values.length-1);separator();
    add('cut','Cut','scissors','Ctrl+X',!selected);add('copy','Copy','copy','Ctrl+C',!selected);add('paste','Paste','clipboard','Ctrl+V');separator();directions();separator();add('select-all','Select all','text-select','Ctrl+A');
    separator();add('home','Home','home');
    menu.hidden=false;menu.style.left='0px';menu.style.top='0px';menu.style.maxHeight=Math.max(0,innerHeight-12)+'px';
    const r=menu.getBoundingClientRect();menu.style.left=Math.max(6,Math.min(x,innerWidth-r.width-6))+'px';menu.style.top=Math.max(6,Math.min(y,innerHeight-r.height-6))+'px';menu.scrollTop=0;
    if(!reducedMotion.matches)menu.animate([{opacity:0,transform:'translateY(-3px)'},{opacity:1,transform:'translateY(0)'}],{duration:100,easing:'ease-out'});
    menu.querySelector('button:not(:disabled)')?.focus({preventScroll:true});
  }
  function valid(c){return !c.cancelled&&!composing&&!deleting&&editor.isContentEditable&&c.noteId===model.activeId&&c.revision===revision&&editor.contains(c.selection.range.commonAncestorContainer);}
  async function run(action){
    if(busy||!context)return;
    const c=context,button=menu.querySelector('[data-action="'+action+'"]');if(!button||button.disabled)return;
    const oldLabel=button.querySelector('span').textContent;
    busy=true;menu.ariaBusy='true';menu.querySelectorAll('button').forEach(b=>b.disabled=true);
    button.classList.add('pending');button.querySelector('span').textContent=action==='home'?'Opening…':action==='paste'?'Pasting…':['copy','copy-image','cut'].includes(action)?'Copying…':'Working…';
    try{
      if(action==='home'){await saveNow();if(!c.cancelled)await request('home');close();return;}
      if(!valid(c))throw new Error('The note changed. Select the content and try again.');restore(c);
      if(action==='copy'||action==='cut'||action==='copy-image'){
        const payload=action==='copy-image'?{html:c.image.outerHTML,text:'',image:c.image.src}:copyPayload();
        const copied=await request('clipboard-write',payload);
        if(c.cancelled)return;
        if(copied!==true)throw new Error('Copy was interrupted. Your note is unchanged.');
        if(action==='cut'){if(!valid(c))throw new Error('Copied, but the note changed. Nothing was cut.');restore(c);command('delete');}
      }else if(action==='paste'){
        const data=await request('clipboard-read');if(c.cancelled)return;if(!valid(c))throw new Error('The note changed. Paste again at the intended position.');restore(c);
        const transfer=new DataTransfer();transfer.setData('text/plain',data.text||'');transfer.setData('text/html',data.html||'');
        if(data.image){const [header,base64]=data.image.split(',');const bytes=Uint8Array.from(atob(base64),c=>c.charCodeAt(0));transfer.items.add(new File([bytes],'clipboard.png',{type:header.slice(5,header.indexOf(';'))}));}
        await paste({preventDefault(){},clipboardData:transfer},()=>valid(c));
      }else if(action==='open-image')await request('view-image',c.image.src);
      else if(action==='remove-image')command('delete');
      else if(action==='undo'||action==='redo')undo(action==='redo');
      else if(action.startsWith('direction-'))setParagraphDirection(action.slice('direction-'.length));
      else if(action==='select-all'){const r=document.createRange();r.selectNodeContents(editor);getSelection().removeAllRanges();getSelection().addRange(r);rememberSelection();}
      close();
    }catch(error){const cancelled=c.cancelled;close();if(!cancelled)showError(error);}
    finally{busy=false;menu.removeAttribute('aria-busy');button.classList.remove('pending');button.querySelector('span').textContent=oldLabel;}
  }
  editor.addEventListener('contextmenu',e=>{e.preventDefault();open(e.clientX,e.clientY,e.target);});
  document.addEventListener('pointerdown',e=>{if(!menu.contains(e.target))close();},true);
  $('writingArea').addEventListener('scroll',()=>close(),{passive:true});window.addEventListener('resize',()=>close());window.addEventListener('blur',()=>close());
  JotBridge.on(data=>{if(data.event==='active-window'&&!data.active)close();});
  document.addEventListener('keydown',e=>{
    if(e.isComposing)return;
    if((e.key==='ContextMenu'||(e.shiftKey&&e.key==='F10'))&&editor.contains(document.activeElement)){
      e.preventDefault();e.stopImmediatePropagation();const s=selection(),r=s?.range.getBoundingClientRect()||editor.getBoundingClientRect();open(Math.max(8,r.left),Math.max(8,r.bottom),null,true);return;
    }
    if(menu.hidden)return;
    if(e.key==='Escape'||e.key==='Tab'){e.preventDefault();e.stopImmediatePropagation();close(true);return;}
    if(busy){e.preventDefault();e.stopImmediatePropagation();return;}
    if(['ArrowLeft','ArrowRight'].includes(e.key)&&e.target.closest('.context-direction-buttons')){
      e.preventDefault();e.stopImmediatePropagation();const buttons=[...e.target.closest('.context-direction-buttons').querySelectorAll('button:not(:disabled)')],i=buttons.indexOf(document.activeElement);
      buttons[(i+(e.key==='ArrowRight'?1:-1)+buttons.length)%buttons.length]?.focus();return;
    }
    if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){
      e.preventDefault();e.stopImmediatePropagation();const buttons=[...menu.querySelectorAll('button:not(:disabled)')],i=buttons.indexOf(document.activeElement);
      const next=e.key==='Home'?0:e.key==='End'?buttons.length-1:(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons[next]?.focus();
    }
  },true);
  return {close,open,run};
})();

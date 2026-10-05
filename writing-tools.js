'use strict';
window.JotWritingTools=(()=>{
  const area=document.getElementById('writingArea'),dock=document.querySelector('.quiet-footer'),bar=document.getElementById('formatBar');
  app.dataset.document='true';area.before(dock);dock.classList.add('document-toolbar');dock.ariaLabel='Document tools';
  const commands=[
    {id:'text',label:'Text',description:'Plain paragraph',icon:'type',terms:'paragraph normal',command:'formatBlock',value:'p'},
    {id:'heading-1',label:'Heading 1',description:'Large section heading',icon:'type',terms:'title h1',command:'formatBlock',value:'h1'},
    {id:'heading-2',label:'Heading 2',description:'Medium section heading',icon:'type',terms:'subtitle h2',command:'formatBlock',value:'h2'},
    {id:'heading-3',label:'Heading 3',description:'Small section heading',icon:'type',terms:'subtitle h3',command:'formatBlock',value:'h3'},
    {id:'bullet-list',label:'Bullet list',description:'An unordered list',icon:'list',terms:'unordered bullets',command:'insertUnorderedList'},
    {id:'numbered-list',label:'Numbered list',description:'A numbered sequence',icon:'list-ordered',terms:'ordered numbers',command:'insertOrderedList'},
    {id:'quote',label:'Quote',description:'A block quote',icon:'text-select',terms:'blockquote',command:'formatBlock',value:'blockquote'},
    {id:'code',label:'Code block',description:'Monospaced text',icon:'code-xml',terms:'pre snippet',command:'formatBlock',value:'pre'},
    {id:'inline-code',label:'Inline code',description:'Format a word or short snippet',icon:'code-xml',terms:'monospace backtick markdown',documentAction:'inlineCode'},
    {id:'divider',label:'Divider',description:'A horizontal section separator',icon:'minus',terms:'rule line separator markdown',documentAction:'insertDivider'},
    {id:'table',label:'Table',description:'Choose rows and columns',icon:'table',terms:'grid cells rows columns markdown',table:true},
    ...['auto','left','center','right','justify'].map(value=>({id:'align-'+value,label:value==='auto'?'Auto alignment':value==='justify'?'Justify':'Align '+value,description:'Selected or current paragraphs',icon:value==='auto'?'type':'align-'+value,terms:'alignment '+value,alignment:value})),
    ...['bold','italic','underline','strikeThrough'].map(value=>({id:value,label:({bold:'Bold',italic:'Italic',underline:'Underline',strikeThrough:'Strikethrough'})[value],description:'Format the selected text',icon:value==='strikeThrough'?'strikethrough':value,terms:'format',command:value})),
    {id:'image',label:'Image',description:'Choose an image from your computer',icon:'image-plus',terms:'picture photo',image:true}
  ];
  let menuState=null,selected=0,mutating=false,frame=0,pointerSelecting=false;
  const menu=document.createElement('div');menu.id='writingCommands';menu.className='writing-commands surface';menu.role='listbox';menu.ariaLabel='Writing commands';menu.hidden=true;
  const menuHeading=document.createElement('div');menuHeading.className='writing-command-heading';menuHeading.textContent='Writing commands';
  const choices=document.createElement('div');choices.className='writing-command-choices';menu.append(menuHeading,choices);document.body.append(menu);
  const bubble=document.createElement('div');bubble.id='selectionTools';bubble.className='selection-tools surface';bubble.role='toolbar';bubble.ariaLabel='Selection formatting';bubble.hidden=true;document.body.append(bubble);
  function button(id,label,icon){const b=document.createElement('button');b.type='button';b.className='icon-button';b.dataset.writingAction=id;b.title=b.ariaLabel=label;b.append(JotDesign.icon(icon));return b;}
  for(const item of commands.filter(item=>['bold','italic','underline','strikeThrough','inline-code','align-left','align-center','align-right','align-justify'].includes(item.id))){const b=button(item.id,item.label,item.icon);b.onclick=()=>run(item);bubble.append(b);}
  bubble.addEventListener('pointerdown',event=>{rememberSelection();event.preventDefault();});
  const block=document.createElement('button');block.id='blockStyleButton';block.type='button';block.className='writing-block-style';block.title=block.ariaLabel='Paragraph style and commands';block.textContent='Text';bar.prepend(block);
  const alignment=button('alignment','Paragraph alignment','align-left');alignment.id='writingAlignment';bar.append(alignment);
  for(const owner of [block,alignment]){owner.setAttribute('aria-haspopup','listbox');owner.setAttribute('aria-controls',menu.id);owner.ariaExpanded='false';}
  menu.tabIndex=-1;
  block.onclick=()=>openFromButton(block,'blocks');alignment.onclick=()=>openFromButton(alignment,'alignment');
  function available(){return ready&&!composing&&!deleting&&!editorLockedByHost&&!document.querySelector('dialog[open]')&&(!window.JotWorkspace||JotWorkspace.view==='note');}
  function close(restore=false){const owner=menuState?.owner;menu.hidden=true;menuState=null;block.ariaExpanded=alignment.ariaExpanded='false';editor.removeAttribute('aria-controls');editor.removeAttribute('aria-activedescendant');editor.removeAttribute('aria-autocomplete');menu.removeAttribute('aria-activedescendant');if(restore&&owner?.isConnected)owner.focus({preventScroll:true});}
  function hide(){close();bubble.hidden=true;}
  function position(surface,rect,preferAbove=false){
    const top=document.querySelector('.document-toolbar')?.getBoundingClientRect().bottom||8;
    surface.style.maxHeight=Math.max(70,innerHeight-top-14)+'px';
    const height=surface.getBoundingClientRect().height;
    let y=preferAbove?rect.top-height-7:rect.bottom+7;
    if(y+height>innerHeight-6)y=rect.top-height-7;
    if(y<top+4)y=Math.max(top+4,Math.min(rect.bottom+7,innerHeight-height-6));
    y=Math.max(top+4,Math.min(y,innerHeight-height-6));
    surface.style.top=y+'px';surface.style.left=Math.max(6,Math.min(rect.left,innerWidth-surface.offsetWidth-6))+'px';
  }
  function caretRect(range){
    let rect=range.getBoundingClientRect();
    if(!rect.height&&range.startContainer.nodeType===Node.TEXT_NODE&&range.startOffset){const previous=range.cloneRange();previous.setStart(range.startContainer,range.startOffset-1);rect=previous.getBoundingClientRect();}
    return rect.height?rect:range.startContainer.parentElement?.getBoundingClientRect()||editor.getBoundingClientRect();
  }
  function slashContext(){
    if(!available()||document.activeElement!==editor)return null;
    const selection=getSelection();if(!selection.isCollapsed||!selection.rangeCount||!editor.contains(selection.anchorNode))return null;
    const range=selection.getRangeAt(0),element=range.startContainer.nodeType===1?range.startContainer:range.startContainer.parentElement;
    if(element.closest('pre,code'))return null;
    const parent=element.closest('p,div,li,h1,h2,h3,blockquote,td,th');if(!parent||parent===editor||!editor.contains(parent))return null;
    // Read only a short suffix before the caret, including across formatting
    // spans. Never serialize or walk from the start of a long paragraph on input.
    let visited=0;
    function lastLeaf(node){while(node?.lastChild&&visited++<128)node=node.lastChild;return node;}
    function previous(node){while(node&&node!==parent&&visited++<128){if(node.previousSibling)return lastLeaf(node.previousSibling);node=node.parentNode;}return null;}
    let node=range.startContainer,end=range.startOffset,text='';const pieces=[];
    if(node.nodeType!==Node.TEXT_NODE){node=end?lastLeaf(node.childNodes[end-1]):previous(node);end=node?.nodeType===Node.TEXT_NODE?node.length:0;}
    while(node&&visited++<128&&text.length<36){
      if(node.nodeType===Node.ELEMENT_NODE&&node.matches('br,img'))break;
      if(node.nodeType===Node.TEXT_NODE){const start=Math.max(0,end-(36-text.length));text=node.data.slice(start,end)+text;pieces.unshift({node,start,length:end-start});}
      if(text.length>=36)break;node=previous(node);end=node?.nodeType===Node.TEXT_NODE?node.length:0;
    }
    const match=/(?:^|\s)\/([\p{L}\p{N} -]{0,32})$/u.exec(text);if(!match)return null;
    let start=text.length-match[1].length-1;
    for(const piece of pieces){if(start<piece.length){const query=range.cloneRange();query.setStart(piece.node,piece.start+start);return {id:model.activeId,range:query,text:'/'+match[1],query:match[1].toLowerCase(),rect:caretRect(range),kind:'slash'};}start-=piece.length;}
    return null;
  }
  function options(state){return commands.filter(item=>(state.kind!=='blocks'||['text','heading-1','heading-2','heading-3','bullet-list','numbered-list','quote','code','inline-code','divider','table','image'].includes(item.id))&&(state.kind!=='alignment'||item.alignment)&&(!item.table||!(getSelection().anchorNode?.parentElement?.closest('table')))&&(!state.query||[item.label,item.terms].join(' ').toLowerCase().includes(state.query)));}
  function renderMenu(state){
    const previous=menuState;menuState=state;if(!previous||previous.query!==state.query||previous.kind!==state.kind)selected=0;
    const items=options(state);selected=Math.max(0,Math.min(selected,items.length-1));choices.replaceChildren();
    for(const [index,item] of items.entries()){
      const b=document.createElement('button');b.type='button';b.tabIndex=-1;b.role='option';b.id='writing-command-'+item.id;b.dataset.writingCommand=item.id;b.ariaSelected=String(index===selected);
      const icon=JotDesign.icon(item.icon),text=document.createElement('span'),label=document.createElement('strong'),description=document.createElement('small');label.textContent=item.label;description.textContent=item.description;text.append(label,description);b.append(icon,text);b.onpointerdown=event=>event.preventDefault();b.onclick=()=>run(item);choices.append(b);
    }
    if(!items.length){const empty=document.createElement('p');empty.className='writing-no-results';empty.textContent='No matching commands. Press Esc to keep typing.';choices.append(empty);}
    menu.hidden=false;bubble.hidden=true;position(menu,state.rect);
    const active=choices.querySelector('[aria-selected=true]');
    if(state.kind==='slash'){editor.setAttribute('aria-controls',menu.id);editor.setAttribute('aria-autocomplete','list');if(active)editor.setAttribute('aria-activedescendant',active.id);else editor.removeAttribute('aria-activedescendant');}
    else{state.owner.ariaExpanded='true';if(active)menu.setAttribute('aria-activedescendant',active.id);menu.focus({preventScroll:true});}
    active?.scrollIntoView({block:'nearest'});
  }
  function openFromButton(owner,kind){if(!available())return;if(menuState?.kind===kind){close(true);return;}rememberSelection();renderMenu({kind,owner,id:model.activeId,query:'',rect:owner.getBoundingClientRect()});}
  function run(item){
    if(!available())return;let state=menuState;if(state&&state.id!==model.activeId){hide();return;}
    if(state?.kind==='slash'){state=slashContext();if(!state||!options(state).some(option=>option.id===item.id)){hide();return;}}
    if(item.table){const context={owner:state?.owner||null,slash:state?.kind==='slash'?state:null,rect:state?.rect};hide();JotDocumentBlocks.openTable(context);return;}
    flushTypingHistory();restoreSelection();rememberHistorySelection();mutating=true;
    try{
      if(state?.kind==='slash'){
        if(!editor.contains(state.range.commonAncestorContainer)||state.range.toString()!==state.text){hide();return;}
        getSelection().removeAllRanges();getSelection().addRange(state.range);document.execCommand('delete');rememberSelection();
      }
      close();bubble.hidden=true;
      if(item.alignment)setParagraphAlignment(item.alignment);
      else if(item.documentAction)JotDocumentBlocks[item.documentAction]();
      else if(item.image){onEdit('command');document.getElementById('imageInput').click();}
      else command(item.command,item.value);
    }finally{mutating=false;}
    schedule();
  }
  function refresh(){
    frame=0;if(!available()){hide();return;}
    if(menuState&&menuState.kind!=='slash')return;
    const slash=slashContext();if(slash){if(options(slash).length)renderMenu(slash);else close();bubble.hidden=true;return;}if(menuState?.kind==='slash')close();
    const selection=getSelection();
    if(pointerSelecting||document.activeElement!==editor&&!bubble.contains(document.activeElement)||!selection.rangeCount||selection.isCollapsed||!editor.contains(selection.anchorNode)||!editor.contains(selection.focusNode)){bubble.hidden=true;return;}
    const range=selection.getRangeAt(0),rect=range.getBoundingClientRect(),viewport=area.getBoundingClientRect();
    if(rect.bottom<viewport.top||rect.top>viewport.bottom){bubble.hidden=true;return;}
    bubble.hidden=false;position(bubble,rect,true);
    for(const b of bubble.children){const item=commands.find(value=>value.id===b.dataset.writingAction);b.ariaPressed=String(item.command?document.queryCommandState(item.command):item.id==='inline-code'?JotDocumentBlocks.isInlineCode():selectedAlignmentBlocks().every(node=>paragraphAlignment(node)===item.alignment));}
  }
  function schedule(){if(!frame)frame=requestAnimationFrame(refresh);}
  editor.addEventListener('input',()=>{if(!mutating)schedule();});
  document.addEventListener('selectionchange',schedule);editor.addEventListener('compositionstart',hide);editor.addEventListener('compositionend',schedule);
  document.addEventListener('pointerdown',event=>{if(editor.contains(event.target)){pointerSelecting=true;bubble.hidden=true;}else if(!event.target.closest('#writingCommands,#selectionTools,#blockStyleButton,#writingAlignment'))hide();},true);
  document.addEventListener('pointerup',()=>{pointerSelecting=false;schedule();},true);
  document.addEventListener('focusin',event=>{if(!event.target.closest('#editor,#writingCommands,#selectionTools,#blockStyleButton,#writingAlignment'))hide();});
  document.addEventListener('keydown',event=>{
    if(!menuState||event.isComposing)return;
    if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();close(true);return;}
    if(!['ArrowDown','ArrowUp','Enter'].includes(event.key)||event.ctrlKey||event.metaKey||event.altKey)return;
    if(menuState.kind==='slash'){const latest=slashContext();if(!latest){close();return;}renderMenu(latest);}
    const items=options(menuState);if(!items.length){close();return;}event.preventDefault();event.stopImmediatePropagation();
    if(event.key==='Enter'){if(items[selected])run(items[selected]);}
    else{selected=(selected+(event.key==='ArrowDown'?1:items.length-1))%Math.max(1,items.length);renderMenu(menuState);}
  },true);
  area.addEventListener('scroll',()=>{close();schedule();},{passive:true});window.addEventListener('resize',hide);window.addEventListener('blur',hide);
  JotBridge.on(data=>{if(['workspace-view','prepare-quit','flush','active-window'].includes(data.event))hide();});
  return {hide,refresh,schedule,get mutating(){return mutating;},commands};
})();

'use strict';
window.JotNoteIcons=(()=>{
  // Formatting controls still use Lucide; note identities are emoji-only.
  Object.assign(window.JotIconPaths,{
    'code-xml':'<path d="m18 16 4-4-4-4m-12 0-4 4 4 4m8.5-12-5 16"/>',
    'chevron-right':'<path d="m9 18 6-6-6-6"/>',
    'align-left':'<path d="M21 5H3M15 12H3M17 19H3"/>',
    'align-center':'<path d="M21 5H3M17 12H7M19 19H5"/>',
    'align-right':'<path d="M21 5H3M21 12H9M21 19H7"/>',
    'align-justify':'<path d="M3 5h18M3 12h18M3 19h18"/>'
  });
  const defaultValue='emoji:📝';
  // Legacy IDs are never rewritten merely by opening an old note/file.
  const legacy={'notepad-text':'📝','book-open':'📖','briefcase-business':'💼','code-xml':'💻',lightbulb:'💡','list-checks':'✅','calendar-days':'📅',heart:'❤️',star:'⭐',flag:'🚩',coffee:'☕','music-2':'🎵'};
  const data=window.JotEmojiData,accepted=new Set(data.aliases);
  const entries=data.rows.map(([emoji,name,group,subgroup,version])=>({emoji,name,group,subgroup,version,
    search:(name+' '+data.groups[group]+' '+subgroup.replaceAll('-',' ')).toLowerCase(),
    tones:[...emoji].filter(char=>{const point=char.codePointAt(0);return point>=0x1f3fb&&point<=0x1f3ff;})}));
  const byEmoji=new Map(entries.map(entry=>[entry.emoji,entry]));
  function valid(value){return typeof value==='string'&&(value.startsWith('icon:')&&Object.hasOwn(legacy,value.slice(5))||value.startsWith('emoji:')&&accepted.has(value.slice(6)));}
  function normalize(value){return valid(value)?value:defaultValue;}
  function emojiFor(value){value=normalize(value);return value.startsWith('icon:')?legacy[value.slice(5)]:value.slice(6);}
  function render(value){const span=document.createElement('span');span.className='note-symbol note-emoji';span.ariaHidden='true';span.textContent=emojiFor(value);return span;}
  function filter(query='',category='all',tone='all'){
    const words=query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return entries.filter(entry=>(category==='all'||entry.group===Number(category))&&(tone==='all'||tone==='default'&&!entry.tones.length||entry.tones.includes(tone))&&words.every(word=>entry.search.includes(word)||entry.emoji.includes(word)));
  }
  const byId=id=>document.getElementById(id),pageSize=96;
  let dialog=null,context=null,busy=false,page=0,filtered=[],searchTimer=0,roving=0;
  function element(tag,className,text){const node=document.createElement(tag);if(className)node.className=className;if(text)node.textContent=text;return node;}
  function createPicker(){
    if(dialog)return;
    dialog=element('dialog','note-icon-dialog surface');dialog.id='noteIconDialog';dialog.ariaLabel='Choose note emoji';
    const heading=element('div','dialog-header'),title=element('h2','','Note emoji'),close=element('button','icon-button');
    close.type='button';close.ariaLabel='Close emoji picker';close.append(JotDesign.icon('x'));close.onclick=()=>dialog.close();heading.append(title,close);dialog.append(heading);
    const search=element('input','note-emoji-search');search.id='noteEmojiSearch';search.type='search';search.autocomplete='off';search.spellcheck=false;search.placeholder='Search all emojis';search.ariaLabel='Search emojis';
    search.oninput=()=>{clearTimeout(searchTimer);searchTimer=setTimeout(()=>refreshResults(),60);};dialog.append(search);
    const filters=element('div','note-emoji-filters'),category=element('select');category.id='noteEmojiCategory';category.ariaLabel='Emoji category';
    category.add(new Option('All categories','all'));data.groups.forEach((name,index)=>{if(entries.some(entry=>entry.group===index))category.add(new Option(name,String(index)));});
    const tone=element('select');tone.id='noteEmojiTone';tone.ariaLabel='Skin tone';
    for(const [value,label]of [['all','All tones'],['default','Default tone'],['🏻','Light'],['🏼','Medium-light'],['🏽','Medium'],['🏾','Medium-dark'],['🏿','Dark']])tone.add(new Option(label,value));
    category.onchange=tone.onchange=()=>refreshResults();filters.append(category,tone);dialog.append(filters);
    const grid=element('div','note-icon-choices');grid.id='noteEmojiResults';grid.role='group';grid.ariaLabel='Emoji results';dialog.append(grid);
    const empty=element('p','note-emoji-empty','No emojis match. Try a name such as “book”, “heart”, or “coffee”.');empty.id='noteEmojiEmpty';empty.hidden=true;dialog.append(empty);
    const paging=element('div','note-emoji-paging'),count=element('span');count.id='noteEmojiCount';count.role='status';count.ariaLive='polite';
    const previous=element('button','icon-button');previous.id='noteEmojiPrevious';previous.type='button';previous.ariaLabel='Previous emoji page';previous.append(JotDesign.icon('chevron-left'));previous.onclick=()=>setPage(page-1);
    const next=element('button','icon-button');next.id='noteEmojiNext';next.type='button';next.ariaLabel='Next emoji page';next.append(JotDesign.icon('chevron-right'));next.onclick=()=>setPage(page+1);paging.append(count,previous,next);dialog.append(paging);
    const preview=element('p','note-emoji-preview');preview.id='noteEmojiPreview';dialog.append(preview);
    const custom=element('form','note-custom-emoji'),input=element('input');input.id='noteCustomEmoji';input.maxLength=64;input.autocomplete='off';input.spellcheck=false;input.placeholder='Or paste an emoji';input.ariaLabel='Custom emoji';
    const apply=element('button','secondary-button','Use');apply.type='submit';apply.disabled=true;
    input.oninput=()=>{apply.disabled=busy||!valid('emoji:'+input.value.trim());byId('noteIconError').hidden=true;};
    custom.onsubmit=event=>{event.preventDefault();if(!apply.disabled)choose('emoji:'+input.value.trim());};custom.append(input,apply);dialog.append(custom);
    dialog.append(element('p','note-icon-hint',`Unicode ${data.version} · Newer emojis may look like boxes or separate symbols on older Windows. Their names and saved values are kept.`));
    const error=element('p');error.id='noteIconError';error.role='alert';error.hidden=true;dialog.append(error);
    const status=element('p','note-icon-hint');status.id='noteIconStatus';status.role='status';status.hidden=true;dialog.append(status);
    dialog.addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    dialog.addEventListener('close',()=>{clearTimeout(searchTimer);const owner=context?.owner;context=null;if(owner?.isConnected)owner.focus({preventScroll:true});});
    dialog.addEventListener('keydown',navigate);document.body.append(dialog);
  }
  function preview(entry){byId('noteEmojiPreview').replaceChildren(render('emoji:'+entry.emoji),element('span','',entry.name));}
  function refreshResults(){clearTimeout(searchTimer);filtered=filter(byId('noteEmojiSearch').value,byId('noteEmojiCategory').value,byId('noteEmojiTone').value);setPage(0);}
  function setPage(next,focusIndex=null){
    page=Math.max(0,Math.min(next,Math.max(0,Math.ceil(filtered.length/pageSize)-1)));roving=0;
    const grid=byId('noteEmojiResults'),fragment=document.createDocumentFragment(),start=page*pageSize,selected=emojiFor(context?.note.icon);
    for(const [index,entry]of filtered.slice(start,start+pageSize).entries()){
      const button=element('button');button.type='button';button.dataset.noteIcon='emoji:'+entry.emoji;button.title=button.ariaLabel=entry.name;button.ariaPressed=String(entry.emoji===selected);button.tabIndex=index===0?0:-1;
      button.append(render(button.dataset.noteIcon));button.onclick=()=>choose(button.dataset.noteIcon);
      button.onfocus=()=>{roving=index;grid.querySelectorAll('button').forEach(item=>item.tabIndex=item===button?0:-1);preview(entry);};button.onpointerenter=()=>preview(entry);fragment.append(button);
    }
    grid.replaceChildren(fragment);grid.scrollTop=0;grid.hidden=!filtered.length;byId('noteEmojiEmpty').hidden=!!filtered.length;
    byId('noteEmojiCount').textContent=filtered.length?`${start+1}–${Math.min(start+pageSize,filtered.length)} of ${filtered.length.toLocaleString('en-US')}`:'0 results';
    byId('noteEmojiPrevious').disabled=busy||page===0;byId('noteEmojiNext').disabled=busy||start+pageSize>=filtered.length;
    if(focusIndex!==null&&grid.children.length)grid.children[Math.max(0,Math.min(focusIndex,grid.children.length-1))].focus({preventScroll:true});
  }
  function navigate(event){
    if(event.isComposing)return;
    if(event.key==='Escape'){
      // Composition WebView hosts do not consistently perform the native
      // dialog Escape default. Keep the normal cancellable close contract.
      event.preventDefault();event.stopPropagation();
      if(busy)return;
      if(typeof dialog.requestClose==='function')dialog.requestClose();
      else if(dialog.dispatchEvent(new Event('cancel',{cancelable:true})))dialog.close();
      return;
    }
    if(busy)return;
    if(event.target===byId('noteEmojiSearch')&&event.key==='ArrowDown'){event.preventDefault();refreshResults();byId('noteEmojiResults').firstElementChild?.focus();return;}
    const current=event.target.closest('[data-note-icon]');if(!current||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(event.key))return;
    event.preventDefault();const grid=byId('noteEmojiResults'),columns=getComputedStyle(grid).gridTemplateColumns.split(' ').length;let target=roving;
    if(event.key==='Home')target=0;else if(event.key==='End')target=grid.children.length-1;
    else if(event.key==='PageUp'||event.key==='PageDown'){setPage(page+(event.key==='PageUp'?-1:1),roving);return;}
    else target+=event.key==='ArrowLeft'?-1:event.key==='ArrowRight'?1:event.key==='ArrowUp'?-columns:columns;
    if(target<0&&page>0)setPage(page-1,pageSize+target);
    else if(target>=grid.children.length&&(page+1)*pageSize<filtered.length)setPage(page+1,target-grid.children.length);
    else grid.children[Math.max(0,Math.min(target,grid.children.length-1))]?.focus();
  }
  function open(note,{owner=null,onSave=null}={}){
    if(!note?.id||busy)return;createPicker();window.JotMenus?.close();window.JotEditorMenu?.close();context={note:{...note},owner,onSave};
    byId('noteCustomEmoji').value='';byId('noteIconError').hidden=true;byId('noteEmojiSearch').value='';byId('noteEmojiCategory').value='all';byId('noteEmojiTone').value='all';dialog.querySelector('.note-custom-emoji button').disabled=true;refreshResults();
    const current=emojiFor(note.icon);preview(byEmoji.get(current)||{emoji:current,name:'Current emoji'});
    if(!dialog.open)dialog.showModal();byId('noteEmojiSearch').focus({preventScroll:true});
  }
  async function choose(value){
    if(!context||busy||!value.startsWith('emoji:')||!valid(value))return;
    const current=context;busy=true;dialog.ariaBusy='true';dialog.querySelectorAll('button,input,select').forEach(item=>item.disabled=true);byId('noteIconStatus').textContent='Saving emoji…';byId('noteIconStatus').hidden=false;
    const target=dialog.querySelector('[data-note-icon="'+CSS.escape(value)+'"]')||dialog.querySelector('.note-custom-emoji button');target.classList.add('pending');
    try{
      if(current.onSave)await current.onSave(value,current.note);
      else{if(typeof activeNote==='function'&&activeNote()?.id===current.note.id)await saveNow();await JotBridge.request('note-metadata',{id:current.note.id,icon:value});}
      if(typeof activeNote==='function'&&activeNote()?.id===current.note.id)activeNote().icon=value;
      document.dispatchEvent(new CustomEvent('jot-note-icon',{detail:{id:current.note.id,icon:value}}));refreshEditor();dialog.close();
    }catch(error){JotBridge.reportError(error,'note-icon');const label=byId('noteIconError');label.textContent=error.message||'The emoji could not be saved.';label.hidden=false;}
    finally{busy=false;dialog.removeAttribute('aria-busy');byId('noteIconStatus').hidden=true;target.classList.remove('pending');dialog.querySelectorAll('button,input,select').forEach(item=>item.disabled=false);dialog.querySelector('.note-custom-emoji button').disabled=!valid('emoji:'+byId('noteCustomEmoji').value.trim());byId('noteEmojiPrevious').disabled=page===0;byId('noteEmojiNext').disabled=(page+1)*pageSize>=filtered.length;}
  }
  function refreshEditor(){const target=byId('noteIconPreview');if(!target)return;target.removeAttribute('data-icon');target.replaceChildren(render(typeof activeNote==='function'?activeNote()?.icon:null));}
  document.addEventListener('click',event=>{
    if(event.target.closest('#menuButton'))refreshEditor();const button=event.target.closest('#noteIconButton');if(!button||typeof activeNote!=='function'||!activeNote())return;
    const note={...activeNote()};closePanels();open(note,{owner:window.JotWorkspace?document.querySelector('.workspace-tab [aria-selected=true]'):byId('menuButton')});
  });
  return {defaultValue,entries,catalogueVersion:data.version,valid,normalize,emojiFor,render,filter,open,choose,refreshEditor};
})();

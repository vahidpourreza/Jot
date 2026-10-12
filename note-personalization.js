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
  const searchText=value=>String(value).normalize('NFKD').replace(/\p{M}/gu,'').toLowerCase().replaceAll('ي','ی').replaceAll('ك','ک').replace(/[_:\-\u200c]+/g,' ').replace(/\s+/g,' ').trim();
  const aliases={
    '📝':'note memo write writing یادداشت نوشتن','📖':'read reading کتاب مطالعه','📚':'books library کتاب کتابخانه',
    '💻':'code coding developer computer laptop برنامه نویسی کامپیوتر','👩‍💻':'code coding developer programmer برنامه نویس','👨‍💻':'code coding developer programmer برنامه نویس','🧑‍💻':'code coding developer programmer برنامه نویس',
    '☕':'coffee tea cafe قهوه چای کافه','❤️':'love heart قلب عشق','👍':'yes good like approve thumbs up بله خوب تایید','👎':'no dislike thumbs down نه',
    '✅':'done check tick complete success انجام تایید','❌':'no cross cancel error fail خطا لغو','💡':'idea light bulb ایده',
    '💼':'work job business office کار','📅':'calendar date schedule meeting تقویم جلسه','📌':'pin pinned important سنجاق مهم','📍':'location place مکان',
    '😊':'smile happy smiley خوشحال لبخند','😂':'laugh lol joy خنده','😢':'cry sad گریه ناراحت','🔥':'fire hot آتش','⭐':'star favorite favourite ستاره',
    '🎉':'party celebrate celebration جشن','🚀':'rocket launch موشک','⚙️':'settings gear تنظیمات','🗑️':'trash delete bin سطل حذف','🔍':'find search جستجو',
    '🇮🇷':'iran persia persian ایران فارسی','🇺🇸':'usa us america united states','🇬🇧':'uk britain england united kingdom','🇹🇷':'turkey turkiye',
  };
  const artworkKey=value=>[...value].filter(char=>char.codePointAt(0)!==0xfe0f).map(char=>char.codePointAt(0).toString(16)).join('-');
  const emojiBase=value=>value.replace(/[\u{1f3fb}-\u{1f3ff}]/gu,'');
  const entries=data.rows.map(([emoji,name,group,subgroup,version],order)=>({emoji,name,group,subgroup,version,order,
    nameSearch:searchText(name),emojiSearch:searchText(emoji),aliasSearch:searchText(aliases[emojiBase(emoji)]||''),search:searchText(name+' '+data.groups[group]+' '+subgroup+' '+(aliases[emojiBase(emoji)]||'')),
    tones:[...emoji].filter(char=>{const point=char.codePointAt(0);return point>=0x1f3fb&&point<=0x1f3ff;})}));
  const byEmoji=new Map(entries.map(entry=>[entry.emoji,entry]));
  function valid(value){return typeof value==='string'&&(value.startsWith('icon:')&&Object.hasOwn(legacy,value.slice(5))||value.startsWith('emoji:')&&accepted.has(value.slice(6)));}
  function normalize(value){return valid(value)?value:defaultValue;}
  function emojiFor(value){value=normalize(value);return value.startsWith('icon:')?legacy[value.slice(5)]:value.slice(6);}
  function artworkFor(value){return window.JotEmojiArtwork?.files[artworkKey(emojiFor(value))]||null;}
  function render(value){
    const emoji=emojiFor(value),span=document.createElement('span');span.className='note-symbol note-emoji';span.ariaHidden='true';
    const file=artworkFor(value);if(!file){span.textContent=emoji;return span;}
    const image=document.createElement('img');image.src='assets/emoji/artwork/'+file;image.alt='';image.draggable=false;image.decoding='async';
    const label=document.createElement('span');label.hidden=true;label.textContent=emoji;
    span.dataset.artwork='true';span.append(image,label);return span;
  }
  function filter(query='',category='all',tone='all'){
    const normalized=searchText(query),words=normalized.split(' ').filter(Boolean);
    const matches=entries.filter(entry=>(category==='all'||entry.group===Number(category))&&(tone==='all'||tone==='default'&&!entry.tones.length||entry.tones.includes(tone))&&words.every(word=>entry.search.includes(word)||entry.emojiSearch.includes(word)));
    if(!words.length)return matches;
    const score=entry=>entry.emoji===query.trim()?10000:entry.nameSearch===normalized?9000:entry.nameSearch.startsWith(normalized)?8000:words.length===1&&entry.aliasSearch.split(' ').includes(normalized)?6000:words.reduce((sum,word)=>sum+(entry.nameSearch.split(' ').includes(word)?200:entry.nameSearch.includes(word)?100:10),0)-entry.tones.length;
    return matches.sort((a,b)=>score(b)-score(a)||a.order-b.order);
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
    const grid=element('div','note-icon-choices');grid.id='noteEmojiResults';grid.role='group';grid.ariaLabel='Emoji results';dialog.append(grid);
    const empty=element('p','note-emoji-empty','No emojis match. Try a name such as “book”, “heart”, or “coffee”.');empty.id='noteEmojiEmpty';empty.hidden=true;dialog.append(empty);
    const paging=element('div','note-emoji-paging'),count=element('span');count.id='noteEmojiCount';count.role='status';count.ariaLive='polite';
    const previous=element('button','icon-button');previous.id='noteEmojiPrevious';previous.type='button';previous.ariaLabel='Previous emoji page';previous.append(JotDesign.icon('chevron-left'));previous.onclick=()=>setPage(page-1);
    const next=element('button','icon-button');next.id='noteEmojiNext';next.type='button';next.ariaLabel='Next emoji page';next.append(JotDesign.icon('chevron-right'));next.onclick=()=>setPage(page+1);paging.append(count,previous,next);dialog.append(paging);
    const preview=element('p','note-emoji-preview');preview.id='noteEmojiPreview';dialog.append(preview);
    dialog.append(element('p','note-icon-hint',`All ${entries.length.toLocaleString('en-US')} emojis · Search names, aliases, or a skin tone. Artwork works offline.`));
    const error=element('p');error.id='noteIconError';error.role='alert';error.hidden=true;dialog.append(error);
    const status=element('p','note-icon-hint');status.id='noteIconStatus';status.role='status';status.hidden=true;dialog.append(status);
    dialog.addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    dialog.addEventListener('close',()=>{clearTimeout(searchTimer);const owner=context?.owner;context=null;if(owner?.isConnected)owner.focus({preventScroll:true});});
    dialog.addEventListener('keydown',navigate);document.body.append(dialog);
  }
  function preview(entry){byId('noteEmojiPreview').replaceChildren(render('emoji:'+entry.emoji),element('span','',entry.name));}
  function refreshResults(){clearTimeout(searchTimer);filtered=filter(byId('noteEmojiSearch').value);setPage(0);}
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
    byId('noteIconError').hidden=true;byId('noteEmojiSearch').value='';refreshResults();
    const current=emojiFor(note.icon);preview(byEmoji.get(current)||{emoji:current,name:'Current emoji'});
    if(!dialog.open)dialog.showModal();byId('noteEmojiSearch').focus({preventScroll:true});
  }
  async function choose(value){
    if(!context||busy||!value.startsWith('emoji:')||!valid(value))return;
    const current=context;busy=true;dialog.ariaBusy='true';dialog.querySelectorAll('button,input,select').forEach(item=>item.disabled=true);byId('noteIconStatus').textContent='Saving emoji…';byId('noteIconStatus').hidden=false;
    const target=dialog.querySelector('[data-note-icon="'+CSS.escape(value)+'"]');target?.classList.add('pending');
    try{
      if(current.onSave)await current.onSave(value,current.note);
      else{if(typeof activeNote==='function'&&activeNote()?.id===current.note.id)await saveNow();await JotBridge.request('note-metadata',{id:current.note.id,icon:value});}
      if(typeof activeNote==='function'&&activeNote()?.id===current.note.id)activeNote().icon=value;
      document.dispatchEvent(new CustomEvent('jot-note-icon',{detail:{id:current.note.id,icon:value}}));refreshEditor();dialog.close();
    }catch(error){JotBridge.reportError(error,'note-icon');const label=byId('noteIconError');label.textContent=error.message||'The emoji could not be saved.';label.hidden=false;}
    finally{busy=false;dialog.removeAttribute('aria-busy');byId('noteIconStatus').hidden=true;target?.classList.remove('pending');dialog.querySelectorAll('button,input').forEach(item=>item.disabled=false);byId('noteEmojiPrevious').disabled=page===0;byId('noteEmojiNext').disabled=(page+1)*pageSize>=filtered.length;}
  }
  function refreshEditor(){const target=byId('noteIconPreview');if(!target)return;target.removeAttribute('data-icon');target.replaceChildren(render(typeof activeNote==='function'?activeNote()?.icon:null));}
  document.addEventListener('click',event=>{
    if(event.target.closest('#menuButton'))refreshEditor();const button=event.target.closest('#noteIconButton');if(!button||typeof activeNote!=='function'||!activeNote())return;
    const note={...activeNote()};closePanels();open(note,{owner:window.JotWorkspace?document.querySelector('.workspace-tab [aria-selected=true]'):byId('menuButton')});
  });
  return {defaultValue,entries,catalogueVersion:data.version,valid,normalize,emojiFor,artworkFor,render,filter,open,choose,refreshEditor};
})();

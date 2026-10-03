'use strict';
window.JotDocumentHeading=(()=>{
  const drafts=new Map();
  const heading=document.createElement('header');heading.id='documentHeading';heading.className='document-heading';
  const emoji=document.createElement('button');emoji.id='documentEmoji';emoji.type='button';emoji.className='document-emoji';emoji.title=emoji.ariaLabel='Change note emoji';
  const title=document.createElement('textarea');title.id='documentTitle';title.className='document-title';title.rows=1;title.maxLength=140;title.placeholder='Untitled';title.ariaLabel='Note title';title.dir='auto';title.spellcheck=false;
  const feedback=document.createElement('div');feedback.className='document-title-feedback';feedback.hidden=true;
  const message=document.createElement('span');message.role='status';
  const retry=document.createElement('button');retry.type='button';retry.textContent='Retry';retry.hidden=true;
  feedback.append(message,retry);heading.append(emoji,title,feedback);document.getElementById('writingArea').prepend(heading);
  const fileIdentity=document.getElementById('fileIdentity');if(fileIdentity)heading.append(fileIdentity);
  let currentId=null,timer=0,chain=Promise.resolve(),serial=0,titleComposing=false;
  const current=()=>typeof activeNote==='function'&&(!window.JotWorkspace||JotWorkspace.view==='note')?activeNote():null;
  function resize(){title.style.height='0px';title.style.height=Math.min(120,Math.max(32,title.scrollHeight))+'px';}
  function status(text,error=false){feedback.hidden=!text;feedback.dataset.error=String(error);message.textContent=text||'';retry.hidden=!error;if(text==='Saving title…')heading.ariaBusy='true';else heading.removeAttribute('aria-busy');}
  function render(){
    const note=current();if(!note)return;
    if(currentId!==note.id){currentId=note.id;titleComposing=false;status('');}
    const draft=drafts.get(note.id);if(draft)note.title=draft.title;
    const value=draft?.title??note.title??'';
    if(!titleComposing&&title.value!==value){title.value=value;resize();}
    if(!title.style.height)resize();
    if(!emoji.firstChild||emoji.dataset.value!==(note.icon||'')){emoji.dataset.value=note.icon||'';emoji.replaceChildren(JotNoteIcons.render(note.icon));}
  }
  function update(){
    const note=current();if(!note||note.id!==currentId)return;
    const value=title.value.replace(/[\r\n]+/g,' ').slice(0,140);if(value!==title.value)title.value=value;
    if(value===note.title&&!drafts.has(note.id))return;
    if(drafts.get(note.id)?.title===value)return;
    note.title=value;drafts.set(note.id,{title:value,serial:++serial});resize();status('');
    window.JotNoteFiles?.edited(note.id);
    clearTimeout(timer);timer=setTimeout(()=>flush().catch(()=>{}),400);
  }
  function flush(){
    if(current()?.id===currentId&&title.value!==(current().title||''))update();
    clearTimeout(timer);
    const save=async()=>{
      while(drafts.size){
        const [id,draft]=drafts.entries().next().value;
        if(currentId===id)status('Saving title…');
        try{
          await JotBridge.request('note-metadata',{id,title:draft.title});
          if(drafts.get(id)===draft)drafts.delete(id);
          window.JotNoteFiles?.edited(id);
          if(currentId===id)status('');
        }catch(error){if(currentId===id)status('Title could not be saved.',true);JotBridge.reportError(error,'note-title');throw error;}
      }
    };
    chain=chain.catch(()=>{}).then(save);return chain;
  }
  title.addEventListener('input',event=>{if(!event.isComposing)update();});
  title.addEventListener('compositionstart',()=>{titleComposing=true;});
  title.addEventListener('compositionend',()=>{titleComposing=false;update();});
  title.addEventListener('blur',()=>{titleComposing=false;if(current())update();flush().catch(()=>{});});
  title.addEventListener('keydown',event=>{
    if(event.isComposing)return;
    if(event.key==='Enter'){event.preventDefault();const id=currentId;flush().then(()=>{if(current()?.id===id&&document.activeElement===title)editor.focus({preventScroll:true});}).catch(()=>{});}
  });
  retry.onclick=()=>flush().catch(()=>{});
  emoji.onclick=()=>{const note=current();if(note)JotNoteIcons.open(note,{owner:emoji});};
  JotBridge.on(data=>{if(['note-identity','workspace-view','tabs-changed'].includes(data.event))queueMicrotask(render);});
  document.addEventListener('jot-note-icon',render);
  window.addEventListener('resize',()=>{if(!heading.closest('[hidden]'))resize();});
  JotNoteEditor.ready.then(render).catch(()=>{});
  return {render,flush,hasPending:id=>drafts.has(id),get pending(){return drafts.size>0;}};
})();

'use strict';
// Document blocks share the editor's selection, history, save and bidi pipeline.
// No second editing engine, document model or persisted UI markers are needed.
window.JotDocumentBlocks=(()=>{
  JotIconPaths.table='<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/>';
  const bar=document.getElementById('formatBar'),area=document.getElementById('writingArea');
  const button=document.createElement('button');button.id='tableButton';button.type='button';button.className='icon-button';button.title=button.ariaLabel='Insert table or edit current table';button.append(JotDesign.icon('table'));bar.append(button);
  const panel=document.createElement('div');panel.id='tableTools';panel.className='table-tools surface';panel.hidden=true;panel.tabIndex=-1;panel.role='dialog';panel.ariaLabel='Table tools';document.body.append(panel);
  button.setAttribute('aria-haspopup','dialog');button.setAttribute('aria-controls',panel.id);button.ariaExpanded='false';
  let state=null,mutating=false;
  function available(){return ready&&!composing&&!deleting&&!editorLockedByHost&&!document.querySelector('dialog[open]')&&(!window.JotWorkspace||JotWorkspace.view==='note');}
  function element(node){return node?.nodeType===Node.ELEMENT_NODE?node:node?.parentElement;}
  function cellAtSelection(){const s=getSelection(),cell=element(s.anchorNode)?.closest('td,th');return s.rangeCount&&cell&&editor.contains(cell)&&cell.contains(s.focusNode)?cell:null;}
  function focusCell(cell,atEnd=false){editor.focus({preventScroll:true});const range=document.createRange();range.selectNodeContents(cell.querySelector('p,div')||cell);range.collapse(!atEnd);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();cell.scrollIntoView({block:'nearest',inline:'nearest'});}
  function close(restore=false){const old=state;state=null;panel.hidden=true;button.ariaExpanded='false';if(restore&&old?.id===model.activeId&&available()){if(old.owner?.isConnected)old.owner.focus({preventScroll:true});else restoreSelection();}}
  function position(owner,rect){const r=rect||owner?.getBoundingClientRect()||bar.getBoundingClientRect();panel.style.maxHeight=Math.max(100,innerHeight-70)+'px';panel.style.left=Math.max(6,Math.min(r.left,innerWidth-panel.offsetWidth-6))+'px';panel.style.top=Math.max(6,Math.min(r.bottom+6,innerHeight-panel.offsetHeight-6))+'px';}
  function title(text){const label=document.createElement('strong');label.className='table-tools-title';label.textContent=text;panel.append(label);}
  function action(id,label,callback,disabled=false){const b=document.createElement('button');b.type='button';b.className='table-tool-action';b.dataset.tableAction=id;b.textContent=label;b.disabled=disabled;b.onclick=callback;panel.append(b);return b;}
  function transaction(change){
    if(!available())return false;
    flushTypingHistory();restoreSelection();rememberHistorySelection();
    const before=editor.innerHTML,selection=editorSelectionState();mutating=true;
    function rollback(){
      if(editor.innerHTML===before)return;
      editor.innerHTML=before;bookmark=null;bookmarkDirection=null;
      if(!restoreEditorSelection(selection))restoreSelection();updateEmpty();refreshToolbar();
    }
    try{
      let changed;try{changed=change();}catch(error){rollback();throw error;}
      if(changed===false){rollback();return false;}
      rememberSelection();onEdit('command');refreshToolbar();return true;
    }
    finally{mutating=false;window.JotWritingTools?.schedule();}
  }
  function makeCell(header=false){const cell=document.createElement(header?'th':'td'),p=document.createElement('p');p.dir='auto';p.append(document.createElement('br'));cell.append(p);return cell;}
  function simpleTable(table){const rows=[...table.rows],width=rows[0]?.cells.length;return !!width&&rows.every(row=>row.cells.length===width&&[...row.cells].every(cell=>cell.colSpan===1&&cell.rowSpan===1&&!cell.querySelector('table')));}
  function insertTable(rows=3,columns=3,context=null){
    rows=Number(rows);columns=Number(columns);
    if(!Number.isInteger(rows)||rows<1||rows>20||!Number.isInteger(columns)||columns<1||columns>12||context&&context.id!==model.activeId)return false;
    if(cellAtSelection())return false;
    if(context?.slash&&(!editor.contains(context.slash.range.commonAncestorContainer)||context.slash.range.toString()!==context.slash.text))return false;
    const inserted=transaction(()=>{
      if(context?.slash){getSelection().removeAllRanges();getSelection().addRange(context.slash.range);getSelection().getRangeAt(0).deleteContents();}
      if(cellAtSelection())return false;
      const table=document.createElement('table'),head=document.createElement('thead'),body=document.createElement('tbody');
      for(let row=0;row<rows;row++){const tr=document.createElement('tr');for(let column=0;column<columns;column++)tr.append(makeCell(row===0));(row===0?head:body).append(tr);}
      table.append(head,body);
      // A temporary marker locates the browser-inserted copy; it is removed
      // before onEdit records HTML or any save can observe it.
      table.dataset.jotInsertion='table';document.execCommand('insertHTML',false,table.outerHTML+'<p dir="auto"><br></p>');
      const inserted=editor.querySelector('table[data-jot-insertion="table"]');if(!inserted)return false;
      inserted.removeAttribute('data-jot-insertion');focusCell(inserted.rows[0].cells[0]);return true;
    });
    if(inserted)close();return inserted;
  }
  function openTable({owner=null,slash=null,rect=null}={}){
    if(!available())return;
    rememberSelection();window.JotWritingTools?.hide();window.JotEditorMenu?.close();
    state={id:model.activeId,owner,slash};panel.replaceChildren();title('Insert table');
    const hint=document.createElement('p');hint.className='table-tools-hint';hint.textContent='Choose rows and columns. The first row is a heading.';panel.append(hint);
    const form=document.createElement('form');form.className='table-size-form';
    function field(name,value,max){const label=document.createElement('label');label.textContent=name;const input=document.createElement('input');input.type='number';input.min='1';input.max=String(max);input.step='1';input.value=String(value);input.required=true;input.setAttribute('aria-label',name);input.dataset.tableSize=name.toLowerCase();label.append(input);form.append(label);return input;}
    const rows=field('Rows',3,20),columns=field('Columns',3,12),submit=document.createElement('button');submit.type='submit';submit.className='table-insert-button';submit.textContent='Insert table';form.append(submit);panel.append(form);
    const preview=document.createElement('div');preview.className='table-size-preview';preview.setAttribute('aria-hidden','true');
    for(let i=0;i<36;i++){const square=document.createElement('span');preview.append(square);}
    function paint(){for(const [i,square] of [...preview.children].entries())square.dataset.selected=String(Math.floor(i/6)<Number(rows.value)&&i%6<Number(columns.value));}
    rows.oninput=columns.oninput=paint;paint();panel.append(preview);
    form.onsubmit=event=>{event.preventDefault();if(!state||!form.reportValidity())return;insertTable(rows.value,columns.value,state);};
    panel.hidden=false;button.ariaExpanded='true';position(owner,rect);rows.focus({preventScroll:true});rows.select();
  }
  function removeTable(table){const p=document.createElement('p');p.dir='auto';p.append(document.createElement('br'));table.replaceWith(p);focusCell(p);}
  function tableAction(name,cell=cellAtSelection()){
    if(!available()||!cell||!editor.contains(cell))return false;
    const table=cell.closest('table');if(name!=='delete-table'&&!simpleTable(table))return false;
    const rows=[...table.rows],row=cell.parentElement,rowIndex=rows.indexOf(row),column=cell.cellIndex;
    const changed=transaction(()=>{
      if(!editor.contains(cell))return false;
      if(name==='delete-table'||name==='delete-row'&&rows.length===1||name==='delete-column'&&row.cells.length===1){removeTable(table);return true;}
      if(name==='row-before'||name==='row-after'){
        const next=document.createElement('tr');for(let i=0;i<row.cells.length;i++)next.append(makeCell(name==='row-before'&&row.parentElement.tagName==='THEAD'));
        // Adding below the heading starts/extends the body rather than
        // accidentally creating another heading row.
        if(row.parentElement.tagName==='THEAD'&&name==='row-after'){let body=table.tBodies[0];if(!body){body=document.createElement('tbody');table.append(body);}body.prepend(next);}
        else if(name==='row-before')row.before(next);else row.after(next);
        focusCell(next.cells[column]);
      }else if(name==='column-before'||name==='column-after'){
        for(const r of rows){const next=makeCell(r.cells[column].tagName==='TH');if(name==='column-before')r.cells[column].before(next);else r.cells[column].after(next);}
        focusCell(row.cells[column+(name==='column-after'?1:0)]);
      }else if(name==='delete-row'){row.remove();focusCell(table.rows[Math.min(rowIndex,table.rows.length-1)].cells[column]);}
      else if(name==='delete-column'){for(const r of rows)r.cells[column].remove();focusCell(row.cells[Math.min(column,row.cells.length-1)]);}
      else return false;
      return true;
    });
    if(changed)close();return changed;
  }
  function openOptions(cell){
    rememberSelection();window.JotWritingTools?.hide();window.JotEditorMenu?.close();state={id:model.activeId,owner:button,cell};panel.replaceChildren();title('Table');
    const simple=simpleTable(cell.closest('table'));
    for(const [id,label] of [['row-before','Add row above'],['row-after','Add row below'],['column-before','Add column before'],['column-after','Add column after'],['delete-row','Delete row'],['delete-column','Delete column'],['delete-table','Delete table']])action(id,label,()=>{if(state?.id===model.activeId)tableAction(id,state.cell);},!simple&&id!=='delete-table');
    if(!simple){const hint=document.createElement('p');hint.className='table-tools-hint';hint.textContent='Merged or uneven tables can be edited, but their row and column structure is preserved.';panel.append(hint);}
    const hint=document.createElement('p');hint.className='table-tools-hint';hint.textContent='Tab / Shift+Tab moves between cells. Tab in the last cell adds a row.';panel.append(hint);
    panel.hidden=false;button.ariaExpanded='true';position(button);panel.querySelector('button:not(:disabled)')?.focus({preventScroll:true});
  }
  button.addEventListener('pointerdown',event=>{rememberSelection();event.preventDefault();});
  button.onclick=()=>{if(!panel.hidden){close(true);return;}if(!available())return;const cell=cellAtSelection();if(cell)openOptions(cell);else openTable({owner:button});};
  function insertDivider(){return transaction(()=>{document.execCommand('insertHTML',false,'<hr><p dir="auto"><br></p>');});}
  function inlineCode(){return transaction(()=>{
    const s=getSelection(),range=s.getRangeAt(0),start=element(range.startContainer),end=element(range.endContainer),existing=start.closest('code');
    if(existing&&existing.contains(range.endContainer)){
      const first=existing.firstChild,last=existing.lastChild;if(!first)return false;existing.replaceWith(...existing.childNodes);range.setStartBefore(first);range.setEndAfter(last);
    }else{
      const block=start.closest('p,div,li,h1,h2,h3,blockquote,td,th');if(block!==end.closest('p,div,li,h1,h2,h3,blockquote,td,th')||start.closest('pre'))return false;
      const code=document.createElement('code');code.dir='ltr';code.append(range.collapsed?document.createTextNode('code'):range.extractContents());range.insertNode(code);range.selectNodeContents(code);
    }
    s.removeAllRanges();s.addRange(range);return true;
  });}
  function isInlineCode(){const s=getSelection(),code=element(s.anchorNode)?.closest('code');return !!code&&editor.contains(code)&&code.contains(s.focusNode);}
  editor.addEventListener('keydown',event=>{
    if(event.defaultPrevented||event.key!=='Tab'||event.ctrlKey||event.metaKey||event.altKey||event.isComposing||!available())return;
    const cell=cellAtSelection();if(!cell)return;const table=cell.closest('table'),cells=[...table.rows].flatMap(row=>[...row.cells]),index=cells.indexOf(cell),next=cells[index+(event.shiftKey?-1:1)];
    if(next){event.preventDefault();focusCell(next,event.shiftKey);}
    else if(!event.shiftKey&&simpleTable(table)){event.preventDefault();if(tableAction('row-after',cell)){focusCell(table.rows[table.rows.length-1].cells[0]);rememberHistorySelection();}}
    // Shift+Tab from the first cell, and Tab from a merged table's final
    // cell, retain the browser's normal focus-exit behavior.
  });
  panel.addEventListener('keydown',event=>{
    if(event.key==='Escape'){event.preventDefault();event.stopPropagation();close(true);return;}
    if(!state?.cell)return;
    const controls=[...panel.querySelectorAll('button:not(:disabled)')];let index=controls.indexOf(document.activeElement);
    if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();index=event.key==='Home'?0:event.key==='End'?controls.length-1:(index+(event.key==='ArrowDown'?1:controls.length-1))%controls.length;controls[index]?.focus();}
  });
  document.addEventListener('pointerdown',event=>{if(!event.target.closest('#tableTools,#tableButton'))close();},true);
  document.addEventListener('focusin',event=>{if(!event.target.closest('#tableTools,#tableButton'))close();});
  editor.addEventListener('compositionstart',()=>close());area.addEventListener('scroll',()=>close(),{passive:true});window.addEventListener('resize',()=>close());window.addEventListener('blur',()=>close());
  JotBridge.on(data=>{if(['workspace-view','prepare-quit','flush','active-window'].includes(data.event))close();});
  return {openTable,insertTable,tableAction,insertDivider,inlineCode,isInlineCode,close,get mutating(){return mutating;}};
})();

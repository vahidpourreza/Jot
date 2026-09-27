'use strict';
window.contextFinished=false;window.contextResults=[];
(async()=>{
  const check=(name,passed)=>contextResults.push({name:'context-'+name,passed:!!passed});
  const oldHtml=editor.innerHTML,oldHistory=histories.get(model.activeId),oldTheme=model.prefs.theme;
  const select=(backward=false)=>{editor.focus();const r=document.createRange();r.selectNodeContents(editor);const s=getSelection();s.removeAllRanges();s.addRange(r);if(backward)s.setBaseAndExtent(editor,editor.childNodes.length,editor,0);rememberSelection();};
  const open=()=>JotEditorMenu.open(innerWidth-2,innerHeight-2,null,true);
  const key=value=>document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:value,bubbles:true}));
  try{
    flushTypingHistory();editor.innerHTML='<p>Currency حذف شد.</p><p>Second line</p>';bookmark=null;onEdit('command');select(true);open();
    const m=$('editorMenu');check('menu-and-shortcuts',m.role==='menu'&&m.querySelectorAll('[role=menuitem]').length===7&&m.textContent.includes('Ctrl+C'));
    check('home-navigation-with-icon-no-shortcut',m.querySelector('[data-action=home] span').textContent==='Home'&&!!m.querySelector('[data-action=home] svg')&&!m.querySelector('[data-action=home] kbd').textContent);
    const r=m.getBoundingClientRect();check('viewport-bounded',r.left>=5&&r.top>=5&&r.right<=innerWidth-5&&r.bottom<=innerHeight-5);
    key('End');check('keyboard-end',document.activeElement.dataset.action==='home');key('ArrowDown');check('keyboard-wrap',document.activeElement===m.querySelector('button:not(:disabled)'));
    key('Escape');check('escape-restores-backward-selection',m.hidden&&getSelection().anchorNode===editor&&getSelection().anchorOffset===editor.childNodes.length&&getSelection().focusOffset===0);
    editor.dispatchEvent(new KeyboardEvent('keydown',{key:'F10',shiftKey:true,bubbles:true}));check('shift-f10-opens',!m.hidden);key('Tab');check('tab-dismisses',m.hidden);
    for(const theme of ['dark','light']){
      JotDesign.apply({...model.prefs,theme});open();check('distinct-'+theme+'-context-surface',getComputedStyle(m).backgroundColor!==getComputedStyle(app).backgroundColor);
      JotEditorMenu.close();setNoteMenuVisible(true);check('distinct-'+theme+'-more-surface',getComputedStyle($('menu')).backgroundColor!==getComputedStyle(app).backgroundColor);closePanels();
    }
    select();open();await JotEditorMenu.run('copy');check('copy-keeps-mixed-text',editor.textContent==='Currency حذف شد.Second line');
    select();open();await JotEditorMenu.run('cut');check('cut-removes-selection',!editor.textContent.trim());
    undo();check('cut-undo-restores',editor.textContent==='Currency حذف شد.Second line');undo(true);check('cut-redo',!editor.textContent.trim());undo();
    select();open();await JotEditorMenu.run('paste');check('paste-plain-with-bidi',editor.textContent==='Money.Currency حذف شد.'&&!editor.querySelector('pre,code,b,[style]')&&editor.firstElementChild.dir==='rtl');
    open();await JotEditorMenu.run('select-all');check('select-all-covers-text',getSelection().toString()===editor.innerText);
    const c=document.createElement('canvas');c.width=600;c.height=200;const src=c.toDataURL();
    editor.innerHTML='<p>Before <img src="'+src+'"> after</p>';bookmark=null;onEdit('command');const image=editor.querySelector('img');
    JotEditorMenu.open(100,80,image);check('image-actions',!!m.querySelector('[data-action=copy-image]')&&!!m.querySelector('[data-action=open-image]'));
    await JotEditorMenu.run('copy-image');check('image-copy-does-not-resize',editor.querySelector('img').src===src);
    JotEditorMenu.open(100,80,image);await JotEditorMenu.run('remove-image');check('remove-image-only',!editor.querySelector('img')&&editor.textContent==='Before  after');undo();check('remove-image-undo-original',editor.querySelector('img')?.src===src);
    select();open();document.body.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true}));check('outside-click-dismisses',m.hidden);
    open();window.dispatchEvent(new Event('resize'));check('resize-dismisses',m.hidden);
    select();getSelection().collapseToStart();rememberSelection();open();check('empty-selection-disables-copy-cut',m.querySelector('[data-action=copy]').disabled&&m.querySelector('[data-action=cut]').disabled&&!m.querySelector('[data-action=paste]').disabled);
  }catch(e){contextResults.push({name:'context-test-exception',passed:false,detail:e.message});}
  finally{
    JotEditorMenu.close();flushTypingHistory();editor.innerHTML=oldHtml;bookmark=null;histories.set(model.activeId,oldHistory);onEdit('command');JotDesign.apply({...model.prefs,theme:oldTheme});await saveNow();window.contextFinished=true;
  }
})();

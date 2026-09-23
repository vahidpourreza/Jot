(async()=>{
  window.testResults=[];
  const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const assert=(condition,message)=>{if(!condition)throw new Error(message);};
  async function test(name,run){const start=performance.now();try{await run();testResults.push({name,passed:true,elapsedMs:Math.round(performance.now()-start)});}catch(e){testResults.push({name,passed:false,error:e.message});}}
  function write(html){editor.innerHTML=html;editor.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText'}));bookmark=null;}
  function select(text){const walker=document.createTreeWalker(editor,NodeFilter.SHOW_TEXT);let node;while(node=walker.nextNode()){const i=node.textContent.indexOf(text);if(i>=0){const range=document.createRange();range.setStart(node,i);range.setEnd(node,i+text.length);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();return;}}throw new Error('selection missing');}
  await test('toolbar-visible-by-default-and-stays-visible',()=>{assert(!$('formatBar').hidden,'hidden toolbar');write('<p>Typing with tools</p>');assert(!$('formatBar').hidden,'typing hid toolbar');});
  await test('blank-note-has-no-placeholder-and-keeps-bidi-caret',()=>{
    write('<p><br></p>');setInputDirection('rtl');
    assert(!$('placeholder')&&!$('saveState')&&!$('saveLabel'),'placeholder or save badge remains');
    assert(getComputedStyle(editor.firstElementChild).direction==='rtl','Persian empty caret');
    setInputDirection('ltr');
    assert(getComputedStyle(editor.firstElementChild).direction==='ltr','English empty caret');
    write('<p>سلام mixed text</p><p>English متن</p>');setInputDirection('rtl');
    assert(getComputedStyle(editor.children[0]).direction==='rtl'&&getComputedStyle(editor.children[1]).direction==='ltr','keyboard overwrote existing text directions');
  });
  await test('note-header-has-left-add-right-controls-and-no-app-icon',()=>{
    const buttons=[...document.querySelectorAll('#handle .window-actions>button')];
    assert(buttons.map(button=>button.id).join(',')==='menuButton,pinButton,hideButton','wrong button order');
    const bounds=buttons.map(button=>button.getBoundingClientRect());
    assert(bounds.every((rect,i)=>i===0||rect.left>bounds[i-1].left),'wrong control ordering');
    const add=$('newButton').getBoundingClientRect(),header=$('handle').getBoundingClientRect();
    assert(add.left-header.left<12&&add.right<bounds[0].left,'add is not at left');
    assert(header.right-bounds.at(-1).right<12,'close is not at the right edge');
    assert(!$('closeAppButton')&&!$('quitButton')&&!document.querySelector('#handle .drag-grip'),'obsolete note controls');
    assert($('hideButton').dataset.icon==='x','hide must use X');
    assert(!$('menuButton').closest('.quiet-footer'),'More still in footer');
    assert(!$('notesButton')&&!document.querySelector('#handle [data-icon=jot]'),'note app icon remains');
    assert([...document.querySelectorAll('#handle button')].map(button=>button.id).join(',')==='newButton,menuButton,pinButton,hideButton','keyboard order differs from visual order');
  });
  await test('sticky-menu-styling-preserves-every-existing-option',()=>{
    $('menuButton').click();
    const panel=$('menu').getBoundingClientRect(),bounds=app.getBoundingClientRect();
    assert(!$('menu').hidden&&panel.top===bounds.top&&panel.left===bounds.left&&panel.width===bounds.width,'menu is not a full-width top sheet');
    assert(panel.left>=bounds.left&&panel.right<=bounds.right&&panel.bottom<=bounds.bottom,'menu outside window');
    const ids=['menuNotesButton','themeButton','smallerButton','largerButton','exportButton','copyButton','deleteButton'];
    assert(ids.every(id=>$(id)?.closest('#menuActions')),'an existing option was removed or relocated');
    assert(!$('appearanceButton')&&!$('copyTextButton'),'removed note options remain');
    assert(!$('menu').querySelector('.note-palette'),'old inset palette remains');
    assert(!$('menu').textContent.includes('Quit Jot'),'shutdown remains in note menu');
    editor.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true}));
    assert($('menu').hidden,'menu did not dismiss');
  });
  await test('all-nineteen-colors-visible-without-scrolling',async()=>{
    $('menuButton').click();
    await sleep(300);
    const buttons=[...$('noteColors').querySelectorAll('button')],rects=buttons.map(button=>button.getBoundingClientRect());
    const palette=$('noteColors').getBoundingClientRect();
    assert(buttons.length===19&&new Set(buttons.map(button=>button.dataset.noteColor)).size===19,'missing or duplicate color');
    assert(buttons.map(b=>b.dataset.noteColor).join(',')==='crimson,red,orange,amber,yellow,lime,green,emerald,teal,cyan,sky,blue,indigo,violet,purple,fuchsia,pink,rose,neutral','palette is not ordered by color family');
    assert(new Set(rects.map(r=>r.top)).size===1,'palette is not a single row');
    assert(rects.every(r=>r.left>=palette.left&&r.right<=palette.right+.5&&r.top>=palette.top&&r.bottom<=palette.bottom),'a color is outside the visible palette');
    assert(buttons.every((button,i)=>button.contains(document.elementFromPoint(rects[i].x+rects[i].width/2,rects[i].y+rects[i].height/2))),'a color is covered or unreachable');
    assert(rects.every(r=>r.width>=15&&r.height===40),'color targets too small');
    assert($('noteColors').scrollWidth<=$('noteColors').clientWidth&&$('noteColors').scrollHeight<=$('noteColors').clientHeight,'palette requires scrolling');
    const current=buttons.find(button=>button.getAttribute('aria-pressed')==='true');
    assert(document.activeElement===current,'selected color was not focused');
    current.dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}));
    assert(document.activeElement===buttons.at(-1)&&$('noteColors').scrollLeft===0,'last color cannot be reached without scrolling');
    document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Home',bubbles:true}));
    assert(document.activeElement===buttons[0],'first color cannot be reached');
    buttons[0].dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));
    assert(document.activeElement===buttons[1],'right arrow does not follow palette order');
    buttons[1].dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}));
    assert(document.activeElement===buttons[0],'left arrow does not follow palette order');
    $('noteColors').dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true}));
    assert($('noteColors').scrollLeft===0,'wheel moved colors offscreen');
    const initialColor=activeNote().color,html=editor.innerHTML;
    buttons.at(-1).click();
    for(let i=0;i<100&&activeNote().color!==buttons.at(-1).dataset.noteColor;i++)await sleep(15);
    assert(activeNote().color===buttons.at(-1).dataset.noteColor&&editor.innerHTML===html,'palette changed content or failed to select');
    await setNoteColor(initialColor);
    document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}));
    assert($('menu').hidden&&document.activeElement===$('menuButton'),'Escape did not close menu and restore focus');
  });
  await test('restored-more-options-theme-and-size-work',async()=>{
    assert(['copyButton','exportButton','smallerButton','largerButton','themeButton'].every(id=>$(id).closest('#menuActions')),'options missing from More');
    const theme=model.prefs.theme;$('themeButton').click();
    for(let i=0;i<100&&(await request('load')).prefs.theme===theme;i++)await sleep(10);
    assert(model.prefs.theme!==theme,'restored theme button failed');
    await setPreference({theme});
    const previous=model.prefs.fontSize;$('largerButton').click();
    for(let i=0;i<100&&model.prefs.fontSize!==previous+1;i++)await sleep(10);
    assert(model.prefs.fontSize===previous+1,'restored text-size action failed');
    await setPreference({fontSize:previous});
    $('menuButton').click();$('copyButton').click();await sleep(160);
    assert($('menu').hidden,'copy did not dismiss More menu');
  });
  await test('more-labels-describe-the-action-and-theme-target',async()=>{
    assert($('menuNotesButton').textContent.trim()==='All notes','notes label');
    assert($('copyButton').textContent.trim()==='Copy'&&$('copyButton').title.includes('images'),'rich-copy label');
    assert(!$('copyTextButton'),'plain-copy option remains');
    assert($('exportButton').textContent.trim()==='Export'&&$('deleteButton').textContent.trim()==='Delete','menu labels are not concise');
    assert(getComputedStyle($('themeButton')).fontSize==='12px','theme text is not compact');
    assert(document.querySelector('.size-label').textContent==='Font size'&&$('fontSize').textContent.endsWith(' px'),'font-size units missing');
    const theme=model.prefs.theme;
    for(const mode of ['light','dark']){
      await setPreference({theme:mode});
      assert($('themeLabel').textContent===(mode==='light'?'Dark':'Light')+' mode','theme label does not match action');
    }
    await setPreference({theme});
  });
  await test('pin-has-no-solid-background-and-footer-stays-neutral',async()=>{
    const initialTheme=model.prefs.theme;
    for(const theme of ['dark','light']){
      await setPreference({theme,coloredIcons:true});
      app.dataset.activeWindow='true';
      assert(getComputedStyle(editor).color===getComputedStyle(document.documentElement).color,'note header color leaked into writing text');
      const writingTop=$('writingArea').getBoundingClientRect().top;
      app.dataset.activeWindow='false';
      assert($('writingArea').getBoundingClientRect().top===writingTop,'inactive header shifted the writing area');
      app.dataset.activeWindow='true';
      $('pinButton').click();
      for(let i=0;i<50&&$('pinButton').getAttribute('aria-pressed')!=='true';i++)await sleep(10);
      assert($('pinButton').getAttribute('aria-pressed')==='true','pin did not toggle');
      await sleep(140);
      assert(getComputedStyle($('pinButton')).backgroundColor==='rgba(0, 0, 0, 0)','pin has solid background');
      assert($('pinButton').querySelector('path[fill="currentColor"]'),'pin does not use a filled body');
      const probe=document.createElement('span');probe.style.color='var(--muted-foreground)';app.append(probe);
      assert(getComputedStyle($('imageButton')).color===getComputedStyle(probe).color,'footer image icon is accented');
      probe.style.color='var(--foreground)';
      assert(getComputedStyle($('formatButton')).color===getComputedStyle(probe).color,'active footer format icon is accented');
      probe.remove();
      $('pinButton').click();
      for(let i=0;i<50&&$('pinButton').getAttribute('aria-pressed')!=='false';i++)await sleep(10);
      assert($('pinButton').getAttribute('aria-pressed')==='false','pin did not clear');
      assert(!$('pinButton').querySelector('path[fill="currentColor"]'),'unpin retained filled body');
    }
    await setPreference({theme:initialTheme});
  });
  await test('x-hides-current-note-after-save-and-cannot-quit-app',async()=>{
    const id=model.activeId;write('<p>Hide-note save sentinel</p>');
    $('hideButton').click();
    for(let i=0;i<100&&app.dataset.saveState!=='saved';i++)await sleep(15);
    const disk=await request('load');
    assert(disk.notes.some(note=>note.id===id&&note.html.includes('Hide-note save sentinel')),'X did not save the note');
    let rejected=false;try{await request('quit');}catch{rejected=true;}
    assert(rejected,'note window was allowed to quit the app');
  });
  await test('color-picker-opens-upward-without-extra-tools-menu',async()=>{
    assert(!$('blockMenuButton')&&!$('blockMenu')&&!$('blockLabel'),'Text style options remain');
    $('colorMenuButton').click();assert(!$('colorMenu').hidden,'color menu failed');
    await sleep(190);
    assert($('colorMenu').getBoundingClientRect().bottom<$('colorMenuButton').getBoundingClientRect().top,'color picker did not open upward');
    assert(!$('formatMoreButton')&&!$('formatMoreMenu'),'extra tools are still hidden in a menu');
    assert(document.querySelector('[data-command=strikeThrough]').closest('#formatBar')&&document.querySelector('[data-command=removeFormat]').closest('#formatBar'),'extra tools missing from row');
    assert(!$('selectedColor')&&!$('colorMenuButton').querySelector('i'),'T still has an underline indicator');
    closeFormatMenus();
  });
  await test('mixed-paragraph-directions',()=>{write('<p>English starts here فارسی</p><p>سلام English ۱۲۳</p>');const blocks=editor.querySelectorAll('p');assert(getComputedStyle(blocks[0]).direction==='ltr','LTR');assert(getComputedStyle(blocks[1]).direction==='rtl','RTL');});
  await test('formatting-controls-share-one-bottom-row',()=>{
    const footer=document.querySelector('.quiet-footer').getBoundingClientRect();
    assert($('formatBar').closest('.quiet-footer')&&$('writingArea').getBoundingClientRect().bottom<=footer.top,'toolbar is not below the editor');
    const controls=[...$('formatBar').querySelectorAll('button'),$('imageButton'),$('formatButton')];
    assert(controls.every(button=>{const r=button.getBoundingClientRect();return r.top>=footer.top&&r.bottom<=footer.bottom&&r.left>=footer.left&&r.right<=footer.right;}),'bottom controls overflow or wrap');
  });
  await test('footer-svg-icons-share-size-and-center-in-both-themes',async()=>{
    const previousTheme=model.prefs.theme;
    for(const theme of ['dark','light']){
      await setPreference({theme});
      const buttons=[...document.querySelectorAll('.quiet-footer button')],centers=[];
      for(const button of buttons){
        const svg=button.querySelector(':scope>svg');assert(svg,'icon is nested in a baseline-producing wrapper');
        const b=button.getBoundingClientRect(),s=svg.getBoundingClientRect();
        assert(s.width===16&&s.height===16,'inconsistent icon dimensions');
        assert(Math.abs(s.x+s.width/2-b.x-b.width/2)<.1&&Math.abs(s.y+s.height/2-b.y-b.height/2)<.1,'icon is not centered within its button');
        centers.push(s.y+s.height/2);
      }
      assert(Math.max(...centers)-Math.min(...centers)<.1,'icons do not share a vertical center');
      assert($('formatButton').dataset.icon==='type'&&getComputedStyle($('formatButton').querySelector('svg')).transform==='none','original unrotated T toggle is not restored');
    }
    await setPreference({theme:previousTheme});
  });
  await test('format-bold-italic-underline-color',async()=>{write('<p>Selected words</p>');select('Selected');for(const command of ['bold','italic','underline'])document.querySelector('[data-command="'+command+'"]').click();document.querySelector('[data-color="#60a5fa"]').click();assert(/font-weight: (bold|700)|<b>/.test(editor.innerHTML),'bold');assert(editor.innerHTML.includes('italic'),'italic');assert(editor.innerHTML.includes('underline'),'underline');assert(editor.innerHTML.includes('96, 165, 250'),'color');await sleep(160);assert(JotDesign.hexColor(getComputedStyle($('colorMenuButton')).color)==='#60a5fa','T glyph does not show selected text color');});
  await test('inline-strikethrough-and-clear-formatting-work',()=>{write('<p>More tools inline</p>');select('tools');document.querySelector('[data-command=strikeThrough]').click();assert(/line-through|<strike>|<s>/.test(editor.innerHTML),'strikethrough failed');document.querySelector('[data-command=removeFormat]').click();assert(!/line-through|<strike>|<s>/.test(editor.innerHTML),'clear formatting failed');});
  await test('neutral-css-before-preferences-load',()=>{
    const root=document.documentElement,original=root.style.cssText,theme=root.dataset.theme;
    try{root.style.removeProperty('--primary');for(const mode of ['dark','light']){root.dataset.theme=mode;const hex=JotDesign.hexColor(getComputedStyle(root).getPropertyValue('--primary'));assert(hex.slice(1,3)===hex.slice(3,5)&&hex.slice(3,5)===hex.slice(5,7),'startup accent is not neutral');}}
    finally{root.style.cssText=original;root.dataset.theme=theme;}
  });
  await test('bullet-list-undo-redo',()=>{write('<p>List item</p>');select('List item');document.querySelector('[data-command=insertUnorderedList]').click();assert(editor.querySelector('ul li'),'list');undo();assert(!editor.querySelector('ul'),'undo');undo(true);assert(editor.querySelector('ul'),'redo');});
  await test('paste-keeps-tables-and-formatting-removes-active-content',async()=>{const data=new DataTransfer();data.setData('text/html','<p>Before <b>bold</b></p><table><tr><th>Head</th><th>عنوان</th></tr><tr><td colspan="2">Body</td></tr></table><script>evil()</script><p onclick="evil()">After</p>');editor.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));await sleep(80);assert(editor.querySelector('table th')&&editor.querySelector('td[colspan="2"]'),'table structure lost');assert(!editor.querySelector('script,[onclick]'),'unsafe markup retained');});
  let sample;
  await test('pasted-heading-levels-and-code-structure',()=>{const html=sanitizeHtml('<h1>Title</h1><h3>Subheading</h3><pre><code>if (ok) {\n  run();\n}</code></pre>');const root=document.createElement('div');root.innerHTML=html;assert(root.querySelector('h1')&&root.querySelector('h3'),'heading hierarchy lost');assert(root.querySelector('code').textContent.includes('  run();'),'code whitespace lost');});
  await test('rich-paste-html-plus-image-preserves-order',async()=>{
    const canvas=document.createElement('canvas');canvas.width=600;canvas.height=200;const c=canvas.getContext('2d');c.fillStyle='#27272a';c.fillRect(0,0,600,200);c.fillStyle='#60a5fa';c.fillRect(26,28,5,140);c.fillStyle='#fff';c.font='25px sans-serif';c.fillText('An image inside your note',55,90);c.font='18px sans-serif';c.fillText('Click the thumbnail to see the original.',55,130);
    sample=canvas.toDataURL('image/png');const blob=await new Promise(resolve=>canvas.toBlob(resolve));
    write('<p></p>');editor.focus();bookmark=null;
    const data=new DataTransfer();data.setData('text/html','<p>Meeting notes</p><p>یادداشت فارسی با English</p><img src="cid:sample"><table><tr><td>Keep</td><td>Structure</td></tr></table><p>After image</p>');data.items.add(new File([blob],'sample.png',{type:'image/png'}));
    editor.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));
    for(let i=0;i<100&&!editor.querySelector('img');i++)await sleep(30);
    assert(editor.textContent.includes('Meeting notes')&&editor.textContent.includes('After image'),'text discarded when bitmap present');
    assert(editor.querySelector('img')&&editor.querySelector('table'),'image/table lost');
    await editor.querySelector('img').decode();await saveNow();
  });
  await test('image-thumbnail-keeps-original-resolution',()=>{const image=editor.querySelector('img');const rect=image.getBoundingClientRect();assert(rect.width<=74&&rect.height<=50,'preview too big');assert(image.naturalWidth===600,'original shrunk');image.click();});
  await test('file-image-inserts-inline-without-forcing-new-paragraph',async()=>{
    const previous=editor.innerHTML;
    write('<p>Before After</p>');select('After');getSelection().getRangeAt(0).collapse(true);rememberSelection();
    const bytes=Uint8Array.from(atob(sample.split(',')[1]),c=>c.charCodeAt(0));
    await insertImages([new File([bytes],'inline.png',{type:'image/png'})]);
    assert(editor.querySelectorAll('p').length===1&&editor.querySelector('p img')&&editor.textContent.includes('Before')&&editor.textContent.includes('After'),'image forced separate paragraph');
    write(previous);await saveNow();
  });
  await test('copy-everything-html-text-images-and-table',async()=>{
    const range=document.createRange();range.selectNodeContents(editor);getSelection().removeAllRanges();getSelection().addRange(range);
    const data=new DataTransfer();editor.dispatchEvent(new ClipboardEvent('copy',{clipboardData:data,bubbles:true,cancelable:true}));
    const html=data.getData('text/html');assert(html.includes('<table')&&html.includes('data:image/png')&&html.includes('Meeting notes'),'copy lost structure/image');assert(data.getData('text/plain').includes('After image'),'plain fallback missing');
    const copy=copyPayload(true);assert(copy.image&&copy.text.includes('Meeting notes'),'mixed copy lacks native image payload');write('<p></p>');bookmark=null;command('insertHTML',copy.html);await saveNow();
    assert(editor.querySelector('table')&&editor.querySelector('img'),'copy/paste round trip failed');
  });
  await test('toolbar-toggle-persists-and-can-reopen',async()=>{await setPreference({toolbarVisible:false});assert($('formatBar').hidden,'toggle hide');assert($('formatButton').dataset.icon==='type'&&getComputedStyle($('formatButton').querySelector('svg')).transform==='none','hidden-state T was changed or rotated');await setPreference({toolbarVisible:true});assert(!$('formatBar').hidden,'toggle show');assert(getComputedStyle($('formatButton').querySelector('svg')).transform==='none','expanded-state T was rotated');});
  await test('save-failure-keeps-draft-and-retry',async()=>{
    const id=model.activeId;activeNote().id='invalid';model.activeId='invalid';revision++;
    let rejected=false;try{await saveNow();}catch{rejected=true;}assert(rejected,'bad write accepted');
    activeNote().id=id;model.activeId=id;revision++;await saveNow();assert(editor.textContent.includes('Meeting notes'),'draft lost');
  });
  await test('clipboard-error-does-not-mark-note-unsaved',async()=>{
    await saveNow();const before=editor.innerHTML;
    showError(Object.assign(new Error('The clipboard is temporarily busy. Your note is safe.'),{operation:'clipboard-write',code:'clipboard-busy',logged:true}));
    assert(app.dataset.saveState==='saved'&&editor.innerHTML===before,'clipboard error changed save state or content');
    assert(!$('saveLabel'),'visible save state');
    await request('clipboard-write',copyPayload(true));
    assert($('error').hidden,'copy success did not dismiss clipboard error');
  });
  await test('large-note-save',async()=>{
    const html=editor.innerHTML;const start=performance.now();write('<p>'+('A mixed یادداشت line. '.repeat(3000))+'</p>');await saveNow();const elapsed=performance.now()-start;
    testResults.push({name:'large-note-timing',passed:true,characters:editor.textContent.length,elapsedMs:Math.round(elapsed)});
    write(html);await saveNow();assert(elapsed<2000,'large note slow');
  });
  await test('local-icons-fonts',async()=>{await document.fonts.ready;assert(document.fonts.check('16px IRANSansX'),'font');assert(document.querySelectorAll('svg.lucide').length>15,'icons');});
  getSelection().removeAllRanges();closePanels();await saveNow();window.testsFinished=true;
})().catch(error=>{window.testResults.push({name:'test-runner',passed:false,error:error.stack});window.testsFinished=true;});

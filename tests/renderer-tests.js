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
  await test('header-controls-left-aligned-in-requested-order',()=>{
    const buttons=[...document.querySelectorAll('#handle .window-actions>button')];
    assert(buttons.map(button=>button.id).join(',')==='notesButton,closeAppButton,hideButton,newButton,pinButton','wrong button order');
    const bounds=buttons.map(button=>button.getBoundingClientRect());
    assert(bounds[0].left<20&&bounds.every((rect,i)=>i===0||rect.left>bounds[i-1].left),'not left aligned');
    assert(document.querySelector('#notesButton [data-icon]')===null&&$('notesButton').dataset.icon==='notepad-text','wrong app icon');
  });
  await test('toolbar-menus-open-and-close',()=>{
    $('blockMenuButton').click();assert(!$('blockMenu').hidden,'style menu failed');
    $('colorMenuButton').click();assert(!$('colorMenu').hidden&&$('blockMenu').hidden,'color menu failed');
    $('formatMoreButton').click();assert(!$('formatMoreMenu').hidden&&$('colorMenu').hidden,'more menu failed');
    closeFormatMenus();
  });
  await test('mixed-paragraph-directions',()=>{write('<p>English starts here فارسی</p><p>سلام English ۱۲۳</p>');const blocks=editor.querySelectorAll('p');assert(getComputedStyle(blocks[0]).direction==='ltr','LTR');assert(getComputedStyle(blocks[1]).direction==='rtl','RTL');});
  await test('format-bold-italic-underline-color',()=>{write('<p>Selected words</p>');select('Selected');for(const command of ['bold','italic','underline'])document.querySelector('[data-command="'+command+'"]').click();document.querySelector('[data-color="#60a5fa"]').click();assert(/font-weight: (bold|700)|<b>/.test(editor.innerHTML),'bold');assert(editor.innerHTML.includes('italic'),'italic');assert(editor.innerHTML.includes('underline'),'underline');assert(editor.innerHTML.includes('96, 165, 250'),'color');});
  await test('headings-list-undo-redo',()=>{write('<p>List item</p>');select('List item');document.querySelector('[data-block=h2]').click();assert(editor.querySelector('h2'),'heading');document.querySelector('[data-block=p]').click();document.querySelector('[data-command=insertUnorderedList]').click();assert(editor.querySelector('ul li'),'list');undo();assert(!editor.querySelector('ul'),'undo');undo(true);assert(editor.querySelector('ul'),'redo');});
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
  await test('toolbar-toggle-persists-and-can-reopen',async()=>{await setPreference({toolbarVisible:false});assert($('formatBar').hidden,'toggle hide');await setPreference({toolbarVisible:true});assert(!$('formatBar').hidden,'toggle show');});
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

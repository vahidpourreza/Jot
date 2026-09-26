(async()=>{
  window.bidiResults=[];window.bidiFinished=false;
  const original=editor.innerHTML;
  const assert=(ok,message)=>{if(!ok)throw new Error(message);};
  async function test(name,run){try{await run();bidiResults.push({name,passed:true});}catch(error){bidiResults.push({name,passed:false,error:error.message});}}
  function write(text){flushTypingHistory();editor.innerHTML=plainToHtml(text);bookmark=null;onEdit('command');}
  function caret(block,offset){const point=JotBidi.pointAt(block,offset);getSelection().setBaseAndExtent(...point,...point);rememberSelection();}
  const examples=[
    '- Currency enum حذف شد.',
    '- Money.Currency حذف شد.',
    '- ورودی Currency از Money.FromAmount و Money.Zero حذف شد.',
    '- کنترل یکسان‌بودن Currency در عملیات Money حذف شد.',
    '- Invoice.Currency و CreditNote.Currency حذف شدند.',
    '- پارامتر Currency از Invoice و InvoiceLine حذف شد.',
    '- کنترل‌های Currency از تغییر پلن و محاسبه مصرف حذف شدند.',
    '- CURRENCY و CURRENCY_MISMATCH از Resources حذف شدند.'
  ];
  window.bidiExampleText=examples.join('\n\n');
  try{
    for(const [index,text] of examples.entries())await test('bidi-user-example-'+(index+1),()=>{
      write(text);const p=editor.firstElementChild;
      assert(p.textContent===text,'changed logical text');assert(p.dir==='rtl'&&getComputedStyle(p).direction==='rtl','Persian sentence not RTL');
      for(const token of JotBidi.tokens(text))assert([...p.querySelectorAll('bdi')].some(node=>node.textContent===token.text&&node.dir==='ltr'&&getComputedStyle(node).unicodeBidi==='isolate'),'English/technical unit not isolated: '+token.text);
      const copied=copyPayload(true);assert(copied.text===text,'plain Copy added characters or reordered text');
      assert(copied.html.includes('<bdi')&&!/[\u2066-\u2069\u202a-\u202e]/.test(copied.text),'direction controls leaked into copied text');
    });
    for(const text of ['Use نام as a key.','The text سلام دنیا is Persian.','English starts here فارسی','Version 2.5 is ready.','Use the label سلام دنیا.'])await test('bidi-English-prose-'+text,()=>{write(text);assert(editor.firstElementChild.dir==='ltr','English paragraph turned RTL');assert(editor.textContent===text,'English text changed');});
    const units=[
      'https://example.com/a?x=1&y=2#section','dev+test@example.com','C:\\Work\\notes.txt',
      '"C:\\Program Files\\Jot\\notes.txt"','src/components/note.tsx','Money.FromAmount(12.5)',
      '(API v2.5)','v1.2.3-beta','+98 912 123 4567','+1 (650) 555-1212','(021) 33445566','۱۴۰۵/۰۷/۰۴','١٢:٣٠','-12.5%','۱٬۲۳۴٫۵٪',
      '/usr/local/bin','/api/subscriptions?center=10&lang=fa','"/home/me/My Notes/file.txt"','--output=result.json','C#','C++','.NET 10','List<Money>','Dictionary<string, List<Money>>','Money[]'
    ];
    for(const unit of units)await test('bidi-technical-unit-'+unit,()=>{
      const text='مقدار '+unit+' ثبت شد.';write(text);
      assert(editor.firstElementChild.dir==='rtl'&&editor.textContent===text,'base direction/text changed');
      assert([...editor.querySelectorAll('bdi')].some(node=>node.textContent===unit&&node.dir==='ltr'),'unit split or not LTR isolated');
    });
    await test('bidi-ASCII-unit-visual-order',()=>{
      write('آدرس https://example.com/a?x=12&y=34 ثبت شد.');
      const token=editor.querySelector('bdi'),walker=document.createTreeWalker(token,NodeFilter.SHOW_TEXT),rects=[];let node;
      while(node=walker.nextNode())for(let i=0;i<node.length;i++){const range=document.createRange();range.setStart(node,i);range.setEnd(node,i+1);const rect=range.getBoundingClientRect();rects.push({x:rect.x,y:rect.y});}
      assert(rects.every((rect,i)=>i===0||Math.abs(rect.y-rects[i-1].y)>2||rect.x>=rects[i-1].x-.5),'ASCII URL characters visually reversed');
    });
    await test('bidi-wrapping-keeps-backward-selection-and-bookmark',()=>{
      const text='شروع Money.Currency و متن پایان';flushTypingHistory();editor.innerHTML=plainToHtml(text);
      const node=editor.firstElementChild.firstChild;getSelection().setBaseAndExtent(node,25,node,4);rememberSelection();const selected=getSelection().toString();
      normalizeDirection();assert(getSelection().toString()===selected&&bookmark.toString()===selected,'selection or formatting bookmark changed');
      const p=editor.firstElementChild,s=getSelection();assert(JotBidi.pointOffset(p,s.anchorNode,s.anchorOffset)>JotBidi.pointOffset(p,s.focusNode,s.focusOffset),'backward selection reversed');
    });
    await test('bidi-caret-does-not-jump-to-previous-paragraph',()=>{
      flushTypingHistory();editor.innerHTML='<p>Previous</p><p>Currency حذف شد.</p>';const p=editor.lastElementChild;
      getSelection().setBaseAndExtent(p.firstChild,0,p.firstChild,0);rememberSelection();normalizeDirection();
      assert(p.contains(getSelection().anchorNode)&&JotBidi.pointOffset(p,getSelection().anchorNode,getSelection().anchorOffset)===0,'caret moved into previous paragraph');
    });
    await test('bidi-isolation-spans-rich-inline-formatting',()=>{
      flushTypingHistory();editor.innerHTML='<p>آدرس https://exa<b>mple</b>.com ثبت شد.</p>';bookmark=null;onEdit('command');
      const token=[...editor.querySelectorAll('bdi')].find(node=>node.textContent==='https://example.com');
      assert(token&&token.querySelector('b')&&editor.textContent==='آدرس https://example.com ثبت شد.','inline formatting or technical unit lost');
    });
    await test('bidi-no-wrapper-nesting-or-rewrite-on-repeat',()=>{
      write('متن Money.Currency و API در نسخه 2.5');const html=editor.innerHTML,wrapper=editor.querySelector('bdi');normalizeDirection();normalizeDirection();
      assert(editor.innerHTML===html&&editor.querySelector('bdi')===wrapper&&!editor.querySelector('bdi bdi'),'repeated normalization rebuilt/nested tokens');
    });
    await test('bidi-user-direction-override-undo-save-and-auto',async()=>{
      write('Currency enum حذف شد.\nOther text');const p=editor.firstElementChild,other=editor.lastElementChild;caret(p,4);
      setParagraphDirection('ltr');assert(p.dir==='ltr'&&p.dataset.jotDirection==='ltr'&&other.dir==='ltr','manual direction did not apply only to paragraph');
      await saveNow();assert(activeNote().html.includes('data-jot-direction="ltr"'),'manual direction not saved');
      const html=sanitizeHtml(editor.innerHTML);assert(html.includes('data-jot-direction="ltr"'),'save/load sanitizer lost direction');
      undo();assert(editor.firstElementChild.dir==='rtl'&&!editor.firstElementChild.dataset.jotDirection,'Undo did not restore automatic direction');
      undo(true);assert(editor.firstElementChild.dir==='ltr','Redo did not restore explicit direction');
      caret(editor.firstElementChild,4);setParagraphDirection('auto');assert(editor.firstElementChild.dir==='rtl'&&!editor.firstElementChild.dataset.jotDirection,'Auto did not restore smart direction');
    });
    await test('bidi-selection-direction-applies-to-multiple-paragraphs',()=>{
      write('First text\nSecond text\nThird text');const first=editor.children[0],second=editor.children[1];
      const range=document.createRange();range.setStart(first,0);range.setEnd(second,second.childNodes.length);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();
      setParagraphDirection('rtl');assert(editor.children[0].dataset.jotDirection==='rtl'&&editor.children[1].dataset.jotDirection==='rtl'&&!editor.children[2].dataset.jotDirection,'override leaked outside selection');
    });
    await test('bidi-direction-control-handles-root-boundary-caret',()=>{
      write('Currency enum حذف شد.\nOther text');getSelection().setBaseAndExtent(editor,0,editor,0);rememberSelection();setParagraphDirection('ltr');
      assert(editor.firstElementChild.dataset.jotDirection==='ltr'&&!editor.lastElementChild.dataset.jotDirection,'boundary caret did not resolve its paragraph');
    });
    await test('bidi-numbers-emoji-zwnj-and-logical-copy',()=>{
      const text='✅ کنترل‌های Currency در نسخه ۱۴۰۵/۰۷/۰۴ حذف شدند. 👩‍💻';write(text);assert(editor.firstElementChild.dir==='rtl'&&copyPayload(true).text===text,'emoji/ZWNJ/numbers changed');
    });
    await test('bidi-keyboard-change-does-not-reverse-existing-text',()=>{
      write('سلام Currency\nEnglish نام\nhttps://example.com\n۱۲۳.۴۵\n✅');const before=[...editor.children].map(p=>p.dir).join(',');setInputDirection('rtl');setInputDirection('ltr');
      assert([...editor.children].map(p=>p.dir).join(',')===before,'existing paragraph direction followed keyboard');
    });
    await test('bidi-IME-is-not-rewritten-mid-composition',()=>{
      write('Before');editor.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true}));editor.firstElementChild.textContent='Currency حذف شد.';
      editor.dispatchEvent(new InputEvent('input',{bubbles:true,isComposing:true}));setInputDirection('rtl');assert(!editor.querySelector('bdi'),'composition was rewritten while active');
      editor.dispatchEvent(new CompositionEvent('compositionend',{bubbles:true}));assert(editor.querySelector('bdi')&&editor.firstElementChild.dir==='rtl','composition result not normalized');
    });
    await test('bidi-root-text-and-soft-breaks-retain-content',()=>{
      flushTypingHistory();editor.innerHTML='Currency حذف شد.<br>English متن';bookmark=null;onEdit('command');
      assert(editor.firstElementChild.tagName==='P'&&editor.textContent==='Currency حذف شد.English متن'&&editor.querySelector('br'),'root text/soft break lost');
    });
    await test('bidi-list-items-and-table-cells-have-independent-directions',()=>{
      flushTypingHistory();editor.innerHTML='<ul><li>Currency حذف شد.</li><li>English متن</li></ul><table><tr><td>Money.Zero حذف شد.</td><td>English متن</td></tr></table>';bookmark=null;onEdit('command');
      assert([...editor.querySelectorAll('li,td')].map(node=>node.dir).join(',')==='rtl,ltr,rtl,ltr','list/cell direction leaked across items');
    });
    await test('bidi-original-explicit-code-remains-LTR',()=>{
      flushTypingHistory();editor.innerHTML='<pre><code>Money.Zero // فارسی</code></pre>';bookmark=null;onEdit('command');assert(editor.querySelector('pre').dir==='ltr'&&editor.querySelector('code').dir==='ltr'&&!editor.querySelector('bdi'),'code flow changed');
    });
    await test('bidi-hyphenated-English-words-are-not-command-line-flags',()=>{
      write('Hide-note save sentinel');assert(!editor.querySelector('bdi')&&editor.textContent==='Hide-note save sentinel','ordinary hyphenated word was split');
    });
    await test('bidi-long-unbroken-text-does-not-stall-token-recognition',()=>{
      const start=performance.now();JotBidi.tokens('متن '+ 'a'.repeat(40000)+'@localhost');
      assert(performance.now()-start<1500,'token recognition stalled on long text');
    });
    await test('bidi-quoted-url-keeps-surrounding-punctuation-outside-unit',()=>{
      const text='آدرس «https://example.com/a». صحیح است.';write(text);
      assert(editor.textContent===text&&editor.querySelector('bdi').textContent==='https://example.com/a','URL absorbed sentence punctuation');
    });
    await test('bidi-image-boundary-caret-and-original-data-are-preserved',()=>{
      const canvas=document.createElement('canvas');canvas.width=5;canvas.height=3;const src=canvas.toDataURL('image/png');
      flushTypingHistory();editor.innerHTML='<p>قبل Money.Currency <img src="'+src+'"> بعد API</p>';const p=editor.firstElementChild,img=p.querySelector('img');
      const range=document.createRange();range.setStartAfter(img);range.collapse(true);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();
      const before=JotBidi.pointOffset(p,getSelection().anchorNode,getSelection().anchorOffset);normalizeDirection();
      assert(JotBidi.pointOffset(p,getSelection().anchorNode,getSelection().anchorOffset)===before,'caret crossed image');
      assert(p.querySelector('img')===img&&img.src===src&&!p.querySelector('bdi img'),'image changed or wrapped as text');
    });
    await test('bidi-deleting-a-selection-keeps-undo-and-logical-order',()=>{
      write('قبل Money.Currency بعد');const p=editor.firstElementChild;const start=JotBidi.pointAt(p,4),end=JotBidi.pointAt(p,18);
      getSelection().setBaseAndExtent(...start,...end);rememberSelection();command('delete');
      assert(editor.textContent==='قبل  بعد','deleted wrong mixed-direction range');undo();assert(editor.textContent==='قبل Money.Currency بعد','Undo lost mixed-direction selection');
    });
    await test('bidi-source-directional-marks-are-not-silently-rewritten',()=>{
      const text='\u200fمتن می‌ماند API';write(text);assert(copyPayload(true).text===text,'original direction/joining marks changed');
    });
  }finally{composing=false;flushTypingHistory();editor.innerHTML=original;bookmark=null;onEdit('command');await saveNow();window.bidiFinished=true;}
})().catch(error=>{window.bidiResults.push({name:'bidi-runner',passed:false,error:error.stack});window.bidiFinished=true;});

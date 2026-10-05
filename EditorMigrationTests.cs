using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    // Run from the isolated native self-test runner. Fixtures enter through the
    // editor adapter, never by replacing ProseMirror's managed DOM or history.
    private async Task VerifyEditorMigration(List<object> checks)
    {
        const string image="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJ1cAAAAASUVORK5CYII=";
        const string title="Independent document title · عنوان";
        var legacyHtml="<p dir=\"rtl\" data-jot-direction=\"rtl\" data-jot-align=\"right\" style=\"text-align:right\">Currency <b>حذف</b> شد.</p>"
            +"<p data-jot-plain-line=\"true\"><span style=\"font-family:Consolas;font-size:19px;color:rgb(255,0,0);background-color:rgb(255,255,0)\">Code</span> <i><u>emphasis</u></i> <s>removed</s> <code>inline()</code> <a href=\"https://example.com/document\">reference</a></p>"
            +"<p>Before <img src=\""+image+"\" alt=\"Original image\" width=\"1\" height=\"1\"> after</p><p><br></p>"
            +"<table><tbody><tr><th>Heading</th><th>عنوان</th></tr><tr><td colspan=\"2\">Preserved cell</td></tr></tbody></table>";
        foreach(var mode in new[]{"tab","window"})
        {
            void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="editor-migration-"+mode+"-"+name,passed,evidence});
            var directory=Path.Combine(testOutput,"editor-migration-"+mode);Directory.CreateDirectory(directory);
            var target=new JotSession(true,directory){ExerciseLifecycle=true};
            MainWindow? note=null;
            try
            {
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode,fontSize=16,lineHeight=1.95,autoSaveFiles=false}));
                var id=await target.Store.Create();
                await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title,icon="emoji:👨‍👩‍👧‍👦",color="teal"}));
                await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id,html=legacyHtml,plain="Legacy fixture remains unchanged until edited."}));
                note=await target.OpenNoteDefault(id);note.Width=800;note.Height=650;
                await note.WaitFor("window.jotReady===true&&!!window.JotRichEditor?.ready&&!!JotRichEditor.editor?.view");
                var compatibilityWarning=JsonSerializer.Deserialize<string>(await note.Script("document.querySelector('.editor-compatibility-warning')?.textContent||''"));
                Check("supported-legacy-document-is-editable",await note.Script("editor.isContentEditable&&!JotRichEditor.blocked")=="true",compatibilityWarning);
                if(await note.Script("JotRichEditor.blocked")=="true")throw new InvalidOperationException("Supported migration fixture was rejected: "+compatibilityWarning);

                async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
                async Task Key(string key,string code,int virtualKey,int modifiers=0)
                {
                    foreach(var type in new[]{"rawKeyDown","keyUp"})
                        await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code,windowsVirtualKeyCode=virtualKey,modifiers}));
                }
                async Task PasteFromTestClipboard()
                {
                    // Browser Ctrl+V would read the user's real OS clipboard.
                    // This route exercises the native bridge's isolated fixture.
                    await note.Script("window.migrationPasteDone=false;JotEditorMenu.open(40,55,null,true);JotEditorMenu.run('paste').then(()=>window.migrationPasteDone=true)");
                    await note.WaitFor("window.migrationPasteDone===true");
                }
                async Task TypeMarkdownPrefix(string prefix)
                {
                    // Input.insertText emulates IME/emoji insertion and bypasses
                    // ordinary keypress input rules. Exercise actual text keys.
                    foreach(var character in prefix)
                    {
                        var key=character.ToString();var code=character=='#'?"Digit3":character=='-'?"Minus":"Space";
                        var virtualKey=character=='#'?51:character=='-'?189:32;var modifiers=character=='#'?8:0;
                        await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyDown",key,code,text=key,unmodifiedText=key,windowsVirtualKeyCode=virtualKey,modifiers}));
                        await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code,windowsVirtualKeyCode=virtualKey,modifiers}));
                    }
                }
                async Task Fixture(string html)
                {
                    await note.Script("JotRichEditor.setContent("+JsonSerializer.Serialize(html)+");JotRichEditor.editor.commands.focus('end');window.migrationFixtureHtml=JotRichEditor.getHTML()");
                    await note.Flush();
                    // Native final-flush temporarily makes the app inert. Start
                    // each input fixture with an explicit focused editor again.
                    await note.Script("JotRichEditor.editor.commands.focus('end');JotRichEditor.editor.view.focus();window.migrationFixtureSettled=false;requestAnimationFrame(()=>requestAnimationFrame(()=>window.migrationFixtureSettled=true))");
                    await note.WaitFor("window.migrationFixtureSettled===true");
                }
                async Task Select(string text,bool backward=false)
                {
                    await note.Script("(()=>{const text="+JsonSerializer.Serialize(text)+",backward="+(backward?"true":"false")+";let found=false;JotRichEditor.editor.state.doc.descendants((node,pos)=>{if(found||!node.isText)return;const offset=node.text.indexOf(text);if(offset<0)return;const from=pos+offset,to=from+text.length;JotRichEditor.editor.commands.focus();JotRichEditor.restoreSelection({anchor:backward?to:from,head:backward?from:to});found=true;});if(!found)throw Error('Migration fixture text not found');window.migrationSelection=JSON.stringify(JotRichEditor.selection());window.migrationSelected=getSelection().toString()})()");
                }
                async Task CheckScript(string name,string predicate)
                {
                    var passed=await note.Script(predicate)=="true";
                    Check(name,passed,passed?null:JsonSerializer.Deserialize<JsonElement>(await note.Script("({html:JotRichEditor.getHTML(),text:JotRichEditor.getText(),selection:JotRichEditor.selection(),selected:getSelection().toString(),active:document.activeElement?.id,locked:editorLockedByHost,editorClass:editor.className,insideEditorcn:!!editor.closest('.jot-block-editor'),viewport:{width:innerWidth,scrollWidth:document.documentElement.scrollWidth},boxes:['#editor','#richEditorHost','.jot-block-editor','.block-editor-content','#writingArea','#app','#workspaceViews'].map(selector=>{const element=document.querySelector(selector);if(!element)return {selector};const css=getComputedStyle(element);return {selector,className:element.className,bounds:element.getBoundingClientRect().toJSON(),clientWidth:element.clientWidth,scrollWidth:element.scrollWidth,width:css.width,minWidth:css.minWidth,padding:css.padding,display:css.display,flex:css.flex}})})")));
                }

                Check("real-prosemirror-runtime",await note.Script("editor===JotRichEditor.editor.view.dom&&editor.classList.contains('ProseMirror')&&JotRichEditor.editor.extensionManager.extensions.some(extension=>extension.name==='paragraph')")=="true");
                if(mode=="tab")
                {
                    using var schemaChecks=JsonDocument.Parse(await note.Script("JotRichEditor.runSchemaChecks()"));
                    foreach(var result in schemaChecks.RootElement.EnumerateArray())Check("schema-"+result.GetProperty("name").GetString(),result.GetProperty("passed").GetBoolean(),result.Clone());
                }
                Check("actual-editorcn-content-and-controls-mounted",await note.Script("!!document.querySelector('[data-jot-editorcn=\"true\"] .block-editor-content')&&!!document.querySelector('.jot-editor-toolbar')")=="true");
                Check("runtime-assets-are-local",await note.Script("[...document.scripts].every(script=>!script.src||new URL(script.src).origin===location.origin)&&performance.getEntriesByType('resource').every(entry=>!/^https?:/.test(entry.name)||new URL(entry.name).origin===location.origin)")=="true");
                await note.Flush();
                Check("opening-does-not-rewrite-legacy-html",(await target.Store.LoadNote(id))!.Value.GetProperty("html").GetString()==legacyHtml);
                var savedFile=Path.Combine(directory,"Different filename.jot");note.TestSaveNoteFilePath=savedFile;await note.SaveNoteFile(id,true);
                var initialBytes=await File.ReadAllBytesAsync(savedFile);
                await note.Reload();await note.WaitFor("window.jotReady&&JotRichEditor?.ready&&editor.isContentEditable");await note.Flush();
                Check("linked-legacy-reload-stays-clean",!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean()&&(await File.ReadAllBytesAsync(savedFile)).SequenceEqual(initialBytes));
                await CheckScript("old-rich-content-readable","(()=>{const doc=document.createElement('div');doc.innerHTML=JotRichEditor.getHTML();return !!doc.querySelector('strong,b')&&!!doc.querySelector('em,i')&&!!doc.querySelector('u')&&!!doc.querySelector('s,strike')&&!!doc.querySelector('code')&&doc.querySelector('a')?.getAttribute('href')==='https://example.com/document'&&doc.querySelector('img')?.getAttribute('src')==="+JsonSerializer.Serialize(image)+"&&doc.querySelector('table')?.rows.length===2&&doc.querySelector('td')?.colSpan===2&&doc.textContent.includes('Currency حذف شد.')})()");
                await CheckScript("old-bidi-typography-and-alignment","(()=>{const p=editor.querySelector('p'),span=[...editor.querySelectorAll('span')].find(node=>node.textContent==='Code');return getComputedStyle(p).direction==='rtl'&&getComputedStyle(p).textAlign==='right'&&!!span&&getComputedStyle(span).fontSize==='19px'&&getComputedStyle(span).fontFamily.includes('Consolas')&&getComputedStyle(span).color==='rgb(255, 0, 0)'&&getComputedStyle(span).backgroundColor==='rgb(255, 255, 0)'})()");
                await note.Script("JotRichEditor.editor.commands.focus('end')");await Type(" migrated edit");await note.Flush();
                var updated=(await target.Store.LoadNote(id))!.Value;
                Check("edited-legacy-note-retains-image-and-metadata",updated.GetProperty("html").GetString()!.Contains(image)&&updated.GetProperty("title").GetString()==title&&updated.GetProperty("icon").GetString()=="emoji:👨‍👩‍👧‍👦"&&updated.GetProperty("color").GetString()=="teal");
                Check("edit-marks-file-dirty-without-writing-manual-file",(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean()&&(await File.ReadAllBytesAsync(savedFile)).SequenceEqual(initialBytes));
                await note.SaveNoteFile(id);
                var exported=await NoteFileFormat.Read(savedFile);
                Check("save-keeps-independent-title-and-filename",exported.Note["title"]!.GetValue<string>()==title&&exported.Note["icon"]!.GetValue<string>()=="emoji:👨‍👩‍👧‍👦"&&exported.Note["html"]!.GetValue<string>().Contains(image)&&(await target.Store.LoadFileState(id))!.Value.GetProperty("name").GetString()=="Different filename.jot");

                foreach(var backward in new[]{false,true})
                {
                    await Fixture("<p>Before <strong>selected <em>rich text</em></strong> after.</p><p dir=\"rtl\" data-jot-direction=\"rtl\">متن فارسی</p>");
                    // Select across marks by document positions rather than the
                    // first text node, then verify actual native Ctrl+X/Ctrl+Z.
                    await note.Script("JotRichEditor.editor.commands.focus();JotRichEditor.restoreSelection({anchor:"+(backward?26:8)+",head:"+(backward?8:26)+"});window.migrationBeforeCut=JotRichEditor.getHTML();window.migrationSelection=JSON.stringify(JotRichEditor.selection());window.migrationSelected=getSelection().toString()");
                    await note.Script("window.migrationCutEvents=[];for(const name of ['keydown','cut','copy'])document.addEventListener(name,event=>migrationCutEvents.push({type:event.type,key:event.key,target:event.target.id,active:document.activeElement?.id,selected:getSelection().toString(),selection:JotRichEditor.selection(),prevented:event.defaultPrevented}),{capture:true,once:true})");
                    await CheckScript("cut-fixture-selects-exact-rich-phrase-"+backward,"getSelection().toString()==='selected rich text'&&document.activeElement===editor");
                    await Key("x","KeyX",88,2);await note.WaitFor("!cutting&&!JotRichEditor.getText().includes('selected rich text')");
                    await CheckScript("cut-only-selected-content-"+backward,"JotRichEditor.getText().includes('Before')&&JotRichEditor.getText().includes('متن فارسی')");
                    await Key("z","KeyZ",90,2);
                    await CheckScript("cut-undo-restores-marks-and-selection-"+backward,"JotRichEditor.getHTML()===migrationBeforeCut&&JSON.stringify(JotRichEditor.selection())===migrationSelection&&getSelection().toString()===migrationSelected");
                    await Key("y","KeyY",89,2);
                    await CheckScript("cut-redo-works-"+backward,"!JotRichEditor.getText().includes('selected rich text')&&JotRichEditor.editor.state.selection.empty");
                }
                await Fixture("<p>Before <img src=\""+image+"\" alt=\"Original image\"> after.</p>");
                await note.Script("JotRichEditor.editor.state.doc.descendants((node,pos)=>{if(node.type.name==='image')JotRichEditor.editor.commands.setNodeSelection(pos)});JotRichEditor.editor.view.focus();window.migrationBeforeImageCut=JotRichEditor.getHTML()");
                await Key("x","KeyX",88,2);await note.WaitFor("!cutting&&!editor.querySelector('img')");await Key("z","KeyZ",90,2);
                await CheckScript("image-cut-undo-restores-original-and-node-selection","JotRichEditor.getHTML()===migrationBeforeImageCut&&JotRichEditor.editor.state.selection.node?.type.name==='image'&&JotRichEditor.editor.state.selection.node.attrs.src==="+JsonSerializer.Serialize(image));
                await Fixture("<p>Do not lose selected content.</p>");await Select("selected",true);note.TestClipboardWriteAccepted=false;
                await Key("x","KeyX",88,2);await note.WaitFor("!cutting&&JotToast.has('note-error')");
                await CheckScript("failed-native-copy-never-deletes-content","JotRichEditor.getHTML()===migrationFixtureHtml&&JSON.stringify(JotRichEditor.selection())===migrationSelection");note.TestClipboardWriteAccepted=true;
                note.TestClipboardDelayMs=350;await Select("selected");await Key("x","KeyX",88,2);await note.WaitFor("cutting");
                await note.Script("JotRichEditor.restoreSelection({anchor:1,head:1});window.migrationSelectionAfterMove=JSON.stringify(JotRichEditor.selection())");await note.WaitFor("!cutting");
                await CheckScript("late-copy-does-not-cut-after-selection-moves","JotRichEditor.getHTML()===migrationFixtureHtml&&JSON.stringify(JotRichEditor.selection())===migrationSelectionAfterMove");note.TestClipboardDelayMs=0;

                const string pasted="First line\n\nسطر دوم\n";
                await Fixture("<p></p>");
                note.TestClipboardContent=new ClipboardContent(pasted,"<p style=\"line-height:99px\"><b>First line</b></p><p></p><p>سطر دوم</p><p></p>",null);
                await PasteFromTestClipboard();await note.WaitFor("JotRichEditor.getText().includes('سطر دوم')");
                await CheckScript("plain-paste-retains-only-intentional-line-breaks","JotRichEditor.getText()==="+JsonSerializer.Serialize(pasted)+"&&editor.querySelectorAll('p').length===4&&!editor.querySelector('b,strong,[style*=\"99px\"]')");
                await CheckScript("pasted-lines-do-not-gain-extra-margins","[...editor.querySelectorAll('p')].every(p=>getComputedStyle(p).marginTop==='0px'&&getComputedStyle(p).marginBottom==='0px')");
                for(var cycle=0;cycle<2;cycle++)
                {
                    await note.Script("JotRichEditor.editor.commands.selectAll()");await Key("x","KeyX",88,2);await note.WaitFor("!cutting");
                    note.TestClipboardContent=ClipboardReader.Read(note.TestClipboardData);await PasteFromTestClipboard();await note.WaitFor("JotRichEditor.getText().includes('سطر دوم')");
                    await CheckScript("repeated-cut-paste-does-not-inflate-lines-"+cycle,"JotRichEditor.getText()==="+JsonSerializer.Serialize(pasted)+"&&editor.querySelectorAll('p').length===4");
                }
                await Fixture("<p></p>");
                await note.Script("(()=>{const data=new DataTransfer();data.setData('text/plain','Browser event\\n\\nرویداد');data.setData('text/html','<p><strong>Browser event</strong></p><p></p><p>رویداد</p>');editor.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}))})()");
                await note.WaitFor("JotRichEditor.getText().includes('رویداد')");
                await CheckScript("browser-paste-event-uses-plain-logical-lines","JotRichEditor.getText()==='Browser event\\n\\nرویداد'&&editor.querySelectorAll('p').length===3&&!editor.querySelector('strong')");

                await Fixture("<p>Alpha beta gamma</p>");
                using(var point=JsonDocument.Parse(await note.Script("(()=>{const tree=document.createTreeWalker(editor,NodeFilter.SHOW_TEXT);let text;while(text=tree.nextNode()){const at=text.textContent.indexOf('beta');if(at<0)continue;const range=document.createRange();range.setStart(text,at+1);range.setEnd(text,at+2);const rect=range.getBoundingClientRect();return {x:rect.x+rect.width/2,y:rect.y+rect.height/2}}throw Error('Word fixture absent')})()")))
                {
                    var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseMoved",x,y,button="none",buttons=0,clickCount=0}));
                    for(var clickCount=1;clickCount<=2;clickCount++)foreach(var type in new[]{"mousePressed","mouseReleased"})
                        await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button="left",buttons=type=="mousePressed"?1:0,clickCount}));
                }
                await CheckScript("double-click-selects-word-not-paragraph","getSelection().toString().trim()==='beta'");
                await Fixture("<p>Project فارسی می‌شود</p>");await Type(" · انگلیسی English");
                await CheckScript("bilingual-text-and-zwnj-survive-typing","JotRichEditor.getText()==='Project فارسی می‌شود · انگلیسی English'&&getComputedStyle(editor.querySelector('p')).direction==='rtl'");
                await Fixture("<p>Prefix </p>");
                await note.Script("window.migrationCompositionEvents=[];for(const name of ['compositionstart','compositionend'])editor.addEventListener(name,event=>migrationCompositionEvents.push(event.type),{once:true})");
                const string composition="می‌شود";
                await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.imeSetComposition",JsonSerializer.Serialize(new{text=composition,selectionStart=composition.Length,selectionEnd=composition.Length}));
                await target.Changed(false,id);
                await CheckScript("metadata-refresh-keeps-active-ime","editor.textContent==='Prefix می‌شود'&&migrationCompositionEvents.includes('compositionstart')");
                await Type(composition);await note.Flush();
                await CheckScript("ime-commit-preserves-zwnj-without-duplication","JotRichEditor.getText()==='Prefix می‌شود'&&migrationCompositionEvents.includes('compositionend')");
                await Fixture("<p>AltGr typing </p>");
                await note.Script("window.migrationAltGraph=(()=>{const before=JSON.stringify(JotRichEditor.editor.getJSON()),events=[];for(const [code,key] of [['KeyB','b'],['Digit1','@']]){const event=new KeyboardEvent('keydown',{key,code,ctrlKey:true,altKey:true,bubbles:true,cancelable:true});Object.defineProperty(event,'getModifierState',{value:name=>name==='AltGraph'||name==='Control'||name==='Alt'});editor.dispatchEvent(event);events.push(event.defaultPrevented);}return {prevented:events,unchanged:before===JSON.stringify(JotRichEditor.editor.getJSON())}})()");
                await Type("@");
                await CheckScript("altgraph-does-not-trigger-formatting-or-block-text","migrationAltGraph.prevented.every(value=>value===false)&&migrationAltGraph.unchanged&&JotRichEditor.getText()==='AltGr typing @'&&!JotRichEditor.editor.isActive('bold')&&!editor.querySelector('strong,b,h1,h2,h3')");
                await Fixture("<p>Project فارسی می‌شود</p>");
                await Select("Project");await note.Script("window.migrationBeforeSizing=JotRichEditor.getHTML()");await Key("=","Equal",187,2);await note.WaitFor("model.prefs.fontSize===17");
                await CheckScript("ctrl-plus-changes-note-size-not-document-marks","JotRichEditor.getHTML()===migrationBeforeSizing&&getSelection().toString()==='Project'");
                Check("ctrl-plus-does-not-scale-app",note.Browser.ZoomFactor==1&&(await target.Store.LoadPreferences()).GetProperty("fontSize").GetInt32()==16);
                await Key("0","Digit0",48,2);await note.WaitFor("model.prefs.fontSize===16");
                await note.Script("document.getElementById('writingArea').dispatchEvent(new WheelEvent('wheel',{deltaY:-120,ctrlKey:true,bubbles:true,cancelable:true}))");await note.WaitFor("model.prefs.fontSize===17");
                await note.Flush();Check("ctrl-wheel-persists-note-only-size",(await target.Store.LoadNote(id))!.Value.GetProperty("view").GetProperty("fontSize").GetInt32()==17&&note.Browser.ZoomFactor==1);

                await Fixture("<p>Document</p>");await note.Script("JotRichEditor.editor.chain().focus('end').insertTable({rows:2,cols:3,withHeaderRow:true}).run()");
                await CheckScript("table-insert-has-real-schema-nodes","JotRichEditor.editor.state.doc.toJSON().content.some(node=>node.type==='table')&&editor.querySelector('table').rows.length===2&&editor.querySelector('table').rows[0].cells.length===3");
                await Type("نام پروژه");await Key("Tab","Tab",9);await Type("Project B");
                await CheckScript("table-tab-moves-between-cells","editor.querySelector('table').rows[0].cells[0].textContent==='نام پروژه'&&editor.querySelector('table').rows[0].cells[1].textContent==='Project B'");
                await note.Script("JotRichEditor.editor.chain().focus().addRowAfter().run()");await CheckScript("table-row-command","editor.querySelector('table').rows.length===3");
                await Key("z","KeyZ",90,2);await CheckScript("table-row-is-undoable","editor.querySelector('table').rows.length===2");
                await note.Flush();await note.SaveNoteFile(id);
                Check("table-export-is-portable-html",(await NoteFileFormat.Read(savedFile)).Note["html"]!.GetValue<string>().Contains("<table"));

                await Fixture("<p></p>");await TypeMarkdownPrefix("# ");await Type("Heading");await Key("Enter","Enter",13);
                await CheckScript("markdown-heading-input-rule","!!editor.querySelector('h1')&&editor.querySelector('h1').textContent==='Heading'");
                await Fixture("<p></p>");await TypeMarkdownPrefix("- ");await Type("List item");
                await CheckScript("markdown-list-input-rule","!!editor.querySelector('ul li')&&editor.querySelector('li').textContent==='List item'");
                await Fixture("<p></p>");await note.Script("JotRichEditor.editor.chain().focus().setHorizontalRule().run()");
                await CheckScript("divider-command","!!editor.querySelector('hr')");
                await Fixture("<p></p>");await Type("/heading");
                await note.WaitFor("[...document.querySelectorAll('.block-editor-slash-menu-item')].some(item=>item.textContent.includes('Heading 1'))");
                await note.Script("[...document.querySelectorAll('.block-editor-slash-menu-item')].find(item=>item.textContent.includes('Heading 1')).click()");
                await Type("Slash heading");
                await CheckScript("editorcn-slash-command-inserts-heading","editor.querySelector('h1')?.textContent==='Slash heading'&&!editor.textContent.includes('/heading')");
                await note.Script("if(!document.querySelector('.jot-editor-toolbar')?.getClientRects().length)document.getElementById('formatButton').click()");
                await note.ClickControl(".jot-editor-toolbar button[aria-label='Insert table']");
                await note.WaitFor("!!document.querySelector('.jot-table-insert')?.getClientRects().length");
                await Key("Escape","Escape",27);
                await note.WaitFor("!document.querySelector('.jot-table-insert')?.getClientRects().length&&document.activeElement===editor");
                await CheckScript("table-dialog-cancel-keeps-document","!document.querySelector('.jot-table-insert')?.getClientRects().length&&editor.querySelector('h1')?.textContent==='Slash heading'");

                if(mode=="tab")
                {
                    await Fixture("<p>First tab</p>");await Type(" first edit");
                    var peer=await target.Store.Create();await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=peer,html="<p>Second tab</p>",plain="Second tab"}));
                    await note.SwitchNoteTab(peer);await note.WaitFor("model.activeId==="+JsonSerializer.Serialize(peer)+"&&JotRichEditor.ready&&editor.isContentEditable");
                    await note.Script("JotRichEditor.editor.commands.focus('end')");await Type(" second edit");await Key("z","KeyZ",90,2);
                    await CheckScript("second-tab-undo-is-isolated","JotRichEditor.getText()==='Second tab'");
                    await note.SwitchNoteTab(id);await note.WaitFor("model.activeId==="+JsonSerializer.Serialize(id)+"&&JotRichEditor.ready&&editor.isContentEditable");
                    await CheckScript("first-tab-keeps-own-content","JotRichEditor.getText()==='First tab first edit'");await Key("z","KeyZ",90,2);
                    await CheckScript("first-tab-keeps-own-undo-history","JotRichEditor.getText()==='First tab'");
                    foreach(var destination in new[]{"home","settings"})
                    {
                        await Fixture("<p>Portal safety</p>");await Type(" history");await Select("Portal");
                        await note.WaitFor("!!document.querySelector('.jot-selection-bubble')?.getClientRects().length");
                        await note.Script("window.migrationPortalSelection=JSON.stringify(JotRichEditor.selection());window.migrationPortalHtml=JotRichEditor.getHTML()");
                        await note.SwitchNoteTab(destination);await note.WaitFor("JotWorkspace.view==="+JsonSerializer.Serialize(destination));
                        await CheckScript(destination+"-hides-note-portals-and-disables-engine","!JotRichEditor.editor.isEditable&&[...document.querySelectorAll('.jot-selection-bubble,.jot-editor-positioner,.block-editor-slash-menu,.block-editor-bubble-overlay')].every(element=>!element.getClientRects().length||getComputedStyle(element).visibility==='hidden')&&JotRichEditor.command('insertText','must not leak')===false&&JotRichEditor.getHTML()===migrationPortalHtml");
                        await note.SwitchNoteTab(id);await note.WaitFor("JotWorkspace.view==='note'&&JotRichEditor.editor.isEditable&&!editorLockedByHost");
                        await CheckScript(destination+"-return-restores-note-selection","JotRichEditor.getHTML()===migrationPortalHtml&&JSON.stringify(JotRichEditor.selection())===migrationPortalSelection");
                        await note.Script("JotRichEditor.undo()");
                        await CheckScript(destination+"-return-keeps-note-history","JotRichEditor.getText()==='Portal safety'");
                    }
                }

                await Fixture("<p>Latest native flush</p>");await Type(" must survive");await note.Flush(true);
                Check("native-final-flush-saves-latest-input",(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Latest native flush must survive");
                await CheckScript("native-final-flush-holds-editing","editorLockedByHost&&app.inert");
                note.Post(new{@event="resume-editing"});await note.WaitFor("!editorLockedByHost&&!app.inert");
                await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=true}));
                await note.Script("JotRichEditor.editor.commands.focus('end');JotRichEditor.editor.view.focus()");await Type(" auto-saved");
                await CheckScript("auto-save-fixture-input-reaches-editor","JotRichEditor.getText()==='Latest native flush must survive auto-saved'");await note.Flush();
                for(var attempt=0;attempt<100;attempt++)
                {
                    // Reading the file here with FileShare.Read can itself deny
                    // the writer's atomic ReplaceFile. Observe store state only.
                    if(!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean())break;
                    await Task.Delay(50);
                }
                await target.WaitForNoteFiles();
                Check("linked-file-auto-save-still-works",(await NoteFileFormat.Read(savedFile)).Note["plain"]!.GetValue<string>()=="Latest native flush must survive auto-saved"&&!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean(),new{filePlain=(await NoteFileFormat.Read(savedFile)).Note["plain"]!.GetValue<string>(),storedPlain=(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString(),state=await target.Store.LoadFileState(id)});
                await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=false}));

                await Fixture("<p>Saved before a failed flush</p>");
                await SetStoreTrigger(target.Store,"CREATE TRIGGER fail_editor_migration_flush BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic editor migration flush failure');END;");
                try
                {
                    await Type(" unsaved draft");var refused=false;
                    try{await note.HideAfterSaving(true);}catch(IOException){refused=true;}
                    await note.WaitFor("!editorLockedByHost&&!app.inert");
                    Check("failed-native-flush-keeps-window-and-draft",refused&&!note.windowClosed&&note.IsVisible&&await note.Script("editor.isContentEditable&&JotRichEditor.getText()==='Saved before a failed flush unsaved draft'")=="true");
                    Check("failed-native-flush-does-not-overwrite-stored-body",(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Saved before a failed flush");
                }
                finally{await SetStoreTrigger(target.Store,"DROP TRIGGER fail_editor_migration_flush;");}
                await note.Flush();
                Check("retry-saves-retained-draft",(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Saved before a failed flush unsaved draft");

                await note.Script("(()=>{const start=performance.now();JotRichEditor.setContent('<p>'+('Long bilingual paragraph فارسی می‌شود '.repeat(2500))+'</p><p>End</p>');JotRichEditor.editor.commands.focus('end');window.migrationLongNote={loadMs:performance.now()-start,characters:JotRichEditor.getText().length};})()");
                var inputWatch=System.Diagnostics.Stopwatch.StartNew();await Type(" typed");inputWatch.Stop();await note.Flush();
                Check("long-note-remains-editable-without-content-loss",await note.Script("JotRichEditor.getText().endsWith('End typed')&&migrationLongNote.characters>90000")=="true",new{inputMilliseconds=inputWatch.ElapsedMilliseconds,document=JsonSerializer.Deserialize<JsonElement>(await note.Script("migrationLongNote"))});
                await Fixture("<p>Clean writing surface · متن فارسی</p>");

                foreach(var theme in new[]{"light","dark"})foreach(var width in new[]{320,800})
                {
                    note.Width=width;await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));await Task.Delay(100);
                    await CheckScript("responsive-writing-surface-"+theme+"-"+width,"(()=>{const bounds=editor.getBoundingClientRect(),css=getComputedStyle(editor);return document.documentElement.scrollWidth<=innerWidth+1&&bounds.left>=0&&bounds.right<=innerWidth+1&&css.visibility==='visible'&&css.fontSize!=='0px'&&editor.isContentEditable})()");
                    await note.ClickControl(".jot-editor-toolbar button[aria-label='Insert table']");
                    await note.WaitFor("!!document.querySelector('.jot-table-insert')?.getClientRects().length");
                    await CheckScript("responsive-table-popover-"+theme+"-"+width,"(()=>{const bounds=document.querySelector('.jot-table-insert').getBoundingClientRect();return bounds.left>=0&&bounds.right<=innerWidth+1&&bounds.top>=0&&bounds.bottom<=innerHeight+1})()");
                    await note.Capture("editor-migration-"+mode+"-"+theme+"-"+width);
                    await Key("Escape","Escape",27);
                }
                Check("no-runtime-errors",note.RuntimeErrors.Count==0,note.RuntimeErrors.ToArray());
                Check("fixtures-use-no-live-user-windows-or-clipboard",target.Windows.All(window=>window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.Left< -10000&&window.Top< -10000)&&note.TestClipboardData is not null);
            }
            catch(Exception error)
            {
                var windows=new List<object>();
                foreach(var window in target.Windows.ToArray())
                {
                    string? state=null;
                    if(window.Browser.CoreWebView2 is not null)
                    {
                        try{state=await window.Script("JSON.stringify({url:location.href,error:document.getElementById('error')?.textContent,workspaceError:document.getElementById('workspaceError')?.textContent,body:document.body.innerText.slice(0,4000),scripts:[...document.scripts].map(script=>script.src),ready:window.jotReady,factory:typeof JotEditorFactory,bridge:typeof JotBridge,editor:typeof JotRichEditor,selection:window.JotRichEditor?.selection(),selected:getSelection().toString(),active:document.activeElement?.id,cutEvents:window.migrationCutEvents,beforeCutSelection:window.migrationSelection,beforeCutSelected:window.migrationSelected,cutting:typeof cutting==='undefined'?null:cutting,locked:typeof editorLockedByHost==='undefined'?null:editorLockedByHost})").WaitAsync(TimeSpan.FromSeconds(3));}
                        catch(Exception inspectionError){state=inspectionError.ToString();}
                    }
                    windows.Add(new{mode=window.Mode,runtime=window.RuntimeErrors.ToArray(),actions=window.TestHostActions.ToArray(),clipboardWritten=window.TestClipboardData is not null,state});
                }
                await File.WriteAllTextAsync(Path.Combine(directory,"migration-failure.json"),JsonSerializer.Serialize(new{error=error.ToString(),windows},new JsonSerializerOptions{WriteIndented=true}));
                throw;
            }
            finally
            {
                if(note is not null){note.TestClipboardWriteAccepted=true;note.TestClipboardDelayMs=0;note.Post(new{@event="resume-editing"});}
                target.StopFileAutoSave();await target.WaitForNoteFiles();target.DisposeGlobalShortcuts();
                foreach(var window in target.Windows.ToArray())window.ClosePermanently();
            }
        }
    }
}

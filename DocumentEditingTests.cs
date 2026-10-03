using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyDocumentEditing(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="document-editing-"+name,passed,evidence});
        const string ready="window.jotReady===true&&!!window.JotDocumentHeading&&!!window.JotWritingTools&&editor.isContentEditable&&!editorLockedByHost";
        foreach(var mode in new[]{"tab","window"})
        {
            var directory=Path.Combine(testOutput,"document-editing-"+mode);Directory.CreateDirectory(directory);
            var target=new JotSession(true,directory){ExerciseLifecycle=true};
            try
            {
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode,autoSaveFiles=false}));
                var note=await target.NewNote();note.Width=900;note.Height=640;await note.WaitFor(ready);var id=note.NoteId!;
                async Task Key(string key,int code,int modifiers=0)
                {
                    if(key=="Enter")await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyDown",key,code=key,windowsVirtualKeyCode=code,modifiers,text="\r",unmodifiedText="\r"}));
                    else await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="rawKeyDown",key,code=key.Length==1?"Key"+key.ToUpperInvariant():key,windowsVirtualKeyCode=code,modifiers}));
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code=key.Length==1?"Key"+key.ToUpperInvariant():key,windowsVirtualKeyCode=code,modifiers}));
                }
                async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
                async Task Title(string text)
                {await note.Script("document.getElementById('documentTitle').focus();document.getElementById('documentTitle').select()");await Type(text);}
                await note.Script("""
                    window.documentFixture=html=>{
                      JotWritingTools.hide();flushTypingHistory();closePanels();editor.innerHTML=sanitizeHtml(html);bookmark=null;bookmarkDirection=null;
                      normalizeDirection();updateEmpty();editor.focus();getSelection().removeAllRanges();
                      const range=document.createRange();range.selectNodeContents(editor.firstElementChild||editor);range.collapse(false);getSelection().addRange(range);rememberSelection();
                      histories.set(model.activeId,{values:[editor.innerHTML],selections:[editorSelectionState()],index:0,time:0,kind:''});
                      typingHistoryPending=false;onEdit('command');clearTimeout(saveTimer);
                    };
                    documentFixture('<p>Body stays separate from the title.</p>');
                    """);
                Check("heading-and-top-tools-exist-"+mode,await note.Script("document.getElementById('documentTitle').ariaLabel==='Note title'&&document.getElementById('documentEmoji').ariaLabel==='Change note emoji'&&document.querySelector('.document-toolbar').getBoundingClientRect().bottom<=document.getElementById('writingArea').getBoundingClientRect().top+1")=="true");
                await Title("Project title · عنوان پروژه");await note.Flush();
                var saved=(await target.Store.LoadNote(id))!.Value;
                Check("native-flush-saves-title-without-changing-body-"+mode,saved.GetProperty("title").GetString()=="Project title · عنوان پروژه"&&saved.GetProperty("plain").GetString()=="Body stays separate from the title.");
                await Title("Title saved after idle");await note.WaitFor("!JotDocumentHeading.pending&&activeNote().title==='Title saved after idle'");
                Check("title-idle-save-persists-"+mode,(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Title saved after idle");
                await Title("Document title different from filename");
                var path=Path.Combine(directory,"Disk filename.jot");note.TestSaveNoteFilePath=path;await note.SaveNoteFile(id,true);
                Check("save-file-flushes-title-and-keeps-filename-separate-"+mode,(await NoteFileFormat.Read(path)).Note["title"]!.GetValue<string>()=="Document title different from filename"&&(await target.Store.LoadFileState(id))!.Value.GetProperty("name").GetString()=="Disk filename.jot"&&await note.Script("document.getElementById('documentTitle').value==='Document title different from filename'&&document.querySelector('#fileIdentity .file-name').textContent==='Disk filename.jot'")=="true");
                await Title("Press Enter to write");await Key("Enter",13);
                await note.WaitFor("document.activeElement===editor&&!JotDocumentHeading.pending");
                Check("title-enter-focuses-body-without-inserting-title-newline-"+mode,(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Press Enter to write"&&await note.Script("editor.textContent==='Body stays separate from the title.'")=="true");

                // Simulate the final composition input arriving before its
                // compositionend event, the same boundary forced close must save.
                await note.Script("(()=>{const title=document.getElementById('documentTitle');title.focus();title.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true}));title.value='عنوان نهایی هنگام بستن';title.dispatchEvent(new InputEvent('input',{bubbles:true,isComposing:true,inputType:'insertCompositionText',data:'عنوان نهایی هنگام بستن'}))})()");
                await note.Flush();
                Check("forced-flush-retains-last-visible-title-composition-"+mode,(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="عنوان نهایی هنگام بستن");
                await note.Script("document.getElementById('documentTitle').dispatchEvent(new CompositionEvent('compositionend',{bubbles:true}));editor.focus()");
                await VerifyDocumentEditingEdges(note,target,id,mode,checks);
                if(mode=="tab")
                {
                    var other=await target.Store.Create();await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=other,title="Other tab keeps its title"}));
                    await Title("Latest title before switching");await note.SwitchNoteTab(other);await note.WaitFor(ready+"&&activeNote().id==="+JsonSerializer.Serialize(other));
                    await note.WaitFor("document.getElementById('documentTitle').value==='Other tab keeps its title'");
                    Check("switch-saves-old-title-without-overwriting-destination",(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Latest title before switching"&&(await target.Store.LoadNote(other))!.Value.GetProperty("title").GetString()=="Other tab keeps its title");
                    await note.SwitchNoteTab(id);await note.WaitFor(ready+"&&activeNote().id==="+JsonSerializer.Serialize(id));
                }

                await note.Script("documentFixture('<p><br></p>')");await Type("/heading");
                await note.WaitFor("!document.getElementById('writingCommands').hidden&&document.querySelectorAll('[data-writing-command]').length===3");
                Check("slash-filters-heading-commands-at-caret-"+mode,await note.Script("editor.textContent==='/heading'&&document.activeElement===editor&&document.querySelector('[data-writing-command=heading-1]').ariaSelected==='true'")=="true");
                await note.Capture("document-slash-menu-"+mode);
                await Key("ArrowDown",40);await Key("Enter",13);
                Check("keyboard-slash-heading-removes-only-command-text-"+mode,await note.Script("!!editor.querySelector('h2')&&!editor.textContent.includes('/heading')&&document.getElementById('writingCommands').hidden")=="true");
                await Key("z",90,2);
                Check("one-undo-restores-slash-and-original-paragraph-"+mode,await note.Script("editor.textContent==='/heading'&&!!editor.querySelector('p')&&!editor.querySelector('h2')")=="true");
                await note.Script("documentFixture('<p><br></p>')");await Type("/heading");await note.WaitFor("!document.getElementById('writingCommands').hidden");await Key("Escape",27);
                Check("escape-preserves-literal-slash-text-"+mode,await note.Script("document.getElementById('writingCommands').hidden&&editor.textContent==='/heading'")=="true");
                await note.Script("documentFixture('<p><br></p>')");await Type("C:/Users/name/file.txt /usr/bin https://example.test/path");await Task.Delay(80);
                Check("paths-and-urls-remain-literal-"+mode,await note.Script("document.getElementById('writingCommands').hidden&&editor.textContent==='C:/Users/name/file.txt /usr/bin https://example.test/path'")=="true");

                await note.Script("documentFixture('<p>Alpha bravo charlie.</p><p>Second paragraph.</p>');(()=>{const text=editor.firstElementChild.firstChild;getSelection().setBaseAndExtent(text,6,text,11);rememberSelection();JotWritingTools.schedule()})()");
                await note.WaitFor("!document.getElementById('selectionTools').hidden");
                await note.Capture("document-selection-tools-"+mode);
                await note.ClickControl("#selectionTools [data-writing-action=bold]");
                Check("selection-bubble-bold-keeps-selected-word-"+mode,await note.Script("getSelection().toString()==='bravo'&&[...editor.querySelectorAll('b,strong,span')].some(element=>element.textContent==='bravo'&&(getComputedStyle(element).fontWeight==='700'||getComputedStyle(element).fontWeight==='bold'))")=="true");
                await note.WaitFor("!document.getElementById('selectionTools').hidden");await note.ClickControl("#selectionTools [data-writing-action=align-justify]");
                Check("selection-bubble-justify-keeps-range-and-other-paragraph-"+mode,await note.Script("getSelection().toString()==='bravo'&&editor.firstElementChild.style.textAlign==='justify'&&!editor.children[1].style.textAlign")=="true");
                await Key("z",90,2);
                Check("selection-justify-undo-retains-selection-"+mode,await note.Script("getSelection().toString()==='bravo'&&!editor.firstElementChild.style.textAlign")=="true");
                await note.Script("getSelection().collapseToEnd();JotWritingTools.hide()");
                await note.ClickControl("#blockStyleButton");
                Check("top-style-menu-takes-keyboard-focus-"+mode,await note.Script("document.activeElement.id==='writingCommands'&&document.getElementById('blockStyleButton').ariaExpanded==='true'&&!!document.getElementById('writingCommands').getAttribute('aria-activedescendant')")=="true");
                await Key("Escape",27);
                Check("top-style-escape-returns-to-trigger-"+mode,await note.Script("document.getElementById('writingCommands').hidden&&document.activeElement.id==='blockStyleButton'&&document.getElementById('blockStyleButton').ariaExpanded==='false'")=="true");

                foreach(var theme in new[]{"dark","light"})
                {
                    await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));
                    foreach(var width in new[]{320,360,900})
                    {
                        note.Width=width;note.Height=640;await Task.Delay(100);
                        var evidence=JsonSerializer.Deserialize<JsonElement>(await note.Script("(()=>{const title=document.getElementById('documentTitle').getBoundingClientRect(),emoji=document.getElementById('documentEmoji').getBoundingClientRect(),tools=document.querySelector('.document-toolbar').getBoundingClientRect(),area=document.getElementById('writingArea').getBoundingClientRect();return {width:innerWidth,scroll:document.documentElement.scrollWidth,title:{left:title.left,right:title.right,bottom:title.bottom},emoji:{left:emoji.left,right:emoji.right},tools:{top:tools.top,bottom:tools.bottom},area:{top:area.top,bottom:area.bottom}}})()"));
                        Check("heading-tools-fit-"+mode+"-"+theme+"-"+width,await note.Script("(()=>{const title=document.getElementById('documentTitle').getBoundingClientRect(),emoji=document.getElementById('documentEmoji').getBoundingClientRect(),tools=document.querySelector('.document-toolbar').getBoundingClientRect(),area=document.getElementById('writingArea').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth+1&&title.left>=0&&title.right<=innerWidth&&emoji.left>=0&&emoji.right<=innerWidth&&tools.bottom<=area.top+1&&area.bottom-area.top>180})()")=="true",evidence);
                        await note.Capture("document-editing-"+mode+"-"+theme+"-"+width);
                    }
                }
                await Title("Final title before closing");
                if(mode=="tab")await note.CloseNoteTab(id);else await note.HideAfterSaving();
                Check("close-retains-last-title-"+mode,(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Final title before closing");
                Check("offscreen-and-no-renderer-errors-"+mode,note.RuntimeErrors.Count==0&&target.Windows.All(window=>window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost));
            }
            finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        }
    }

    private async Task VerifyDocumentEditingEdges(MainWindow note,JotSession target,string id,string mode,List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="document-editing-edge-"+name+"-"+mode,passed,evidence});
        async Task Key(string key,int code)
        {
            // CDP rawKeyDown does not insert a paragraph. Supply Enter's
            // character event just as a real native keyboard press would.
            if(key=="Enter")await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyDown",key,code=key,windowsVirtualKeyCode=code,text="\r",unmodifiedText="\r"}));
            else await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="rawKeyDown",key,code=key,windowsVirtualKeyCode=code}));
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code=key,windowsVirtualKeyCode=code}));
        }
        async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
        async Task Title(string text)
        {await note.Script("document.getElementById('documentTitle').focus();document.getElementById('documentTitle').select()");await Type(text);}

        await note.Flush();
        await note.Script("(()=>{const title=document.getElementById('documentTitle');title.focus();title.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true}));title.value='عنوان در حال ترکیب';title.dispatchEvent(new InputEvent('input',{bubbles:true,isComposing:true,inputType:'insertCompositionText',data:'عنوان در حال ترکیب'}))})()");
        await target.Changed(false,id);await Task.Delay(70);
        Check("metadata-refresh-does-not-replace-title-ime",await note.Script("document.getElementById('documentTitle').value==='عنوان در حال ترکیب'")=="true");
        await note.Flush();
        Check("forced-flush-preserves-ime-after-metadata-refresh",(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="عنوان در حال ترکیب");
        await note.Script("document.getElementById('documentTitle').dispatchEvent(new CompositionEvent('compositionend',{bubbles:true}));editor.focus()");await note.Flush();

        // Delay the title acknowledgement without touching the native app or
        // user data. Enter must not steal focus back after a later user action.
        await note.Script("window.originalTitleRequest=JotBridge.request;window.releaseTitleRequest=null;JotBridge.request=(action,payload)=>action==='note-metadata'?new Promise((resolve,reject)=>{window.releaseTitleRequest=()=>originalTitleRequest(action,payload).then(resolve,reject)}):originalTitleRequest(action,payload)");
        try
        {
            await Title("Delayed Enter title");await Key("Enter",13);await note.WaitFor("typeof releaseTitleRequest==='function'");
            await note.Script("document.getElementById('documentEmoji').focus();JotBridge.request=originalTitleRequest;releaseTitleRequest()");
            await note.WaitFor("!JotDocumentHeading.pending");await Task.Delay(70);
            Check("late-enter-save-does-not-steal-new-focus",await note.Script("document.activeElement===document.getElementById('documentEmoji')")=="true");
        }
        finally{await note.Script("JotBridge.request=originalTitleRequest");}

        var persistedTitle=(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString();
        var body=(await target.Store.LoadNote(id))!.Value.GetProperty("html").GetString();
        await SetStoreTrigger(target.Store,"CREATE TRIGGER fail_document_title BEFORE UPDATE OF title ON notes BEGIN SELECT RAISE(ABORT,'synthetic title failure');END;");
        await note.Script("window.releaseTitleRequest=null;JotBridge.request=(action,payload)=>action==='note-metadata'?new Promise((resolve,reject)=>{window.releaseTitleRequest=()=>originalTitleRequest(action,payload).then(resolve,reject)}):originalTitleRequest(action,payload)");
        try
        {
            await Title("Earlier pending title");
            await note.WaitFor("typeof releaseTitleRequest==='function'");
            await Title("Latest title survives a failed older save");
            await note.Script("JotBridge.request=originalTitleRequest;releaseTitleRequest()");
            await note.WaitFor("document.querySelector('.document-title-feedback').dataset.error==='true'");
            bool refused=false;try{await note.Flush();}catch(IOException){refused=true;}
            const string retained="JotDocumentHeading.pending&&document.getElementById('documentTitle').value==='Latest title survives a failed older save'&&!document.querySelector('.document-title-feedback button').hidden&&!app.inert";
            const string failureSnapshot="({title:document.getElementById('documentTitle').value,pending:JotDocumentHeading.pending,inert:app.inert,locked:editorLockedByHost,active:document.activeElement?.id,error:document.querySelector('.document-title-feedback').dataset.error,feedback:document.querySelector('.document-title-feedback').textContent,retryHidden:document.querySelector('.document-title-feedback button').hidden})";
            var immediateFailureEvidence=JsonSerializer.Deserialize<JsonElement>(await note.Script(failureSnapshot));
            for(int attempt=0;attempt<10&&await note.Script(retained)!="true";attempt++)await Task.Delay(30);
            var failureEvidence=JsonSerializer.Deserialize<JsonElement>(await note.Script(failureSnapshot));
            Check("failed-native-flush-retains-latest-title-draft",refused&&await note.Script(retained)=="true",new{refused,immediate=immediateFailureEvidence,settled=failureEvidence});
            Check("failed-title-save-does-not-rewrite-saved-title-or-body",(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()==persistedTitle&&(await target.Store.LoadNote(id))!.Value.GetProperty("html").GetString()==body);
        }
        finally
        {
            await note.Script("JotBridge.request=originalTitleRequest");
            await SetStoreTrigger(target.Store,"DROP TRIGGER fail_document_title;");
        }
        await note.ClickControl(".document-title-feedback button");await note.WaitFor("!JotDocumentHeading.pending");
        Check("retry-persists-latest-title-and-clears-error",(await target.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Latest title survives a failed older save"&&await note.Script("document.querySelector('.document-title-feedback').hidden")=="true");

        await note.Script("documentFixture('<p><br></p>')");await Type("/usr");
        await note.WaitFor("document.getElementById('writingCommands').hidden&&editor.textContent==='/usr'");
        await Key("Enter",13);await Type("next line");
        Check("unmatched-slash-enter-inserts-normal-newline",await note.Script("editor.children.length>=2&&editor.firstElementChild.textContent==='/usr'&&editor.lastElementChild.textContent==='next line'&&document.getElementById('writingCommands').hidden")=="true",JsonSerializer.Deserialize<JsonElement>(await note.Script("({html:editor.innerHTML,text:editor.textContent,active:document.activeElement?.id,menuHidden:document.getElementById('writingCommands').hidden})")));

        await note.Script("documentFixture('<p><br></p>')");await Type("/heading");await note.WaitFor("!document.getElementById('writingCommands').hidden");
        await note.Script("document.execCommand('insertText',false,' 2');editor.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true,cancelable:true}))");
        Check("enter-rechecks-query-before-next-animation-frame",await note.Script("!!editor.querySelector('h2')&&!editor.querySelector('h1')&&!editor.textContent.includes('/heading')")=="true");

        await note.Script("documentFixture('<p><br></p>')");await Type("/heading");await note.WaitFor("!document.getElementById('writingCommands').hidden");
        await note.Script("(()=>{const oldChoice=document.querySelector('[data-writing-command=heading-1]');document.execCommand('insertText',false,' unavailable');oldChoice.click()})()");
        Check("stale-command-click-cannot-consume-new-query",await note.Script("editor.textContent==='/heading unavailable'&&!!editor.querySelector('p')&&!editor.querySelector('h1')")=="true");

        // Measure work, not wall-clock thresholds: querying near a long
        // paragraph's end must not stringify its entire preceding text.
        await note.Script("documentFixture('<p>'+('long paragraph text '.repeat(10000))+' /heading</p>');window.slashScanEvidence=(()=>{const original=Range.prototype.toString,lengths=[];Range.prototype.toString=function(){const value=original.call(this);lengths.push(value.length);return value};const start=performance.now();try{for(let i=0;i<10;i++)JotWritingTools.refresh()}finally{Range.prototype.toString=original}return {largestRangeString:Math.max(0,...lengths),calls:lengths.length,elapsedMs:performance.now()-start,characters:editor.textContent.length}})()");
        Check("slash-scan-is-bounded-on-long-paragraph",await note.Script("slashScanEvidence.largestRangeString<=128&&slashScanEvidence.characters>200000&&!document.getElementById('writingCommands').hidden&&document.querySelectorAll('[data-writing-command]').length===3")=="true",JsonSerializer.Deserialize<JsonElement>(await note.Script("slashScanEvidence")));
        await note.Script("documentFixture('<p>Body stays separate from the title.</p>')");await note.Flush();
    }
}

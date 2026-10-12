using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    // Test through the current editor API/native input bridge, never by mutating
    // the ProseMirror DOM. Every note belongs to an offscreen isolated session.
    private async Task VerifyEditorToolbarWorkflow(List<object> checks)
    {
        foreach(var mode in new[]{"tab","window"})
        {
            var target=new JotSession(true,Path.Combine(testOutput,"editor-toolbar-"+mode)){ExerciseLifecycle=true};
            MainWindow? note=null;
            void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="editor-toolbar-"+mode+"-"+name,passed,evidence});
            try
            {
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=16,lineHeight=1.95,autoSaveFiles=false}));
                var id=await target.Store.Create();
                note=mode=="tab"?await target.OpenAsTab(id):await target.OpenNoteWindow(id);
                note.Width=1200;note.Height=700;
                await note.WaitFor("window.jotReady&&window.JotRichEditor?.ready&&editor.isContentEditable");
                async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
                async Task Escape()
                {
                    foreach(var type in new[]{"rawKeyDown","keyUp"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key="Escape",code="Escape",windowsVirtualKeyCode=27}));
                    await Task.Delay(100);
                }
                async Task Fixture(string html)
                {
                    await note.Script("JotRichEditor.setContent("+JsonSerializer.Serialize(html)+");JotRichEditor.editor.commands.focus('end');JotRichEditor.editor.view.focus()");
                    await Task.Delay(80);
                }
                async Task OpenPanel(string label)
                {
                    await note.WaitFor("!document.querySelector('.jot-editor-popover[data-ending-style]')");
                    var literal=JsonSerializer.Serialize(label);
                    if(await note.Script("[...document.querySelectorAll('.jot-editor-toolbar button')].some(button=>button.getAttribute('aria-label')==="+literal+"&&button.getClientRects().length)")=="true")
                        await note.Script("[...document.querySelectorAll('.jot-editor-toolbar button')].find(button=>button.getAttribute('aria-label')==="+literal+").click()");
                    else
                    {
                        await note.ClickControl(".jot-editor-toolbar button[aria-label='More editor tools']");
                        await note.WaitFor("[...document.querySelectorAll('.jot-editor-menu-item')].some(item=>item.textContent==="+literal+")");
                        await note.Script("[...document.querySelectorAll('.jot-editor-menu-item')].find(item=>item.textContent==="+literal+").click()");
                    }
                }
                async Task Assert(string name,string predicate)
                {
                    var passed=await note.Script(predicate)=="true";
                    Check(name,passed,passed?null:JsonSerializer.Deserialize<JsonElement>(await note.Script("({html:JotRichEditor.getHTML(),selection:JotRichEditor.selection(),active:document.activeElement?.outerHTML?.slice(0,300),width:innerWidth,height:innerHeight,toolbar:document.querySelector('.jot-editor-toolbar')?.getBoundingClientRect().toJSON(),menus:[...document.querySelectorAll('.jot-editor-positioner,.jot-slash-positioner')].map(element=>({text:element.textContent,rect:element.getBoundingClientRect().toJSON()}))})")));
                }

                await Fixture("<p>Alpha <strong>bold</strong> · متن فارسی</p>");
                await Assert("full-width-top-row", "(()=>{const bar=document.getElementById('formatBar'),r=bar.getBoundingClientRect(),writing=document.getElementById('writingArea').getBoundingClientRect();return !bar.hidden&&r.width>=document.getElementById('app').clientWidth-24&&r.bottom<=writing.top+1&&r.height>=30&&r.height<55})()");
                await Assert("single-image-affordance-wide", "[...document.querySelectorAll('.jot-editor-toolbar button')].filter(button=>button.getClientRects().length&&button.getAttribute('aria-label')==='Image from computer').length===1");
                await Assert("unlink-adjacent-and-disabled-without-link", "(()=>{const add=document.querySelector('.jot-editor-toolbar button[aria-label=\"Edit link\"]'),remove=document.querySelector('.jot-editor-toolbar button[aria-label=\"Remove link\"]');return !!add&&!!remove&&remove.disabled&&Math.abs(add.getBoundingClientRect().right-remove.getBoundingClientRect().left)<8})()");

                await note.Script("JotRichEditor.restoreSelection({anchor:1,head:6});JotRichEditor.editor.view.focus();window.toolbarSelected=JSON.stringify(JotRichEditor.selection())");
                await OpenPanel("Edit link");await note.WaitFor("document.activeElement?.getAttribute('aria-label')==='Link address'");
                await Type("https://example.com/jot");
                await note.ClickControl(".jot-link-editor button[type='submit']");
                await note.WaitFor("editor.querySelector('a')?.textContent==='Alpha'");
                await Assert("link-keeps-original-selected-phrase", "editor.querySelector('a')?.getAttribute('href')==='https://example.com/jot'&&editor.textContent==='Alpha bold · متن فارسی'");
                await note.ClickControl(".jot-editor-toolbar button[aria-label='Remove link']");
                await Assert("explicit-unlink-removes-mark-only", "!editor.querySelector('a')&&editor.textContent==='Alpha bold · متن فارسی'");

                await note.Script("window.toolbarBeforeTypography=JotRichEditor.getHTML()");
                await OpenPanel("Font size and line height");await note.WaitFor("!!document.querySelector('#jot-note-font-size')?.getClientRects().length");
                await note.Script("(()=>{const select=document.getElementById('jot-note-font-size');select.value='19';select.dispatchEvent(new Event('change',{bubbles:true}))})()");
                await note.WaitFor("model.prefs.fontSize===19&&!document.querySelector('.jot-writing-preferences')");await note.Flush();
                Check("note-only-font-size",(await target.Store.LoadNote(id))!.Value.GetProperty("view").GetProperty("fontSize").GetInt32()==19&&(await target.Store.LoadPreferences()).GetProperty("fontSize").GetInt32()==16);
                await OpenPanel("Font size and line height");await note.WaitFor("!!document.querySelector('#jot-note-line-height')?.getClientRects().length");
                await note.Script("(()=>{const select=document.getElementById('jot-note-line-height');select.value='1.5';select.dispatchEvent(new Event('change',{bubbles:true}))})()");
                await note.WaitFor("model.prefs.lineHeight===1.5&&!document.querySelector('.jot-writing-preferences')");
                await Assert("typography-does-not-change-rich-content", "JotRichEditor.getHTML()===toolbarBeforeTypography");
                await OpenPanel("Font size and line height");await note.WaitFor("!!document.querySelector('.jot-writing-setting button')?.getClientRects().length");
                await note.Script("document.querySelector('.jot-writing-setting button').click()");await note.WaitFor("model.prefs.fontSize===16");await note.Flush();
                Check("font-use-default-restores-inheritance",!(await target.Store.LoadNote(id))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));

                await Fixture("<p></p>");await Type("/");await note.WaitFor("!!document.querySelector('.jot-slash-menu')");
                await Assert("slash-covers-writing-tools", "['table','image','bold','italic','underline','strike','inlineCode','subscript','superscript','textColor','link','unlink','fontSize','align-justify','direction-rtl','csharp','clearFormatting','find'].every(id=>!!document.querySelector('.jot-slash-menu [data-command=\"'+id+'\"]'))&&!document.querySelector('[data-command=\"table-rowAfter\"]')");
                await Escape();await Assert("slash-escape-preserves-prefix", "!document.querySelector('.jot-slash-menu')&&JotRichEditor.getText()==='/'");
                await Fixture("<p></p>");await Type("/table");await note.WaitFor("!!document.querySelector('[data-command=table]')");
                await note.ClickControl("[data-command=table]");await note.WaitFor("!!document.querySelector('.jot-table-insert')?.getClientRects().length");
                await Assert("table-popover-preserves-prefix-until-accepted", "JotRichEditor.getText()==='/table'");
                await Escape();await Assert("table-cancel-keeps-prefix", "JotRichEditor.getText()==='/table'&&!editor.querySelector('table')");
                await Fixture("<p></p>");await Type("/table");await note.WaitFor("!!document.querySelector('[data-command=table]')");
                await note.ClickControl("[data-command=table]");await note.WaitFor("!!document.querySelector('.jot-table-insert button[type=submit]')?.getClientRects().length");
                await note.ClickControl(".jot-table-insert button[type=submit]");
                await Assert("table-accept-replaces-prefix", "!!editor.querySelector('table')&&!JotRichEditor.getText().includes('/table')");
                await note.Script("JotRichEditor.editor.commands.undo()");
                await Assert("table-insertion-one-undo-step", "!editor.querySelector('table')&&JotRichEditor.getText()==='/table'");

                await Fixture("<p></p>");await Type("/link");await note.WaitFor("!!document.querySelector('[data-command=link]')");
                await note.ClickControl("[data-command=link]");await note.WaitFor("!!document.querySelector('.jot-link-editor')?.getClientRects().length");await Escape();
                await Assert("link-cancel-preserves-prefix", "JotRichEditor.getText()==='/link'&&!editor.querySelector('a')");
                await Fixture("<p></p>");await Type("/link");await note.WaitFor("!!document.querySelector('[data-command=link]')");
                await note.Script("window.toolbarEqualDocument=JotRichEditor.getHTML();document.querySelector('[data-command=link]').click();JotRichEditor.loadNote('isolated-equal-toolbar-note',toolbarEqualDocument);JotRichEditor.loadNote(model.activeId,toolbarEqualDocument)");
                await Task.Delay(100);
                await Assert("queued-panel-cannot-follow-equal-content-note-switch", "!document.querySelector('.jot-link-editor')?.getClientRects().length&&JotRichEditor.getText()==='/link'");
                await Fixture("<p></p>");await Type("/csharp");await note.WaitFor("!!document.querySelector('[data-command=csharp]')");
                await note.ClickControl("[data-command=csharp]");await Type("public class Example { }");
                await Assert("csharp-code-language-and-highlighting", "JotRichEditor.editor.getAttributes('codeBlock').language==='csharp'&&!!editor.querySelector('pre code.language-csharp')&&!!editor.querySelector('.hljs-keyword')");
                await note.WaitFor("[...document.querySelectorAll('.block-editor-bubble-btn')].some(button=>button.textContent.trim()==='C#')");
                await note.Script("[...document.querySelectorAll('.block-editor-bubble-btn')].find(button=>button.textContent.trim()==='C#').click()");
                await Assert("csharp-listed-in-language-picker", "[...document.querySelectorAll('.block-editor-bubble-dropdown-item')].some(item=>item.textContent.trim()==='C#')");await Escape();

                foreach(var width in new[]{320,480,800,1200})
                {
                    note.Width=width;await Fixture("<p>Responsive tools</p>");await Task.Delay(100);
                    await Assert("responsive-row-"+width, "(()=>{const bar=document.querySelector('.jot-editor-toolbar'),r=bar.getBoundingClientRect();return r.width>0&&bar.scrollWidth<=bar.clientWidth+1&&document.documentElement.scrollWidth<=innerWidth+1&&r.left>=0&&r.right<=innerWidth+1&&!!bar.querySelector('[aria-label=\"More editor tools\"]')})()");
                    await OpenPanel("Insert table");await note.WaitFor("!!document.querySelector('.jot-table-insert')?.getClientRects().length");
                    await Assert("responsive-table-panel-"+width, "(()=>{const r=document.querySelector('.jot-table-insert').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1})()");
                    if(width is 320 or 1200)await note.Capture("editor-toolbar-"+mode+"-"+width);
                    await Escape();
                }
                note.Width=320;note.Height=350;
                await Fixture(string.Concat(Enumerable.Range(0,40).Select(index=>"<p>Scroll line "+index+" · متن</p>"))+"<p></p>");
                await Type("/");await note.WaitFor("!!document.querySelector('.jot-slash-menu')");await Task.Delay(100);
                await Assert("slash-fits-narrow-shallow-scrolled-note", "(()=>{const r=document.querySelector('.jot-slash-menu').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&r.height>40})()");
                await note.Capture("editor-slash-"+mode+"-shallow");
                await note.Script("document.getElementById('writingArea').scrollTop=0");await Task.Delay(100);
                await Assert("slash-dismisses-when-anchor-scrolls-away", "!document.querySelector('.jot-slash-menu')&&JotRichEditor.getText().endsWith('/')");
                Check("no-runtime-errors",note.RuntimeErrors.Count==0,note.RuntimeErrors.ToArray());
                Check("isolated-offscreen-windows",target.Windows.All(window=>window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&window.Left< -10000&&window.Top< -10000));
            }
            catch(Exception error)
            {
                string? state=null;
                if(note?.Browser.CoreWebView2 is not null)
                {
                    try{state=await note.Script("JSON.stringify({html:JotRichEditor.getHTML(),prefs:model.prefs,view:activeNote()?.view,active:document.activeElement?.outerHTML,locked:editorLockedByHost,panels:[...document.querySelectorAll('.jot-editor-positioner,.jot-slash-positioner')].map(element=>({html:element.outerHTML,rect:element.getBoundingClientRect().toJSON()})),layout:['#app','#workspaceHandle','#workspaceViews','.document-toolbar','#formatBar','#writingArea'].map(selector=>{const element=document.querySelector(selector);return {selector,rect:element?.getBoundingClientRect().toJSON()}})})").WaitAsync(TimeSpan.FromSeconds(3));}
                    catch(Exception inspectionError){state=inspectionError.ToString();}
                }
                await File.WriteAllTextAsync(Path.Combine(testOutput,"toolbar-failure-"+mode+".json"),JsonSerializer.Serialize(new{error=error.ToString(),state},new JsonSerializerOptions{WriteIndented=true}));
                throw;
            }
            finally
            {
                target.StopFileAutoSave();await target.WaitForNoteFiles();target.DisposeGlobalShortcuts();
                foreach(var window in target.Windows.ToArray())window.ClosePermanently();
            }
        }
    }
}

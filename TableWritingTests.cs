using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyTableWriting(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="table-writing-"+name,passed,evidence});
        foreach(var mode in new[]{"tab","window"})
        {
            var directory=Path.Combine(testOutput,"table-writing-"+mode);Directory.CreateDirectory(directory);
            var target=new JotSession(true,directory){ExerciseLifecycle=true};
            try
            {
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode,autoSaveFiles=false}));
                var note=await target.NewNote();note.Width=800;note.Height=640;
                await note.WaitFor("window.jotReady===true&&!!window.JotDocumentBlocks&&!!window.JotWritingTools&&editor.isContentEditable&&!editorLockedByHost");var id=note.NoteId!;
                async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
                async Task Key(string key,int code,int modifiers=0)
                {
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="rawKeyDown",key,code=key,windowsVirtualKeyCode=code,modifiers}));
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code=key,windowsVirtualKeyCode=code,modifiers}));
                }
                await note.Script("""
                    window.tableFixture=html=>{
                      JotWritingTools.hide();JotDocumentBlocks.close();flushTypingHistory();closePanels();editor.innerHTML=sanitizeHtml(html);bookmark=null;bookmarkDirection=null;
                      normalizeDirection();updateEmpty();editor.focus();getSelection().removeAllRanges();
                      const range=document.createRange();range.selectNodeContents(editor.firstElementChild||editor);range.collapse(false);getSelection().addRange(range);rememberSelection();
                      histories.set(model.activeId,{values:[editor.innerHTML],selections:[editorSelectionState()],index:0,time:0,kind:''});typingHistoryPending=false;clearTimeout(saveTimer);
                    };
                    window.tableFocus=(row,column,end=false)=>{
                      const cell=editor.querySelector('table').rows[row].cells[column],range=document.createRange();editor.focus();range.selectNodeContents(cell.querySelector('p')||cell);range.collapse(!end);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();
                    };
                    tableFixture('<p><br></p>');
                    """);
                await Type("/table");await note.WaitFor("!document.getElementById('writingCommands').hidden&&!!document.querySelector('[data-writing-command=table]')");
                await note.ClickControl("[data-writing-command=table]");
                Check("slash-opens-dimensions-without-consuming-text-"+mode,await note.Script("!document.getElementById('tableTools').hidden&&editor.textContent==='/table'&&document.activeElement.dataset.tableSize==='rows'")=="true");
                await Key("Escape",27);
                Check("cancel-keeps-slash-and-body-"+mode,await note.Script("document.getElementById('tableTools').hidden&&editor.textContent==='/table'&&document.activeElement===editor")=="true");
                await note.Script("window.tableFailureRevision=revision;window.originalTableCommand=document.execCommand;document.execCommand=function(name,...args){return name==='insertHTML'?false:originalTableCommand.call(this,name,...args)}");
                try
                {
                    await note.Script("JotWritingTools.refresh()");await note.ClickControl("[data-writing-command=table]");await note.ClickControl(".table-insert-button");
                    Check("failed-insertion-rolls-back-slash-without-save-or-history-"+mode,await note.Script("editor.textContent==='/table'&&!editor.querySelector('table')&&revision===tableFailureRevision&&histories.get(model.activeId).values.at(-1)===editor.innerHTML&&editor.contains(getSelection().anchorNode)")=="true");
                }
                finally{await note.Script("document.execCommand=originalTableCommand");}
                await note.Script("JotWritingTools.refresh()");await note.ClickControl("[data-writing-command=table]");
                await note.Script("document.querySelector('[data-table-size=rows]').value='2';document.querySelector('[data-table-size=columns]').value='3'");await note.ClickControl(".table-insert-button");
                Check("slash-inserts-table-and-focuses-first-cell-"+mode,await note.Script("(()=>{const table=editor.querySelector('table');return table?.rows.length===2&&table.rows[0].cells.length===3&&table.rows[0].cells[0].tagName==='TH'&&table.rows[1].cells[0].tagName==='TD'&&table.rows[0].cells[0].contains(getSelection().anchorNode)&&!editor.textContent.includes('/table')&&!editor.querySelector('[data-jot-insertion]')})()")=="true");
                await note.Script("undo()");
                Check("table-and-slash-are-one-undo-step-"+mode,await note.Script("!editor.querySelector('table')&&editor.textContent==='/table'")=="true");
                await note.Script("undo(true);tableFocus(0,0)");await Type("نام پروژه");await Key("Tab",9);await Type("Project B");
                Check("cell-typing-is-bilingual-and-tab-moves-forward-"+mode,await note.Script("(()=>{const t=editor.querySelector('table');return t.rows[0].cells[0].textContent==='نام پروژه'&&t.rows[0].cells[1].textContent==='Project B'&&t.rows[0].cells[0].querySelector('p').dir==='rtl'&&t.rows[0].cells[1].querySelector('p').dir==='ltr'})()")=="true");
                await Key("Tab",9,8);
                Check("shift-tab-moves-to-previous-cell-"+mode,await note.Script("editor.querySelector('table').rows[0].cells[0].contains(getSelection().anchorNode)")=="true");
                await note.Script("tableFocus(1,2)");await Type("آخرین خانه");await Key("Tab",9);
                Check("last-cell-tab-adds-row-and-focuses-first-column-"+mode,await note.Script("(()=>{const t=editor.querySelector('table');return t.rows.length===3&&t.rows[2].cells.length===3&&t.rows[2].cells[0].contains(getSelection().anchorNode)&&t.rows[1].cells[2].textContent==='آخرین خانه'})()")=="true");
                await note.Script("undo()");Check("new-row-is-undoable-"+mode,await note.Script("editor.querySelector('table').rows.length===2&&editor.querySelector('table').rows[1].cells[2].textContent==='آخرین خانه'")=="true");await note.Script("undo(true);tableFocus(1,1)");
                await note.ClickControl("#tableButton");await note.ClickControl("[data-table-action=column-after]");
                Check("menu-adds-column-across-heading-and-body-"+mode,await note.Script("[...editor.querySelector('table').rows].every(row=>row.cells.length===4)&&editor.querySelector('table').rows[0].cells[2].tagName==='TH'")=="true");
                await note.ClickControl("#tableButton");await note.ClickControl("[data-table-action=delete-column]");
                await note.ClickControl("#tableButton");await note.ClickControl("[data-table-action=row-before]");
                Check("row-before-keeps-content-"+mode,await note.Script("editor.querySelector('table').rows.length===4&&editor.textContent.includes('Project B')&&editor.textContent.includes('آخرین خانه')")=="true");
                await note.ClickControl("#tableButton");await note.ClickControl("[data-table-action=delete-row]");
                await note.ClickControl("#tableButton");await note.ClickControl("[data-table-action=delete-table]");
                Check("delete-table-leaves-editable-paragraph-"+mode,await note.Script("!editor.querySelector('table')&&!!editor.querySelector('p')&&editor.contains(getSelection().anchorNode)")=="true");
                await note.Script("undo()");await note.Flush();
                var persisted=(await target.Store.LoadNote(id))!.Value.GetProperty("html").GetString();
                Check("table-persists-in-store-"+mode,persisted!.Contains("<table")&&persisted.Contains("Project B")&&!persisted.Contains("data-jot-insertion"));
                var file=Path.Combine(directory,"Table document.jot");note.TestSaveNoteFilePath=file;await note.SaveNoteFile(id,true);
                var exported=(await NoteFileFormat.Read(file)).Note["html"]!.GetValue<string>();
                Check("portable-file-keeps-table-and-bilingual-text-"+mode,exported.Contains("<table")&&exported.Contains("Project B")&&exported.Contains("نام پروژه"));
                await note.Reload();await note.WaitFor("window.jotReady===true&&!!window.JotDocumentBlocks&&!!editor.querySelector('table')");
                Check("reload-retains-table-shape-and-content-"+mode,await note.Script("editor.querySelector('table').rows.length===3&&[...editor.querySelector('table').rows].every(row=>row.cells.length===3)&&editor.textContent.includes('Project B')")=="true");
                foreach(var theme in new[]{"dark","light"})
                {
                    await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));
                    foreach(var width in new[]{320,800})
                    {
                        note.Width=width;await Task.Delay(100);
                        await note.Script("editor.focus();const range=document.createRange();range.selectNodeContents(editor.querySelector('td'));range.collapse(true);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection()");
                        await note.ClickControl("#tableButton");
                        Check("responsive-menu-and-table-"+mode+"-"+theme+"-"+width,await note.Script("(()=>{const table=editor.querySelector('table').getBoundingClientRect(),panel=document.getElementById('tableTools').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth+1&&table.right<=innerWidth&&panel.left>=0&&panel.right<=innerWidth&&panel.bottom<=innerHeight})()")=="true");
                        await note.Capture("table-writing-"+mode+"-"+theme+"-"+width);await Key("Escape",27);
                    }
                }
                // Self-contained fixtures after reload: never access user notes.
                await note.Script("editor.innerHTML=sanitizeHtml('<p>word</p>');bookmark=null;normalizeDirection();editor.focus();getSelection().selectAllChildren(editor.firstElementChild);rememberSelection();JotDocumentBlocks.inlineCode()");
                Check("inline-code-keeps-selected-word-"+mode,await note.Script("editor.querySelector('code')?.textContent==='word'&&getSelection().toString()==='word'")=="true");
                await note.Script("JotDocumentBlocks.inlineCode()");Check("inline-code-can-be-toggled-off-"+mode,await note.Script("!editor.querySelector('code')&&getSelection().toString()==='word'")=="true");
                await note.Script("getSelection().collapseToEnd();rememberSelection();JotDocumentBlocks.insertDivider();normalizeDirection()");
                Check("divider-remains-top-level-through-sanitizer-"+mode,await note.Script("!!editor.querySelector(':scope>hr')&&new DOMParser().parseFromString(sanitizeHtml(editor.innerHTML),'text/html').body.querySelector('hr')?.parentElement.tagName==='BODY'")=="true");
                await note.Script("editor.innerHTML=sanitizeHtml('<table><tbody><tr><td colspan=2>merged متن</td></tr><tr><td>A</td><td>B</td></tr></tbody></table><p><br></p>');bookmark=null;normalizeDirection();editor.focus();getSelection().selectAllChildren(editor.querySelector('td'));getSelection().collapseToStart();rememberSelection()");
                await note.ClickControl("#tableButton");
                Check("merged-table-structure-is-protected-"+mode,await note.Script("document.querySelector('[data-table-action=column-after]').disabled&&!document.querySelector('[data-table-action=delete-table]').disabled&&editor.querySelector('td').colSpan===2")=="true");await Key("Escape",27);
                Check("offscreen-and-no-renderer-errors-"+mode,note.RuntimeErrors.Count==0&&target.Windows.All(window=>window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost));
            }
            finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        }
    }
}

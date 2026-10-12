using System.IO;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyNoteSearch(List<object> checks)
    {
        var remapped=ShortcutBindings.ValidateOverrides(JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"find-previous","Shift+F4"}}));
        checks.Add(new{name="note-search-shift-function-key-remains-configurable",passed=remapped["find-previous"]!.GetValue<string>()=="Shift+F4"});
        foreach(var mode in new[]{"tab","window"})
        {
            var target=new JotSession(true,Path.Combine(testOutput,"note-search-"+mode)){ExerciseLifecycle=true};
            void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="note-search-"+mode+"-"+name,passed,evidence});
            try
            {
                var id=await target.Store.Create();var note=mode=="tab"?await target.OpenAsTab(id):await target.OpenNoteWindow(id);
                note.Width=800;note.Height=600;await note.WaitFor("window.jotReady&&!!JotRichEditor?.find&&editor.isContentEditable");
                async Task Key(string key,string code,int vk,int modifiers=0)
                {foreach(var type in new[]{"rawKeyDown","keyUp"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code,windowsVirtualKeyCode=vk,modifiers}));}
                await note.Script("JotRichEditor.setContent('<p>Alpha <strong>Project</strong> alpha</p><p>پروژه می‌شود ۱۲۳</p><p>End project café</p>');JotRichEditor.editor.view.focus();JotRichEditor.restoreSelection({anchor:1,head:6});window.findOriginalSelection=JSON.stringify(JotRichEditor.selection());window.findOriginalHtml=JotRichEditor.getHTML()");
                await note.Flush();var stored=(await target.Store.LoadNote(id))!.Value.GetRawText();
                await Key("f","KeyF",70,2);await note.WaitFor("JotRichEditor.find.isOpen");
                Check("ctrl-f-opens-and-prefills-selection",await note.Script("document.activeElement.id==='noteFindInput'&&document.getElementById('noteFindInput').value==='Alpha'&&JotRichEditor.find.count===2")=="true");
                await note.Script("JotRichEditor.find.setQuery('project')");
                Check("matches-across-rich-inline-nodes",await note.Script("JotRichEditor.find.count===2&&document.querySelectorAll('#editor .jot-find-match').length===2")=="true");
                await Key("F3","F3",114);Check("next-match",await note.Script("JotRichEditor.find.activeIndex===1")=="true");
                await Key("F3","F3",114);Check("next-wraps",await note.Script("JotRichEditor.find.activeIndex===0")=="true");
                await Key("F3","F3",114,8);Check("previous-wraps",await note.Script("JotRichEditor.find.activeIndex===1")=="true");
                await note.Script("JotRichEditor.find.setQuery('ميشود')");Check("persian-arabic-forms-and-zwnj-search",await note.Script("JotRichEditor.find.count===1")=="true");
                await note.Script("JotRichEditor.find.setQuery('123')");Check("localized-digits-search",await note.Script("JotRichEditor.find.count===1")=="true");
                await note.Script("JotRichEditor.find.setQuery('cafe')");Check("accent-folding-search",await note.Script("JotRichEditor.find.count===1")=="true");
                await note.Script("JotRichEditor.find.setQuery('[not a regex]')");Check("literal-no-match-state",await note.Script("JotRichEditor.find.count===0&&document.getElementById('noteFindCount').textContent==='No matches'")=="true");
                await note.Script("JotRichEditor.find.setQuery('alpha');document.querySelector('#noteFind [aria-label=\"Match case\"]').click()");
                Check("case-sensitive-option",await note.Script("JotRichEditor.find.count===1")=="true");
                await Key("Escape","Escape",27);
                Check("escape-restores-caret-and-removes-highlights",await note.Script("!JotRichEditor.find.isOpen&&document.activeElement===editor&&JSON.stringify(JotRichEditor.selection())===findOriginalSelection&&!editor.querySelector('.jot-find-match')")=="true");
                await note.Flush();Check("find-does-not-edit-save-or-change-undo",(await target.Store.LoadNote(id))!.Value.GetRawText()==stored&&await note.Script("JotRichEditor.getHTML()===findOriginalHtml&&!JotRichEditor.canUndo()")=="true");
                await note.Script("JotRichEditor.find.open();JotRichEditor.find.setQuery('Alpha')");
                foreach(var width in new[]{320d,600d})
                {note.Width=width;await Task.Delay(100);Check("find-row-fits-"+width,await note.Script("(()=>{const r=document.getElementById('noteFind').getBoundingClientRect();return r.right<=innerWidth+1&&document.getElementById('noteFind').scrollWidth<=document.getElementById('noteFind').clientWidth&&document.getElementById('noteFindInput').getBoundingClientRect().width>=50})()")=="true");await note.Capture("note-search-"+mode+"-"+width);}
                note.Width=320;await Task.Delay(100);await note.Script("JotRichEditor.find.close()");
                await note.ClickControl(".jot-editor-toolbar button[aria-label='More editor tools']");
                await note.WaitFor("[...document.querySelectorAll('.jot-editor-menu-item')].some(item=>item.textContent.includes('Find in note'))");
                await note.Script("[...document.querySelectorAll('.jot-editor-menu-item')].find(item=>item.textContent.includes('Find in note')).dataset.findAction='true'");
                await note.ClickControl(".jot-editor-menu-item[data-find-action=true]");
                try{await note.WaitFor("JotRichEditor.find.isOpen&&document.activeElement.id==='noteFindInput'");}
                catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"find-overflow-failure-"+mode+".json"),await note.Script("({open:JotRichEditor.find.isOpen,active:document.activeElement?.outerHTML,hidden:app.hidden,inert:app.inert,ready,view:window.JotWorkspace?.view,menus:[...document.querySelectorAll('.jot-editor-menu')].map(n=>n.outerHTML)})"));throw;}
                Check("responsive-more-find-keeps-search-focus",await note.Script("document.activeElement.id==='noteFindInput'")=="true");
                await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>{{"find-note","Alt+KeyF"}}}));
                await note.WaitFor("JotShortcutBindings.label('find-note')==='Alt+F'");await note.Script("JotRichEditor.find.close()");
                await Key("f","KeyF",70,1);await note.WaitFor("JotRichEditor.find.isOpen&&document.activeElement.id==='noteFindInput'");
                Check("custom-alt-find-shortcut-works",await note.Script("JotRichEditor.find.isOpen")=="true");
                await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>()}));
                await note.WaitFor("JotShortcutBindings.label('find-note')==='Ctrl+F'");
                if(mode=="tab")
                {
                    var other=await target.Store.Create();await note.SwitchNoteTab(other);await note.WaitFor("model.activeId==="+JsonSerializer.Serialize(other)+"&&ready");
                    Check("switch-clears-old-note-highlights",await note.Script("!JotRichEditor.find.isOpen&&!editor.querySelector('.jot-find-match')")=="true");
                    await note.SwitchHomeView();await note.WaitFor("JotWorkspace.view==='home'");await Key("f","KeyF",70,2);
                    Check("home-ctrl-f-focuses-library-search",await note.Script("document.activeElement.id==='homeSearch'&&!JotRichEditor.find.isOpen")=="true");
                }
                Check("no-renderer-errors",note.RuntimeErrors.Count==0,note.RuntimeErrors);
            }
            finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        }
    }
}

using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyNotePersonalization(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="note-personalization-"+name,passed,evidence});
        var s=new JotSession(true,Path.Combine(testOutput,"note-personalization")){ExerciseLifecycle=true};
        try
        {
            var id=await s.Store.Create();var note=new MainWindow(s,"note",id);note.Reveal();await note.WaitFor("window.jotReady===true");
            Check("default-is-memo-emoji",(await s.Store.LoadNote(id))!.Value.GetProperty("icon").GetString()==NoteStore.DefaultNoteIcon&&await note.Script("JotNoteIcons.normalize(null)==='emoji:📝'&&JotNoteIcons.render(null).textContent==='📝'")=="true");
            foreach(var icon in new[]{"icon:notepad-text","icon:book-open","icon:code-xml","emoji:📝","emoji:❤️","emoji:👨‍👩‍👧‍👦","emoji:🇮🇷","emoji:1️⃣","emoji:👍🏽"})
                Check("valid-icon-"+icon,NoteStore.ValidateNoteIcon(icon)==icon&&await note.Script("JotNoteIcons.valid("+JsonSerializer.Serialize(icon)+")")=="true");
            foreach(var icon in new[]{"icon:unknown","emoji:abc","emoji:🙂🙂","emoji:<img>","emoji:\u200f","emoji:\u20E3","emoji:🏽","emoji:"+new string('a',40)})
            {
                var rejected=false;try{NoteStore.ValidateNoteIcon(icon);}catch(InvalidDataException){rejected=true;}
                Check("invalid-icon-"+JsonSerializer.Serialize(icon),rejected&&await note.Script("!JotNoteIcons.valid("+JsonSerializer.Serialize(icon)+")")=="true");
            }
            await note.ClickControl("#menuButton");await note.ClickControl("#noteIconButton");
            Check("picker-has-only-labeled-emoji",await note.Script("document.getElementById('noteIconDialog').open&&document.querySelectorAll('[data-note-icon]').length===96&&[...document.querySelectorAll('[data-note-icon]')].every(button=>button.ariaLabel&&button.dataset.noteIcon.startsWith('emoji:'))")=="true");
            await note.Script("window.personalizationEmojiSaved=false;JotNoteIcons.choose('emoji:💻').then(()=>window.personalizationEmojiSaved=true)");await note.WaitFor("window.personalizationEmojiSaved&&!document.getElementById('noteIconDialog').open");
            Check("picker-saves-native-metadata",(await s.Store.LoadNote(id))!.Value.GetProperty("icon").GetString()=="emoji:💻");
            await note.Reload();
            Check("chosen-emoji-survives-reload",await note.Script("activeNote().icon==='emoji:💻'")=="true");
            await note.Script("JotNoteIcons.open(activeNote());window.iconSaved=false;JotNoteIcons.choose('emoji:📝').then(()=>window.iconSaved=true)");await note.WaitFor("window.iconSaved===true");
            Check("custom-emoji-saves-and-renders-as-text",(await s.Store.LoadNote(id))!.Value.GetProperty("icon").GetString()=="emoji:📝"&&await note.Script("JotNoteIcons.render('emoji:📝').textContent==='📝'&&!JotNoteIcons.render('emoji:<img>').querySelector('img')")=="true");
            await note.Script("""
                window.personalizationFixture=()=>{
                  flushTypingHistory();editor.innerHTML='<p><b>First</b> paragraph</p><p data-jot-direction="rtl">مرحله دوم</p><p>Last paragraph</p>';bookmark=null;normalizeDirection();onEdit('command');
                  editor.focus();const range=document.createRange();range.selectNodeContents(editor.firstElementChild);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();
                };personalizationFixture();window.beforeAlignment=editor.innerHTML;
                """);
            await note.Script("editor.innerHTML='<p>Before</p><p></p><p>After</p>';bookmark=null;normalizeDirection();onEdit('command');editor.focus();getSelection().selectAllChildren(editor);rememberSelection();setParagraphAlignment('center')");
            Check("selected-empty-paragraph-keeps-alignment-for-later-writing",await note.Script("[...editor.children].every(p=>p.style.textAlign==='center')")=="true");
            await note.Script("personalizationFixture();window.beforeAlignment=editor.innerHTML");
            await note.ClickControl("#menuButton");await note.ClickControl("[data-paragraph-alignment='center']");
            Check("alignment-only-affects-selected-paragraph",await note.Script("editor.children[0].style.textAlign==='center'&&!editor.children[1].style.textAlign&&!editor.children[2].style.textAlign&&getSelection().toString()==='First paragraph'")=="true");
            await note.Script("undo()");Check("alignment-undo-restores-html-and-selection",await note.Script("editor.innerHTML===beforeAlignment&&getSelection().toString()==='First paragraph'")=="true");
            await note.Script("undo(true)");Check("alignment-redo-restores-style",await note.Script("editor.children[0].style.textAlign==='center'&&getSelection().toString()==='First paragraph'")=="true");
            await note.Script("(()=>{editor.focus();const range=document.createRange();range.setStart(editor.children[0],0);range.setEnd(editor.children[1].firstChild,0);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();setParagraphAlignment('left')})()");
            Check("selection-boundary-excludes-next-paragraph",await note.Script("editor.children[0].style.textAlign==='left'&&!editor.children[1].style.textAlign")=="true");
            // WebView script evaluations share global lexical bindings. Keep
            // fixture variables local, so a second `const range` does not abort.
            Check("multi-paragraph-fixture-executes",await note.Script("(()=>{editor.focus();const range=document.createRange();range.setStart(editor.children[0],0);range.setEnd(editor.children[1],editor.children[1].childNodes.length);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();setParagraphAlignment('justify');return true})()")=="true");
            Check("justify-applies-to-multiple-selected-paragraphs-and-keeps-direction",await note.Script("editor.children[0].style.textAlign==='justify'&&editor.children[1].style.textAlign==='justify'&&editor.children[1].dataset.jotDirection==='rtl'&&editor.children[1].dir==='rtl'&&!editor.children[2].style.textAlign")=="true",JsonSerializer.Deserialize<JsonElement>(await note.Script("[...editor.children].map(p=>({align:p.style.textAlign,saved:p.dataset.jotAlign,direction:p.dir,manual:p.dataset.jotDirection}))")));
            await note.Flush();await note.Reload();
            Check("alignment-survives-storage-and-load",await note.Script("editor.children[0].style.textAlign==='justify'&&editor.children[1].dataset.jotAlign==='justify'&&editor.children[1].dataset.jotDirection==='rtl'")=="true",JsonSerializer.Deserialize<JsonElement>(await note.Script("[...editor.children].map(p=>({align:p.style.textAlign,saved:p.dataset.jotAlign,direction:p.dir,manual:p.dataset.jotDirection}))")));
            Check("sanitizer-retains-safe-alignment-only",await note.Script("(()=>{const holder=document.createElement('div');holder.innerHTML=sanitizeHtml('<p style=\"text-align:center\" onclick=\"bad()\">A</p><p data-jot-align=\"right\">B</p><p style=\"text-align:var(--evil)\">C</p>');return holder.children[0].style.textAlign==='center'&&holder.children[1].style.textAlign==='right'&&!holder.children[0].hasAttribute('onclick')&&!holder.children[2].style.textAlign})()")=="true");
            await note.Script("editor.focus();getSelection().selectAllChildren(editor.firstElementChild);rememberSelection();setParagraphAlignment('auto')");
            Check("automatic-alignment-resets-just-selected-paragraph",await note.Script("!editor.children[0].style.textAlign&&!editor.children[0].dataset.jotAlign&&editor.children[1].style.textAlign==='justify'")=="true",JsonSerializer.Deserialize<JsonElement>(await note.Script("[...editor.children].map(p=>({align:p.style.textAlign,saved:p.dataset.jotAlign,direction:p.dir,manual:p.dataset.jotDirection}))")));
            await note.Script("editor.focus();getSelection().selectAllChildren(editor.firstElementChild);rememberSelection();setParagraphAlignment('right');window.personalizationPasted=false;const data=new DataTransfer();data.setData('text/plain','Pasted one\\nPasted two');paste({preventDefault(){},clipboardData:data}).then(()=>window.personalizationPasted=true)");await note.WaitFor("window.personalizationPasted===true");
            Check("plain-paste-follows-destination-alignment",await note.Script("[...editor.querySelectorAll('p')].filter(p=>p.textContent.startsWith('Pasted')).every(p=>p.style.textAlign==='right')&&editor.textContent.includes('Pasted two')")=="true");
            await note.Flush();
            var file=Path.Combine(s.Store.Root,"personalized.jot");await File.WriteAllBytesAsync(file,NoteFileFormat.Encode((await s.Store.LoadNote(id))!.Value,"jot"));
            var portable=await NoteFileFormat.Read(file);
            Check("portable-file-keeps-icon-and-paragraph-alignment",portable.Note["icon"]?.ToString()=="emoji:📝"&&portable.Note["html"]!.ToString().Contains("data-jot-align=\"right\""));
            note.Width=360;note.Height=620;
            await note.ClickControl("#menuButton");await note.Capture("note-personalization-settings-360");
            Check("alignment-options-fit-without-horizontal-overflow",await note.Script("(()=>{const row=document.querySelector('.alignment-row');return row.scrollWidth<=row.clientWidth&&document.querySelectorAll('[data-paragraph-alignment]').length===5})()")=="true");
            await note.ClickControl("#noteIconButton");await note.Capture("note-personalization-picker-360");await note.Script("document.getElementById('noteIconDialog').close()");
            Check("no-renderer-errors",note.RuntimeErrors.Count==0);
            Check("all-tests-are-isolated",s.Windows.All(w=>w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Left< -10000&&w.Top< -10000));
        }
        finally{foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
    }
}

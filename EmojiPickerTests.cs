using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyEmojiPicker(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="emoji-picker-"+name,passed,evidence});
        var s=new JotSession(true,Path.Combine(testOutput,"emoji-picker")){ExerciseLifecycle=true};
        try
        {
            var id=await s.Store.Create();var note=new MainWindow(s,"note",id);note.Width=900;note.Height=650;note.Reveal();await note.WaitFor("window.jotReady===true");
            Check("new-note-default-is-memo",(await s.Store.LoadNote(id))!.Value.GetProperty("icon").GetString()=="emoji:📝"&&await note.Script("JotNoteIcons.defaultValue==='emoji:📝'&&JotNoteIcons.render(null).textContent==='📝'")=="true");
            using var values=JsonDocument.Parse(await note.Script("JotEmojiData.aliases"));
            var emojis=values.RootElement.EnumerateArray().Select(value=>value.GetString()!).ToArray();
            Check("complete-final-unicode-catalogue",await note.Script("JotNoteIcons.catalogueVersion==='18.0'&&JotNoteIcons.entries.length===3963&&JotNoteIcons.entries.every(entry=>JotNoteIcons.valid('emoji:'+entry.emoji))")=="true",new{fullyQualified=3963,accepted=emojis.Length});
            Check("every-bundled-sequence-validates-in-native-and-js",emojis.Length==5235&&emojis.All(value=>NoteStore.ValidateNoteIcon("emoji:"+value)=="emoji:"+value)&&await note.Script("JotEmojiData.aliases.every(value=>JotNoteIcons.valid('emoji:'+value))")=="true");
            foreach(var invalid in new[]{"emoji:abc","emoji:🙂🙂","emoji:🏽","emoji:<img>","emoji:\u200f","emoji:©abc","icon:unknown","emoji:\uD800"})
            {
                bool rejected=false;try{NoteStore.ValidateNoteIcon(invalid);}catch(InvalidDataException){rejected=true;}
                Check("invalid-rejected-"+JsonSerializer.Serialize(invalid),rejected&&await note.Script("!JotNoteIcons.valid("+JsonSerializer.Serialize(invalid)+")")=="true");
            }
            foreach(var legacy in new[]{"icon:notepad-text","icon:book-open","icon:briefcase-business","icon:code-xml","icon:lightbulb","icon:list-checks","icon:calendar-days","icon:heart","icon:star","icon:flag","icon:coffee","icon:music-2"})
                Check("legacy-preserved-and-rendered-as-emoji-"+legacy,NoteStore.NormalizeNoteIcon(legacy)==legacy&&await note.Script("JotNoteIcons.normalize("+JsonSerializer.Serialize(legacy)+")==="+JsonSerializer.Serialize(legacy)+"&&JotNoteIcons.render("+JsonSerializer.Serialize(legacy)+").tagName==='SPAN'")=="true");
            Check("search-covers-flags-zwj-keycaps-and-new-sequences",await note.Script("['flag: Iran','woman technologist','keycap: 1','cracking face'].every(name=>JotNoteIcons.filter(name).length>0)&&JotNoteIcons.filter('📝').some(entry=>entry.emoji==='📝')")=="true");
            Check("skin-tone-filter-retains-mixed-tone-sequences",await note.Script("JotNoteIcons.filter('','all','🏽').every(entry=>entry.tones.includes('🏽'))&&JotNoteIcons.filter('','all','🏽').some(entry=>new Set(entry.tones).size>1)&&JotNoteIcons.filter('','all','default').every(entry=>entry.tones.length===0)")=="true");
            // Use a real editor revision as the baseline. Merely assigning raw
            // HTML neither saves it nor applies the editor's bidi normalization.
            await note.Script("editor.innerHTML='<p><b>Editor text remains unchanged</b></p><p>یادداشت فارسی</p>';bookmark=null;normalizeDirection();onEdit('command')");
            await note.Flush();
            var bodyBefore=(await s.Store.LoadNote(id))!.Value;
            await note.Script("window.emojiBefore=editor.innerHTML;window.emojiPlainBefore=editorPlainText();JotNoteIcons.open(activeNote())");
            Check("picker-has-only-emoji-and-bounded-dom",await note.Script("document.getElementById('noteIconDialog').open&&document.querySelectorAll('[data-note-icon]').length===96&&[...document.querySelectorAll('[data-note-icon]')].every(button=>button.dataset.noteIcon.startsWith('emoji:')&&button.ariaLabel)&&document.activeElement.id==='noteEmojiSearch'")=="true");
            async Task Key(string key,int code){foreach(var type in new[]{"rawKeyDown","keyUp"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code=key,windowsVirtualKeyCode=code}));}
            await Key("ArrowDown",40);await Key("ArrowRight",39);
            Check("keyboard-arrows-use-roving-tabstop",await note.Script("document.activeElement===document.getElementById('noteEmojiResults').children[1]&&document.querySelectorAll('#noteEmojiResults [tabindex=\"0\"]').length===1")=="true");
            await Key("PageDown",34);
            Check("keyboard-page-navigation-reaches-next-results",await note.Script("document.getElementById('noteEmojiCount').textContent.startsWith('97–')&&document.activeElement===document.getElementById('noteEmojiResults').children[1]")=="true");
            await note.Script("document.getElementById('noteEmojiSearch').value='woman technologist';document.getElementById('noteEmojiSearch').dispatchEvent(new Event('input'))");
            await note.WaitFor("document.getElementById('noteEmojiResults').children.length===6");
            Check("search-has-all-tone-variants",await note.Script("[...document.getElementById('noteEmojiResults').children].every(button=>button.ariaLabel.startsWith('woman technologist'))")=="true");
            await note.Script("document.getElementById('noteEmojiTone').value='🏽';document.getElementById('noteEmojiTone').dispatchEvent(new Event('change'))");
            Check("tone-control-filters-visible-results",await note.Script("document.getElementById('noteEmojiResults').children.length===1&&document.querySelector('#noteEmojiResults button').dataset.noteIcon==='emoji:👩🏽‍💻'")=="true");
            await note.ClickControl("#noteEmojiResults button");await note.WaitFor("!document.getElementById('noteIconDialog').open");
            var bodyAfter=(await s.Store.LoadNote(id))!.Value;
            var bodyEvidence=JsonSerializer.Deserialize<JsonElement>(await note.Script("({before:emojiBefore,after:editor.innerHTML,plainBefore:emojiPlainBefore,plainAfter:editorPlainText()})"));
            Check("chosen-emoji-saves-without-changing-content",bodyAfter.GetProperty("icon").GetString()=="emoji:👩🏽‍💻"&&bodyBefore.GetProperty("html").GetString()==bodyAfter.GetProperty("html").GetString()&&bodyBefore.GetProperty("plain").GetString()==bodyAfter.GetProperty("plain").GetString()&&await note.Script("editor.innerHTML===emojiBefore&&editorPlainText()===emojiPlainBefore")=="true",bodyEvidence);
            await note.Reload();Check("emoji-and-content-survive-reload",await note.Script("activeNote().icon==='emoji:👩🏽‍💻'&&editorPlainText()==="+JsonSerializer.Serialize(bodyBefore.GetProperty("plain").GetString())+"&&!!editor.querySelector('b,strong')")=="true");
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{320,360,900})
            {
                note.Width=width;note.Height=540;await s.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));
                await note.Script("JotNoteIcons.open(activeNote())");await Task.Delay(100);
                var bounds=JsonSerializer.Deserialize<JsonElement>(await note.Script("(()=>{const d=document.getElementById('noteIconDialog'),r=d.getBoundingClientRect(),g=document.getElementById('noteEmojiResults');return {viewport:{width:innerWidth,height:innerHeight},dialog:{left:r.left,right:r.right,top:r.top,bottom:r.bottom,scrollWidth:d.scrollWidth,clientWidth:d.clientWidth,scrollHeight:d.scrollHeight,clientHeight:d.clientHeight},grid:{scrollWidth:g.scrollWidth,clientWidth:g.clientWidth},children:[...d.children].map(child=>({className:child.className,scrollWidth:child.scrollWidth,clientWidth:child.clientWidth,left:child.getBoundingClientRect().left,right:child.getBoundingClientRect().right}))}})()"));
                Check("fits-viewport-"+theme+"-"+width,await note.Script("(()=>{const d=document.getElementById('noteIconDialog'),r=d.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight&&d.scrollWidth<=d.clientWidth&&document.getElementById('noteEmojiResults').scrollWidth<=document.getElementById('noteEmojiResults').clientWidth})()")=="true",bounds);
                if(width is 320 or 900)await note.Capture("emoji-picker-"+theme+"-"+width);
                await note.Script("document.getElementById('noteIconDialog').close()");
            }
            await note.Script("JotNoteIcons.open(activeNote());document.getElementById('noteEmojiSearch').value='not-a-real-emoji-name';document.getElementById('noteEmojiSearch').dispatchEvent(new Event('input'))");
            await note.WaitFor("!document.getElementById('noteEmojiEmpty').hidden");
            Check("empty-search-and-font-limitation-are-explained",await note.Script("document.getElementById('noteEmojiResults').hidden&&document.getElementById('noteEmojiCount').textContent==='0 results'&&document.querySelector('#noteIconDialog .note-icon-hint').textContent.includes('older Windows')")=="true");
            await note.Script("window.emojiCancelCount=0;document.getElementById('noteIconDialog').addEventListener('cancel',()=>emojiCancelCount++)");
            await Key("Escape",27);await note.WaitFor("!document.getElementById('noteIconDialog').open");
            Check("escape-closes-picker-with-standard-cancel-event",await note.Script("!document.getElementById('noteIconDialog').open&&emojiCancelCount===1")=="true");
            await note.Script("JotNoteIcons.open(activeNote(),{onSave:()=>new Promise(resolve=>window.finishEmojiSave=resolve)});window.emojiBusyDone=false;JotNoteIcons.choose('emoji:📝').then(()=>emojiBusyDone=true)");
            await note.WaitFor("document.getElementById('noteIconDialog').ariaBusy==='true'");await Key("Escape",27);
            Check("escape-does-not-dismiss-in-flight-save",await note.Script("document.getElementById('noteIconDialog').open&&!emojiBusyDone")=="true");
            await note.Script("finishEmojiSave()");await note.WaitFor("emojiBusyDone&&!document.getElementById('noteIconDialog').open");
            Check("no-renderer-errors",note.RuntimeErrors.Count==0,note.RuntimeErrors);
            Check("isolated-offscreen-test",s.Windows.All(window=>window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.Left< -10000&&window.Top< -10000));
        }
        finally{foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
    }
}

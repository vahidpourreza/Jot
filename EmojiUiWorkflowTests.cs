using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyEmojiUiWorkflow(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="emoji-ui-"+name,passed,evidence});
        var s=new JotSession(true,Path.Combine(testOutput,"emoji-ui-workflow")){ExerciseLifecycle=true};
        Task Phase(string value)=>File.WriteAllTextAsync(Path.Combine(testOutput,"emoji-ui-phase.txt"),value);
        try
        {
            await Phase("creating-fixture");
            var id=await s.Store.Create();
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Compact note · عنوان",icon="emoji:🇮🇷"}));
            var home=s.Home();home.Width=1000;home.Height=650;await home.WaitFor("window.jotReady===true");
            await Phase("home-ready-switching-note");
            await home.SwitchNoteTab(id);await home.WaitFor("window.jotReady===true&&!!window.JotDocumentHeading&&!!window.JotEmojiArtwork");
            await Phase("note-ready-decoding-artwork");
            Check("complete-catalogue-and-aliases-have-local-artwork",await home.Script("JotNoteIcons.entries.length===3963&&JotNoteIcons.entries.every(entry=>JotNoteIcons.artworkFor('emoji:'+entry.emoji))&&JotEmojiData.aliases.every(emoji=>JotNoteIcons.artworkFor('emoji:'+emoji))")=="true");
            Check("stored-icons-and-variation-aliases-unchanged",await home.Script("JotNoteIcons.normalize('icon:coffee')==='icon:coffee'&&JotNoteIcons.normalize('emoji:❤')==='emoji:❤'&&JotNoteIcons.artworkFor('emoji:❤')===JotNoteIcons.artworkFor('emoji:❤️')")=="true");
            // Exhaustive decoding is available for artwork updates. Routine regression
            // runs cover every newly added design plus representative complex sequences;
            // generate-emoji-artwork.mjs --check verifies every local file and mapping.
            var allArtwork=Environment.GetCommandLineArgs().Contains("--all-emoji-artwork");
            await home.Script("window.checkAllEmojiArtwork="+(allArtwork?"true":"false"));
            await home.Script("""
                window.emojiArtworkCheck={done:false,loaded:0,total:0,exhaustive:checkAllEmojiArtwork,failed:[]};
                (async()=>{
                  const representative=[...JotNoteIcons.entries.slice(0,96),...JotNoteIcons.entries.filter(entry=>entry.version==='18.0')].map(entry=>entry.emoji);
                  representative.push('🇮🇷','🇺🇸','🇬🇧','🇨🇶','🏴󠁧󠁢󠁥󠁮󠁧󠁿','🏴󠁧󠁢󠁳󠁣󠁴󠁿','🏴󠁧󠁢󠁷󠁬󠁳󠁿','👩🏽‍💻','👁️‍🗨️','1️⃣','#️⃣','©️','❤️','📝');
                  const files=checkAllEmojiArtwork?Object.values(JotEmojiArtwork.files):[...new Set(representative.map(emoji=>JotNoteIcons.artworkFor('emoji:'+emoji)))];let next=0;
                  emojiArtworkCheck.total=files.length;
                  await Promise.all(Array.from({length:24},async()=>{
                    while(next<files.length){const file=files[next++],image=new Image();image.src='assets/emoji/artwork/'+file;
                      let timeout;
                      try{await Promise.race([image.decode(),new Promise((_,reject)=>{timeout=setTimeout(()=>reject(Error('decode timeout')),10000)})]);if(!image.naturalWidth)throw Error('empty image');emojiArtworkCheck.loaded++;}
                      catch{emojiArtworkCheck.failed.push(file);}
                      finally{clearTimeout(timeout);}
                    }
                  }));emojiArtworkCheck.done=true;
                })();
                """);
            await home.WaitFor("emojiArtworkCheck.done",600);
            await Phase("artwork-decoded-search-and-layout");
            Check((allArtwork?"every":"representative")+"-packaged-svg-decodes-in-webview",await home.Script("emojiArtworkCheck.loaded===emojiArtworkCheck.total&&emojiArtworkCheck.total>=100&&emojiArtworkCheck.failed.length===0")=="true",JsonSerializer.Deserialize<JsonElement>(await home.Script("emojiArtworkCheck")));
            Check("header-renders-actual-flag-image",await home.Script("document.querySelector('#documentEmoji img')?.complete&&document.querySelector('#documentEmoji img').naturalWidth>0&&document.querySelector('#documentEmoji .note-emoji').textContent==='🇮🇷'")=="true");
            Check("search-aliases-and-persian",await home.Script("JotNoteIcons.filter('coffee').some(entry=>entry.emoji==='☕')&&JotNoteIcons.filter('ایران')[0]?.emoji==='🇮🇷'&&JotNoteIcons.filter('cafe').some(entry=>entry.emoji==='☕')&&JotNoteIcons.filter('code').some(entry=>entry.emoji==='💻')")=="true");
            Check("search-ranks-exact-and-tokenized-names",await home.Script("JotNoteIcons.filter('📝')[0]?.emoji==='📝'&&JotNoteIcons.filter('woman_technologist')[0]?.emoji==='👩‍💻'&&JotNoteIcons.filter('woman technologist medium skin')[0]?.emoji==='👩🏽‍💻'&&JotNoteIcons.filter('cracking face')[0]?.emoji==='🫫'")=="true");
            await home.ClickControl("#documentEmoji");
            Check("picker-only-search-grid-paging",await home.Script("document.getElementById('noteIconDialog').open&&!document.querySelector('#noteIconDialog select,#noteIconDialog form,#noteCustomEmoji')&&document.querySelectorAll('#noteEmojiResults button').length===96&&document.activeElement.id==='noteEmojiSearch'")=="true");
            await home.Script("document.getElementById('noteEmojiSearch').value='cracking face';document.getElementById('noteEmojiSearch').dispatchEvent(new Event('input'))");
            await home.WaitFor("document.querySelectorAll('#noteEmojiResults button').length===1&&document.querySelector('#noteEmojiResults img').complete");
            Check("unicode18-results-are-artwork-not-missing-font-glyphs",await home.Script("document.querySelector('#noteEmojiResults img').naturalWidth>0&&document.querySelector('#noteEmojiResults button').ariaLabel==='cracking face'")=="true");
            await home.ClickControl("#noteEmojiResults button");await home.WaitFor("!document.getElementById('noteIconDialog').open");
            Check("choosing-emoji-preserves-title",(await s.Store.LoadNote(id))!.Value.GetProperty("icon").GetString()=="emoji:🫫"&&(await s.Store.LoadNote(id))!.Value.GetProperty("title").GetString()=="Compact note · عنوان");
            var other=await s.Store.Create();await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=other,title="Another note",icon="emoji:👩🏽‍💻"}));
            await home.SwitchNoteTab(other);await home.SetTabPinned(id,true);
            foreach(var width in new[]{320,600,1000})
            {
                home.Width=width;await Task.Delay(100);
                const string centers="[...document.querySelectorAll('.workspace-tab[data-kind=note]>[role=tab] .tab-title')].map(label=>{const r=label.getBoundingClientRect(),p=label.closest('[role=tab]').getBoundingClientRect();return {title:label.textContent,labelCenter:(r.top+r.bottom)/2,tabCenter:(p.top+p.bottom)/2,difference:Math.abs((r.top+r.bottom)-(p.top+p.bottom))/2}})";
                Check("all-tab-title-centers-"+width,await home.Script(centers+".every(item=>item.difference<=1)")=="true",JsonSerializer.Deserialize<JsonElement>(await home.Script(centers)));
                Check("compact-heading-fits-"+width,await home.Script("(()=>{const h=document.getElementById('documentHeading').getBoundingClientRect(),t=document.getElementById('documentTitle').getBoundingClientRect(),e=document.getElementById('documentEmoji').getBoundingClientRect();return h.height<=60&&t.height<=30&&e.width===30&&t.right<=innerWidth&&h.left>=0&&document.documentElement.scrollWidth<=innerWidth})()")=="true");
                await home.ClickControl("#documentEmoji");await Task.Delay(80);
                Check("picker-responsive-"+width,await home.Script("(()=>{const d=document.getElementById('noteIconDialog'),r=d.getBoundingClientRect(),g=document.getElementById('noteEmojiResults');return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight&&d.scrollWidth<=d.clientWidth&&g.scrollWidth<=g.clientWidth})()")=="true");
                if(width is 320 or 1000)await home.Capture("emoji-ui-picker-"+width);
                await home.Script("document.getElementById('noteIconDialog').close()");
            }
            home.Width=320;
            await home.Script("(()=>{const t=document.getElementById('documentTitle');t.value='A long note title that wraps naturally while remaining compact and editable · عنوان طولانی یادداشت برای بررسی چیدمان';t.dispatchEvent(new Event('input',{bubbles:true}))})()");
            await home.Flush();
            Check("long-title-wraps-with-bounded-height",await home.Script("(()=>{const t=document.getElementById('documentTitle');return t.getBoundingClientRect().height>28&&t.getBoundingClientRect().height<=84&&t.value.includes('عنوان')&&t.getBoundingClientRect().right<=innerWidth})()")=="true");
            await s.OpenNoteWindow(other);var note=s.Windows.Single(window=>window.Mode=="note"&&window.NoteId==other);note.Width=420;note.Height=520;await note.WaitFor("window.jotReady===true&&!!window.JotDocumentHeading");
            await note.WaitFor("document.querySelector('#documentEmoji img')?.complete===true");
            Check("standalone-heading-shares-local-artwork",await note.Script("document.querySelector('#documentEmoji img').naturalWidth>0&&parseFloat(getComputedStyle(document.getElementById('documentTitle')).fontSize)===18")=="true");
            Check("isolated-and-no-renderer-errors",s.Windows.All(window=>window.RuntimeErrors.Count==0&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.Left< -10000&&window.Top< -10000));
            await Phase("checks-complete");
        }
        finally{s.StopFileAutoSave();await s.WaitForNoteFiles();foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
    }
}

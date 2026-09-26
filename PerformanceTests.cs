using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyPerformance(List<object> checks, MainWindow note)
    {
        await note.Script("""
            window.perfDone=false;
            (async()=>{
              const original=editor.innerHTML, samples=[];
              editor.innerHTML=Array.from({length:1800},(_,i)=>'<p>Line '+i+' — mixed فارسی text for typing.</p>').join('');
              normalizeDirection();updateEmpty();historyRecord('command');
              let target=editor.lastElementChild.firstChild;
              while(target.firstChild)target=target.firstChild;
              const range=document.createRange();range.selectNodeContents(target);range.collapse(false);
              getSelection().removeAllRanges();getSelection().addRange(range);
              for(let i=0;i<24;i++){
                await new Promise(r=>setTimeout(r,20));
                const walker=document.createTreeWalker(editor.lastElementChild,NodeFilter.SHOW_TEXT);let next;while(next=walker.nextNode())target=next;
                const start=performance.now();target.appendData('x');
                editor.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText'}));
                samples.push(performance.now()-start);clearTimeout(saveTimer);
              }
              samples.sort((a,b)=>a-b);
              window.perfTyping={medianMs:samples[12],p95Ms:samples[22],paragraphs:1800};
              editor.innerHTML=original;onEdit('command');await saveNow();
              window.perfDone=true;
            })().catch(error=>{window.perfFailure=String(error);window.perfDone=true;});
            """);
        await note.WaitFor("window.perfDone===true");
        using var typing = JsonDocument.Parse(await note.Script("window.perfTyping"));
        var isolated = new NoteStore(Path.Combine(testOutput,"performance-store"));
        var ids = Enumerable.Range(0,16).Select(_=>Guid.NewGuid().ToString()).ToArray();
        var content = new string('x',320_000);
        await isolated.Save(JsonSerializer.SerializeToElement(new {
            version=2, activeId=ids[0], prefs=NoteStore.Defaults(),
            notes=ids.Select(id=>new {id,color="crimson",html="<p>"+content+"</p>",plain="Synthetic performance fixture",updatedAt=0})
        }));
        var gaps = new List<double>(); var clock=Stopwatch.StartNew(); double previous=clock.Elapsed.TotalMilliseconds;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval=TimeSpan.FromMilliseconds(10) };
        timer.Tick+=(_,_)=>{var now=clock.Elapsed.TotalMilliseconds;gaps.Add(now-previous);previous=now;};
        var saves = new List<double>(); timer.Start();
        try
        {
            for(int i=0;i<5;i++)
            {
                var watch=Stopwatch.StartNew();
                await isolated.SaveNote(JsonSerializer.SerializeToElement(new { id=ids[0],html="<p>"+content+i+"</p>",plain="Synthetic fixture",updatedAt=i }));
                await isolated.Load(); saves.Add(watch.Elapsed.TotalMilliseconds);
                await Task.Delay(30);
            }
        }
        finally { timer.Stop(); }
        saves.Sort(); gaps.Sort();
        var report = new { typing=typing.RootElement.Clone(),store=new { bytes=new FileInfo(isolated.FilePath).Length,medianSaveMs=saves[2],maxUiGapMs=gaps.DefaultIfEmpty(0).Max(),uiTicks=gaps.Count } };
        await File.WriteAllTextAsync(Path.Combine(testOutput,"performance.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        checks.Add(new { name="performance-fixtures-complete-without-editor-errors",passed=await note.Script("!window.perfFailure")=="true"&&gaps.Count>0 });
        var index=(await isolated.LoadIndex())!.Value;
        checks.Add(new { name="index-load-excludes-rich-html-and-original-images",passed=index.GetRawText().Length<10000&&index.GetProperty("notes").EnumerateArray().All(n=>!n.TryGetProperty("html",out _)) });
        // The cache must observe external changes and must never publish a failed write.
        var cachedBefore=(await isolated.Load())!.Value;
        var pendingPath=isolated.FilePath+".tmp";
        Directory.CreateDirectory(pendingPath);
        bool rejected=false;
        try { await isolated.SaveNote(JsonSerializer.SerializeToElement(new{id=ids[0],html="<p>Must not publish</p>",plain="Must not publish",updatedAt=99})); }
        catch(IOException){rejected=true;}
        catch(UnauthorizedAccessException){rejected=true;}
        finally { Directory.Delete(pendingPath); }
        checks.Add(new { name="failed-write-keeps-committed-cache-and-index",passed=rejected&&(await isolated.Load())!.Value.GetRawText()==cachedBefore.GetRawText()&&(await isolated.LoadIndex())!.Value.GetRawText()==index.GetRawText() });
        await File.WriteAllTextAsync(isolated.FilePath,"invalid-external-change");
        rejected=false;try{await isolated.Load();}catch(JsonException){rejected=true;}
        checks.Add(new { name="cache-does-not-mask-external-corruption",passed=rejected&&await File.ReadAllTextAsync(isolated.FilePath)=="invalid-external-change" });
    }

    private async Task VerifyReadyPaint(List<object> checks,MainWindow note)
    {
        var originalTheme=JsonSerializer.Deserialize<string>(await note.Script("model.prefs.theme"));
        foreach(var mode in new[]{"light","dark"})
        {
            await note.Script("setPreference({theme:"+JsonSerializer.Serialize(mode)+"}).then(()=>window.paintThemeReady="+JsonSerializer.Serialize(mode)+")");
            await note.WaitFor("window.paintThemeReady==="+JsonSerializer.Serialize(mode));
            var id=await store.Create();
            var fresh=new MainWindow(session,"note",id);
            checks.Add(new{name="new-note-hidden-before-ready-"+mode,passed=!fresh.ContentReady&&fresh.Opacity==0&&!fresh.IsVisible});
            fresh.Reveal();await fresh.WaitFor("window.jotReady===true");
            for(int i=0;i<100&&!fresh.ContentReady;i++)await Task.Delay(10);
            checks.Add(new{name="first-visible-header-is-crimson-"+mode,passed=fresh.ContentReady&&await fresh.Script("model.prefs.theme==="+JsonSerializer.Serialize(mode)+"&&app.dataset.noteColor==='crimson'&&JotDesign.hexColor(window.jotInitialHeader)===JotDesign.hexColor(JotDesign.noteColor('crimson').primary)")=="true",elapsedMs=fresh.ContentReadyMs});
            checks.Add(new{name="editor-loads-only-its-own-note-"+mode,passed=await fresh.Script("model.notes.length===1&&model.notes[0].id===model.activeId")=="true"&&!fresh.TestHostActions.Contains("load")&&fresh.TestHostActions.Contains("note-load")});
            await fresh.Capture("first-ready-"+mode);
            await fresh.Script("window.reappliedPreferences=0;JotBridge.on(data=>{if(data.event==='preferences')window.reappliedPreferences++;});editor.innerHTML='<p>Typing must not restyle every window</p>';onEdit();saveNow().then(()=>window.paintSaved=true)");
            await fresh.WaitFor("window.paintSaved===true");
            checks.Add(new{name="autosave-does-not-reapply-preferences-"+mode,passed=await fresh.Script("window.reappliedPreferences===0")=="true"});
            var saves=fresh.TestHostActions.Count(action=>action=="save");
            await fresh.Script("saveNow().then(()=>window.cleanSaved=true)");await fresh.WaitFor("window.cleanSaved===true");
            checks.Add(new{name="unchanged-note-does-not-rewrite-store-"+mode,passed=saves==fresh.TestHostActions.Count(action=>action=="save")});
            fresh.ClosePermanently();await store.Delete(id);
        }
        await note.Script("setPreference({theme:"+JsonSerializer.Serialize(originalTheme)+"}).then(()=>window.paintThemeRestored=true)");
        await note.WaitFor("window.paintThemeRestored===true");
        await session.Changed(preferences:false);
    }
}

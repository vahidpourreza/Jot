using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyIncrementalContent(List<object> checks)
    {
        var root=Path.Combine(testOutput,"incremental-content");Directory.CreateDirectory(root);
        var target=new JotSession(true,root){ExerciseLifecycle=true};
        void Check(string name,bool passed)=>checks.Add(new{name="incremental-content-"+name,passed});
        try
        {
            var id=await target.Store.Create();var other=await target.Store.Create();
            await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Working note",group="Projects"}));
            await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=other,title="Untouched neighbor"}));
            var home=target.Home();await home.WaitFor("window.jotReady===true&&JotWorkspace.view==='home'");
            var note=target.OpenNote(id);await note.WaitFor("window.jotReady===true&&ready");
            await home.Script("folder='Projects';query='needle';renderFolders();renderCards();window.perfNeighbor=cardViews.get("+JsonSerializer.Serialize(other)+")?.card");
            async Task Edit(string value){await note.Script("editor.innerHTML="+JsonSerializer.Serialize("<p>"+value+"</p>")+";onEdit();window.incrementSaved=false;saveNow().then(()=>window.incrementSaved=true)");await note.WaitFor("window.incrementSaved===true");await Task.Delay(100);}
            var loads=home.TestHostActions.Count(action=>action=="index-load");
            await Edit("New needle text");
            Check("changed-note-enters-current-search-without-index-query",await home.Script("document.querySelectorAll('#cards .note-card').length===1&&document.querySelector('#cards .card-snippet').textContent.includes('needle')")=="true"&&home.TestHostActions.Count(action=>action=="index-load")==loads);
            await Edit("No match anymore");
            Check("changed-note-leaves-current-search",await home.Script("document.querySelectorAll('#cards .note-card').length===0")=="true");
            await home.SwitchNoteTab(other);await Edit("Saved while library hidden");
            await home.SwitchHomeView();await home.WaitFor("JotWorkspace.view==='home'");
            Check("hidden-library-reconciles-when-shown",await home.Script("homeData.notes.find(n=>n.id==="+JsonSerializer.Serialize(id)+").plain==='Saved while library hidden'")=="true"&&home.TestHostActions.Count(action=>action=="index-load")==loads);
            await home.Script("folder=null;query='';renderFolders();renderCards()");
            Check("unrelated-card-keeps-its-dom",await home.Script("cardViews.get("+JsonSerializer.Serialize(other)+").card===window.perfNeighbor")=="true");
            await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Still fresh metadata"}));await target.Changed(false,id);
            await home.WaitFor("homeData.notes.find(n=>n.id==="+JsonSerializer.Serialize(id)+").title==='Still fresh metadata'");
            Check("metadata-continues-full-library-reconciliation",home.TestHostActions.Count(action=>action=="index-load")>loads);

            note.TestSaveNoteFilePath=Path.Combine(root,"File state race.jot");await note.SaveNoteFile(id,true);
            await note.WaitFor("!!activeNote().noteFile?.path&&!JotNoteFiles.isDirty(activeNote())");
            await note.Script("""
                window.perfRealRequest=JotBridge.request;window.perfStateReadHeld=false;
                JotBridge.request=(action,payload)=>{
                  const result=window.perfRealRequest(action,payload);
                  if(action!=='note-file-state')return result;
                  return result.then(value=>new Promise(resolve=>{window.perfReleaseState=()=>resolve(value);window.perfStateReadHeld=true;}));
                };
                window.perfManualSaveDone=false;JotNoteFiles.run('file-save').then(()=>window.perfManualSaveDone=true);
                """);
            await note.WaitFor("window.perfStateReadHeld===true");
            await Edit("Newer unsaved content overtakes older clean file state");
            await note.WaitFor("JotNoteFiles.state(activeNote()).dirty===true");
            await note.Script("JotBridge.request=window.perfRealRequest;window.perfReleaseState()");
            await note.WaitFor("window.perfManualSaveDone===true");
            Check("late-file-state-reply-cannot-clear-newer-dirty-event",await note.Script("JotNoteFiles.state(activeNote()).dirty&&JotNoteFiles.isDirty(activeNote())")=="true");

            var textId=await target.Store.Create();const string text="Saved plain text";
            await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=textId,html="<p>"+text+"</p>",plain=text,updatedAt=1}));
            await target.Store.BindNoteFile(textId,Path.Combine(root,"Plain.txt"),"txt","fixture-digest",NoteFileFormat.ContentDigest(JsonSerializer.SerializeToElement(new{plain=text}),"txt"));
            await SetStoreTrigger(target.Store,"CREATE TRIGGER reject_unchanged_file_flag BEFORE UPDATE OF extra ON notes WHEN OLD.id='"+textId+"' AND json_extract(OLD.extra,'$.fileDirty')=json_extract(NEW.extra,'$.fileDirty') BEGIN SELECT RAISE(ABORT,'redundant file-state write');END;");
            try{
                await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=textId,html="<p><b>"+text+"</b></p>",plain=text,updatedAt=2}));
                Check("plain-files-ignore-rich-only-change-without-rewriting-dirty-flag",!(await target.Store.LoadFileState(textId))!.Value.GetProperty("dirty").GetBoolean());
                await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=textId,html="<p>Changed text</p>",plain="Changed text",updatedAt=3}));
                Check("plain-file-change-becomes-dirty",(await target.Store.LoadFileState(textId))!.Value.GetProperty("dirty").GetBoolean());
                await target.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=textId,html="<p>"+text+"</p>",plain=text,updatedAt=4}));
                Check("undo-to-saved-plain-text-clears-dirty",!(await target.Store.LoadFileState(textId))!.Value.GetProperty("dirty").GetBoolean());
            }finally{await SetStoreTrigger(target.Store,"DROP TRIGGER reject_unchanged_file_flag;");}
            Check("no-runtime-errors",target.Windows.All(window=>window.RuntimeErrors.Count==0));
        }
        finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }

    private async Task VerifyResponsiveness(List<object> checks)
    {
        var root=Path.Combine(testOutput,"responsiveness");Directory.CreateDirectory(root);
        var target=new JotSession(true,root){ExerciseLifecycle=true};
        var operations=new ConcurrentDictionary<string,int>();
        var ids=Enumerable.Range(0,240).Select(_=>Guid.NewGuid().ToString()).ToArray();
        await target.Store.Save(JsonSerializer.SerializeToElement(new{version=2,activeId=ids[0],prefs=NoteStore.Defaults(),notes=ids.Select((id,index)=>new{id,title="Fixture "+index,html="<p>"+new string('x',2000)+"</p>",plain=new string('x',2000),color="crimson",updatedAt=index})}));
        try
        {
            var home=target.Home();await home.WaitFor("window.jotReady===true&&JotWorkspace.view==='home'");
            var note=target.OpenNote(ids[0]);await note.WaitFor("window.jotReady===true&&ready");
            await Task.Delay(200);var indexBefore=home.TestHostActions.Count(action=>action=="index-load");
            target.Store.ObserveOperation=name=>operations.AddOrUpdate(name,1,(_,value)=>value+1);
            var saveTimes=new List<double>();
            for(var i=0;i<8;i++)
            {
                var watch=Stopwatch.StartNew();await note.Script("window.perfSaved=false;editor.innerHTML='<p>Latest synthetic edit "+i+"</p>';onEdit();saveNow().then(()=>window.perfSaved=true)");
                await note.WaitFor("window.perfSaved===true");saveTimes.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(90);
            }
            await Task.Delay(100);
            var saveOperations=operations.ToDictionary();var indexLoads=home.TestHostActions.Count(action=>action=="index-load")-indexBefore;
            checks.Add(new{name="responsiveness-visible-library-keeps-latest-draft",passed=await home.Script("homeData.notes.find(n=>n.id==="+JsonSerializer.Serialize(ids[0])+").plain==='Latest synthetic edit 7'")=="true"});
            target.Store.ObserveOperation=null;note.ClosePermanently();
            foreach(var id in ids.Take(28))await home.SwitchNoteTab(id);
            await home.Script("window.perfTabRenders=0;window.perfOriginalRenderTabs=JotWorkspace.renderTabs;JotWorkspace.renderTabs=(...args)=>{window.perfTabRenders++;return window.perfOriginalRenderTabs(...args)};window.perfKeySamples=[];for(let i=0;i<60;i++){const start=performance.now();editor.lastChild.firstChild.appendData('x');onEdit();clearTimeout(saveTimer);window.perfKeySamples.push(performance.now()-start)}");
            using var typing=JsonDocument.Parse(await home.Script("({renders:window.perfTabRenders,keys:window.perfKeySamples.length,medianMs:window.perfKeySamples.toSorted((a,b)=>a-b)[30],p95Ms:window.perfKeySamples.toSorted((a,b)=>a-b)[57]})"));
            await home.Script("JotWorkspace.renderTabs=window.perfOriginalRenderTabs");await home.Flush();
            checks.Add(new{name="responsiveness-typing-retains-latest-content",passed=(await target.Store.LoadNote(ids[27]))!.Value.GetProperty("plain").GetString()!.EndsWith(new string('x',60))});
            await home.Script("window.perfPlainReads=0;window.perfOriginalPlainText=editorPlainText;editorPlainText=(...args)=>{window.perfPlainReads++;return window.perfOriginalPlainText(...args)}");
            var cleanSaves=home.TestHostActions.Count(action=>action=="save");
            for(var i=0;i<4;i++)await home.Flush();
            var cleanPlainReads=int.Parse(await home.Script("window.perfPlainReads"));await home.Script("editorPlainText=window.perfOriginalPlainText");
            checks.Add(new{name="responsiveness-clean-flush-does-not-write",passed=cleanSaves==home.TestHostActions.Count(action=>action=="save")});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"responsiveness.json"),JsonSerializer.Serialize(new{fixture=new{notes=240,plainCharacters=2000,openTabs=28,saveCount=8,keyCount=60,cleanFlushes=4},saves=new{indexLoads,operations=saveOperations,medianMs=saveTimes.Order().ElementAt(4),maxMs=saveTimes.Max()},typing=typing.RootElement.Clone(),cleanFlush=new{plainTextExtractions=cleanPlainReads}},new JsonSerializerOptions{WriteIndented=true}));
            checks.Add(new{name="responsiveness-no-runtime-errors",passed=target.Windows.All(window=>window.RuntimeErrors.Count==0)});
        }
        finally{target.Store.ObserveOperation=null;target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }
}

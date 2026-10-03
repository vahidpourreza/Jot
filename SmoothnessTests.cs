using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Jot;

public partial class MainWindow
{
    private async Task MeasureIndexSmoothness(List<object> checks)
    {
        await Script("""
            window.indexPerfDone=false;
            (async()=>{
              const saved={data:homeData,selection:selectedId};
              const samples=[];let replaced=0;
              try{
                homeData={...homeData,notes:Array.from({length:250},(_,i)=>({id:crypto.randomUUID(),title:'Note '+i,plain:'First line '+i+'\nمتن فارسی English preview',group:'Group '+i%5,updatedAt:Date.now()-i*1000,hasImage:false}))};
                selectedId=null;renderCards();
                for(let i=0;i<16;i++){
                  await new Promise(r=>setTimeout(r,25));
                  const unaffected=$('cards').lastElementChild;
                  const start=performance.now();homeData.notes[0].plain+='x';renderCards();
                  // Include layout: this is work before a frame, not just JS DOM calls.
                  $('cards').getBoundingClientRect();samples.push(performance.now()-start);
                  if(unaffected!==$('cards').lastElementChild)replaced++;
                }
                samples.sort((a,b)=>a-b);
                window.indexPerf={notes:250,medianMs:samples[8],p95Ms:samples[15],unaffectedRowReplacements:replaced};
              }finally{homeData=saved.data;selectedId=saved.selection;renderCards();}
              window.indexPerfDone=true;
            })().catch(error=>{window.indexPerfError=String(error);window.indexPerfDone=true;});
            """);
        await WaitFor("window.indexPerfDone===true");
        using var index=JsonDocument.Parse(await Script("window.indexPerf"));
        var frameProperty=Browser.GetType().GetField("FpsDividerProperty",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(null) as DependencyProperty;
        await File.WriteAllTextAsync(Path.Combine(testOutput,"smoothness.json"),JsonSerializer.Serialize(new {
            index=index.RootElement.Clone(),composition=new {fpsDivider=frameProperty is null?null:Browser.GetValue(frameProperty),renderTier=RenderCapability.Tier>>16},
            limitation="Offscreen workload timing, not actual desktop input-to-display latency or Telegram equivalence."
        },new JsonSerializerOptions{WriteIndented=true}));
        checks.Add(new{name="index-smoothness-workload-completes",passed=await Script("!window.indexPerfError")=="true"});
    }
    private async Task MeasureWindowAndImageWork(List<object> checks,MainWindow note)
    {
        var before=(note.Width,note.Height);var sizes=new List<double>();
        try
        {
            for(int i=0;i<24;i++)
            {
                var watch=Stopwatch.StartNew();note.Width=400+i*7;note.Height=330+i*5;note.UpdateLayout();
                sizes.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(20);
            }
        }
        finally{note.Width=before.Width;note.Height=before.Height;note.UpdateLayout();}
        await note.Script("""
            window.imagePerfDone=false;
            (async()=>{
              const original=editor.innerHTML,samples=[];
              try{
                const canvas=document.createElement('canvas');canvas.width=canvas.height=480;
                const context=canvas.getContext('2d'),pixels=context.createImageData(480,480);let seed=12345;
                for(let i=0;i<pixels.data.length;i+=4){seed=(Math.imul(seed,1664525)+1013904223)|0;pixels.data[i]=seed&255;pixels.data[i+1]=(seed>>>8)&255;pixels.data[i+2]=(seed>>>16)&255;pixels.data[i+3]=255;}
                context.putImageData(pixels,0,0);const src=canvas.toDataURL('image/png');
                editor.innerHTML='<p>Before <img src="'+src+'"> between <img src="'+src+'"> after</p><p>English فارسی</p>';
                normalizeDirection();updateEmpty();historyRecord('command');
                let target=editor.lastElementChild.firstChild;
                for(let i=0;i<16;i++){
                  await new Promise(r=>setTimeout(r,20));const walker=document.createTreeWalker(editor.lastElementChild,NodeFilter.SHOW_TEXT);let next;while(next=walker.nextNode())target=next;
                  const start=performance.now();target.appendData('x');
                  editor.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText'}));
                  samples.push(performance.now()-start);clearTimeout(saveTimer);
                }
                samples.sort((a,b)=>a-b);window.imagePerf={bytes:editor.innerHTML.length,medianMs:samples[8],p95Ms:samples[15]};
              }finally{editor.innerHTML=original;onEdit('command');await saveNow();}
              window.imagePerfDone=true;
            })().catch(error=>{window.imagePerfError=String(error);window.imagePerfDone=true;});
            """);
        await note.WaitFor("window.imagePerfDone===true");sizes.Sort();
        using var typing=JsonDocument.Parse(await note.Script("window.imagePerf"));
        await File.WriteAllTextAsync(Path.Combine(testOutput,"window-and-image-work.json"),JsonSerializer.Serialize(new{
            imageTyping=typing.RootElement.Clone(),resizeLayout=new{medianMs=sizes[12],p95Ms=sizes[22]},
            limitation="Measures dispatcher layout/JS work only; offscreen windows cannot establish live drag, compositor frame pacing, or Telegram parity."
        },new JsonSerializerOptions{WriteIndented=true}));
        checks.Add(new{name="image-heavy-typing-and-native-resize-workloads-complete",passed=await note.Script("!window.imagePerfError")=="true"&&note.Width==before.Width&&note.Height==before.Height&&!note.Topmost&&note.Opacity==0});
    }
    private async Task VerifyIndexReconciliation(List<object> checks)
    {
        var result=await Script("""
            (()=>{
              const saved={data:homeData,selection:selectedId};
              const results=[];const check=(name,passed)=>results.push({name,passed});
              try{
                homeData={...homeData,notes:[{id:'a',plain:'First\nPreview',updatedAt:1},{id:'b',title:'Second',plain:'سلام English',group:'*',updatedAt:2}]};
                selectedId=null;renderCards();
                const first=cardViews.get('a').card,second=cardViews.get('b').card;
                check('index-row-has-no-group-badge',!first.querySelector('.card-group'));
                renderCards();check('unchanged-index-rows-reuse-dom',cardViews.get('a').card===first&&cardViews.get('b').card===second);
                homeData.notes=homeData.notes.map(note=>({...note,...(note.id==='a'?{title:'Updated',group:'New group',updatedAt:3}:{})}));renderCards();
                check('updated-index-row-keeps-node-and-reorders',cardViews.get('a').card===first&&$('cards').firstElementChild===first&&first.querySelector('strong').textContent==='Updated');
                editNote(cardViews.get('a').note);check('cached-row-actions-use-current-metadata',$('noteTitleInput').value==='Updated'&&homeData.notes.find(n=>n.id==='a').group==='New group');$('metadataDialog').close();
                check('all-notes-keeps-every-folder-visible',$('cards').children.length===2&&!!$('folderButton')&&!!$('homeSearch'));
                homeData.notes=homeData.notes.filter(note=>note.id!=='a');renderCards();check('deleted-index-rows-release-cache',!cardViews.has('a')&&!first.isConnected);
                homeData.notes[0].title='Currency enum حذف شد.';homeData.notes[0].group='Work یادداشت تیم';renderCards();
                check('index-previews-use-smart-mixed-language-direction',cardViews.get('b').title.dir==='rtl'&&cardViews.get('b').title.querySelector('bdi')?.textContent==='Currency enum');
              }finally{homeData=saved.data;selectedId=saved.selection;renderCards();}
              return results;
            })()
            """);
        using(var doc=JsonDocument.Parse(result))foreach(var check in doc.RootElement.EnumerateArray())checks.Add(check.Clone());
        var requests=TestHostActions.Count(action=>action=="index-load");
        Hide();await WaitFor("indexVisible===false");
        await session.Changed(false);await session.Changed(false);await session.Changed(false);await Task.Delay(150);
        checks.Add(new{name="hidden-index-defers-autosave-refreshes",passed=TestHostActions.Count(action=>action=="index-load")==requests&&await Script("indexDirty===true")=="true"});
        Reveal();await WaitFor("indexVisible===true&&indexDirty===false&&refreshScheduled===false");
        checks.Add(new{name="revealing-index-coalesces-pending-refreshes",passed=TestHostActions.Count(action=>action=="index-load")==requests+1});
    }
}

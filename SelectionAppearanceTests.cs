using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifySelectionAppearance(List<object> checks)
    {
        var selectionSession=new JotSession(true,Path.Combine(testOutput,"selection-appearance")){ExerciseLifecycle=true};
        try
        {
            var note=new MainWindow(selectionSession,"note",await selectionSession.Store.Create()){Width=1000,Height=620};
            note.Reveal();await note.WaitFor("window.jotReady===true");
            var savedPreferences=(await selectionSession.Store.LoadPreferences()).GetRawText();
            var savedView=(await selectionSession.Store.LoadNote(note.NoteId!))!.Value.GetProperty("view").GetRawText();
            await note.Script("""
                window.selectionDiagnostics=[];window.selectionChecks=[];
                window.selectionFixture=()=>{
                  flushTypingHistory();closePanels();
                  editor.innerHTML=plainToHtml([
                    'The selected line should keep its own highlight, not overlap the next line.',
                    'Unselected English line with ascenders and descenders: Agjpqy.',
                    'این سطر فارسی با حروف بلند و نقطه‌ها برای بررسی انتخاب متن است.',
                    'نوشتهٔ فارسی با آ و اِعراب، می‌توانم متن را بخوانم.',
                    'A wrapped paragraph repeats its words so that selection crosses a soft line break. '.repeat(4),
                    'Last unchanged line.'
                  ].join('\n'));
                  bookmark=null;bookmarkDirection=null;normalizeDirection();updateEmpty();editor.focus();
                  window.selectionOriginalHtml=editor.innerHTML;
                  const range=document.createRange();range.selectNodeContents(editor.firstElementChild);
                  getSelection().removeAllRanges();getSelection().addRange(range);
                };
                window.selectionMeasure=(label)=>{
                  const css=getComputedStyle(editor),canvas=document.createElement('canvas'),ctx=canvas.getContext('2d');
                  ctx.font=css.fontWeight+' '+css.fontSize+' '+css.fontFamily;
                  const metric=ctx.measureText('Agjpqy نوشتهٔ فارسی آ اِعراب می‌توانم'),rows=[];
                  for(const p of editor.children){
                    const r=document.createRange();r.selectNodeContents(p);
                    rows.push({paragraph:p.getBoundingClientRect().toJSON(),text:[...r.getClientRects()].map(b=>b.toJSON())});
                  }
                  return {label,font:css.fontFamily,size:css.fontSize,lineHeight:css.lineHeight,weight:css.fontWeight,
                    selected:getSelection().toString(),htmlUnchanged:editor.innerHTML===selectionOriginalHtml,
                    ascent:metric.fontBoundingBoxAscent,descent:metric.fontBoundingBoxDescent,
                    glyphAscent:metric.actualBoundingBoxAscent,glyphDescent:metric.actualBoundingBoxDescent,rows};
                };
                window.selectionInk=()=>{
                  const css=getComputedStyle(editor),canvas=document.createElement('canvas'),ctx=canvas.getContext('2d');
                  const font=css.fontWeight+' '+css.fontSize+' '+css.fontFamily,text='Agjpqy نوشتهٔ فارسی آ اِعراب می‌توانم';
                  ctx.font=font;canvas.width=Math.ceil(ctx.measureText(text).width)+16;canvas.height=128;
                  ctx.font=font;ctx.fillStyle='#ffffff';ctx.textBaseline='alphabetic';ctx.fillText(text,8,80);
                  return ctx.getImageData(0,0,canvas.width,canvas.height).data;
                };
                window.selectionFontsReady=false;
                Promise.all(['400 13px JotEditor','700 13px JotEditor','400 13px IRANSansX','700 13px IRANSansX']
                  .map(font=>document.fonts.load(font))).then(()=>window.selectionFontsReady=true);
                """);
            await note.WaitFor("window.selectionFontsReady===true");
            await note.Script("""
                (()=>{
                  selectionFixture();
                  const check=(name,passed,evidence)=>selectionChecks.push({name:'selection-appearance-'+name,passed,evidence});
                  for(let size=13;size<=24;size++)for(const spacing of [1.2,1.5,1.75,1.95,2.2,2.5])for(const weight of [400,700]){
                    const key=size+'-'+spacing+'-'+weight;
                    document.documentElement.style.setProperty('--editor-size',size+'px');
                    document.documentElement.style.setProperty('--editor-line-height',spacing);
                    editor.style.fontWeight=weight;editor.style.fontFamily="IRANSansX, 'Segoe UI', sans-serif";
                    const before=selectionMeasure('original-'+key),beforeInk=selectionInk();
                    editor.style.removeProperty('font-family');
                    const after=selectionMeasure('compact-'+key),afterInk=selectionInk();
                    selectionDiagnostics.push({before,after});
                    const pitch=after.rows[1].paragraph.y-after.rows[0].paragraph.y;
                    check('line-bounds-'+key,after.rows.every(row=>row.text.every(box=>box.height<=pitch+.02)),{pitch,rangeHeight:after.rows[0].text[0].height});
                    check('spacing-and-wrap-unchanged-'+key,after.rows.every((row,i)=>Math.abs(row.paragraph.y-before.rows[i].paragraph.y)<.02&&Math.abs(row.paragraph.height-before.rows[i].paragraph.height)<.02&&row.text.length===before.rows[i].text.length&&row.text.every((box,j)=>Math.abs(box.width-before.rows[i].text[j].width)<.02)));
                    check('glyphs-and-content-unchanged-'+key,after.glyphAscent===before.glyphAscent&&after.glyphDescent===before.glyphDescent&&after.htmlUnchanged&&after.selected===before.selected);
                    // Tall bold Arabic ink can exceed a compact line's highlight
                    // box. Keep every painted pixel, not an impossible promise
                    // that all glyph ink fits inside 1.2x native selection boxes.
                    const unchangedInk=beforeInk.length===afterInk.length&&beforeInk.some(value=>value!==0)&&beforeInk.every((value,i)=>value===afterInk[i]);
                    const noLineClipping=[editor,...editor.children].every(node=>{const style=getComputedStyle(node);return style.overflowX==='visible'&&style.overflowY==='visible'&&style.clipPath==='none'&&!/(paint|strict|content)/.test(style.contain)});
                    check('bilingual-ink-unclipped-'+key,unchangedInk&&noLineClipping,{unchangedInk,noLineClipping,ascent:after.ascent,descent:after.descent,glyphAscent:after.glyphAscent,glyphDescent:after.glyphDescent});
                  }
                  check('reproduced-original-overflow',selectionDiagnostics[0].before.rows[0].text[0].height>parseFloat(selectionDiagnostics[0].before.lineHeight));
                  check('editor-only-font-alias',getComputedStyle(editor).fontFamily.startsWith('JotEditor')&&!getComputedStyle(document.body).fontFamily.includes('JotEditor'));
                  editor.style.removeProperty('font-weight');
                  document.documentElement.style.setProperty('--editor-size','13px');
                  document.documentElement.style.setProperty('--editor-line-height','1.2');
                })();
                """);
            using(var results=JsonDocument.Parse(await note.Script("selectionChecks")))
                foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
            await File.WriteAllTextAsync(Path.Combine(testOutput,"selection-geometry.json"),await note.Script("selectionDiagnostics"));
            foreach(var theme in new[]{"dark","light"})
            {
                await note.Script("document.documentElement.dataset.theme="+JsonSerializer.Serialize(theme)+";selectionFixture()");
                await note.Capture("selection-compact-"+theme+"-one-line");
                await note.Script("getSelection().selectAllChildren(editor)");await note.Capture("selection-compact-"+theme+"-all-lines");
                await note.Script("editor.style.fontWeight='700';selectionFixture();getSelection().selectAllChildren(editor)");await note.Capture("selection-compact-"+theme+"-bold-lines");
                await note.Script("editor.style.removeProperty('font-weight')");
            }
            await note.Script("""
                (()=>{
                  const check=(name,passed)=>selectionChecks.push({name:'selection-appearance-'+name,passed});
                  editor.innerHTML=sanitizeHtml('<p><b>Bold</b> and <i>italic</i> <span style="font-family:Consolas;font-size:18px;color:rgb(120,80,160)">custom font</span></p><p>فارسی با <b>متن ضخیم</b> و تصویر <img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8VQAAAAASUVORK5CYII="> باقی می‌ماند.</p>');
                  normalizeDirection();const before=editor.innerHTML;editor.focus();getSelection().selectAllChildren(editor);
                  const copied=copyPayload(true);check('rich-copy-retains-formatting',copied.html.includes('<b>Bold</b>')&&copied.html.includes('Consolas')&&copied.html.includes('<img')&&editor.innerHTML===before);
                  check('custom-font-preserved',getComputedStyle(editor.querySelector('span[style]')).fontFamily==='Consolas');
                  check('bold-face-inherits-compact-metrics',getComputedStyle(editor.querySelector('b')).fontFamily.startsWith('JotEditor')&&getComputedStyle(editor.querySelector('b')).fontWeight==='700');
                })();
                """);
            using(var results=JsonDocument.Parse(await note.Script("selectionChecks.slice(-3)")))
                foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
            checks.Add(new{name="selection-appearance-saved-writing-preferences-unchanged",passed=(await selectionSession.Store.LoadPreferences()).GetRawText()==savedPreferences&&(await selectionSession.Store.LoadNote(note.NoteId!))!.Value.GetProperty("view").GetRawText()==savedView});
            checks.Add(new{name="selection-appearance-isolated",passed=selectionSession.Windows.All(w=>w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Left< -10000&&w.Top< -10000)});
        }
        finally{foreach(var window in selectionSession.Windows.ToArray())window.ClosePermanently();}
    }
}

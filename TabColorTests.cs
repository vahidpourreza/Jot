using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyTabColors(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="tab-colors-"+name,passed,evidence});
        var session=new JotSession(true,Path.Combine(testOutput,"tab-colors"));
        try
        {
            Check("style-is-not-a-preference",!(await session.Store.LoadPreferences()).TryGetProperty("tabColorStyle",out _)&&!NoteStore.Defaults().ContainsKey("tabColorStyle"));
            var home=session.Home();home.Width=960;home.Height=600;await home.WaitFor("window.jotReady===true");
            using var colors=JsonDocument.Parse(await home.Script("JotAccents.map(item=>item.slug)"));
            var palette=colors.RootElement.EnumerateArray().Select(item=>item.GetString()!).ToArray();var ids=new List<string>();
            foreach(var color in palette)
            {
                var id=await session.Store.Create();ids.Add(id);
                await session.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title=color,color}));
            }
            await home.SetTransferredTabs(ids.ToArray(),null,false);await home.SetTabPinned(ids[0],true);await home.SwitchHomeView(true);
            Check("settings-has-no-tab-color-choice",await home.Script("!document.querySelector('[data-tab-colors-choice],.tab-color-card,#tabColorStyleLabel')&&!document.getElementById('workspaceHandle').hasAttribute('data-tab-color-style')")=="true");
            await home.Script("""
                window.tabColorProbe=()=>{
                  const canvas=document.createElement('canvas');canvas.width=canvas.height=1;const ctx=canvas.getContext('2d',{willReadFrequently:true});
                  const rgba=color=>{ctx.clearRect(0,0,1,1);ctx.fillStyle=color;ctx.fillRect(0,0,1,1);return [...ctx.getImageData(0,0,1,1).data]};
                  const same=(a,b)=>a.every((value,i)=>Math.abs(value-b[i])<=1);
                  const luminance=rgb=>rgb.slice(0,3).map(value=>value/255).map(value=>value<=.04045?value/12.92:((value+.055)/1.055)**2.4).reduce((sum,value,i)=>sum+value*[.2126,.7152,.0722][i],0);
                  return [...document.querySelectorAll('.workspace-tab[data-kind=note]')].map(tab=>{
                    const note=JotWorkspace.tabs.find(note=>note.id===tab.querySelector('[role=tab]').dataset.workspaceId),accent=JotDesign.noteColor(note.color),style=getComputedStyle(tab);
                    const background=rgba(style.backgroundColor),foreground=rgba(style.color),light=luminance(background),dark=luminance(foreground),active=tab.dataset.active==='true';
                    const token=name=>rgba(style.getPropertyValue(name)),icon=rgba(getComputedStyle(tab.querySelector('.tab-marker')).color);
                    const before=getComputedStyle(tab,'::before');
                    return {id:note.id,color:note.color,pinned:note.tabPinned,active,full:same(background,rgba(accent.primary)),soft:same(background,token(active?'--tab-soft-selected':'--tab-soft-surface')),neutralText:same(foreground,token(active?'--foreground':'--workspace-tab-text')),iconMatchesInk:same(icon,token('--tab-ink')),icon:icon.join(','),background:background.join(','),border:style.borderTopColor,titleWeight:getComputedStyle(tab.querySelector('.tab-title')).fontWeight,contrast:(Math.max(light,dark)+.05)/(Math.min(light,dark)+.05),opaque:background[3]===255,strip:before.display!=='none'&&before.content!=='none'&&parseFloat(before.height)===2&&Number(before.opacity)>0&&same(rgba(before.backgroundColor),rgba(accent.primary)),shadow:style.boxShadow};
                  });
                }
                """);
            const string settled="document.getAnimations().every(animation=>animation.playState!=='running'||!Number.isFinite(animation.effect.getTiming().iterations))";
            foreach(var theme in new[]{"dark","light"})
            {
                await session.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));await home.SwitchHomeView();await home.WaitFor(settled);
                string? legacyBaseline=null;
                foreach(var legacy in new[]{"full","accent"})
                {
                    // Read old saved profiles through the removed setting:
                    // reading ignores the value without migrating its raw row.
                    await SetStoreTrigger(session.Store,"INSERT INTO settings(key,value) VALUES('tabColorStyle',json_quote('"+legacy+"')) ON CONFLICT(key) DO UPDATE SET value=excluded.value;");
                    await session.Changed();await home.WaitFor(settled);
                    var effective=await new NoteStore(session.Store.Root).LoadPreferences();
                    Check("legacy-setting-ignored-without-rewriting-row-"+theme+"-"+legacy,!effective.TryGetProperty("tabColorStyle",out _)&&Convert.ToString(await StoreScalar(session.Store,"SELECT value FROM settings WHERE key='tabColorStyle';"))==JsonSerializer.Serialize(legacy));
                    var rendered=await home.Script("JSON.stringify(tabColorProbe())");
                    if(legacyBaseline is null)legacyBaseline=rendered;
                    else Check("legacy-full-and-accent-render-identically-"+theme,legacyBaseline==rendered);
                }
                using var inactive=JsonDocument.Parse(await home.Script("tabColorProbe()"));
                var inactiveRows=inactive.RootElement.EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,row=>row.Clone());
                var selectedRows=new List<JsonElement>();
                foreach(var id in ids)
                {
                    await home.SwitchNoteTab(id);await home.WaitFor(settled);
                    using var selected=JsonDocument.Parse(await home.Script("tabColorProbe().find(row=>row.active)"));selectedRows.Add(selected.RootElement.Clone());
                }
                var all=selectedRows.Concat(inactiveRows.Values).ToArray();
                Check("all-19-colors-combine-tint-and-top-strip-"+theme,inactiveRows.Count==19&&selectedRows.Count==19&&all.All(row=>row.GetProperty("soft").GetBoolean()&&row.GetProperty("strip").GetBoolean()&&!row.GetProperty("full").GetBoolean()&&row.GetProperty("opaque").GetBoolean()));
                Check("selected-and-inactive-text-stays-readable-"+theme,all.All(row=>row.GetProperty("contrast").GetDouble()>=4.5&&row.GetProperty("neutralText").GetBoolean()&&row.GetProperty("iconMatchesInk").GetBoolean()),all.Select(row=>new{color=row.GetProperty("color").GetString(),active=row.GetProperty("active").GetBoolean(),contrast=row.GetProperty("contrast").GetDouble()}).ToArray());
                Check("selection-keeps-soft-surface-border-and-weight-"+theme,selectedRows.All(row=>{var other=inactiveRows[row.GetProperty("id").GetString()!];return row.GetProperty("background").GetString()!=other.GetProperty("background").GetString()&&row.GetProperty("border").GetString()!=other.GetProperty("border").GetString()&&row.GetProperty("titleWeight").GetString()=="500"&&row.GetProperty("shadow").GetString()=="none";}));
                Check("pinned-tab-also-combines-tint-and-strip-"+theme,all.Count(row=>row.GetProperty("pinned").GetBoolean())==2&&all.Where(row=>row.GetProperty("pinned").GetBoolean()).All(row=>row.GetProperty("soft").GetBoolean()&&row.GetProperty("strip").GetBoolean()));
                Check("note-body-stays-neutral-"+theme,await home.Script("getComputedStyle(editor).backgroundColor===getComputedStyle(document.getElementById('writingArea')).backgroundColor")=="true");
            }
            Check("presentation-does-not-rewrite-note-colors",(await session.Store.LoadTabHeaders(ids)).Select(note=>note.GetProperty("color").GetString()).SequenceEqual(palette));
            var sampleColors=new[]{"crimson","blue","emerald"};
            var sampleIds=sampleColors.Select(color=>ids[Array.IndexOf(palette,color)]).ToArray();
            foreach(var id in ids.Except(sampleIds))await home.CloseNoteTab(id,rememberClosed:false);
            await home.CloseNoteTab("settings");await home.SetTabPinned(sampleIds[0],false);
            var sampleTitles=new[]{"Kiosk.jot","Note 2","Ideas"};
            for(var index=0;index<sampleIds.Length;index++)await session.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=sampleIds[index],title=sampleTitles[index]}));
            await home.RefreshTabHeaders();await home.SwitchNoteTab(sampleIds[1]);
            foreach(var theme in new[]{"dark","light"})
            {
                await session.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));
                foreach(var width in new[]{720})
                {
                    home.Width=width;await home.WaitFor("innerWidth<="+width+"&&innerWidth>"+(width-40)+"&&document.getAnimations().every(animation=>animation.playState!=='running'||!Number.isFinite(animation.effect.getTiming().iterations))");
                    await home.Capture("tab-colors-combined-sample-"+theme+"-"+width);
                    if(width==720)
                    {
                        using var clip=JsonDocument.Parse(await home.Script("({x:0,y:0,width:innerWidth,height:Math.min(innerHeight,document.getElementById('workspaceHandle').getBoundingClientRect().bottom+60),scale:1})"));
                        using var screenshot=JsonDocument.Parse(await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",JsonSerializer.Serialize(new{format="png",captureBeyondViewport=false,clip=clip.RootElement})));
                        await File.WriteAllBytesAsync(Path.Combine(home.testOutput,"tab-colors-combined-header-"+theme+".png"),Convert.FromBase64String(screenshot.RootElement.GetProperty("data").GetString()!));
                    }
                }
            }
            Check("test-isolated-and-no-renderer-errors",home.RuntimeErrors.Count==0&&!home.Topmost&&!home.ShowActivated&&!home.ShowInTaskbar&&home.Opacity==0&&home.Left< -10000&&home.Top< -10000);
        }
        finally{session.DisposeGlobalShortcuts();foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}

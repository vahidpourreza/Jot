using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyLibrary(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="library-"+name,passed});
        var s=new JotSession(true,Path.Combine(testOutput,"library")){ExerciseLifecycle=true};
        try
        {
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var a=await s.Store.Create();var b=await s.Store.Create();var blank=await s.Store.Create();
            await s.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=a,html="<p>Currency enum حذف شد.</p><p>Design decisions</p>",plain="Currency enum حذف شد.\nDesign decisions",updatedAt=1000}));
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=a,color="blue"}));
            await s.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=b,html="<p>Release checklist</p>",plain="Release checklist",updatedAt=2000}));
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=b,title="Shipping",color="amber"}));
            await s.Store.CreateFolder("Work");await s.Store.CreateFolder("پروژه شخصی");
            Check("empty-folders-persist",(await s.Store.LoadIndex())!.Value.GetProperty("folders").GetArrayLength()==2);
            bool duplicate=false;try{await s.Store.CreateFolder("work");}catch(InvalidDataException){duplicate=true;}Check("folder-duplicates-rejected",duplicate);
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=a,group="Work"}));
            var home=s.Home();await home.WaitFor("window.jotReady===true");home.Width=640;home.Height=570;await Task.Delay(120);
            Check("logo-in-permanent-home-tab-without-extra-heading",await home.Script("!!document.querySelector('#workspaceHandle img[alt=Jot]')&&document.querySelector('#workspaceTabs [role=tab]').dataset.workspaceId==='home'&&!document.querySelector('#workspaceHandle h1,#workspaceHandle h2')")=="true");
            Check("familiar-home-window-control-icons-and-order",await home.Script("[...document.querySelectorAll('#workspaceHandle .index-window-actions button')].map(b=>b.id).join(',')==='workspaceMinimize,workspaceMaximize,workspaceClose'&&[['workspaceMinimize','window-minimize'],['workspaceMaximize','window-maximize'],['workspaceClose','window-close']].every(([id,icon])=>document.getElementById(id).querySelector('svg').innerHTML===JotDesign.icon(icon).innerHTML)")=="true");
            Check("new-and-settings-in-appbar-not-header",await home.Script("!!document.querySelector('.library-appbar #homeNew')&&!!document.querySelector('.library-appbar #settingsButton')&&!document.querySelector('#workspaceHandle #homeNew')")=="true");
            Check("cards-keep-actions-in-context-menu-and-offer-selection",await home.Script("document.querySelectorAll('.note-card').length===3&&!document.querySelector('.card-edit,.note-card button:not(.card-open)')&&document.querySelectorAll('.note-card .card-select').length===3&&[...document.querySelectorAll('.card-pin')].every(pin=>pin.hidden)")=="true");
            Check("untitled-note-has-no-generated-title",await home.Script("document.querySelector('[data-note-id=\""+blank+"\"] .card-title').hidden&&!document.querySelector('[data-note-id=\""+blank+"\"]').textContent.includes('New note')")=="true");
            Check("note-colors-visible-in-cards",await home.Script("getComputedStyle(document.querySelector('[data-note-id=\""+a+"\"]')).borderTopColor!==getComputedStyle(document.querySelector('[data-note-id=\""+b+"\"]')).borderTopColor")=="true");
            Check("mixed-preview-keeps-persian-direction",await home.Script("document.querySelector('[data-note-id=\""+a+"\"] .card-snippet>span').dir==='rtl'")=="true");
            await home.Script("document.getElementById('homeSearch').value='design';search()");Check("english-content-search",await home.Script("document.querySelectorAll('.note-card').length===1")=="true");
            await home.Script("document.getElementById('homeSearch').value='حذف';search()");Check("persian-content-search",await home.Script("document.querySelectorAll('.note-card').length===1")=="true");
            Check("search-folds-keyboard-variants-without-editing-notes",await home.Script("searchText('كي ۱۲')===searchText('کی 12')")=="true");
            await home.Script("document.getElementById('homeSearch').value='missing words';search()");Check("no-results-state",await home.Script("!document.getElementById('homeEmpty').hidden&&document.getElementById('emptyTitle').textContent==='No matching notes'")=="true");
            await home.ClickControl("#clearSearch");await home.ClickControl("#listView");await home.WaitFor("homeData.prefs.libraryView==='list'");
            Check("list-layout-is-one-row-per-note",await home.Script("document.getElementById('cards').dataset.view==='list'&&getComputedStyle(document.getElementById('cards')).gridTemplateColumns.split(' ').length===1")=="true");
            await home.ClickControl("#gridView");await home.WaitFor("homeData.prefs.libraryView==='grid'");
            Check("grid-has-multiple-columns",await home.Script("getComputedStyle(document.getElementById('cards')).gridTemplateColumns.split(' ').length>=2")=="true");
            await home.Script("folder='Work';renderFolders();renderCards();saveIndexView()");Check("folder-filters-notes",await home.Script("document.querySelectorAll('.note-card').length===1")=="true");
            await home.ClickControl("#settingsButton");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");await home.ClickControl("#settingsBack");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            Check("settings-return-keeps-folder-and-view",await home.Script("folder==='Work'&&viewMode==='grid'&&document.querySelectorAll('.note-card').length===1")=="true");
            var bounds=home.ReadWindowPixels();await home.ClickControl("#workspaceMaximize");await home.WaitFor("fullscreen");Check("home-fullscreen",home.IsWindowFullscreen&&home.Width==1200&&home.Height==760&&!home.Topmost);
            Check("maximized-home-shows-restore-overlapping-windows",await home.Script("document.getElementById('workspaceMaximize').title==='Restore'&&document.querySelector('#workspaceMaximize svg').innerHTML===JotDesign.icon('window-restore').innerHTML")=="true");
            await home.ClickControl("#settingsButton");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");await home.ClickControl("#workspaceMaximize");await home.WaitFor("!fullscreen");
            Check("settings-can-exit-fullscreen-to-original-size",home.ReadWindowPixels()==bounds);await home.ClickControl("#settingsBack");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            await home.RightClickControl("[data-note-id='"+a+"'] .card-open");
            Check("right-click-has-library-actions",await home.Script("['open','tab','rename','move','copy','export','delete'].every(id=>document.querySelector('#contentContextMenu [data-action='+id+']'))")=="true");
            await home.ClickControl("#contentContextMenu [data-action=rename]");await home.Script("document.getElementById('noteTitleInput').value='Project outline'");await home.ClickControl("#metadataSave");await home.WaitFor("!document.getElementById('metadataDialog').open&&homeData.notes.some(n=>n.title==='Project outline')");
            Check("rename-keeps-content-and-folder",(await s.Store.LoadNote(a))!.Value.GetProperty("group").GetString()=="Work"&&(await s.Store.LoadNote(a))!.Value.GetProperty("plain").GetString()!.Contains("Currency"));
            await home.RightClickControl("[data-note-id='"+a+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=move]");await home.ClickControl("#moveFolders button:first-child");await home.WaitFor("!document.getElementById('moveDialog').open");
            Check("move-to-unfiled-keeps-empty-folder",(await s.Store.LoadNote(a))!.Value.GetProperty("group").GetString()==""&&(await s.Store.LoadIndex())!.Value.GetProperty("folders").EnumerateArray().Any(f=>f.GetString()=="Work"));
            await s.Store.RenameFolder("Work","Research");await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=b,group="Research"}));await s.Store.RemoveFolder("Research");
            Check("remove-folder-keeps-notes",await s.Store.Contains(b)&&(await s.Store.LoadNote(b))!.Value.GetProperty("group").GetString()=="");
            await home.Script("newFolder();document.getElementById('noteTitleInput').value='Drafts'");await home.ClickControl("#metadataSave");await home.WaitFor("!document.getElementById('metadataDialog').open&&folder==='Drafts'");
            Check("new-folder-form-persists-empty-folder",(await s.Store.LoadIndex())!.Value.GetProperty("folders").EnumerateArray().Any(f=>f.GetString()=="Drafts"));
            await home.Script("renameFolder('Drafts');document.getElementById('noteTitleInput').value='Ideas'");await home.ClickControl("#metadataSave");await home.WaitFor("!document.getElementById('metadataDialog').open&&folder==='Ideas'");
            Check("rename-folder-form-keeps-selection",await home.Script("document.getElementById('folderLabel').textContent==='Ideas'")=="true");
            await home.Script("removeFolder('Ideas')");await home.ClickControl("#confirmApply");await home.WaitFor("!document.getElementById('libraryConfirm').open&&folder===null");
            Check("remove-folder-form-preserves-note-count",(await s.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==3);
            var image=JsonSerializer.Deserialize<string>(await home.Script("(()=>{const c=document.createElement('canvas');c.width=600;c.height=200;return c.toDataURL();})()"))!;
            await s.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=b,html="<p>Original <img src=\""+image+"\"> image</p>",plain="Original image",updatedAt=3000}));await s.Changed(false);await home.WaitFor("homeData.notes.some(n=>n.plain==='Original image')");
            await home.Script("folder=null;renderFolders();renderCards()");await home.RightClickControl("[data-note-id='"+b+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=copy]");await home.WaitFor("document.getElementById('contentContextMenu').hidden");
            Check("copy-keeps-text-and-original-image",home.TestClipboardData?.GetData(DataFormats.Bitmap) is BitmapSource bmp&&bmp.PixelWidth==600&&bmp.PixelHeight==200&&(string?)home.TestClipboardData?.GetData(DataFormats.UnicodeText)=="Original image");
            await home.RightClickControl("[data-note-id='"+b+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=export]");await home.WaitFor("document.getElementById('contentContextMenu').hidden");Check("export-keeps-original-image",home.TestExportHtml?.Contains(image)==true);
            await home.RightClickControl("[data-note-id='"+blank+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=delete]");await home.ClickControl("#confirmCancel");Check("delete-cancel-keeps-note",await s.Store.Contains(blank));
            await home.RightClickControl("[data-note-id='"+blank+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=delete]");await home.ClickControl("#confirmApply");await home.WaitFor("!document.getElementById('libraryConfirm').open&&homeData.notes.length===2");Check("confirmed-delete-removes-note",!await s.Store.Contains(blank)&&Directory.GetFiles(Path.Combine(s.Store.Root,"trash")).Length==1);
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,520,900})
            {
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await s.Changed();home.Width=width;home.Height=560;await Task.Delay(180);await home.Script("renderCards()");
                Check("responsive-no-horizontal-overflow-"+theme+"-"+width,await home.Script("document.querySelector('.library-app').scrollWidth<=innerWidth&&document.querySelector('.library-appbar').scrollWidth<=innerWidth&&document.querySelectorAll('.note-card').length===2")=="true");
                await home.Capture("library-"+theme+"-"+width);
                if(width==520){await home.Script("viewMode='list';renderCards()");await home.Capture("library-list-"+theme);await home.Script("viewMode='grid';renderCards()");}
            }
            Check("no-renderer-errors",s.Windows.All(w=>w.RuntimeErrors.Count==0));
        }
        finally{foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
    }
    private async Task VerifyTabbedNotes(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="tabs-"+name,passed});
        var path=Path.Combine(testOutput,"tabs");var s=new JotSession(true,path){ExerciseLifecycle=true};JotSession? restart=null;
        try
        {
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            const string normalShell="!document.querySelector('#noteTabs,.workspace-tabs,.workspace-header,#newTabButton,#newWindowButton')&&document.getElementById('newButton').ariaLabel==='New note'";
            const string permanentHome="document.querySelector('.workspace-tabs [role=tab]').dataset.workspaceId==='home'&&document.querySelectorAll('.workspace-tab[data-home=true]').length===1&&!document.querySelector('.workspace-tab[data-home=true] .tab-close')";
            var a=await s.NewNote();await a.WaitFor("window.jotReady===true");var first=a.NoteId!;
            Check("normal-note-has-no-tab-dom-or-tab-menu-actions",await a.Script(normalShell)=="true"&&!a.Tabbed);
            Check("familiar-note-window-controls-and-order",await a.Script("[...document.querySelectorAll('#handle button')].map(b=>b.id).join(',')==='newButton,menuButton,pinButton,minimizeButton,fullscreenButton,hideButton'&&[['minimizeButton','window-minimize'],['fullscreenButton','window-maximize'],['hideButton','window-close']].every(([id,icon])=>document.getElementById(id).querySelector('svg').innerHTML===JotDesign.icon(icon).innerHTML)")=="true");
            await a.ClickControl("#newButton");
            for(int i=0;i<100&&s.Windows.Count(w=>w.Mode=="note")<2;i++)await Task.Delay(30);
            var separate=s.Windows.Single(w=>w.Mode=="note"&&w!=a);await separate.WaitFor("window.jotReady===true");var separateId=separate.NoteId!;
            Check("normal-note-plus-still-creates-separate-window",a.NoteId==first&&first!=separateId&&await separate.Script(normalShell)=="true");
            await a.Script("editor.innerHTML='<p>First draft</p><p>Currency حذف شد.</p>';onEdit('command');clearTimeout(saveTimer);setPreference({fontSize:20,lineHeight:2.2,toolbarVisible:false}).then(()=>setNoteColor('blue')).then(()=>window.firstConfigured=true)");await a.WaitFor("window.firstConfigured===true");
            var home=s.Home();await home.WaitFor("window.jotReady===true");var handle=home.source!.Handle;
            Check("home-is-first-selected-nonclosable-tab-before-any-notes",await home.Script(permanentHome+"&&document.querySelectorAll('.workspace-tabs [role=tab]').length===1&&document.querySelector('.workspace-tabs [role=tab]').ariaSelected==='true'")=="true");
            await a.Script("editor.innerHTML='<p>First draft</p><p>Transfer retry keeps this final text</p>';onEdit();clearTimeout(saveTimer)");
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_standalone_tab_save BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic standalone save failure');END;");
            foreach(var action in new[]{"transfer","delete"})
            {
                bool failed=false;try{if(action=="transfer")await s.OpenAsTab(first);else await s.DeleteLibraryNote(first);}catch(IOException){failed=true;}
                await a.WaitFor("!app.inert&&!editorLockedByHost");
                Check("standalone-"+action+"-save-failure-keeps-draft-editable",failed&&!a.windowClosed&&a.IsVisible&&a.NoteId==first&&await s.Store.Contains(first)&&await a.Script("editor.isContentEditable&&editor.textContent.includes('Transfer retry keeps this final text')")=="true");
            }
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_standalone_tab_save;");
            await home.RightClickControl("[data-note-id='"+first+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=tab]");
            await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost&&model.activeId==="+JsonSerializer.Serialize(first));
            Check("home-open-in-new-tab-reuses-home-native-window",home.Mode=="home"&&home.source!.Handle==handle&&home.NoteId==first&&home.NoteTabIds.SequenceEqual([first])&&a.windowClosed);
            Check("opening-standalone-note-flushes-final-draft-and-settings",await home.Script("editor.textContent.includes('First draft')&&model.prefs.fontSize===20&&!keepFormatOpen&&activeNote().color==='blue'")=="true");
            Check("workspace-editor-keeps-home-first-and-normal-notes-unchanged",await home.Script(permanentHome)=="true"&&await separate.Script(normalShell)=="true");
            await home.Script("editor.innerHTML='<p>First workspace edit</p>';onEdit('command');clearTimeout(saveTimer)");
            await home.ClickControl(".workspace-add-tab");await home.WaitFor("tabbed&&tabHeaders.length===2&&!tabBusy");var second=home.NoteId!;
            Check("workspace-plus-adds-tab-in-same-native-home-window",home.source!.Handle==handle&&first!=second&&home.NoteTabIds.SequenceEqual([first,second])&&s.Windows.Count(w=>w.Mode=="note")==1);
            Check("switch-saves-last-draft",(await s.Store.LoadNote(first))!.Value.GetProperty("plain").GetString()=="First workspace edit");
            Check("tab-label-query-does-not-load-rich-content",(await home.TabHeaders()).All(h=>!h.TryGetProperty("html",out _)&&h.GetProperty("plain").GetString()!.Length<=160));
            Check("new-tab-has-independent-defaults",await home.Script("!editor.textContent.trim()&&model.prefs.fontSize===16&&keepFormatOpen&&activeNote().color==='crimson'")=="true");
            await home.Script("editor.innerHTML='<p>Second draft</p>';onEdit('command');clearTimeout(saveTimer)");
            await home.SwitchNoteTab(first);Check("switch-restores-content-and-own-settings",await home.Script("editor.textContent==='First workspace edit'&&model.prefs.fontSize===20&&!keepFormatOpen&&activeNote().color==='blue'")=="true");
            await home.Script("undo();window.undoWorked=!editor.textContent.includes('First workspace edit');undo(true)");Check("undo-history-survives-note-tab-switches",await home.Script("window.undoWorked&&editor.textContent==='First workspace edit'")=="true");
            await Task.WhenAll(home.SwitchNoteTab(second),home.SwitchNoteTab(first),home.SwitchNoteTab(second));Check("rapid-switches-serialize",home.NoteId==second&&await home.Script("editor.textContent==='Second draft'")=="true");
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_tab_save BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic save failure');END;");
            await home.Script("editor.innerHTML='<p>Do not lose this draft</p>';onEdit();clearTimeout(saveTimer)");
            foreach(var action in new[]{"note","home","close"})
            {
                bool failed=false;try{if(action=="close")await home.CloseNoteTab(second);else await home.SwitchNoteTab(action=="home"?"home":first);}catch(IOException){failed=true;}
                await home.WaitFor("!app.inert");Check("failed-save-stops-"+action+"-and-keeps-draft-editable",failed&&home.NoteId==second&&home.NoteTabIds.Contains(second)&&await home.Script("editor.textContent==='Do not lose this draft'&&editor.isContentEditable")=="true");
            }
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_tab_save;");await home.SwitchNoteTab(first);
            await home.Script("editor.focus();getSelection().selectAllChildren(editor);getSelection().collapseToEnd();const c=document.createElement('canvas');c.width=240;c.height=120;const src=c.toDataURL();readImage=()=>new Promise(resolve=>window.finishTabImage=()=>resolve(src));insertImages([new File(['fixture'],'fixture.png',{type:'image/png'})])");
            var switching=home.SwitchNoteTab(second);await home.WaitFor("editorLockedByHost");Check("pending-image-delays-switch",!switching.IsCompleted);await home.Script("window.finishTabImage()");await switching;
            Check("pending-image-is-preserved",(await s.Store.LoadNote(first))!.Value.GetProperty("html").GetString()!.Contains("data:image/png;base64,"));
            await home.Script("editor.innerHTML='<p>Saved on returning Home</p>';onEdit();clearTimeout(saveTimer)");
            await home.ClickControl("[role=tab][data-workspace-id=home]");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            Check("home-tab-saves-draft-and-retains-all-note-tabs",home.NoteId is null&&home.source!.Handle==handle&&home.NoteTabIds.SequenceEqual([first,second])&&(await s.Store.LoadNote(second))!.Value.GetProperty("plain").GetString()=="Saved on returning Home"&&await home.Script(permanentHome)=="true");
            await home.CloseNoteTab("home");Check("home-tab-cannot-be-closed",home.IsVisible&&!home.windowClosed&&await home.Script(permanentHome)=="true");
            await home.ClickControl("#settingsButton");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
            Check("settings-adds-one-tab-and-retains-home-and-note-tabs",home.source!.Handle==handle&&home.SettingsTabOpen&&await home.Script(permanentHome+"&&document.querySelectorAll('.workspace-tabs [role=tab]').length===4&&document.querySelectorAll('[role=tab][data-workspace-id=settings]').length===1")=="true");
            await home.ClickControl("#settingsBack");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            await home.RightClickControl("[data-note-id='"+first+"'] .card-open");await home.ClickControl("#contentContextMenu [data-action=tab]");
            await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost&&model.activeId==="+JsonSerializer.Serialize(first));
            Check("opening-existing-workspace-tab-never-duplicates-it",home.NoteTabIds.SequenceEqual([first,second])&&home.source!.Handle==handle&&s.Windows.Count(w=>w.ContainsNote(first))==1);
            for(int cycle=0;cycle<3;cycle++)
            {
                await Task.WhenAll(home.SwitchNoteTab("home"),home.SwitchNoteTab(first));
                await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost&&model.activeId==="+JsonSerializer.Serialize(first));
                var draft="Rapid Home navigation final draft "+cycle;
                await home.Script("editor.innerHTML="+JsonSerializer.Serialize("<p>"+draft+"</p>")+";onEdit();clearTimeout(saveTimer)");await home.Flush();
                Check("rapid-home-navigation-keeps-current-editor-flushable-"+cycle,home.NoteId==first&&home.source!.Handle==handle&&(await s.Store.LoadNote(first))!.Value.GetProperty("plain").GetString()==draft&&await home.Script("!app.inert&&!editorLockedByHost")=="true");
            }
            await home.Script("editor.innerHTML='<p>Fresh library copy</p>';onEdit();clearTimeout(saveTimer)");
            await home.Script("request('library-copy',"+JsonSerializer.Serialize(first)+").then(()=>window.liveCopyDone=true)");await home.WaitFor("window.liveCopyDone===true");
            Check("library-copy-flushes-active-tab",(string?)home.TestClipboardData?.GetData(DataFormats.UnicodeText)=="Fresh library copy");
            await separate.Script("editor.innerHTML='<p>Transferred final draft</p>';onEdit();clearTimeout(saveTimer)");await s.OpenAsTab(separateId);
            Check("open-in-tab-transfers-existing-window-without-duplicate",separate.windowClosed&&home.NoteId==separateId&&s.Windows.Count(w=>w.ContainsNote(separateId))==1&&await home.Script("editor.textContent==='Transferred final draft'")=="true");
            await home.SwitchNoteTab(first);await s.DeleteLibraryNote(separateId);
            Check("library-deletes-inactive-tab-without-closing-home",!await s.Store.Contains(separateId)&&home.NoteId==first&&home.IsVisible&&!home.NoteTabIds.Contains(separateId));
            await s.NewTab(home);var deletedActive=home.NoteId!;await home.DeleteNoteTab(deletedActive);
            Check("deleting-active-tab-keeps-home-and-surviving-notes",!await s.Store.Contains(deletedActive)&&home.IsVisible&&home.NoteId!=deletedActive&&home.NoteTabIds.SequenceEqual([first,second]));
            await home.SwitchNoteTab(second);await home.CloseNoteTab(first);Check("closing-inactive-tab-keeps-note-in-library",home.NoteId==second&&home.NoteTabIds.SequenceEqual([second])&&await s.Store.Contains(first));
            await home.Script("editor.innerHTML='<p>Saved before last tab closes</p>';onEdit();clearTimeout(saveTimer)");
            await home.ClickControl(".tab-close[data-workspace-id='"+second+"']");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            Check("closing-last-note-tab-saves-and-returns-to-permanent-home",home.IsVisible&&!home.windowClosed&&home.NoteId is null&&home.NoteTabIds.Count==0&&(await s.Store.LoadNote(second))!.Value.GetProperty("plain").GetString()=="Saved before last tab closes"&&await home.Script(permanentHome)=="true");
            await s.Store.CreateFolder("Workspace");await s.Changed(false);await home.WaitFor("homeData.folders.includes('Workspace')");
            await home.Script("folder='Workspace';renderFolders();renderCards()");await home.ClickControl(".workspace-add-tab");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost");
            var folderNote=home.NoteId!;Check("new-tab-from-home-retains-selected-folder",(await s.Store.LoadNote(folderNote))!.Value.GetProperty("group").GetString()=="Workspace");
            await home.Script("editor.innerHTML='<p>Detached workspace final draft</p>';onEdit();clearTimeout(saveTimer)");
            var detached=await s.OpenNoteWindow(folderNote);await detached.WaitFor("window.jotReady===true");await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            Check("open-in-separate-window-flushes-workspace-and-restores-normal-note",detached.Mode=="note"&&detached.NoteId==folderNote&&home.NoteTabIds.Count==0&&s.Windows.Count(w=>w.ContainsNote(folderNote))==1&&await detached.Script(normalShell+"&&editor.textContent==='Detached workspace final draft'")=="true");
            await home.Capture("workspace-home");await s.OpenAsTab(first);await s.OpenAsTab(second);await home.Capture("workspace-note");
            Check("all-test-windows-stay-offscreen",s.Windows.All(w=>!w.IsVisible||w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
            await home.Script("editor.innerHTML='<p>Workspace final text before Quit</p>';onEdit();clearTimeout(saveTimer)");
            await s.Quit();Check("quit-flushes-workspace-and-closes-own-windows",s.Windows.Count==0&&(await s.Store.LoadNote(second))!.Value.GetProperty("plain").GetString()=="Workspace final text before Quit");
            var snapshot=await s.Store.LoadWindowSession();var workspaceSnapshot=await s.Store.LoadWorkspaceSession();
            Check("quit-snapshot-retains-workspace-and-ordinary-notes",snapshot.Select(w=>w.NoteId).Concat(workspaceSnapshot?.Tabs??[]).ToHashSet().SetEquals([folderNote,first,second])&&workspaceSnapshot?.SettingsOpen==true);
            restart=new JotSession(true,path){ExerciseLifecycle=true};new MainWindow(restart,"home").StartInTray();await restart.StartWork();
            var ordinary=restart.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var note in ordinary)await note.WaitFor("window.jotReady===true");
            var restoredHome=restart.Windows.Single(w=>w.Mode=="home");
            Check("restart-uses-window-preference-and-retains-settings-tab",ordinary.Select(w=>w.NoteId).ToHashSet().SetEquals([folderNote,first,second])&&restoredHome.IsVisible&&restoredHome.SettingsTabOpen&&!restoredHome.IsSettingsView&&restoredHome.NoteId is null&&(await Task.WhenAll(ordinary.Select(w=>w.Script(normalShell)))).All(result=>result=="true"));
            foreach(var window in restart.Windows.ToArray())window.ClosePermanently();
            // Older builds saved several note ids against a floating window.
            // Restoring that data must never bring tabs back into normal notes.
            await s.Store.SaveWindowSession([new(first,-32000,-32000,480,360,[second,first])]);
            restart=new JotSession(true,path){ExerciseLifecycle=true};new MainWindow(restart,"home").StartInTray();await restart.StartWork();
            var restored=restart.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var note in restored)await note.WaitFor("window.jotReady===true");
            Check("legacy-tab-session-restores-each-note-as-separate-window",restored.Length==2&&restored.Select(w=>w.NoteId!).ToHashSet().SetEquals([first,second])&&restored.All(w=>!w.Tabbed));
            Check("restart-keeps-home-explicit-and-normal-note-ui-clean",!restart.Windows.Single(w=>w.Mode=="home").IsVisible&&(await Task.WhenAll(restored.Select(w=>w.Script(normalShell)))).All(result=>result=="true"));
            Check("no-renderer-errors",a.RuntimeErrors.Count==0&&separate.RuntimeErrors.Count==0&&home.RuntimeErrors.Count==0&&restored.All(w=>w.RuntimeErrors.Count==0));
            var transfer=new JotSession(true,Path.Combine(testOutput,"tab-transfer-quit")){ExerciseLifecycle=true};
            try
            {
                await transfer.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
                var x=await transfer.NewNote();var y=await transfer.NewNote();await x.WaitFor("window.jotReady===true");await y.WaitFor("window.jotReady===true");var xId=x.NoteId!;var yId=y.NoteId!;x.TestSaveDelayMs=180;
                await x.Script("editor.innerHTML='<p>Transfer survives quit</p>';onEdit();clearTimeout(saveTimer)");
                var opening=transfer.OpenAsTab(xId);var quitting=transfer.Quit();await opening;await quitting;
                var saved=await transfer.Store.LoadWindowSession();var transferredWorkspace=await transfer.Store.LoadWorkspaceSession();
                Check("quit-waits-for-accepted-tab-transfer",transfer.Windows.Count==0&&saved.Select(w=>w.NoteId).Concat(transferredWorkspace?.Tabs??[]).ToHashSet().SetEquals([xId,yId])&&(await transfer.Store.LoadNote(xId))!.Value.GetProperty("plain").GetString()=="Transfer survives quit");
            }
            finally{foreach(var w in transfer.Windows.ToArray())w.ClosePermanently();}
            var closingHome=new JotSession(true,Path.Combine(testOutput,"tab-transfer-home-close")){ExerciseLifecycle=true};
            try
            {
                await closingHome.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
                var source=await closingHome.NewNote();await source.WaitFor("window.jotReady===true");var id=source.NoteId!;
                var destination=closingHome.Home();await destination.WaitFor("window.jotReady===true");source.TestSaveDelayMs=500;
                await source.Script("editor.innerHTML='<p>Transfer retains final draft after Home closes</p>';onEdit();clearTimeout(saveTimer)");
                var opening=closingHome.OpenAsTab(id);await source.WaitFor("editorLockedByHost");
                await destination.HideAfterSaving();var fallback=await opening;await fallback.WaitFor("window.jotReady===true");
                Check("closing-home-during-transfer-keeps-visible-normal-note",source.windowClosed&&!destination.IsVisible&&destination.HideRequested&&destination.NoteTabIds.Count==0&&fallback.Mode=="note"&&fallback.IsVisible&&fallback.NoteId==id&&closingHome.Windows.Count(w=>w.ContainsNote(id))==1&&await fallback.Script(normalShell)=="true");
                Check("closing-home-during-transfer-preserves-final-draft",(await closingHome.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Transfer retains final draft after Home closes"&&await fallback.Script("editor.textContent==='Transfer retains final draft after Home closes'&&!app.inert&&!editorLockedByHost")=="true");
                Check("closing-home-transfer-has-no-renderer-errors",source.RuntimeErrors.Count==0&&destination.RuntimeErrors.Count==0&&fallback.RuntimeErrors.Count==0);
            }
            finally{foreach(var w in closingHome.Windows.ToArray())w.ClosePermanently();}
        }
        finally{foreach(var w in s.Windows.ToArray())w.ClosePermanently();if(restart is not null)foreach(var w in restart.Windows.ToArray())w.ClosePermanently();}
    }
}

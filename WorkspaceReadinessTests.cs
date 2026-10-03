using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyWorkspaceReadiness(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="workspace-readiness-"+name,passed});
        var session=new JotSession(true,Path.Combine(testOutput,"workspace-readiness")){ExerciseLifecycle=true};
        MainWindow? home=null;
        try
        {
            home=session.Settings();
            await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
            await home.WaitForWorkspace();
            Check("cold-settings-opens-requested-initial-view",home.IsSettingsView&&home.NoteId is null&&home.workspaceReadyCompletion.Task.IsCompletedSuccessfully);
            var id=await session.Store.Create();
            await home.SwitchNoteTab(id);
            await home.Script("editor.innerHTML='<p>Saved draft survives a workspace reload.</p>';onEdit();clearTimeout(saveTimer)");
            await home.Flush();

            var previousReady=home.workspaceReadyCompletion;
            var previousDocument=home.workspaceDocumentId;
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? flush=null,switchHome=null;
            bool reset=false,staleReadyIgnored=false;
            void OnReloadStarting(object? sender,CoreWebView2NavigationStartingEventArgs args)
            {
                reset=home.workspaceReadyCompletion!=previousReady&&!home.workspaceReadyCompletion.Task.IsCompleted&&!home.editorReadyCompletion.Task.IsCompleted;
                home.WorkspaceReady(previousDocument);
                staleReadyIgnored=!home.workspaceReadyCompletion.Task.IsCompleted;
                // Both requests arrive while the new document has no bridge
                // listeners. They must wait for its own readiness handshake.
                flush=home.FlushEditor();
                switchHome=home.SwitchHomeView();
                started.TrySetResult();
            }
            home.Browser.CoreWebView2.NavigationStarting+=OnReloadStarting;
            var elapsed=Stopwatch.StartNew();
            try
            {
                home.Browser.CoreWebView2.Reload();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(12));
                await Task.WhenAll(flush!,switchHome!).WaitAsync(TimeSpan.FromSeconds(12));
                await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            }
            finally{home.Browser.CoreWebView2.NavigationStarting-=OnReloadStarting;}
            elapsed.Stop();
            Check("reload-replaces-completed-native-readiness",reset);
            Check("previous-document-cannot-acknowledge-reload",staleReadyIgnored&&home.workspaceDocumentId is not null&&home.workspaceDocumentId!=previousDocument);
            Check("concurrent-flush-and-home-wait-for-new-document",home.NoteId is null&&!home.IsSettingsView&&elapsed.ElapsedMilliseconds<12000);
            Check("reload-retains-last-saved-draft",(await session.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()?.Trim()=="Saved draft survives a workspace reload.");
            await home.SwitchNoteTab(id);
            Check("editor-is-ready-after-reload-and-home-return",home.editorReadyCompletion.Task.IsCompletedSuccessfully&&await home.Script("ready&&editor.isContentEditable&&editor.textContent==='Saved draft survives a workspace reload.'")=="true");
            Check("no-renderer-errors-or-desktop-interaction",home.RuntimeErrors.Count==0&&home.Left< -10000&&home.Top< -10000&&home.Opacity==0&&!home.ShowActivated&&!home.ShowInTaskbar&&!home.Topmost);
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}

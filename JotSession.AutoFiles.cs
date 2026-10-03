using System.IO;
using System.Text.Json;

namespace Jot;
internal sealed partial class JotSession
{
    private readonly Dictionary<string,CancellationTokenSource> autoFileTimers=[];
    private readonly Dictionary<string,string> fileSaveErrors=[];
    private readonly HashSet<string> savingFiles=[];
    internal void ClearFileSaveError(string id)=>fileSaveErrors.Remove(id);
    internal async Task NotifyFileState(string id,JsonElement? knownState=null)
    {
        if((knownState??await Store.LoadFileState(id)) is not {} state)return;
        foreach(var window in Windows.ToArray()){
            if(window.NoteId==id)window.SetNoteFileCaption(state);
            window.Post(new{@event="file-state",state,saving=savingFiles.Contains(id),error=fileSaveErrors.GetValueOrDefault(id)});
        }
    }
    internal async Task FileNoteChanged(string id,JsonElement? knownState=null)
    {
        if((knownState??await Store.LoadFileState(id)) is not {} state)return;
        await NotifyFileState(id,state);
        if(state.GetProperty("path").ValueKind!=JsonValueKind.String)return;
        if(!quitting&&(await Store.LoadPreferences()).GetProperty("autoSaveFiles").GetBoolean())ScheduleFileSave(id);
    }
    private void CancelFileTimer(string id){if(autoFileTimers.Remove(id,out var previous)){previous.Cancel();previous.Dispose();}}
    private void ScheduleFileSave(string id)
    {
        if(fileSaveErrors.ContainsKey(id))return;
        CancelFileTimer(id);var timer=new CancellationTokenSource();autoFileTimers[id]=timer;
        async Task SaveLater(){
            try{
                await Task.Delay(1000,timer.Token);
                if(!quitting)await SaveAutoFile(id);
            }catch(OperationCanceledException){}
            catch(Exception error){fileSaveErrors[id]=error.Message;Log.Error("auto-save-file",error);await NotifyFileState(id);}
            finally{if(autoFileTimers.TryGetValue(id,out var current)&&ReferenceEquals(current,timer)){autoFileTimers.Remove(id);timer.Dispose();}}
        }
        _=SaveLater();
    }
    private async Task SaveAutoFile(string id)
    {
        if(!(await Store.LoadPreferences()).GetProperty("autoSaveFiles").GetBoolean())return;
        var state=await Store.LoadFileState(id);
        if(state is null||state.Value.GetProperty("path").ValueKind!=JsonValueKind.String||!state.Value.GetProperty("dirty").GetBoolean())return;
        if(fileSaveErrors.TryGetValue(id,out var failure))throw new IOException("Auto-save is paused: "+failure);
        var host=Windows.FirstOrDefault(window=>window.ContainsNote(id))??Windows.FirstOrDefault(window=>window.Mode=="home");
        if(host is null)return;
        savingFiles.Add(id);await NotifyFileState(id);
        try{await host.SaveNoteFile(id,automatic:true);}
        catch(Exception error){fileSaveErrors[id]=error.Message;throw;}
        finally{savingFiles.Remove(id);await NotifyFileState(id);}
    }
    internal async Task SaveAutoFilesFor(IEnumerable<string> ids)
    {
        if(!(await Store.LoadPreferences()).GetProperty("autoSaveFiles").GetBoolean())return;
        foreach(var id in ids.Distinct().ToArray()){CancelFileTimer(id);await SaveAutoFile(id);}
    }
    internal async Task ConfigureFileAutoSave(bool retryFailures=false)
    {
        foreach(var id in autoFileTimers.Keys.ToArray())CancelFileTimer(id);
        var enabled=(await Store.LoadPreferences()).GetProperty("autoSaveFiles").GetBoolean();
        if(enabled&&retryFailures)fileSaveErrors.Clear();
        foreach(var id in Windows.SelectMany(window=>window.Mode=="home"?window.NoteTabIds:window.NoteId is {} note?new List<string>{note}:new List<string>()).Distinct().ToArray()){
            if(await Store.LoadFileState(id) is not {} state)continue;
            await NotifyFileState(id,state);if(enabled&&state.GetProperty("path").ValueKind==JsonValueKind.String)ScheduleFileSave(id);
        }
    }
    internal void StopFileAutoSave(){foreach(var id in autoFileTimers.Keys.ToArray())CancelFileTimer(id);}
}

public partial class MainWindow
{
    internal void SetNoteFileCaption(JsonElement state)
    {
        var title=state.GetProperty("title").GetString();var fileName=state.GetProperty("name").GetString();
        var name=string.IsNullOrWhiteSpace(title)?fileName:title;
        if(!string.IsNullOrWhiteSpace(fileName)&&!string.IsNullOrWhiteSpace(title)&&title!=fileName)name+=" · "+fileName;
        Title=(string.IsNullOrWhiteSpace(name)?"Note":name)+(state.GetProperty("dirty").GetBoolean()?" *":"")+" — Jot";
    }
}

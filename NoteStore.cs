using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

internal sealed partial class NoteStore
{
    private const int ApplicationId=0x4A4F5431, SchemaVersion=2;
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NoteColors=new("crimson neutral amber blue cyan emerald fuchsia green indigo lime orange pink purple red rose sky teal violet yellow".Split(' '));
    private readonly SemaphoreSlim gate;
    private bool observedDatabase, backedUpThisSession, configured;
    public string Root { get; }
    public string FilePath=>Path.Combine(Root,"jot.db");
    public string LegacyFilePath=>Path.Combine(Root,"notes.json");
    public string BackupPath=>FilePath+".bak";
    internal int BusyTimeoutSeconds { get; init; }=3;
    internal Action<string>? ObserveOperation { get; set; }

    public NoteStore(string root)
    {
        Root=Path.GetFullPath(root);
        gate=Gates.GetOrAdd(Root,_=>new SemaphoreSlim(1,1));
    }
    public static JsonObject Defaults()=>new(){["theme"]="dark",["fontSize"]=16,["lineHeight"]=1.95,["accent"]="neutral",["iconWeight"]=1.8,["coloredIcons"]=false,["toolbarVisible"]=true,["language"]="en",["newNoteTarget"]="tab",["autoSaveFiles"]=false,["globalShortcuts"]=false,["workspacePinned"]=false,["shortcutBindings"]=new JsonObject()};

    // SQLite's async APIs are synchronous. All connection, SQL, JSON and disk work
    // runs on a worker, serialized per store root, never on the WPF dispatcher.
    private Task<T> Run<T>(Func<SqliteConnection,T> action,bool writing=false,[System.Runtime.CompilerServices.CallerMemberName]string operation="")=>Task.Run(async()=>{
        ObserveOperation?.Invoke(operation);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureDatabase();
            using var connection=Connect(FilePath);
            ValidateDatabase(connection);
            if(!configured){EnableWal(connection);configured=true;}
            if(writing&&!backedUpThisSession)
            {
                if(Initialized(connection))BackupCore(connection);
                backedUpThisSession=true;
            }
            return action(connection);
        }
        finally{gate.Release();}
    });

    internal SqliteConnection Connect(string path,bool create=false)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=path,Mode=create?SqliteOpenMode.ReadWriteCreate:SqliteOpenMode.ReadWrite,
            Pooling=false,ForeignKeys=true,DefaultTimeout=BusyTimeoutSeconds
        }.ToString());
        try{connection.Open();Execute(connection,null,"PRAGMA synchronous=FULL;");return connection;}
        catch{connection.Dispose();throw;}
    }
    private static SqliteCommand Command(SqliteConnection connection,SqliteTransaction? transaction,string sql,params (string Name,object? Value)[] values)
    {
        var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=sql;
        foreach(var (name,value) in values)command.Parameters.AddWithValue(name,value??DBNull.Value);
        return command;
    }
    private static int Execute(SqliteConnection connection,SqliteTransaction? transaction,string sql,params (string Name,object? Value)[] values)
    {using var command=Command(connection,transaction,sql,values);return command.ExecuteNonQuery();}
    private static object? Scalar(SqliteConnection connection,SqliteTransaction? transaction,string sql,params (string Name,object? Value)[] values)
    {using var command=Command(connection,transaction,sql,values);return command.ExecuteScalar();}
    private static bool Initialized(SqliteConnection c,SqliteTransaction? tx=null)=>Convert.ToInt64(Scalar(c,tx,"SELECT initialized FROM app_state WHERE singleton=1;"))==1;
    private void ValidateDatabase(SqliteConnection connection)
    {
        if(Convert.ToInt64(Scalar(connection,null,"PRAGMA application_id;"))!=ApplicationId)
            throw new InvalidDataException("This is not a Jot database. The file was left untouched.");
        var version=Convert.ToInt64(Scalar(connection,null,"PRAGMA user_version;"));
        if(version==1){UpgradeNotePreferences(connection);version=SchemaVersion;}
        if(version!=SchemaVersion)
            throw new InvalidDataException("This database requires a different Jot version. The file was left untouched.");
    }

    public Task<JsonElement?> Load()=>Run(c=>Initialized(c)?ReadModel(c,null,false): (JsonElement?)null);
    public Task<JsonElement?> LoadIndex()=>Run(c=>{InitializeMissingFileDirty(c);return Initialized(c)?ReadModel(c,null,true):(JsonElement?)null;},true);
    public Task<JsonElement> LoadPreferences()=>Run(c=>JsonSerializer.SerializeToElement(ReadPreferences(c)));
    public Task<JsonElement?> LoadNote(string id)=>Run(c=>ReadNote(c,null,id) is { } note?JsonSerializer.SerializeToElement(note):(JsonElement?)null);
    public Task<bool> Contains(string id)=>Run(c=>Scalar(c,null,"SELECT 1 FROM notes WHERE id=$id;",("$id",id)) is not null);
    public Task<string?> LoadNoteColor(string id)=>Run(c=>Scalar(c,null,"SELECT color FROM notes WHERE id=$id;",("$id",id)) as string);

    private static JsonObject ReadPreferences(SqliteConnection connection,SqliteTransaction? tx=null)
    {
        var prefs=Defaults();using var command=Command(connection,tx,"SELECT key,value FROM settings;");using var reader=command.ExecuteReader();
        while(reader.Read())prefs[reader.GetString(0)]=JsonNode.Parse(reader.GetString(1));
        if(prefs["newNoteTarget"] is not JsonValue target||!target.TryGetValue<string>(out var destination)||destination is not ("window" or "tab"))prefs["newNoteTarget"]="tab";
        // The former tint/strip choice is retired; both are always displayed.
        prefs.Remove("tabColorStyle");
        ShortcutBindings.NormalizeStored(prefs);
        prefs["accent"]="neutral";prefs["coloredIcons"]=false;prefs["language"]="en";return prefs;
    }
    private const string NoteColumns="n.id,n.title,COALESCE(g.name,''),n.color,n.plain,n.legacy_title,n.updated_at,n.has_image,n.extra";
    private static JsonObject ReadRow(SqliteDataReader reader,bool summary)
    {
        var extra=JsonNode.Parse(reader.GetString(8))!.AsObject();
        var note=summary?new JsonObject():extra;
        note["libraryPinned"]=extra["libraryPinned"] is JsonValue pin&&pin.TryGetValue<bool>(out var pinned)&&pinned;
        var icon=extra["icon"] is JsonValue iconValue&&iconValue.TryGetValue<string>(out var iconName)?iconName:null;
        note["icon"]=NormalizeNoteIcon(icon);
        if(summary){note["noteFile"]=extra["noteFile"]?.DeepClone();note["fileDirty"]=extra["fileDirty"]?.DeepClone();}
        note["id"]=reader.GetString(0);note["title"]=reader.GetString(1);note["group"]=reader.GetString(2);note["color"]=reader.GetString(3);
        note["plain"]=reader.GetString(4);note["legacyTitle"]=reader.GetString(5);note["updatedAt"]=reader.GetInt64(6);
        if(summary)note["hasImage"]=reader.GetBoolean(7);else{note["html"]=reader.GetString(9);note["view"]=ReadNotePreferences(reader.GetString(10));}
        return note;
    }
    private static JsonObject? ReadNote(SqliteConnection connection,SqliteTransaction? tx,string id)
    {
        using var command=Command(connection,tx,"SELECT "+NoteColumns+",n.html,v.value FROM notes n LEFT JOIN groups g ON g.id=n.group_id JOIN note_preferences v ON v.note_id=n.id WHERE n.id=$id;",("$id",id));
        using var reader=command.ExecuteReader();return reader.Read()?ReadRow(reader,false):null;
    }
    private static JsonElement ReadModel(SqliteConnection connection,SqliteTransaction? tx,bool summary)
    {
        // Use a snapshot so notes, active id and preferences are mutually consistent.
        using var snapshot=tx is null?connection.BeginTransaction(deferred:true):null;
        tx??=snapshot;
        var state=Scalar(connection,tx,"SELECT extra FROM app_state WHERE singleton=1;") as string??"{}";
        var data=summary?new JsonObject():JsonNode.Parse(state)!.AsObject();var notes=new JsonArray();
        using(var command=Command(connection,tx,"SELECT "+NoteColumns+(summary?"":",n.html,v.value")+" FROM notes n LEFT JOIN groups g ON g.id=n.group_id"+(summary?"":" JOIN note_preferences v ON v.note_id=n.id")+" ORDER BY n.position,n.id;"))
        using(var reader=command.ExecuteReader())while(reader.Read())notes.Add(ReadRow(reader,summary));
        data["version"]=2;data["activeId"]=Scalar(connection,tx,"SELECT active_id FROM app_state WHERE singleton=1;") as string;
        data["notes"]=notes;data["prefs"]=ReadPreferences(connection,tx);
        var folders=new JsonArray();
        using(var command=Command(connection,tx,"SELECT name FROM groups ORDER BY name COLLATE NOCASE;"))
        using(var reader=command.ExecuteReader())while(reader.Read())folders.Add(reader.GetString(0));
        data["folders"]=folders;
        var result=JsonSerializer.SerializeToElement(data);snapshot?.Commit();return result;
    }

    public Task<string> Create()=>Run(connection=>{
        var id=Guid.NewGuid().ToString();using var tx=connection.BeginTransaction();
        var position=Convert.ToInt64(Scalar(connection,tx,"SELECT COALESCE(MIN(position),0)-1 FROM notes;"));
        InsertNote(connection,tx,new JsonObject{["id"]=id,["color"]="crimson",["html"]="<p dir=\"auto\"><br></p>",["plain"]="",["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},position,"neutral",ReadPreferences(connection,tx));
        Execute(connection,tx,"UPDATE app_state SET initialized=1,active_id=$id WHERE singleton=1;",("$id",id));tx.Commit();return id;
    },true);

    public Task<JsonElement> SaveNote(JsonElement note)=>Run(connection=>{
        ValidateNote(note);var id=note.GetProperty("id").GetString()!;var html=note.GetProperty("html").GetString()!;var plain=note.GetProperty("plain").GetString()!;
        var updated=Timestamp(note);using var tx=connection.BeginTransaction();
        // Never write title, group or color from a possibly stale editor snapshot.
        int changed=Execute(connection,tx,"UPDATE notes SET html=$html,plain=$plain,updated_at=$time,has_image=$image WHERE id=$id;",
            ("$id",id),("$html",html),("$plain",plain),("$time",updated),("$image",html.Contains("<img",StringComparison.OrdinalIgnoreCase)));
        if(changed!=1)throw new InvalidDataException("Note not found. Your text is still in the editor.");
        RefreshFileDirty(connection,tx,id);
        Execute(connection,tx,"UPDATE app_state SET active_id=$id WHERE singleton=1;",("$id",id));tx.Commit();return note.Clone();
    },true);

    public Task<JsonElement> SaveMetadata(JsonElement patch)=>Run(connection=>{
        var id=patch.GetProperty("id").GetString()??throw new InvalidDataException("Invalid note.");using var tx=connection.BeginTransaction();
        // Read only metadata, not another copy of a potentially large image-bearing note.
        string title,group,color;
        using(var command=Command(connection,tx,"SELECT n.title,COALESCE(g.name,''),n.color FROM notes n LEFT JOIN groups g ON g.id=n.group_id WHERE n.id=$id;",("$id",id)))
        using(var reader=command.ExecuteReader()){
            if(!reader.Read())throw new InvalidDataException("Note not found.");title=reader.GetString(0);group=reader.GetString(1);color=reader.GetString(2);
        }
        if(patch.TryGetProperty("title",out var t))title=t.GetString()?.Trim()??"";
        if(patch.TryGetProperty("group",out var g))group=g.GetString()?.Trim()??"";
        if(title.Length>140||group.Length>64)throw new InvalidDataException("The title or group name is too long.");
        if(patch.TryGetProperty("color",out var col)){color=col.GetString()??"";if(!NoteColors.Contains(color))throw new InvalidDataException("Choose a color from the note palette.");}
        Execute(connection,tx,"UPDATE notes SET title=$title,group_id=$group,color=$color WHERE id=$id;",("$id",id),("$title",title),("$group",GroupId(connection,tx,group)),("$color",color));
        if(patch.TryGetProperty("icon",out var icon)){
            if(icon.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Choose a valid note icon.");
            ValidateNoteIcon(icon.GetString());
            Execute(connection,tx,"UPDATE notes SET extra=json_set(extra,'$.icon',$icon) WHERE id=$id;",("$icon",icon.GetString()),("$id",id));
        }
        RefreshFileDirty(connection,tx,id);
        tx.Commit();return JsonSerializer.SerializeToElement(new{id,title,group,color});
    },true);

    public Task<JsonElement> SavePreferences(JsonElement patch)=>Run(connection=>{
        using var tx=connection.BeginTransaction();var prefs=ReadPreferences(connection,tx);
        if(patch.TryGetProperty("theme",out var theme)&&(theme.ValueKind!=JsonValueKind.String||theme.GetString() is not ("light" or "dark")))
            throw new InvalidDataException("Choose Light or Dark mode for Jot.");
        if(patch.TryGetProperty("fontSize",out var fontSize)&&(fontSize.ValueKind!=JsonValueKind.Number||!fontSize.TryGetInt32(out var size)||size<13||size>24))
            throw new InvalidDataException("Choose a font size between 13 and 24 pixels.");
        if(patch.TryGetProperty("lineHeight",out var lineHeight)&&(lineHeight.ValueKind!=JsonValueKind.Number||!lineHeight.TryGetDouble(out var height)||!double.IsFinite(height)||height<1.2||height>2.5))
            throw new InvalidDataException("Choose a line height between 1.2 and 2.5.");
        if(patch.TryGetProperty("newNoteTarget",out var newNoteTarget)&&(newNoteTarget.ValueKind!=JsonValueKind.String||newNoteTarget.GetString() is not ("window" or "tab")))
            throw new InvalidDataException("Choose Tabs or Separate windows for new notes.");
        foreach(var key in new[]{"autoSaveFiles","globalShortcuts","workspacePinned"})if(patch.TryGetProperty(key,out var flag)&&flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Choose a valid "+key+" setting.");
        if(patch.TryGetProperty("shortcutBindings",out var bindings))prefs["shortcutBindings"]=ShortcutBindings.ValidateOverrides(bindings);
        foreach(var key in new[]{"theme","fontSize","lineHeight","iconWeight","toolbarVisible","language","newNoteTarget","autoSaveFiles","globalShortcuts","workspacePinned"})if(patch.TryGetProperty(key,out var value))prefs[key]=JsonNode.Parse(value.GetRawText());
        if(patch.TryGetProperty("libraryView",out var libraryView))
        {if(libraryView.GetString() is not ("grid" or "list"))throw new InvalidDataException("Choose Grid or List view.");prefs["libraryView"]=libraryView.GetString();}
        prefs["language"]="en";prefs["accent"]="neutral";prefs["coloredIcons"]=false;WritePreferences(connection,tx,prefs);
        if(patch.TryGetProperty("fontSize",out _)||patch.TryGetProperty("lineHeight",out _))RefreshAllFileDirty(connection,tx);
        Execute(connection,tx,"UPDATE app_state SET initialized=1 WHERE singleton=1;");tx.Commit();return JsonSerializer.SerializeToElement(new{prefs});
    },true);

    public Task Delete(string id)=>Run(connection=>{
        using var tx=connection.BeginTransaction();var note=ReadNote(connection,tx,id)??throw new InvalidDataException("Note not found.");
        // A recovery copy must be durable before the transactional deletion starts.
        var trash=Path.Combine(Root,"trash");Directory.CreateDirectory(trash);
        var archive=JsonSerializer.SerializeToUtf8Bytes(new{deletedAt=DateTimeOffset.UtcNow,note});
        using(var stream=new FileStream(Path.Combine(trash,$"{id}-{Guid.NewGuid():N}.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough))
        {stream.Write(archive);stream.Flush(true);}
        Execute(connection,tx,"DELETE FROM notes WHERE id=$id;",("$id",id));
        Execute(connection,tx,"UPDATE app_state SET active_id=(SELECT id FROM notes ORDER BY position,id LIMIT 1) WHERE singleton=1 AND active_id=$id;",("$id",id));
        tx.Commit();return true;
    },true);

    // Full-model replacement is reserved for fixtures/import, not normal autosave.
    public Task Save(JsonElement value)=>Run(connection=>{ReplaceModel(connection,value);return true;},true);
    public Task Import(JsonElement value)=>Run(connection=>{if(!Initialized(connection)){Validate(value);ReplaceModel(connection,PreserveLegacyWritingDefaults(value));}return true;},true);
    public Task Backup()=>Run(connection=>{BackupCore(connection);return true;});

    private static object? GroupId(SqliteConnection c,SqliteTransaction tx,string name)
    {
        if(name.Length==0)return null;
        Execute(c,tx,"INSERT INTO groups(name) VALUES($name) ON CONFLICT(name) DO NOTHING;",("$name",name));
        return Scalar(c,tx,"SELECT id FROM groups WHERE name=$name;",("$name",name));
    }
    private static void WritePreferences(SqliteConnection c,SqliteTransaction tx,JsonObject prefs)
    {
        foreach(var pair in prefs)Execute(c,tx,"INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",("$key",pair.Key),("$value",pair.Value?.ToJsonString()??"null"));
    }
    private static void InsertNote(SqliteConnection c,SqliteTransaction tx,JsonObject note,long position,string legacyColor,JsonObject defaults)
    {
        string Text(string key)=>note[key]?.GetValue<string>()??"";
        var extra=(JsonObject)note.DeepClone();foreach(var key in new[]{"id","title","group","color","plain","legacyTitle","updatedAt","html","hasImage","view"})extra.Remove(key);
        var color=note["color"]?.GetValue<string>()??(NoteColors.Contains(legacyColor)?legacyColor:"neutral");
        var html=Text("html");Execute(c,tx,"""
            INSERT INTO notes(id,title,group_id,color,plain,legacy_title,updated_at,has_image,extra,html,position)
            VALUES($id,$title,$group,$color,$plain,$legacy,$time,$image,$extra,$html,$position);
            """,("$id",Text("id")),("$title",Text("title")),("$group",GroupId(c,tx,Text("group"))),("$color",color),("$plain",Text("plain")),("$legacy",Text("legacyTitle")),("$time",note["updatedAt"]?.GetValue<long>()??0),
            ("$image",html.Contains("<img",StringComparison.OrdinalIgnoreCase)),("$extra",extra.ToJsonString()),("$html",html),("$position",position));
        var view=DefaultNotePreferences(defaults);
        if(note["view"] is JsonObject supplied)ApplyNotePreferencePatch(view,JsonSerializer.SerializeToElement(ReadNotePreferences(supplied.ToJsonString())));
        Execute(c,tx,"INSERT INTO note_preferences(note_id,value) VALUES($id,$value);",("$id",Text("id")),("$value",view.ToJsonString()));
    }
    private static void ReplaceModel(SqliteConnection connection,JsonElement value)
    {
        Validate(value);var data=JsonNode.Parse(value.GetRawText())!.AsObject();var prefs=data["prefs"] as JsonObject??Defaults();var legacyColor=prefs["accent"]?.GetValue<string>()??"neutral";
        prefs["accent"]="neutral";prefs["coloredIcons"]=false;prefs["language"]="en";
        using var tx=connection.BeginTransaction();Execute(connection,tx,"DELETE FROM notes;DELETE FROM groups;DELETE FROM settings;");
        long position=0;foreach(var note in data["notes"]!.AsArray())InsertNote(connection,tx,note!.AsObject(),position++,legacyColor,prefs);
        if(data["folders"] is JsonArray folders)foreach(var folder in folders)GroupId(connection,tx,FolderName(folder!.GetValue<string>()));
        var active=data["activeId"]?.GetValue<string>();if(active is not null&&Scalar(connection,tx,"SELECT 1 FROM notes WHERE id=$id;",("$id",active)) is null)active=null;
        foreach(var key in new[]{"version","notes","prefs","activeId","folders"})data.Remove(key);
        Execute(connection,tx,"UPDATE app_state SET initialized=1,active_id=$active,extra=$extra WHERE singleton=1;",("$active",active),("$extra",data.ToJsonString()));WritePreferences(connection,tx,prefs);tx.Commit();
    }
    private static long Timestamp(JsonElement note)=>note.TryGetProperty("updatedAt",out var time)?time.GetInt64():0;
    private static void ValidateNote(JsonElement note)
    {
        if(note.ValueKind!=JsonValueKind.Object||!note.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||!Guid.TryParse(id.GetString(),out _)
            ||!note.TryGetProperty("html",out var html)||html.ValueKind!=JsonValueKind.String||!note.TryGetProperty("plain",out var plain)||plain.ValueKind!=JsonValueKind.String)
            throw new InvalidDataException("Invalid note data. Your existing notes were left unchanged.");
        if(note.TryGetProperty("updatedAt",out var time)&&(time.ValueKind!=JsonValueKind.Number||!time.TryGetInt64(out _)))throw new InvalidDataException("Invalid note timestamp.");
        if(note.TryGetProperty("view",out var view)&&view.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))throw new InvalidDataException("Invalid note settings. Your existing notes were left unchanged.");
        foreach(var key in new[]{"title","group","color","legacyTitle"})if(note.TryGetProperty(key,out var field)&&field.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))throw new InvalidDataException("Invalid note metadata.");
    }
    internal static void Validate(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("version",out var version)||!version.TryGetInt32(out var number)||number!=2
            ||!value.TryGetProperty("notes",out var notes)||notes.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid notes file. The original was left untouched.");
        var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var note in notes.EnumerateArray()){ValidateNote(note);if(!ids.Add(note.GetProperty("id").GetString()!))throw new InvalidDataException("Duplicate note identifiers. The original was left untouched.");}
        if(value.TryGetProperty("prefs",out var prefs)&&prefs.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))throw new InvalidDataException("Invalid preferences.");
        if(value.TryGetProperty("activeId",out var active)&&active.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))throw new InvalidDataException("Invalid active note.");
    }
}

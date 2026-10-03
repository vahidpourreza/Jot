using System.Text.Json;

namespace Jot;
internal sealed partial class NoteStore
{
    internal Task<JsonElement[]> LoadTabHeaders(IEnumerable<string> noteIds)
    {
        var ids=noteIds.Distinct().ToArray();
        if(ids.Length==0)return Task.FromResult(Array.Empty<JsonElement>());
        return Run(c=>{
            var args=ids.Select((id,index)=>(Name:"$p"+index,Value:(object?)id)).ToArray();
            using var command=Command(c,null,"SELECT id,title,legacy_title,color,substr(plain,1,160),extra FROM notes WHERE id IN ("+string.Join(',',args.Select(p=>p.Name))+");",args);
            using var reader=command.ExecuteReader();var notes=new Dictionary<string,JsonElement>();
            while(reader.Read()){
                using var extra=JsonDocument.Parse(reader.GetString(5));
                notes[reader.GetString(0)]=JsonSerializer.SerializeToElement(new{id=reader.GetString(0),title=reader.GetString(1),legacyTitle=reader.GetString(2),color=reader.GetString(3),plain=reader.GetString(4),
                    icon=extra.RootElement.TryGetProperty("icon",out var icon)&&icon.ValueKind==JsonValueKind.String?icon.GetString():null,
                    noteFile=extra.RootElement.TryGetProperty("noteFile",out var file)?file.Clone():(JsonElement?)null,
                    fileDirty=extra.RootElement.TryGetProperty("fileDirty",out var dirty)&&dirty.ValueKind==JsonValueKind.True});
            }
            return ids.Where(notes.ContainsKey).Select(id=>notes[id]).ToArray();
        });
    }
}

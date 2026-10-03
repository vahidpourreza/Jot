using System.IO;

namespace Jot;
internal sealed partial class NoteStore
{
    private static string FolderName(string name)
    {
        name=name.Trim();
        if(name.Length is 0 or >64||name.Any(char.IsControl))throw new InvalidDataException("Use a folder name between 1 and 64 characters.");
        return name;
    }
    internal Task CreateFolder(string name)=>Run(c=>{
        name=FolderName(name);using var tx=c.BeginTransaction();
        if(Scalar(c,tx,"SELECT 1 FROM groups WHERE name=$name COLLATE NOCASE;",("$name",name)) is not null)throw new InvalidDataException("A folder with that name already exists.");
        GroupId(c,tx,name);Execute(c,tx,"UPDATE app_state SET initialized=1 WHERE singleton=1;");tx.Commit();return true;
    },true);
    internal Task RenameFolder(string name,string replacement)=>Run(c=>{
        replacement=FolderName(replacement);using var tx=c.BeginTransaction();
        if(Scalar(c,tx,"SELECT 1 FROM groups WHERE name=$new COLLATE NOCASE AND name<>$old;",("$new",replacement),("$old",name)) is not null)throw new InvalidDataException("A folder with that name already exists.");
        if(Execute(c,tx,"UPDATE groups SET name=$new WHERE name=$old;",("$new",replacement),("$old",name))!=1)throw new InvalidDataException("Folder not found.");
        tx.Commit();return true;
    },true);
    internal Task RemoveFolder(string name)=>Run(c=>{
        using var tx=c.BeginTransaction();var id=Scalar(c,tx,"SELECT id FROM groups WHERE name=$name;",("$name",name));
        if(id is null)throw new InvalidDataException("Folder not found.");
        Execute(c,tx,"UPDATE notes SET group_id=NULL WHERE group_id=$id;",("$id",id));
        Execute(c,tx,"DELETE FROM groups WHERE id=$id;",("$id",id));tx.Commit();return true;
    },true);
}

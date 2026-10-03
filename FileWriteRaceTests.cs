using System.IO;
using System.Text;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyFileWriteRaces(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-write-race-"+name,passed});
        var directory=Path.Combine(testOutput,"file-write-races");Directory.CreateDirectory(directory);
        var original=Encoding.UTF8.GetBytes("Original saved note.");var changed=Encoding.UTF8.GetBytes("Jot's current edit.");var external=Encoding.UTF8.GetBytes("External editor's newer edit.");
        var expected=NoteFileFormat.Digest(original);
        bool NoTemps()=>Directory.GetFiles(directory,".jot-save-*.tmp").Length==0;
        var normal=Path.Combine(directory,"normal.txt");
        Check("first-save-creates-file",await NoteFileFormat.Write(normal,original,null)==expected&&(await File.ReadAllBytesAsync(normal)).SequenceEqual(original));
        var digest=await NoteFileFormat.Write(normal,changed,expected);
        Check("linked-save-atomically-replaces-with-new-digest",digest==NoteFileFormat.Digest(changed)&&(await File.ReadAllBytesAsync(normal)).SequenceEqual(changed));
        Check("normal-save-leaves-no-temp-or-recovery-files",NoTemps()&&Directory.GetFiles(directory,".jot-recovery-*").Length==0);

        var prechanged=Path.Combine(directory,"prechanged.txt");await File.WriteAllBytesAsync(prechanged,external);
        bool failed=false;try{await NoteFileFormat.Write(prechanged,changed,expected);}catch(IOException){failed=true;}
        Check("change-before-save-is-rejected-with-external-file-intact",failed&&(await File.ReadAllBytesAsync(prechanged)).SequenceEqual(external)&&NoTemps());

        var locked=Path.Combine(directory,"locked.txt");await File.WriteAllBytesAsync(locked,original);
        await using(var writer=new FileStream(locked,FileMode.Open,FileAccess.Write,FileShare.Read|FileShare.Delete))
        {
            failed=false;try{await NoteFileFormat.Write(locked,changed,expected);}catch(IOException){failed=true;}
            Check("already-open-writer-prevents-save",failed&&NoTemps());
        }
        Check("writer-conflict-leaves-original-file-intact",(await File.ReadAllBytesAsync(locked)).SequenceEqual(original));

        var inPlace=Path.Combine(directory,"in-place.txt");await File.WriteAllBytesAsync(inPlace,original);
        bool denied=false;
        await NoteFileFormat.Write(inPlace,changed,expected,async()=>{
            try{await File.WriteAllBytesAsync(inPlace,external);}catch(IOException){denied=true;}
        });
        Check("in-place-write-between-hash-and-commit-is-denied",denied&&(await File.ReadAllBytesAsync(inPlace)).SequenceEqual(changed)&&NoTemps());

        var beforeCommit=Path.Combine(directory,"cancelled.txt");await File.WriteAllBytesAsync(beforeCommit,original);
        failed=false;try{await NoteFileFormat.Write(beforeCommit,changed,expected,()=>throw new IOException("Synthetic precommit failure."));}catch(IOException){failed=true;}
        Check("precommit-failure-preserves-original-and-removes-temp",failed&&(await File.ReadAllBytesAsync(beforeCommit)).SequenceEqual(original)&&NoTemps());

        var replaced=Path.Combine(directory,"replacement-race.txt");await File.WriteAllBytesAsync(replaced,original);
        var swap=Path.Combine(directory,"external-swap.txt");await File.WriteAllBytesAsync(swap,external);
        IOException? race=null;
        // ReplaceFile is the atomic-save route used by editors; MoveFile's
        // overwrite route can reject an open destination even with delete
        // sharing. The normal-write checks above already exercise Replace
        // successfully while Jot holds its read/delete-sharing guard.
        try{await NoteFileFormat.Write(replaced,changed,expected,()=>{File.Replace(swap,replaced,Path.Combine(directory,"external-editor-previous.txt"));return Task.CompletedTask;});}
        catch(IOException error){race=error;}
        var recovery=race?.Data["recoveryPath"] as string;
        Check("replacement-race-does-not-report-save-success",race is not null&&recovery is not null);
        Check("replacement-race-retains-external-version-in-recovery-file",recovery is not null&&File.Exists(recovery)&&(await File.ReadAllBytesAsync(recovery)).SequenceEqual(external));
        Check("replacement-race-keeps-jot-version-and-reports-recovery-location",race is not null&&recovery is not null&&race.Message.Contains(recovery,StringComparison.Ordinal)&&(await File.ReadAllBytesAsync(replaced)).SequenceEqual(changed)&&NoTemps());

        var same=Path.Combine(directory,"same-content-replacement.txt");await File.WriteAllBytesAsync(same,original);
        var sameSwap=Path.Combine(directory,"same-content-swap.txt");await File.WriteAllBytesAsync(sameSwap,original);
        var recoveries=Directory.GetFiles(directory,".jot-recovery-*").Length;
        digest=await NoteFileFormat.Write(same,changed,expected,()=>{File.Replace(sameSwap,same,Path.Combine(directory,"same-content-editor-previous.txt"));return Task.CompletedTask;});
        Check("same-content-replacement-can-complete-without-new-recovery",digest==NoteFileFormat.Digest(changed)&&Directory.GetFiles(directory,".jot-recovery-*").Length==recoveries&&NoTemps());

        var missing=Path.Combine(directory,"missing.txt");
        failed=false;try{await NoteFileFormat.Write(missing,changed,expected);}catch(IOException){failed=true;}
        Check("missing-linked-file-is-not-silently-recreated",failed&&!File.Exists(missing)&&NoTemps());
    }
}

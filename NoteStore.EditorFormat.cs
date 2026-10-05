using System.IO;
using Microsoft.Data.Sqlite;

namespace Jot;

internal sealed partial class NoteStore
{
    private void UpgradeEditorFormat(SqliteConnection connection)
    {
        // The table layout is unchanged. This boundary prevents older editors
        // from accepting and then silently dropping task nodes, marks or other
        // rich content that they do not understand. Never rewrite note HTML.
        var beforeUpgrade=FilePath+".before-v3.bak";
        if(File.Exists(beforeUpgrade))
        {
            VerifyEditorFormatBackup(beforeUpgrade);
            // A prior attempt can create its snapshot and then fail before
            // committing the version marker. The old editor could subsequently
            // save more work. Preserve both the earliest snapshot and a fresh,
            // uniquely named snapshot of the actual database being upgraded.
            var retry=FilePath+".before-v3-current-"+DateTime.UtcNow.Ticks+"-"+Guid.NewGuid().ToString("N")+".bak";
            BackupCore(connection,retry);
            VerifyEditorFormatBackup(retry);
        }
        else
        {
            BackupCore(connection,beforeUpgrade);
            VerifyEditorFormatBackup(beforeUpgrade);
        }
        using var transaction=connection.BeginTransaction();
        Execute(connection,transaction,"PRAGMA user_version=3;");
        transaction.Commit();
        backedUpThisSession=true;
    }

    private static void VerifyEditorFormatBackup(string path)
    {
        // An existing immutable recovery file must itself be valid. Do not
        // overwrite an obstructing/invalid file and claim the upgrade is safe.
        using var backup=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false
        }.ToString());
        backup.Open();
        if(Convert.ToInt64(Scalar(backup,null,"PRAGMA application_id;"))!=ApplicationId
            ||Convert.ToInt64(Scalar(backup,null,"PRAGMA user_version;"))!=2
            ||Scalar(backup,null,"PRAGMA quick_check;") as string!="ok")
            throw new InvalidDataException("The pre-editor-upgrade backup could not be verified. The editor-format upgrade was cancelled; recovery files were preserved.");
    }
}

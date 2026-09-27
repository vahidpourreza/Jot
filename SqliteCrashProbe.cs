using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Jot;

internal static class SqliteCrashProbe
{
    internal static int Run(string[] args)
    {
        try
        {
            if(args.Length!=4||args[3] is not ("committed" or "uncommitted"))return 20;
            var root=Path.GetFullPath(args[1]);
            // The parent creates this nonce only inside its isolated fixture directory.
            if(!Path.GetFileName(root).StartsWith("crash-probe-",StringComparison.Ordinal)||!Guid.TryParseExact(args[2],"N",out _))return 21;
            if(File.ReadAllText(Path.Combine(root,"probe-owner.txt"))!=args[2])return 22;
            using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"jot.db"),Mode=SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=2}.ToString());
            connection.Open();using var command=connection.CreateCommand();
            command.CommandText="PRAGMA synchronous=FULL;PRAGMA wal_autocheckpoint=0;PRAGMA cache_size=8;";command.ExecuteNonQuery();
            using var tx=connection.BeginTransaction();command.Transaction=tx;
            command.CommandText="UPDATE notes SET html=$html,plain=$text;";command.Parameters.AddWithValue("$html",new string('x',200_000));command.Parameters.AddWithValue("$text",args[3]);command.ExecuteNonQuery();
            if(args[3]=="committed")tx.Commit();
            // Terminate only this owned probe, without dispose/checkpoint or a crash dialog.
            Process.GetCurrentProcess().Kill();return 23;
        }
        catch{return 24;}
    }
}

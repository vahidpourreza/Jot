namespace Jot;

internal sealed partial class JotSession
{
    internal Task<MainWindow> ReopenClosedTab(MainWindow owner)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        if(owner.Mode!="home")throw new InvalidOperationException("Tabs are available only in Home.");
        async Task<MainWindow> Reopen()
        {
            await noteOwnershipGate.WaitAsync();
            try
            {
                if(!Windows.Contains(owner)||owner.HideRequested)return owner;
                await owner.ReopenClosedNoteTab();return owner;
            }
            finally{noteOwnershipGate.Release();}
        }
        var pending=Reopen();openingNotes.Add(pending);return ObserveCreation(pending);
    }
}

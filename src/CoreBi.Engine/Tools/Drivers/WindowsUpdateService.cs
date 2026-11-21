using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

internal static class WindowsUpdateService
{
    private const int OperationSucceeded = 2;

    internal sealed record Outcome(string Title, int Code, int Error);

    private static object Create(string id) => Activator.CreateInstance(Type.GetTypeFromProgID(id)
        ?? throw new InvalidOperationException("Serviço do Windows Update indisponível."))!;

    public static Task<List<WuDriver>> SearchAsync() => Task.Run(() =>
    {
        using var scope = new ComScope();
        dynamic session = scope.Track(Create("Microsoft.Update.Session"));
        dynamic searcher = scope.Track(session.CreateUpdateSearcher());
        dynamic result = scope.Track(searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0"));
        dynamic updates = scope.Track(result.Updates);
        var drivers = new List<WuDriver>();
        for (int i = 0; i < (int)updates.Count; i++)
        {
            dynamic update = scope.Track(updates.Item(i));
            dynamic identity = scope.Track(update.Identity);
            drivers.Add(new WuDriver
            {
                Title = (string)update.Title,
                SizeBytes = Convert.ToInt64(update.MaxDownloadSize),
                UpdateId = (string)identity.UpdateID,
                Revision = (int)identity.RevisionNumber
            });
        }
        return drivers;
    });

    public static Task<string> InstallAsync(IReadOnlyList<WuDriver> selected, Action<string> progress) => Task.Run(() =>
    {
        if (selected.Count == 0) throw new ArgumentException("Nenhum driver selecionado.", nameof(selected));
        using var scope = new ComScope();
        dynamic session = scope.Track(Create("Microsoft.Update.Session"));
        dynamic searcher = scope.Track(session.CreateUpdateSearcher());
        dynamic collection = scope.Track(Create("Microsoft.Update.UpdateColl"));
        foreach (var item in selected)
        {
            if (!Guid.TryParse(item.UpdateId, out var id) || item.Revision < 0) throw new ArgumentException("Identidade de atualização inválida.");
            dynamic found = scope.Track(searcher.Search($"IsInstalled=0 and Type='Driver' and UpdateID='{id:D}' and RevisionNumber={item.Revision}"));
            dynamic updates = scope.Track(found.Updates);
            if ((int)updates.Count != 1) throw new InvalidOperationException("A lista mudou. Consulte novamente o Windows Update.");
            dynamic update = scope.Track(updates.Item(0));
            if (!(bool)update.EulaAccepted) update.AcceptEula();
            collection.Add(update);
        }
        progress("Baixando drivers do Windows Update…");
        dynamic downloader = scope.Track(session.CreateUpdateDownloader()); downloader.Updates = collection;
        dynamic download = scope.Track(downloader.Download());
        if ((int)download.ResultCode != OperationSucceeded)
            return "Download incompleto. Nenhum instalador foi iniciado; consulte o Windows Update e tente novamente.";
        progress("Instalando…");
        dynamic installer = scope.Track(session.CreateUpdateInstaller()); installer.Updates = collection;
        dynamic result = scope.Track(installer.Install());
        var outcomes = new List<Outcome>();
        for (int i = 0; i < selected.Count; i++)
        {
            dynamic item = scope.Track(result.GetUpdateResult(i));
            outcomes.Add(new Outcome(selected[i].Title, (int)item.ResultCode, (int)item.HResult));
        }
        return Summarize(outcomes, (bool)result.RebootRequired);
    });

    internal static string Summarize(IReadOnlyList<Outcome> outcomes, bool reboot)
    {
        int ok = outcomes.Count(x => x.Code == OperationSucceeded && x.Error >= 0);
        string text = ok == outcomes.Count ? $"Instalação concluída: {ok} driver(s)." : $"Instalação parcial: {ok} de {outcomes.Count} driver(s).";
        foreach (var failed in outcomes.Where(x => x.Code != OperationSucceeded || x.Error < 0)) text += $"\n{failed.Title}: código {failed.Code}, HRESULT 0x{failed.Error:X8}.";
        return text + (reboot ? "\nReinicie o PC para finalizar." : "");
    }
}

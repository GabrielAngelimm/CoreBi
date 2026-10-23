using System.Runtime.InteropServices;

namespace CoreBi.Engine.Tools.Drivers;

internal static class WindowsUpdateService
{
    internal sealed record Outcome(string Title, int Code, int Error);

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> _objects = new();
        public dynamic Add(object value) { if (!_objects.Contains(value)) _objects.Add(value); return value; }
        public void Dispose()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (Marshal.IsComObject(_objects[i])) Marshal.FinalReleaseComObject(_objects[i]);
        }
    }

    private static object Create(string id) => Activator.CreateInstance(Type.GetTypeFromProgID(id)
        ?? throw new InvalidOperationException("Serviço do Windows Update indisponível."))!;

    public static Task<List<WuDriver>> SearchAsync() => Task.Run(() =>
    {
        using var scope = new ComScope();
        dynamic session = scope.Add(Create("Microsoft.Update.Session"));
        dynamic searcher = scope.Add(session.CreateUpdateSearcher());
        dynamic result = scope.Add(searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0"));
        dynamic updates = scope.Add(result.Updates);
        var drivers = new List<WuDriver>();
        for (int i = 0; i < (int)updates.Count; i++)
        {
            dynamic update = scope.Add(updates.Item(i));
            dynamic identity = scope.Add(update.Identity);
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
        dynamic session = scope.Add(Create("Microsoft.Update.Session"));
        dynamic searcher = scope.Add(session.CreateUpdateSearcher());
        dynamic collection = scope.Add(Create("Microsoft.Update.UpdateColl"));
        foreach (var item in selected)
        {
            if (!Guid.TryParse(item.UpdateId, out var id) || item.Revision < 0) throw new ArgumentException("Identidade de atualização inválida.");
            dynamic found = scope.Add(searcher.Search($"IsInstalled=0 and Type='Driver' and UpdateID='{id:D}' and RevisionNumber={item.Revision}"));
            dynamic updates = scope.Add(found.Updates);
            if ((int)updates.Count != 1) throw new InvalidOperationException("A lista mudou. Consulte novamente o Windows Update.");
            dynamic update = scope.Add(updates.Item(0));
            if (!(bool)update.EulaAccepted) update.AcceptEula();
            collection.Add(update);
        }
        progress("Baixando drivers do Windows Update…");
        dynamic downloader = scope.Add(session.CreateUpdateDownloader()); downloader.Updates = collection;
        dynamic download = scope.Add(downloader.Download());
        if ((int)download.ResultCode != 2)
            return "Download incompleto. Nenhum instalador foi iniciado; consulte o Windows Update e tente novamente.";
        progress("Instalando…");
        dynamic installer = scope.Add(session.CreateUpdateInstaller()); installer.Updates = collection;
        dynamic result = scope.Add(installer.Install());
        var outcomes = new List<Outcome>();
        for (int i = 0; i < selected.Count; i++)
        {
            dynamic item = scope.Add(result.GetUpdateResult(i));
            outcomes.Add(new Outcome(selected[i].Title, (int)item.ResultCode, (int)item.HResult));
        }
        return Summarize(outcomes, (bool)result.RebootRequired);
    });

    internal static string Summarize(IReadOnlyList<Outcome> outcomes, bool reboot)
    {
        int ok = outcomes.Count(x => x.Code == 2 && x.Error >= 0);
        string text = ok == outcomes.Count ? $"Instalação concluída: {ok} driver(s)." : $"Instalação parcial: {ok} de {outcomes.Count} driver(s).";
        foreach (var failed in outcomes.Where(x => x.Code != 2 || x.Error < 0)) text += $"\n{failed.Title}: código {failed.Code}, HRESULT 0x{failed.Error:X8}.";
        return text + (reboot ? "\nReinicie o PC para finalizar." : "");
    }
}

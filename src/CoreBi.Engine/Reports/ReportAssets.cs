namespace CoreBi.Engine.Reports;

internal static class ReportAssets
{
    public static readonly string Css = Load("report.css");
    public static readonly string Script = Load("report.js");

    private static string Load(string file)
    {
        using var stream = typeof(ReportAssets).Assembly.GetManifestResourceStream($"CoreBi.Reports.{file}")
            ?? throw new InvalidOperationException($"Recurso de relatório incorporado não encontrado: {file}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

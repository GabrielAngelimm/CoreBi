namespace CoreBi.Engine.Monitoring;

public class InfoNode
{
    public string Header { get; set; } = "";
    public string Value { get; set; } = "";
    public List<InfoNode> Children { get; } = new();

    public InfoNode Add(string header, string value = "")
    {
        var n = new InfoNode { Header = header, Value = value };
        Children.Add(n);
        return n;
    }
}

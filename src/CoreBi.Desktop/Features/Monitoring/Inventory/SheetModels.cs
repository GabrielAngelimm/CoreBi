using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;


/// <summary>Botão do índice: ícone e categoria. Nome e especificação do componente aparecem na dica e na ficha.</summary>
public sealed record IndexItem(string Key, string Glyph, string Category, string Name, string Spec)
{
    public string Tip => Spec.Length > 0 ? $"{Name} · {Spec}" : Name;

    /// <summary>Canal do componente, o mesmo dos gráficos: pinta o ladrilho do botão e a luz da placa (Steel = aço, neutro).</summary>
    public string Channel => Key switch
    {
        SystemSheets.Cpu => "Cpu",
        SystemSheets.Gpu => "Gpu",
        SystemSheets.Ram => "Memory",
        SystemSheets.Storage => "Storage",
        SystemSheets.Board => "Board",
        _ => "Steel",
    };
}

/// <summary>Leitura grande da placa. First tira o fio e o recuo da primeira.</summary>
public sealed record Readout(string Label, string Value, string Unit = "", string? Tip = null)
{
    public bool First { get; init; }
    /// <summary>A unidade com o espaço antes dela, para ir no mesmo texto do número; vazia sem unidade.</summary>
    public string UnitSuffix => Unit.Length > 0 ? " " + Unit : "";
}

/// <summary>
/// Linha chave/valor. Note aparece em cinza depois do valor ("há 1 ano"); Tone é o veredito da linha (a página é só texto:
/// ele não acende nada; o relatório leva a explicação das âmbar); Help é a explicação do valor. Na tela, a explicação vai
/// para a dica da linha, junto com Tip; só a das linhas âmbar, que diz o que fazer, fica embaixo do valor. Clicar copia o valor.
/// </summary>
public sealed record SpecRow(string Key, string Value, string? Note = null, string? Tip = null, Tone Tone = Tone.None, string? Help = null)
{
    /// <summary>Uma célula por coluna do grupo (módulos de memória lado a lado); Value guarda o texto para copiar.</summary>
    public List<string>? Cells { get; init; }

    /// <summary>Dica da linha: a explicação (quando não é a ação de uma linha âmbar) e o detalhe técnico.</summary>
    public string? Hint => string.Join("\n", new[] { Action == null ? Help : null, Tip }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } hint ? hint : null;

    /// <summary>O que fazer, embaixo do valor: só nas linhas âmbar (o relatório leva o mesmo texto).</summary>
    public string? Action => Tone == Tone.Warn && !string.IsNullOrEmpty(Help) ? Help : null;
}

/// <summary>Grupo de linhas; Help é uma frase sobre o assunto, que vira a dica do título. Tokens: lista em linha (instruções do processador).</summary>
public sealed record SpecGroup(string Title, List<SpecRow> Rows, List<SpecToken>? Tokens = null, string? Help = null)
{
    /// <summary>Cabeçalho das colunas quando as linhas têm Cells.</summary>
    public List<string>? Columns { get; init; }

    /// <summary>Coluna fixa no layout de três colunas; -1 deixa o grupo ir para a mais curta.</summary>
    public int Column { get; init; } = -1;
}

public sealed record SpecToken(string Text, bool On);

public sealed record LinkItem(string Text, string Page);

public sealed class Sheet
{
    public string Key { get; init; } = "";
    public string Eyebrow { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<Readout> Readouts { get; init; } = new();
    public List<SpecGroup> Groups { get; init; } = new();
    public LinkItem? Link { get; init; }
    public List<DiskView>? Disks { get; init; }
}

// Armazenamento: um disco por linha (DataTemplate em SystemView.xaml)

public sealed record VolumeView(string Letter, string Name, string Line, double Weight, double Used, string Tip);
public sealed record DiskView(string Title, string Meta, string Size, string Unit, string Free, List<VolumeView> Volumes,
    string Health = "", Tone Tone = Tone.None)
{
    public bool First { get; init; }
    /// <summary>A unidade com o espaço antes dela, para ir no mesmo texto do tamanho; vazia sem unidade.</summary>
    public string UnitSuffix => Unit.Length > 0 ? " " + Unit : "";
}

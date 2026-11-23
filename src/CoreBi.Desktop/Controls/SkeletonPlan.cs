using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Planta de esqueleto (<c>Skeleton.Layout</c>): uma descrição curta, linha a linha, do que o painel mostra quando tem dados,
/// para o esqueleto ter as mesmas linhas, colunas e alturas do conteúdo e a troca não pular. Comandos separados por ";":
/// <list type="bullet">
/// <item><c>gap 16</c>: desce 16 px;</item>
/// <item><c>row 36: s *, n 80&gt;</c>: uma linha de 36 px com as células dadas;</item>
/// <item><c>rows 4 36: ...</c>: quatro linhas iguais; <c>fill 36: ...</c>: linhas até o fim do painel; <c>lines</c> no lugar de
/// <c>rows</c>/<c>fill</c> põe o fio entre elas, como numa tabela;</item>
/// <item><c>foot 30: ...</c>: linhas presas ao pé do painel, empilhadas de baixo para cima; <c>footline</c> e <c>footgap 12</c>
/// põem um fio ou um vão nessa pilha; <c>center 260: ...</c>: uma linha no meio do que sobra; <c>rest: ...</c>: uma linha com
/// toda a altura que sobra;</item>
/// <item><c>line</c>: um fio de 1 px na largura toda; <c>spread 6: ...</c>: seis linhas que dividem a altura que sobra, com o fio
/// em cima de cada uma; <c>share 7: ...</c>: o mesmo, sem o fio; <c>spaced 4 14: ...</c>: quatro linhas de 14 px com a primeira no topo e a última no pé (eixo de gráfico); <c>bands 4</c> … <c>endbands</c>: o trecho entre eles
/// repetido em quatro faixas iguais do que sobra, centrado em cada uma, com o fio entre elas;</item>
/// <item><c>pad 16 13 14 14</c>, só como primeiro comando: recua a área (esquerda, topo, direita, base);</item>
/// <item><c>col 0 50%</c> … <c>col 50% 50%</c> … <c>end</c>: colunas lado a lado; cada <c>col</c> volta ao topo do bloco e
/// <c>end</c> continua embaixo da mais alta.</item>
/// </list>
/// Célula: tipo e largura (px, % da área ou * para dividir a sobra), com <c>@0.6</c> para a barra ocupar 60 % da célula e
/// <c>&gt;</c> para encostá-la à direita, <c>~</c> para centrá-la e <c>^20</c> para dar 20 px de altura à barra. Tipos: <c>t</c> texto, <c>c</c> legenda, <c>s</c> texto forte, <c>h</c> título,
/// <c>n</c> número de mostrador, <c>box</c> área cheia (gráfico, instrumento), <c>btn</c> botão, <c>pill</c> filtro redondo, <c>dot</c> círculo, <c>ring</c> aro de mostrador, <c>arc</c> mostrador de 3/4 de volta, <c>frame</c> contorno de gráfico,
/// <c>-</c> vão. Barras de texto em célula * variam de largura linha a linha, como dados.
/// </summary>
public static class SkeletonPlan
{
    private static readonly double[] Jitter = { 0.62, 0.84, 0.48, 0.74, 0.56, 0.80, 0.42, 0.70, 0.52, 0.66 };

    private readonly record struct Cell(string Kind, double Px, double Percent, double Star, double Fill, bool Right, double Tall, bool Center);

    public static void Draw(DrawingContext dc, string plan, Rect area, Brush bar, Brush strong)
    {
        double x = area.X, width = area.Width, y = area.Y;
        if (plan.TrimStart().StartsWith("pad ", StringComparison.Ordinal))
        {
            // Recuo da área (L T R B), só como primeiro comando: o de dentro de um cartão
            string first = plan.Split(';', 2)[0];
            var v = first.Trim()[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Num).ToArray();
            area = new Rect(area.X + v[0], area.Y + v[1], Math.Max(0, area.Width - v[0] - v[2]), Math.Max(0, area.Height - v[1] - v[3]));
            plan = plan.Contains(';') ? plan.Split(';', 2)[1] : "";
            x = area.X; width = area.Width; y = area.Y;
        }
        double blockTop = y, blockBottom = y, bottom = area.Bottom;
        int line = 0, bands = 0;
        List<string>? band = null;
        foreach (string raw in plan.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string cmd = raw.Trim();
            if (cmd.Length == 0) continue;
            if (band != null)
            {
                // Faixas: o trecho até endbands se repete em N faixas iguais da altura que sobra, centrado em cada uma, com o fio
                // entre elas (leituras empilhadas, como o odômetro de Armazenamento na janela larga)
                if (cmd != "endbands") { band.Add(cmd); continue; }
                string inner = string.Join(";", band);
                double ih = Height(inner), each = Math.Max(0, bottom - y) / bands;
                for (int i = 0; i < bands; i++)
                {
                    double top = y + i * each;
                    if (i > 0) dc.DrawRectangle(bar, null, new Rect(x, Math.Round(top), width, 1));
                    Draw(dc, inner, new Rect(x, top + Math.Max(0, (each - ih) / 2), width, ih + 1), bar, strong);
                }
                band = null;
                y = bottom;
                continue;
            }
            string[] head = cmd.Split(':', 2);
            string[] words = head[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Uma coluna que chegou ao pé não encerra a planta: col e end ainda levam às colunas seguintes
            if (y >= bottom && words[0] is not ("col" or "end" or "bands")) continue;
            switch (words[0])
            {
                case "gap":
                    y += Num(words[1]);
                    continue;
                case "line":
                    dc.DrawRectangle(bar, null, new Rect(x, Math.Round(y), width, 1));
                    y += 1;
                    continue;
                case "footgap":
                    bottom -= Num(words[1]);
                    continue;
                case "bands":
                    bands = Math.Max(1, (int)Num(words[1]));
                    band = new List<string>();
                    continue;
                case "footline":
                    bottom -= 1;
                    dc.DrawRectangle(bar, null, new Rect(area.X, Math.Round(bottom), area.Width, 1));
                    continue;
                case "col":
                    if (x == area.X && width == area.Width) { blockTop = y; blockBottom = y; }
                    blockBottom = Math.Max(blockBottom, y);
                    y = blockTop;
                    x = area.X + Length(words[1], area.Width);
                    width = words.Length > 2 ? Length(words[2], area.Width) : area.Right - x;
                    continue;
                case "end":
                    y = Math.Max(blockBottom, y);
                    x = area.X;
                    width = area.Width;
                    continue;
            }
            if (head.Length < 2) continue;
            var cells = Cells(head[1]);
            switch (words[0])
            {
                case "foot":
                    {
                        // Linhas presas ao pé, empilhadas de baixo para cima (rodapés e leituras fixas no fim do painel)
                        double fh = Num(words[1]);
                        bottom -= fh;
                        Row(dc, cells, area.X, bottom, area.Width, fh, line++, bar, strong);
                        continue;
                    }
                case "center":
                    {
                        // Uma linha no meio do espaço que sobra entre o que veio de cima e os rodapés (o mostrador de Vida útil)
                        double ch = Num(words[1]);
                        double cy = y + Math.Max(0, bottom - y - ch) / 2;
                        Row(dc, cells, x, cy, width, ch, line++, bar, strong);
                        y = cy + ch;
                        continue;
                    }
                case "rest":
                    Row(dc, cells, x, y, width, Math.Max(0, bottom - y), line++, bar, strong);
                    y = bottom;
                    continue;
                case "spaced" when words.Length >= 3:
                    {
                        // N linhas de H px com o meio da primeira no topo e o da última no pé do que sobra (rótulos de eixo de gráfico)
                        int n = Math.Max(2, (int)Num(words[1]));
                        double sh = Num(words[2]), step = Math.Max(0, bottom - y) / (n - 1);
                        for (int i = 0; i < n; i++, line++) Row(dc, cells, x, y + i * step - sh / 2, width, sh, line, bar, strong);
                        y = bottom;
                        continue;
                    }
                case "spread" or "share":
                    {
                        // N linhas que dividem a altura que sobra; spread põe o fio em cima de cada uma (lista espalhada, como a de
                        // Conexão), share não (barras que se esticam, como os dias do tempo ligado)
                        int n = Math.Max(1, (int)Num(words[1]));
                        double each = Math.Max(0, bottom - y) / n;
                        for (int i = 0; i < n; i++, line++)
                        {
                            if (words[0] == "spread") dc.DrawRectangle(bar, null, new Rect(x, Math.Round(y), width, 1));
                            Row(dc, cells, x, y, width, each, line, bar, strong);
                            y += each;
                        }
                        continue;
                    }
            }
            bool rules = words[0] is "lines";
            int count; double h;
            if (words[0] == "row") { count = 1; h = Num(words[1]); }
            else if (words.Length >= 3) { count = (int)Num(words[1]); h = Num(words[2]); }
            else { count = int.MaxValue; h = Num(words[1]); } // fill: até o fim
            // Linhas fixas só entram inteiras; as que enchem o resto entram enquanto o meio da linha aparece (a lista corta a
            // última pela metade, e o esqueleto também)
            bool fill = count == int.MaxValue;
            for (int i = 0; i < count && (fill ? y + h / 2 <= bottom : y + h <= bottom + 0.5); i++, line++)
            {
                if (rules && i > 0) dc.DrawRectangle(bar, null, new Rect(x, Math.Round(y), width, 1));
                Row(dc, cells, x, y, width, h, line, bar, strong);
                y += h;
            }
        }
    }

    private static void Row(DrawingContext dc, List<Cell> cells, double x, double y, double width, double h, int line, Brush bar, Brush strong)
    {
        double fixedW = 0, stars = 0;
        foreach (var c in cells)
        {
            fixedW += c.Px + c.Percent * width;
            stars += c.Star;
        }
        double unit = stars > 0 ? Math.Max(0, width - fixedW) / stars : 0;
        double cx = x;
        int k = 0;
        foreach (var c in cells)
        {
            double cw = c.Px + c.Percent * width + c.Star * unit;
            Bar(dc, c, new Rect(cx, y, cw, h), line * 3 + k++, bar, strong);
            cx += cw;
        }
    }

    private static void Bar(DrawingContext dc, Cell c, Rect cell, int seed, Brush bar, Brush strong)
    {
        if (c.Kind == "-" || cell.Width < 2) return;
        double mid = cell.Y + cell.Height / 2;
        double fill = c.Fill > 0 ? c.Fill : c.Kind is "t" or "c" or "s" ? (c.Star > 0 ? Jitter[seed % Jitter.Length] : 0.55 + 0.45 * Jitter[seed % Jitter.Length]) : 1;
        double w = Math.Max(8, cell.Width * fill);
        double left = c.Right ? cell.Right - w : c.Center ? cell.X + (cell.Width - w) / 2 : cell.X;
        switch (c.Kind)
        {
            case "box":
                {
                    double th = c.Tall > 0 ? c.Tall : cell.Height;
                    dc.DrawRoundedRectangle(bar, null, new Rect(left, mid - th / 2, w, th), 12, 12);
                    break;
                }
            case "pill":
                {
                    double ph = c.Tall > 0 ? c.Tall : Math.Min(cell.Height, 30);
                    dc.DrawRoundedRectangle(bar, null, new Rect(left, mid - ph / 2, w, ph), ph / 2, ph / 2);
                    break;
                }
            case "btn":
                {
                    double bh = c.Tall > 0 ? c.Tall : Math.Min(cell.Height, 40);
                    dc.DrawRoundedRectangle(bar, null, new Rect(left, mid - bh / 2, w, bh), 12, 12);
                    break;
                }
            case "arc":
                {
                    // Mostrador de 3/4 de volta, aberto embaixo, como o de Vida útil
                    double r = Math.Min(w, cell.Height) / 2 - 7;
                    if (r <= 0) break;
                    var center = new Point(left + w / 2, mid + r * 0.12);
                    Point At(double deg) => new(center.X + r * Math.Cos(deg * Math.PI / 180), center.Y + r * Math.Sin(deg * Math.PI / 180));
                    var arc = new StreamGeometry();
                    using (var g = arc.Open())
                    {
                        g.BeginFigure(At(135), false, false);
                        g.ArcTo(At(45), new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
                    }
                    arc.Freeze();
                    dc.DrawGeometry(null, new Pen(bar, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
                    break;
                }
            case "ring":
                {
                    // Mostrador redondo: só o aro, grosso como o arco dos instrumentos
                    double r = Math.Min(w, cell.Height) / 2 - 7;
                    if (r > 0) dc.DrawEllipse(null, new Pen(bar, 14), new Point(left + w / 2, mid), r, r);
                    break;
                }
            case "frame":
                {
                    // Área de gráfico: o contorno, sem encher (um bloco cheio desse tamanho pesaria mais que o gráfico)
                    double th = c.Tall > 0 ? c.Tall : cell.Height;
                    dc.DrawRoundedRectangle(null, new Pen(bar, 1), new Rect(left + 0.5, mid - th / 2 + 0.5, Math.Max(0, w - 1), Math.Max(0, th - 1)), 6, 6);
                    break;
                }
            case "dot":
                {
                    double r = Math.Min(cell.Width, cell.Height) / 2;
                    dc.DrawEllipse(bar, null, new Point(c.Right ? cell.Right - r : cell.X + r, mid), r, r);
                    break;
                }
            default:
                {
                    double bh = c.Tall > 0 ? c.Tall : c.Kind switch { "c" => 8, "s" => 12, "h" => 16, "n" => 22, _ => 10 };
                    var brush = c.Kind is "s" or "h" or "n" ? strong : bar;
                    dc.DrawRoundedRectangle(brush, null, new Rect(left, mid - bh / 2, w, bh), bh / 2, bh / 2);
                    break;
                }
        }
    }

    /// <summary>Altura que a planta ocupa (linhas que enchem o resto, <c>fill</c>, não contam).</summary>
    public static double Height(string plan)
    {
        double y = 0, blockTop = 0, blockBottom = 0;
        bool inBlock = false, inBands = false;
        foreach (string raw in plan.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string cmd = raw.Trim();
            if (cmd.Length == 0) continue;
            // Faixas enchem o que sobra, como fill: não contam na altura
            if (cmd.StartsWith("bands ", StringComparison.Ordinal)) { inBands = true; continue; }
            if (inBands) { inBands = cmd != "endbands"; continue; }
            string[] words = cmd.Split(':', 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "gap": y += Num(words[1]); break;
                case "pad" when words.Length >= 5: y += Num(words[2]) + Num(words[4]); break;
                case "line": y += 1; break;
                case "row" or "foot" or "center": y += Num(words[1]); break;
                case "footgap": y += Num(words[1]); break;
                case "footline": y += 1; break;
                case "rows" or "lines" when words.Length >= 3: y += Num(words[1]) * Num(words[2]); break;
                case "col":
                    if (!inBlock) { blockTop = y; blockBottom = y; inBlock = true; }
                    blockBottom = Math.Max(blockBottom, y);
                    y = blockTop;
                    break;
                case "end":
                    y = Math.Max(blockBottom, y);
                    inBlock = false;
                    break;
            }
        }
        return y;
    }

    private static List<Cell> Cells(string spec)
    {
        var list = new List<Cell>();
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] p = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            string size = p.Length > 1 ? p[1] : "*";
            bool right = size.EndsWith('>'), center = size.EndsWith('~');
            size = size.TrimEnd('>', '~');
            double fill = 0, tall = 0;
            int up = size.IndexOf('^');
            if (up >= 0) { tall = Num(size[(up + 1)..]); size = size[..up]; }
            int at = size.IndexOf('@');
            if (at >= 0) { fill = Num(size[(at + 1)..]); size = size[..at]; }
            double px = 0, pct = 0, star = 0;
            if (size.EndsWith('*')) star = size.Length > 1 ? Num(size[..^1]) : 1;
            else if (size.EndsWith('%')) pct = Num(size[..^1]) / 100;
            else px = Num(size);
            list.Add(new Cell(p[0], px, pct, star, fill, right, tall, center));
        }
        return list;
    }

    private static double Length(string s, double total) => s.EndsWith('%') ? Num(s[..^1]) / 100 * total : Num(s);

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}

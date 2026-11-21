using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>Projeção de bateria e energia em cartões de relatório a partir de PowerStatus e PowerPlans.</summary>
public static class BatteryInfo
{
    public static List<HealthCard> Read(IEnumerable<SensorReading>? sensors = null)
    {
        var snapshot = PowerStatus.Read();
        var cards = new List<HealthCard>();

        foreach (var b in snapshot.Batteries)
        {
            var m = new List<KeyValuePair<string, string>>();
            void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) m.Add(new(k, v)); }
            Add("Carga atual", b.Percent != null ? $"{b.Percent}%" : null);
            Add("Estado", b.StatusText);
            if (b.RuntimeMin is int run) Add("Autonomia estimada", $"{run / 60}h {run % 60:00}min");
            Add("Capacidade de projeto", b.DesignWh is double d ? $"{d:0.0} Wh" : null);
            Add("Capacidade máxima atual", b.FullWh is double f ? $"{f:0.0} Wh" : null);
            Add("Desgaste", b.WearPct is double w ? $"{w:0.0}%" : null);
            Add("Ciclos de carga", b.Cycles?.ToString());
            Add("Tensão", b.Volts is double v ? $"{v:0.00} V" : null);
            if (b.RateW is double rate) Add(rate > 0 ? "Potência de carga" : "Consumo atual", $"{Math.Abs(rate):0.0} W");
            Add("Fabricante", b.Manufacturer);
            Add("Modelo", b.Model);
            Add("Química", b.Chemistry);

            var reasons = new List<string> { b.Reason };
            if (b.Percent is < 20 && snapshot.OnAc == false) reasons.Add("Carga baixa e fora da tomada.");

            cards.Add(new HealthCard
            {
                Title = b.Title,
                Meta = (b.Manufacturer ?? "") + (b.Percent != null ? $" · {b.Percent}% de carga" : ""),
                Verdict = b.Verdict,
                Label = b.VerdictText.ToUpperInvariant(),
                Reasons = reasons,
                Metrics = m,
            });
        }

        var planDetail = PowerPlans.Read();
        string? plan = planDetail.Active is { } active ? PowerPlans.DisplayName(active.Id, active.Name) : null;
        bool hasBatteries = snapshot.Batteries.Count > 0;
        var em = new List<KeyValuePair<string, string>>
        {
            new("Fonte de energia", !hasBatteries ? "Tomada (PC de mesa)" : snapshot.OnAc == false ? "Bateria" : "Tomada"),
        };
        if (plan != null) em.Add(new("Plano de energia", plan));
        if (sensors != null)
            foreach (var s in sensors.Where(x => x.Type == SensorType.Power && x.RawValue.GetValueOrDefault() > 0).Take(6))
                em.Add(new($"{s.Hardware} · {s.Name}", s.Value));

        var energyReasons = new List<string>
        {
            plan != null ? $"Plano de energia ativo: {plan}." : "Plano de energia não identificado."
        };
        if (planDetail.Active != null && !PowerPlans.IsHighPerformance(planDetail.Active))
            energyReasons.Add("Para jogos e benchmarks, o plano Alto desempenho evita que o PC reduza o clock.");
        if (!hasBatteries)
            energyReasons.Add("O consumo da fonte (PSU) não é medido pelo Windows; os valores abaixo são dos sensores de CPU e GPU.");

        cards.Add(new HealthCard
        {
            Title = "Energia",
            Meta = !hasBatteries ? "Nenhuma bateria detectada" : "Fonte, plano e consumo",
            Verdict = HealthVerdict.Good,
            Label = "EM USO",
            Reasons = energyReasons,
            Metrics = em,
        });
        return cards;
    }
}

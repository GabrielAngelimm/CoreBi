using System.Management;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Engine.Monitoring.Graphics;

public sealed class GpuInfo
{
    public string Name { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public string Vendor { get; init; } = "";
    public List<InfoNode> Sections { get; } = new();
    public ulong DedicatedVramBytes { get; internal set; }
    public bool HasVendorTelemetry => NvmlHandle != IntPtr.Zero;
    public float? WindowsLoad { get; internal set; }

    internal IntPtr NvmlHandle { get; set; }
    internal ulong? AdapterLuid { get; set; }

    public List<KeyValuePair<string, string>> LiveExtras()
    {
        var list = new List<KeyValuePair<string, string>>();
        var h = NvmlHandle;
        if (h == IntPtr.Zero) return list;

        if (Nvml.PState(h) is int p) list.Add(new("Estado de desempenho", $"P{p}"));
        if (Nvml.ThrottleReasons(h) is ulong r) list.Add(new("PerfCap (limitador)", PerfCap(r)));
        if (Nvml.PowerUsageMw(h) is uint pw && Nvml.PowerLimitMw(h) is uint lim && lim > 0)
            list.Add(new("Consumo / limite", $"{pw / 1000.0:0.0} W · {100.0 * pw / lim:0} %"));
        if (Nvml.Util(h) is Nvml.Utilization u) list.Add(new("Controlador de memória", $"{u.Memory} %"));
        if (Nvml.EncoderUtil(h) is uint enc) list.Add(new("Encoder (NVENC)", $"{enc} %"));
        if (Nvml.DecoderUtil(h) is uint dec) list.Add(new("Decoder (NVDEC)", $"{dec} %"));
        if (Nvml.CurPcieGen(h) is uint g && Nvml.CurPcieWidth(h) is uint w) list.Add(new("PCIe em uso", $"{PcieName(g)} x{w}"));
        return list;
    }

    private static string PerfCap(ulong r)
    {
        var parts = new List<string>();
        if ((r & 0x4) != 0) parts.Add("Energia");
        if ((r & Nvml.ThermalSlowdownMask) != 0) parts.Add("Térmico");
        if ((r & 0x8) != 0) parts.Add("HW slowdown");
        if ((r & 0x80) != 0) parts.Add("Power brake");
        if (parts.Count == 0) return (r & 0x1) != 0 ? "Ociosa" : "Nenhum";
        return string.Join(" + ", parts);
    }

    internal static string PcieName(uint gen) => gen switch
    {
        1 => "PCIe 1.1",
        2 => "PCIe 2.0",
        3 => "PCIe 3.0",
        4 => "PCIe 4.0",
        5 => "PCIe 5.0",
        6 => "PCIe 6.0",
        _ => $"PCIe gen {gen}"
    };
}

public static class GpuInfoCollector
{
    private static readonly Regex PciIdRegex = new(
        @"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{4})([0-9A-F]{4}))?(?:&REV_([0-9A-F]{2}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<GpuInfo> Collect()
    {
        var result = new List<GpuInfo>();
        var adapters = Dxgi.Enumerate();
        var nvDevices = Nvml.Devices();

        WmiQuery.ForEach("SELECT * FROM Win32_VideoController", vc =>
        {
            try { result.Add(Build(vc, adapters, nvDevices)); }
            catch (Exception ex)
            {
                DiagnosticLog.Write("gpu.collect", ex);
                var g = new GpuInfo { Name = WmiQuery.GetString(vc, "Name") ?? "GPU" };
                var err = new InfoNode { Header = "ERRO" };
                err.Add("Falha ao ler", ex.Message);
                g.Sections.Add(err);
                result.Add(g);
            }
        }, diagnosticOp: "gpu.wmi");

        // Placa dedicada primeiro (mais VRAM).
        return result.OrderByDescending(g => g.DedicatedVramBytes).ToList();
    }

    private static GpuInfo Build(ManagementBaseObject vc, List<Dxgi.Adapter> adapters, List<(IntPtr Handle, Nvml.PciInfo Pci)> nvDevices)
    {
        string name = WmiQuery.GetString(vc, "Name") ?? "GPU";
        string pnp = WmiQuery.GetString(vc, "PNPDeviceID") ?? "";
        var m = PciIdRegex.Match(pnp);
        string ven = m.Groups[1].Value.ToUpperInvariant(), dev = m.Groups[2].Value.ToUpperInvariant();
        string subDev = m.Groups[3].Value.ToUpperInvariant(), subVen = m.Groups[4].Value.ToUpperInvariant();
        string rev = m.Groups[5].Value.ToUpperInvariant();

        string vendor = ven switch { "10DE" => "NVIDIA", "1002" => "AMD", "8086" => "Intel", _ => ven };
        var spec = GpuSpecDb.Find(ven, dev, name);
        var matchingDxgi = adapters.Where(a => a.VendorId.ToString("X4") == ven && a.DeviceId.ToString("X4") == dev).ToList();
        var dx = matchingDxgi.FirstOrDefault();

        IntPtr h = IntPtr.Zero;
        if (ven == "10DE" && uint.TryParse(dev, System.Globalization.NumberStyles.HexNumber, null, out uint devNum))
        {
            uint pciId = (devNum << 16) | 0x10DE;
            var matchingNv = nvDevices.Where(d => d.Pci.PciDeviceId == pciId).ToList();
            if (matchingNv.Count == 1) h = matchingNv[0].Handle;
            else if (matchingNv.Count > 1 && dx != null) h = matchingNv[0].Handle;
        }

        var pnpProps = PnpProperties(pnp);
        var reg = GpuRegistryReader.ReadDriverHardwareInfo(pnpProps.GetValueOrDefault("DEVPKEY_Device_Driver") as string);

        var gpu = new GpuInfo
        {
            Name = name,
            Vendor = vendor,
            NvmlHandle = h,
            AdapterLuid = matchingDxgi.Count == 1 ? dx?.Luid : null
        };

        var id = Section(gpu, "IDENTIFICAÇÃO");
        id.Add("Nome", name);
        id.Add("Chip", spec?.Chip ?? "—");
        id.Add("Arquitetura", spec?.Arch ?? "—");
        id.Add("Revisão", rev.Length > 0 ? rev : "—");
        id.Add("Fabricante da placa", subVen.Length > 0 ? GpuSpecDb.SubvendorName(subVen) : "—");
        id.Add("Device ID", $"{ven} {dev} · {subVen} {subDev}".Trim(' ', '·'));
        id.Add("Processo", spec?.Process ?? "—");
        id.Add("Área do die", spec != null ? $"{spec.DieMm2} mm²" : "—");
        id.Add("Transistores", spec != null ? $"{spec.TransistorsBi:0.#} bilhões" : "—");
        id.Add("Lançamento", spec?.Release ?? "—");

        uint? cores = h != IntPtr.Zero ? Nvml.Cores(h) : null;
        int shaders = (int)(cores ?? (uint)(spec?.Shaders ?? 0));
        var units = Section(gpu, "UNIDADES DE PROCESSAMENTO");
        if (shaders > 0)
        {
            if (ven == "10DE")
            {
                const int perSm = 128;
                units.Add("CUDA cores", $"{shaders:N0}");
                units.Add("SMs", $"{shaders / perSm}");
                if (spec != null && spec.Arch is "Blackwell" or "Ada Lovelace" or "Ampere")
                {
                    units.Add("Tensor cores", $"{shaders / perSm * 4}");
                    units.Add("RT cores", $"{shaders / perSm}");
                }
            }
            else if (ven == "1002")
            {
                const int perCu = 64;
                units.Add("Stream processors", $"{shaders:N0}");
                units.Add("Compute units", $"{shaders / perCu}");
                if (spec != null && spec.Arch.StartsWith("RDNA", StringComparison.Ordinal))
                    units.Add("Ray accelerators", $"{shaders / perCu}");
            }
            else
            {
                units.Add("Shaders", $"{shaders:N0}");
            }
        }
        if (spec != null)
        {
            units.Add("ROPs / TMUs", $"{spec.Rops} / {spec.Tmus}");
            units.Add("Pixel fillrate", $"{spec.Rops * spec.BoostMHz / 1000.0:0.0} GPixel/s");
            units.Add("Texture fillrate", $"{spec.Tmus * spec.BoostMHz / 1000.0:0.0} GTexel/s");
            units.Add("FP32 (teórico)", $"{(shaders > 0 ? shaders : spec.Shaders) * 2.0 * spec.BoostMHz / 1e6:0.00} TFLOPS");
        }
        if (units.Children.Count == 0) units.Add("Sem dados", "GPU fora do banco de especificações");

        var mem = Section(gpu, "MEMÓRIA");
        ulong vram = 0;
        if (h != IntPtr.Zero && Nvml.MemoryInfo(h) is Nvml.Memory nm) vram = nm.Total;
        if (vram == 0 && dx != null) vram = dx.DedicatedVideo;
        if (vram == 0) vram = GpuRegistryReader.ReadMemorySizeBytes(reg);
        gpu.DedicatedVramBytes = vram;

        bool igpu = spec?.IsIntegrated == true;
        if (igpu || vram < 1UL << 30) mem.Add(igpu ? "Reservada (dedicada)" : "Tamanho", ByteFormat.Binary(vram));
        else mem.Add("Tamanho", $"{ByteFormat.CommercialVramGb(vram):0} GB ({vram >> 20:N0} MB utilizáveis)");
        if (dx != null && dx.SharedSystem > 0) mem.Add("Compartilhada do sistema", ByteFormat.Binary(dx.SharedSystem));
        mem.Add("Tipo", spec?.MemType ?? "—");

        uint? bus = h != IntPtr.Zero ? Nvml.BusWidth(h) : null;
        uint? memClock = h != IntPtr.Zero ? Nvml.MaxClock(h, Nvml.ClockMem) : null;
        if (bus is uint b) mem.Add("Barramento", $"{b} bits");
        if (memClock is uint mc)
        {
            // A NVML reporta metade da taxa efetiva (GDDR6/6X/7): 14001 MHz → 28 Gbps.
            double gbps = mc * 2 / 1000.0;
            mem.Add("Clock da memória", $"{mc} MHz ({gbps:0.0} Gbps efetivos)");
            if (bus is uint bw) mem.Add("Largura de banda", $"{gbps * bw / 8:0.0} GB/s");
        }
        if (h != IntPtr.Zero && Nvml.Bar1(h) is Nvml.Memory bar)
            mem.Add("Resizable BAR", bar.Total > 256UL * 1024 * 1024 ? $"Ativado (BAR1 {ByteFormat.Binary(bar.Total)})" : "Desativado");

        var clk = Section(gpu, "CLOCKS");
        if (spec != null)
        {
            clk.Add("Base (referência)", $"{spec.BaseMHz} MHz");
            clk.Add("Boost (referência)", $"{spec.BoostMHz} MHz");
        }
        if (h != IntPtr.Zero)
        {
            if (Nvml.MaxClock(h, Nvml.ClockGraphics) is uint g) clk.Add("Máximo (curva V/F)", $"{g} MHz");
            if (Nvml.MaxClock(h, Nvml.ClockVideo) is uint v) clk.Add("Video engine máx.", $"{v} MHz");
        }
        if (clk.Children.Count == 0) clk.Add("Sem dados", "—");

        var pcie = Section(gpu, "BARRAMENTO");
        uint? maxGen = h != IntPtr.Zero ? Nvml.MaxPcieGen(h) : null;
        uint? maxW = h != IntPtr.Zero ? Nvml.MaxPcieWidth(h) : null;
        maxGen ??= AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkSpeed"));
        maxW ??= AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkWidth"));
        uint? curGen = h != IntPtr.Zero ? Nvml.CurPcieGen(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkSpeed"));
        uint? curW = h != IntPtr.Zero ? Nvml.CurPcieWidth(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkWidth"));
        if (maxGen is uint mg && maxW is uint mw) pcie.Add("Interface", $"{GpuInfo.PcieName(mg)} x{mw}");
        if (curGen is uint cg && curW is uint cw)
            pcie.Add("Em uso agora", $"{GpuInfo.PcieName(cg)} x{cw}" + (maxGen > curGen ? " (economia de energia em repouso)" : ""));
        if (pnpProps.GetValueOrDefault("DEVPKEY_Device_LocationInfo") is string loc) pcie.Add("Localização", loc);

        if (h != IntPtr.Zero || spec?.TdpW > 0)
        {
            var pwr = Section(gpu, "ENERGIA");
            if (spec?.TdpW > 0) pwr.Add("TDP (referência)", $"{spec.TdpW} W");
            if (h != IntPtr.Zero)
            {
                if (Nvml.PowerLimitMw(h) is uint lim) pwr.Add("Limite atual", $"{lim / 1000.0:0} W");
                if (Nvml.PowerDefaultMw(h) is uint def) pwr.Add("Limite padrão da placa", $"{def / 1000.0:0} W");
                if (Nvml.PowerConstraintsMw(h) is { } pc) pwr.Add("Faixa ajustável", $"{pc.Min / 1000.0:0} – {pc.Max / 1000.0:0} W");
            }
        }

        var drv = Section(gpu, "DRIVER E BIOS");
        string? vbios = (h != IntPtr.Zero ? Nvml.Vbios(h) : null) ?? GpuRegistryReader.ReadString(reg, "HardwareInformation.BiosString");
        drv.Add("BIOS (VBIOS)", vbios ?? "—");
        string wmiVer = WmiQuery.GetString(vc, "DriverVersion") ?? "—";
        string? nvVer = h != IntPtr.Zero ? Nvml.DriverVersion() : null;
        drv.Add("Versão do driver", nvVer != null ? $"{nvVer} ({wmiVer})" : wmiVer);
        if (vc["DriverDate"] is string dd && dd.Length >= 8)
            drv.Add("Data do driver", WmiCalendarDate.Format(dd));
        drv.Add("Assinatura digital", DriverSigner(pnp));
        if (h != IntPtr.Zero)
        {
            if (Nvml.CudaVersion() is string cuda) drv.Add("Versão CUDA", cuda);
            if (Nvml.ComputeCapability(h) is string cc) drv.Add("Compute capability", cc);
        }

        var tech = Section(gpu, "TECNOLOGIAS");
        string? fl = dx?.FeatureLevel;
        tech.Add("DirectX", fl switch
        {
            "12_2" => "DirectX 12 Ultimate (12_2)",
            null => "—",
            _ => $"DirectX 12 ({fl.Replace('_', '.')})"
        });
        var apis = new List<string>();
        if (ven == "10DE" && File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll"))) apis.Add("CUDA");
        if (File.Exists(Path.Combine(Environment.SystemDirectory, "OpenCL.dll"))) apis.Add("OpenCL");
        if (File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"))) apis.Add("Vulkan");
        if (fl != null) { apis.Add("DirectCompute"); apis.Add("DirectML"); }
        if (ven == "10DE") apis.Add("PhysX");
        tech.Add("APIs", apis.Count > 0 ? string.Join(" · ", apis) : "—");
        if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out var feats)) tech.Add("Recursos da arquitetura", feats);

        string memLabel = spec?.MemType.StartsWith("GDDR", StringComparison.Ordinal) == true ? spec.MemType : "";
        gpu.Subtitle = string.Join("  ·  ", new[]
        {
            spec?.Chip, spec?.Arch, spec?.Process,
            vram >= 1UL << 30 && !igpu ? $"{ByteFormat.CommercialVramGb(vram):0} GB {memLabel}".Trim() : null,
            subVen.Length > 0 ? GpuSpecDb.SubvendorName(subVen) : null
        }.Where(s => !string.IsNullOrEmpty(s)));
        return gpu;
    }

    private static InfoNode Section(GpuInfo g, string title)
    {
        var n = new InfoNode { Header = title };
        g.Sections.Add(n);
        return n;
    }

    private static Dictionary<string, object?> PnpProperties(string pnpId)
    {
        var dict = new Dictionary<string, object?>();
        if (pnpId.Length == 0) return dict;
        // Instanciar pelo caminho do objeto falha ("Não encontrado"); via consulta WQL funciona.
        WmiQuery.ForEach($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{pnpId.Replace("\\", "\\\\")}'", item =>
        {
            if (dict.Count > 0 || item is not ManagementObject mo) return;
            using var input = mo.GetMethodParameters("GetDeviceProperties");
            input["devicePropertyKeys"] = new[]
            {
                "DEVPKEY_PciDevice_CurrentLinkSpeed", "DEVPKEY_PciDevice_CurrentLinkWidth",
                "DEVPKEY_PciDevice_MaxLinkSpeed", "DEVPKEY_PciDevice_MaxLinkWidth",
                "DEVPKEY_Device_Driver", "DEVPKEY_Device_LocationInfo"
            };
            using var output = mo.InvokeMethod("GetDeviceProperties", input, null);
            if (output?["deviceProperties"] is ManagementBaseObject[] props)
                foreach (var p in props)
                    using (p)
                        if (p["KeyName"] is string k) dict[k] = p["Data"];
        });
        return dict;
    }

    private static string DriverSigner(string pnpId)
    {
        if (pnpId.Length == 0) return "—";
        string? result = WmiQuery.FirstOrDefault(
            $"SELECT IsSigned, Signer FROM Win32_PnPSignedDriver WHERE DeviceID='{pnpId.Replace("\\", "\\\\")}'",
            d =>
            {
                bool signed = WmiQuery.GetNullableBool(d, "IsSigned") == true;
                string signer = WmiQuery.GetString(d, "Signer") ?? "";
                if (!signed) return "Não assinado";
                return signer.Contains("Windows Hardware Compatibility", StringComparison.OrdinalIgnoreCase)
                    ? "WHQL (Microsoft)" : $"Assinado · {signer}";
            });
        return result ?? "—";
    }

    private static uint? AsUInt(object? v)
    {
        if (v == null) return null;
        try { return Convert.ToUInt32(v); }
        catch { return null; }
    }
}

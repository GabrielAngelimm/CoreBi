using System.Windows.Media;

using System.Windows.Controls;

using System.Windows.Shapes;
namespace CoreBi.Desktop;

// Migration adapter: pages own their XAML; shared telemetry and execution remain coordinated by the shell.
public partial class MainWindow
{
    internal TextBlock DashMachine => PageDash.DashMachine;
    internal TextBlock DashChartLabel => PageDash.DashChartLabel;
    internal TextBlock DashChartDelta => PageDash.DashChartDelta;
    internal TextBlock DashChartValue => PageDash.DashChartValue;
    internal TextBlock DashChartValue2 => PageDash.DashChartValue2;
    internal StackPanel DashRangePanel => PageDash.DashRangePanel;
    internal StackPanel DashMetricPanel => PageDash.DashMetricPanel;
    internal Canvas DashChart => PageDash.DashChart;
    internal TextBlock DashScore => PageDash.DashScore;
    internal TextBlock DashScoreDelta => PageDash.DashScoreDelta;
    internal TextBlock DashScoreSub => PageDash.DashScoreSub;
    internal ItemsControl DashScores => PageDash.DashScores;
    internal ItemsControl DashComponents => PageDash.DashComponents;
    internal Canvas DashRings => PageDash.DashRings;
    internal ItemsControl DashRingLegend => PageDash.DashRingLegend;
    internal TextBlock DashEventsEmpty => PageDash.DashEventsEmpty;
    internal ItemsControl DashEvents => PageDash.DashEvents;
    internal ItemsControl DashEventsAll => PageDash.DashEventsAll;
    internal Button DashEventsMore => PageDash.DashEventsMore;
    internal ItemsControl DashHealth => PageDash.DashHealth;
    internal Border DashHealthBadge => PageDash.DashHealthBadge;
    internal TextBlock DashHealthBadgeText => PageDash.DashHealthBadgeText;
    internal TextBlock SystemSub => PageSystem.SystemSub;
    internal Button BtnReport => PageSystem.BtnReport;
    internal TextBlock StatusText => PageSensors.StatusText;
    internal TextBox SensorSearch => PageSensors.SensorSearch;
    internal WrapPanel GpuChips => PageGpu.GpuChips;
    internal TextBlock GpuName => PageGpu.GpuName;
    internal TextBlock GpuSubtitle => PageGpu.GpuSubtitle;
    internal TextBlock GpuVendor => PageGpu.GpuVendor;
    internal ItemsControl GpuSections => PageGpu.GpuSections;
    internal ItemsControl GpuExtras => PageGpu.GpuExtras;
    internal TextBlock BnCpuName => PageBottleneck.BnCpuName;
    internal TextBlock BnGpuName => PageBottleneck.BnGpuName;
    internal Canvas BnBalanceCanvas => PageBottleneck.BnBalanceCanvas;
    internal Ellipse BnBalanceDot => PageBottleneck.BnBalanceDot;
    internal TextBlock BnBalanceText => PageBottleneck.BnBalanceText;
    internal Border BnLiveCard => PageBottleneck.BnLiveCard;
    internal TextBlock BnLiveTitle => PageBottleneck.BnLiveTitle;
    internal TextBlock BnLiveGame => PageBottleneck.BnLiveGame;
    internal TextBlock BnLiveAdvice => PageBottleneck.BnLiveAdvice;
    internal ItemsControl BnMeters => PageBottleneck.BnMeters;
    internal StackPanel BnDurationPanel => PageBottleneck.BnDurationPanel;
    internal Button BtnBnSession => PageBottleneck.BtnBnSession;
    internal TextBlock BnSessionStatus => PageBottleneck.BnSessionStatus;
    internal StackPanel BnResult => PageBottleneck.BnResult;
    internal TextBlock BnResultTitle => PageBottleneck.BnResultTitle;
    internal ItemsControl BnDistribution => PageBottleneck.BnDistribution;
    internal TextBlock BnResultAdvice => PageBottleneck.BnResultAdvice;
    internal CheckBox ChkOverlay => PageOverlay.ChkOverlay;
    internal Grid OvCornerPanel => PageOverlay.OvCornerPanel;
    internal StackPanel OvScalePanel => PageOverlay.OvScalePanel;
    internal SpreadPanel OvItemsPanel => PageOverlay.OvItemsPanel;
    internal StackPanel OvPresetPanel => PageOverlay.OvPresetPanel;
    internal TextBlock OvStatus => PageOverlay.OvStatus;
    internal System.Windows.Shapes.Ellipse OvStatusDot => PageOverlay.OvStatusDot;
    internal System.Windows.Controls.Primitives.UniformGrid BenchTestPanel => PageBench.BenchTestPanel;
    internal Grid BenchPanelRender => PageBench.BenchPanelRender;
    internal TextBlock BenchDevice => PageBench.BenchDevice;
    internal TextBlock BenchDeviceSpec => PageBench.BenchDeviceSpec;
    internal Image BenchImage => PageBench.BenchImage;
    internal Canvas BenchBuckets => PageBench.BenchBuckets;
    internal TextBlock BenchIdle => PageBench.BenchIdle;
    internal ScaleTransform BenchProgress => PageBench.BenchProgress;
    internal TextBlock BenchStatus => PageBench.BenchStatus;
    internal TextBlock BenchElapsed => PageBench.BenchElapsed;
    internal StackPanel BenchModePanel => PageBench.BenchModePanel;
    internal StackPanel BenchDurationPanel => PageBench.BenchDurationPanel;
    internal Button BtnBench => PageBench.BtnBench;
    internal TextBlock BenchHint => PageBench.BenchHint;
    internal TextBlock BenchMt => PageBench.BenchMt;
    internal TextBlock BenchSt => PageBench.BenchSt;
    internal TextBlock BenchRatio => PageBench.BenchRatio;
    internal TextBlock BenchMtVs => PageBench.BenchMtVs;
    internal TextBlock BenchStVs => PageBench.BenchStVs;
    internal TextBlock BenchRatioSub => PageBench.BenchRatioSub;
    internal ItemsControl BenchLive => PageBench.BenchLive;
    internal ItemsControl BenchHistory => PageBench.BenchHistory;
    internal Grid BenchPanelGpu => PageBench.BenchPanelGpu;
    internal TextBlock GbDevice => PageBench.GbDevice;
    internal TextBlock GbFpsNow => PageBench.GbFpsNow;
    internal TextBlock GbGpuNow => PageBench.GbGpuNow;
    internal RunTrace GbTrace => PageBench.GbTrace;
    internal ScaleTransform GbProgress => PageBench.GbProgress;
    internal TextBlock GbStatus => PageBench.GbStatus;
    internal TextBlock GbDetail => PageBench.GbDetail;
    internal StackPanel GbResPanel => PageBench.GbResPanel;
    internal StackPanel GbDurPanel => PageBench.GbDurPanel;
    internal Button BtnGpuBench => PageBench.BtnGpuBench;
    internal ItemsControl GbResult => PageBench.GbResult;
    internal ItemsControl GbHistory => PageBench.GbHistory;
    internal Grid BenchPanelRt => PageBench.BenchPanelRt;
    internal TextBlock RtDevice => PageBench.RtDevice;
    internal TextBlock RtFpsNow => PageBench.RtFpsNow;
    internal RunTrace RtTrace => PageBench.RtTrace;
    internal ScaleTransform RtProgress => PageBench.RtProgress;
    internal TextBlock RtStatus => PageBench.RtStatus;
    internal TextBlock RtDetail => PageBench.RtDetail;
    internal StackPanel RtResPanel => PageBench.RtResPanel;
    internal StackPanel RtPresetPanel => PageBench.RtPresetPanel;
    internal StackPanel RtModePanel => PageBench.RtModePanel;
    internal Button BtnRtBench => PageBench.BtnRtBench;
    internal ItemsControl RtResult => PageBench.RtResult;
    internal ItemsControl RtHistory => PageBench.RtHistory;
    internal Grid BenchPanelSim => PageBench.BenchPanelSim;
    internal TextBlock SimDevice => PageBench.SimDevice;
    internal TextBlock SimDeviceSpec => PageBench.SimDeviceSpec;
    internal Image SimImage => PageBench.SimImage;
    internal TextBlock SimIdle => PageBench.SimIdle;
    internal ScaleTransform SimProgress => PageBench.SimProgress;
    internal TextBlock SimStatus => PageBench.SimStatus;
    internal TextBlock SimDetail => PageBench.SimDetail;
    internal TextBlock SimFpsNow => PageBench.SimFpsNow;
    internal StackPanel SimDurPanel => PageBench.SimDurPanel;
    internal Button BtnSimBench => PageBench.BtnSimBench;
    internal ItemsControl SimResult => PageBench.SimResult;
    internal ItemsControl SimHistory => PageBench.SimHistory;
    internal Grid BenchPanelMem => PageBench.BenchPanelMem;
    internal TextBlock MemDeviceSpec => PageBench.MemDeviceSpec;
    internal ItemsControl MemResult => PageBench.MemResult;
    internal ScaleTransform MemProgress => PageBench.MemProgress;
    internal TextBlock MemStatus => PageBench.MemStatus;
    internal TextBlock MemDetail => PageBench.MemDetail;
    internal Button BtnMemBench => PageBench.BtnMemBench;
    internal ItemsControl MemHistory => PageBench.MemHistory;
    internal Grid BenchPanelDisk => PageBench.BenchPanelDisk;
    internal TextBlock DiskDevice => PageBench.DiskDevice;
    internal TextBlock DiskDeviceSpec => PageBench.DiskDeviceSpec;
    internal ItemsControl DiskResult => PageBench.DiskResult;
    internal ScaleTransform DiskProgress => PageBench.DiskProgress;
    internal TextBlock DiskStatus => PageBench.DiskStatus;
    internal TextBlock DiskDetail => PageBench.DiskDetail;
    internal WrapPanel DiskDrivePanel => PageBench.DiskDrivePanel;
    internal StackPanel DiskSizePanel => PageBench.DiskSizePanel;
    internal Button BtnDiskBench => PageBench.BtnDiskBench;
    internal ItemsControl DiskHistory => PageBench.DiskHistory;
    internal CheckBox ChkCpu => PageStress.ChkCpu;
    internal CheckBox ChkRam => PageStress.ChkRam;
    internal CheckBox ChkGpu => PageStress.ChkGpu;
    internal StackPanel StressDurationPanel => PageStress.StressDurationPanel;
    internal StackPanel RamFractionPanel => PageStress.RamFractionPanel;
    internal TextBox TempLimit => PageStress.TempLimit;
    internal TextBox GpuTempLimit => PageStress.GpuTempLimit;
    internal StackPanel GpuOptions => PageStress.GpuOptions;
    internal StackPanel GpuResPanel => PageStress.GpuResPanel;
    internal StackPanel GpuMsaaPanel => PageStress.GpuMsaaPanel;
    internal StackPanel GpuIntensityPanel => PageStress.GpuIntensityPanel;
    internal Button BtnStress => PageStress.BtnStress;
    internal TextBlock StressHint => PageStress.StressHint;
    internal Ellipse StLed => PageStress.StLed;
    internal TextBlock StStatus => PageStress.StStatus;
    internal TextBlock StDetail => PageStress.StDetail;
    internal TextBlock StElapsed => PageStress.StElapsed;
    internal ScaleTransform StProgress => PageStress.StProgress;
    internal Border CpuColumn => PageStress.CpuColumn;
    internal Border RamColumn => PageStress.RamColumn;
    internal Border GpuColumn => PageStress.GpuColumn;
    internal TextBlock StTemp => PageStress.StTemp;
    internal TextBlock StTempMax => PageStress.StTempMax;
    internal TextBlock StClock => PageStress.StClock;
    internal TextBlock StPower => PageStress.StPower;
    internal TextBlock StLoad => PageStress.StLoad;
    internal TextBlock StRamErrors => PageStress.StRamErrors;
    internal TextBlock StRam => PageStress.StRam;
    internal TextBlock StRamSize => PageStress.StRamSize;
    internal TextBlock StGpuTemp => PageStress.StGpuTemp;
    internal TextBlock StGpuTempMax => PageStress.StGpuTempMax;
    internal TextBlock StGpuPower => PageStress.StGpuPower;
    internal TextBlock StGpuCap => PageStress.StGpuCap;
    internal TextBlock StFps => PageStress.StFps;
    internal TextBlock StFpsAvg => PageStress.StFpsAvg;
    internal Canvas TempChart => PageStress.TempChart;
    internal ListBox StressLog => PageStress.StressLog;
    /// <summary>Abre uma página pelo nome (Tag do item da navegação), como se o item fosse clicado.</summary>
    /// <summary>Abre uma página do menu; "PageSensors:cpu" (ou :ram, :board) abre Sensores já filtrado no componente.</summary>
    internal void GoToPage(string page)
    {
        int cut = page.IndexOf(':');
        if (cut > 0)
        {
            if (page[..cut] == "PageSensors") PageSensors.ShowComponent(page[(cut + 1)..]);
            page = page[..cut];
        }
        foreach (var child in NavPanel.Children)
            if (child is RadioButton { Tag: string tag } nav && tag == page) nav.IsChecked = true;
    }

    private void ConnectViews()
    {
        PageDash.Owner = this;
        PageSystem.Owner = this;
        PageSystem.Initialize(_features, () => _gpus, GoToPage);
        PageDisks.Initialize(_features, _monitor);
        PageEnergy.Initialize(_features, _monitor, () => _gpus.FirstOrDefault(), MainGpuPower);
        PageNet.Context = _features;
        PageDriver.Context = _features;
        PageSensors.Initialize(_monitor);
        PageGpu.Owner = this;
        PageBottleneck.Owner = this;
        PageOverlay.Owner = this;
        PageBench.Owner = this;
        PageStartup.Context = _features;
        PageApps.Context = _features;
        PageStress.Owner = this;
    }
}


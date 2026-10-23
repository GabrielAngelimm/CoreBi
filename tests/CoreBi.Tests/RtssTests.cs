using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using CoreBi.Desktop;
using CoreBi.Engine.Games;

internal static partial class Program
{
    // Fixture do ABI público, sem RTSS instalado, jogo, privilégios de administrador ou injeção.
    private sealed class RtssFixture : IDisposable
    {
        internal const int App = 128, Slots = App + 512, SmallSize = 4608 + 262144, LargeSize = SmallSize + 32768;
        internal string Name { get; } = $"CoreBi.Tests.RTSS.{Guid.NewGuid():N}";
        internal MemoryMappedFile Mapping { get; }
        internal MemoryMappedViewAccessor View { get; }
        internal int EntrySize { get; }
        internal RtssFixture(bool large = true)
        {
            EntrySize = large ? LargeSize : SmallSize;
            Mapping = MemoryMappedFile.CreateNew(Name, Slots + EntrySize * 4);
            View = Mapping.CreateViewAccessor();
            View.Write(0, 0x52545353u); View.Write(4, large ? 0x00020014u : 0x0002000Eu);
            View.Write(8, 512u); View.Write(12, (uint)App); View.Write(16, 1u);
            View.Write(20, (uint)EntrySize); View.Write(24, (uint)Slots); View.Write(28, 4u);
            View.Write(App, 42u); View.Write(App + 264, 7u); View.Write(App + 272, unchecked((uint)Environment.TickCount));
            View.Write(App + 332, 123u);
            SetOwner(0, "Afterburner"); SetOwner(1, "OtherClient");
        }
        internal int Slot(int index) => Slots + index * EntrySize;
        internal void SetOwner(int index, string owner)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(owner + '\0');
            View.WriteArray(Slot(index) + 256, bytes, 0, bytes.Length);
        }
        internal string ReadString(int offset, int capacity)
        {
            var bytes = new byte[capacity]; View.ReadArray(offset, bytes, 0, bytes.Length);
            int end = Array.IndexOf(bytes, (byte)0);
            return Encoding.Latin1.GetString(bytes, 0, end < 0 ? bytes.Length : end);
        }
        public void Dispose() { View.Dispose(); Mapping.Dispose(); }
    }

    private static void RtssTests()
    {
        Check("RTSS: own slot, atomic content, foreign clients and cleanup", () =>
        {
            using var fixture = new RtssFixture();
            using var client = new RtssOverlayClient(fixture.Name);
            Require(client.Inspect(42, out var target) == RtssStatus.Ready && target.Active && target.RenderedFrame == 123,
                "Active hooked application was not recognized using SDK offsets");
            var content = new RtssContent("<C=27C9BC>144 FPS", [1, 2, 3, 4]);
            Require(client.Publish(content) == RtssStatus.Ready, "Publish failed");
            int slot = fixture.Slot(2);
            string owner = fixture.ReadString(slot + 256, 256);
            Require(owner.StartsWith("CoreBi:") && fixture.ReadString(slot + RtssFixture.SmallSize, 32768) == content.Text,
                "v2.20 must publish extended text in its owned slot");
            Require(fixture.View.ReadByte(slot + 4608) == 1 && fixture.View.ReadUInt32(36) == 0
                && fixture.View.ReadUInt32(32) == client.PublishedFrame, "Objects, lock or frame notification failed");
            Require(client.Publish(content with { Text = "updated" }) == RtssStatus.Ready
                && fixture.ReadString(slot + 256, 256) == owner && fixture.ReadString(fixture.Slot(3) + 256, 256) == "",
                "A data update claimed another OSD slot");
            client.Dispose();
            Require(fixture.View.ReadByte(slot + 256) == 0 && fixture.View.ReadByte(slot + RtssFixture.SmallSize) == 0,
                "Shutdown left owned text visible");
            Require(fixture.ReadString(fixture.Slot(0) + 256, 256) == "Afterburner"
                && fixture.ReadString(fixture.Slot(1) + 256, 256) == "OtherClient", "Another OSD client was overwritten");
            return new { ownedSlot = 2, foreignSlotsPreserved = true, clearedOnDispose = true };
        });
        Check("RTSS: busy renderer never waits or steals the lock", () =>
        {
            using var fixture = new RtssFixture();
            using var client = new RtssOverlayClient(fixture.Name);
            fixture.View.Write(36, 5); // busy bit plus another bit, which must also survive.
            var watch = Stopwatch.StartNew();
            Require(client.Publish(new RtssContent("test", [])) == RtssStatus.Busy, "Busy renderer was overwritten");
            Require(watch.ElapsedMilliseconds < 500 && fixture.View.ReadInt32(36) == 5
                && fixture.ReadString(fixture.Slot(2) + 256, 256) == "", "Lock was forced, waited on or slot claimed while busy");
            fixture.View.Write(36, 4);
            Require(client.Publish(new RtssContent("test", [])) == RtssStatus.Ready && fixture.View.ReadInt32(36) == 4,
                "Unlock erased unrelated lock bits");
            return new { elapsedMs = watch.Elapsed.TotalMilliseconds, unrelatedBitsPreserved = true };
        });
        Check("RTSS: malformed ABI, old text capacity, stale process and exhausted slots", () =>
        {
            using var fixture = new RtssFixture(false);
            using var client = new RtssOverlayClient(fixture.Name);
            Require(client.Publish(new RtssContent("old protocol", [])) == RtssStatus.Ready
                && fixture.ReadString(fixture.Slot(2) + 512, 4096) == "old protocol", "v2.14 extended text not supported");
            Require(client.Publish(new RtssContent(new string('x', 4096), [])) == RtssStatus.ContentTooLarge,
                "Oversized hypertext was truncated into an invalid command");
            Require(client.Publish(new RtssContent("x\0y", [])) == RtssStatus.ContentTooLarge, "Embedded NUL accepted");
            fixture.View.Write(RtssFixture.App + 272, unchecked((uint)Environment.TickCount - 5000));
            Require(client.Inspect(42, out var stale) == RtssStatus.Ready && !stale.Active, "Stale hooked process was treated as active");
            client.Clear(); fixture.SetOwner(2, "Client2"); fixture.SetOwner(3, "Client3");
            Require(client.Publish(new RtssContent("test", [])) == RtssStatus.SlotsFull, "Full slots were stolen");
            fixture.View.Write(24, uint.MaxValue);
            Require(client.Publish(new RtssContent("test", [])) == RtssStatus.Incompatible && fixture.View.ReadInt32(36) == 0,
                "Invalid offsets escaped bounds or left the global lock acquired");
            fixture.View.Write(24, (uint)RtssFixture.Slots); fixture.View.Write(4, 0x00030000u);
            Require(client.Inspect(42, out _) == RtssStatus.Incompatible, "Unknown major ABI accepted");
            return new { oldProtocol = true, invalidOffsetsRejected = true, stalePidRejected = true, foreignSlotsPreserved = true };
        });
        Check("RTSS: server absent and restarted mapping reconnect without stale handles", () =>
        {
            string name = $"CoreBi.Tests.RTSS.{Guid.NewGuid():N}";
            using var client = new RtssOverlayClient(name);
            Require(client.Inspect(42, out _) == RtssStatus.Unavailable, "Missing server claimed ready");
            using (var mapping = MemoryMappedFile.CreateNew(name, 40))
                Require(client.Inspect(42, out _) == RtssStatus.Incompatible, "Uninitialized server claimed ready");
            Require(client.Inspect(42, out _) == RtssStatus.Unavailable, "Mapping handle leaked after operation");
            return new { missingServerSafe = true, noStaleHandle = true };
        });
        Check("RTSS: formatting respects metrics, corners, alpha, bounded graph and untrusted names", () =>
        {
            var frames = new FrameStats(42, "<F=private.txt>", 144, 6.9, 110, Enumerable.Repeat(6.9, 200).ToArray());
            var lines = new[] { new OverlayLine("RAM", ["24,4 GB", "76%"], "32 GB") };
            foreach (string corner in new[] { "TL", "TR", "BL", "BR" })
            {
                var panel = new OverlayPanel();
                panel.Update(frames, lines, OverlayTestItems, "18:00");
                var content = RtssOverlayFormatter.Format(panel, corner, 1.5, 0.5, 1920, 1080);
                Require(content.Text.Contains("76%") && content.Text.Contains("24,4 GB") && content.Text.Contains("<S=150>")
                    && content.Text.Contains("<C=8027C9BC>") && !content.Text.Contains("<F="), "Formatting lost data/alpha/scale or allowed hypertext injection");
                Require(content.Text.StartsWith("<FNT=Bahnschrift SemiCondensed,-13,400,1>")
                    && !content.Text.Contains("<A="), "RTSS profile font/zoom or tabulation could move the canonical fields");
                int width = (int)Math.Round(panel.ActualWidth * 1.5), height = (int)Math.Round(panel.ActualHeight * 1.5);
                Require(content.Text.Contains($"<B={width},{height},R12>") && !content.Text.Contains($"<B={width},{height},1>"),
                    "Rounded WPF card was replaced by a square background or outline");
                int left = corner.EndsWith('R') ? 1920 - width - 12 : 12;
                int top = corner.StartsWith('B') ? 1080 - height - 12 : 12;
                Require(content.Text.Contains($"<P={left},{top}><L0>"), "Fullscreen panel did not use the selected corner");
                Require(content.Objects.Length == 36 + 120 * 4, "Graph grew beyond its bounded history");
                Require(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(content.Objects.AsSpan(32)) == 120,
                    "Graph ABI sample count differs from payload");
            }
            var sparsePanel = new OverlayPanel();
            sparsePanel.Update(null, [], new HashSet<string> { "fps" }, null);
            var sparse = RtssOverlayFormatter.Format(sparsePanel, "TL", 1, 1, 1920, 1080, nativeFrames: true);
            Require(sparse.Text.Contains("<FR>") && !sparse.Text.Contains("1% low") && sparse.Objects.Length == 0,
                "Unavailable ETW should use RTSS's actual FPS, preserving selection");
            sparsePanel.Update(null, [], new HashSet<string> { "fps", "frametime" }, null);
            Require(RtssOverlayFormatter.Format(sparsePanel, "TL", 1, 1, 1920, 1080, nativeFrames: true).Text.Contains("<FT> ms"),
                "Native frametime was lost when WPF reserves an empty field while awaiting ETW");
            Require(RtssOverlayFormatter.SafeText("<LI=x>\n\0").All(c => !char.IsControl(c) && c != '<'), "Untrusted string escaped sanitization");
            Require(OverlayTarget.CoversMonitor(-1920, 0, 1920, 1080, -1920, 0, 0, 1080)
                && !OverlayTarget.CoversMonitor(0, 0, 1900, 1040, 0, 0, 1920, 1080), "Fullscreen client geometry or negative monitor coordinates failed");
            return new { corners = 4, maxGraphSamples = 120, untrustedNamesSanitized = true, noFakeLow1 = true };
        });
    }
}

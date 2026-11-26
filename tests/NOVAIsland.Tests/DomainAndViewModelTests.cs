using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Protocol;
using NOVAIsland.Core.Services;
using NOVAIsland.Core.ViewModels;
using NSubstitute;
using System.Text;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class DomainAndViewModelTests
{
    [Fact]
    public void LrcParser_ShouldParseSortedLinesWithOffsetAndMultiTags()
    {
        const string lrc = """
            [ar:Test Artist]
            [ti:Test Track]
            [offset:500]
            [00:10.50][00:30.00]Chorus line
            [00:05.00]Opening line
            """;

        var lines = LrcParser.ParseSynced(lrc);

        Assert.Equal(3, lines.Count);
        Assert.Equal("Opening line", lines[0].Text);
        Assert.Equal(4.5, lines[0].TimeSeconds, 2);
        Assert.Equal("Chorus line", lines[1].Text);
        Assert.Equal(10.0, lines[1].TimeSeconds, 2);
        Assert.Equal(29.5, lines[2].TimeSeconds, 2);

        Assert.Equal(1, LrcParser.FindActiveLineIndex(lines, 12.0));
    }

    [Fact]
    public void SensitiveContentDetector_ShouldIdentifySecretsAndExcludedApps()
    {
        Assert.True(SensitiveContentDetector.LooksSensitive("ghp_1234567890abcdef123456"));
        Assert.True(SensitiveContentDetector.LooksSensitive("-----BEGIN OPENSSH PRIVATE KEY-----"));
        Assert.False(SensitiveContentDetector.LooksSensitive("Regular copied sentence"));

        Assert.True(SensitiveContentDetector.IsExcludedApp("1Password", ["1Password.exe", "Bitwarden.exe"]));
        Assert.False(SensitiveContentDetector.IsExcludedApp("notepad.exe", ["1Password.exe"]));
        Assert.Equal(ClipboardItemType.Link, SensitiveContentDetector.ClassifyText("https://lrclib.net/api"));
    }

    [Fact]
    public void TimerViewModel_ShouldAdvancePomodoroAndRecordLaps()
    {
        var vm = new TimerViewModel();
        string? completedMessage = null;
        vm.TimerCompleted += (_, msg) => completedMessage = msg;

        vm.SetPomodoroPhase(PomodoroPhase.Focus);
        vm.PomodoroRemainingSeconds = 1;
        vm.IsPomodoroRunning = true;

        vm.TickOneSecond();

        Assert.False(vm.IsPomodoroRunning);
        Assert.Equal(1, vm.PomodoroSessionsCompleted);
        Assert.Equal(PomodoroPhase.ShortBreak, vm.PomodoroPhase);
        Assert.NotNull(completedMessage);

        vm.SelectTab(TimerTab.Stopwatch);
        vm.ToggleActiveTimer();
        vm.TickStopwatchTenth();
        vm.LapStopwatch();
        Assert.Single(vm.StopwatchLaps);
        Assert.Equal(1, vm.StopwatchLaps[0]);
    }

    [Fact]
    public async Task ClipboardViewModel_ShouldFilterPinAndSortCorrectly()
    {
        var repo = Substitute.For<IClipboardRepository>();
        var monitor = Substitute.For<IClipboardMonitorService>();

        var t0 = DateTimeOffset.UtcNow;
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ClipboardItem>
        {
            new("1", ClipboardItemType.Text, "hello world", "Note", null, null, "Notepad", t0, Pinned: false),
            new("2", ClipboardItemType.Link, "https://example.com", "Docs", null, null, "Edge", t0.AddMinutes(-5), Pinned: true),
        });

        var vm = new ClipboardViewModel(repo, monitor);
        await vm.LoadAsync();

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.PinnedCount);
        Assert.Equal("2", vm.FilteredItems[0].Id); // Pinned item comes first

        vm.SetFilter(ClipboardFilterKind.Text);
        Assert.Single(vm.FilteredItems);
        Assert.Equal("1", vm.FilteredItems[0].Id);
    }

    [Fact]
    public void SpringPhysicsConfigMapper_ShouldRespectReduceMotion()
    {
        var instant = SpringPhysicsConfigMapper.FromPreset(AnimationPreset.Fluid, 1.0, reduceMotion: true);
        Assert.True(instant.Instant);

        var snappy = SpringPhysicsConfigMapper.FromPreset(AnimationPreset.Snappy, 1.5, reduceMotion: false);
        Assert.False(snappy.Instant);
        Assert.True(snappy.Period < TimeSpan.FromMilliseconds(200));
    }
}

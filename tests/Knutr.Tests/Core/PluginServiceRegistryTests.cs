using FluentAssertions;
using Knutr.Core.PluginServices;
using Knutr.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knutr.Tests.Core;

public class PluginServiceRegistryTests
{
    private readonly PluginServiceRegistry _registry = new(NullLogger<PluginServiceRegistry>.Instance);

    private static PluginServiceEntry MakeEntry(
        string name,
        string[]? subcommands = null,
        string[]? slashCommands = null,
        bool supportsScan = false)
    {
        return new PluginServiceEntry
        {
            ServiceName = name,
            BaseUrl = $"http://{name}:8080",
            Manifest = new PluginManifest
            {
                Name = name,
                Version = "1.0.0",
                Subcommands = (subcommands ?? []).Select(s => new PluginSubcommand { Name = s }).ToList(),
                SlashCommands = (slashCommands ?? []).Select(c => new PluginSlashCommand { Command = c }).ToList(),
                SupportsScan = supportsScan,
            }
        };
    }

    // ── Register & Lookup ──

    [Fact]
    public void Register_IndexesSubcommand()
    {
        _registry.Register(MakeEntry("sentinel", subcommands: ["sentinel"]));
        _registry.TryGetSubcommandService("sentinel", out var entry).Should().BeTrue();
        entry!.ServiceName.Should().Be("sentinel");
    }

    [Fact]
    public void Register_IndexesSlashCommand()
    {
        _registry.Register(MakeEntry("joke", slashCommands: ["joke"]));
        _registry.TryGetSlashCommandService("joke", out var entry).Should().BeTrue();
        entry!.ServiceName.Should().Be("joke");
    }

    [Fact]
    public void Register_SubcommandConflict_KeepsFirst()
    {
        _registry.Register(MakeEntry("first", subcommands: ["deploy"]));
        _registry.Register(MakeEntry("second", subcommands: ["deploy"]));

        _registry.TryGetSubcommandService("deploy", out var entry).Should().BeTrue();
        entry!.ServiceName.Should().Be("first");
    }

    [Fact]
    public void Register_SlashCommandConflict_KeepsFirst()
    {
        _registry.Register(MakeEntry("first", slashCommands: ["ping"]));
        _registry.Register(MakeEntry("second", slashCommands: ["ping"]));

        _registry.TryGetSlashCommandService("ping", out var entry).Should().BeTrue();
        entry!.ServiceName.Should().Be("first");
    }

    [Fact]
    public void TryGetSubcommandService_NotFound_ReturnsFalse()
    {
        _registry.TryGetSubcommandService("nonexistent", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetSlashCommandService_NotFound_ReturnsFalse()
    {
        _registry.TryGetSlashCommandService("nonexistent", out _).Should().BeFalse();
    }

    // ── GetScanCapable ──

    [Fact]
    public void GetScanCapable_ReturnsOnlyScanServices()
    {
        _registry.Register(MakeEntry("scanner", supportsScan: true));
        _registry.Register(MakeEntry("noscan", supportsScan: false));

        var scanCapable = _registry.GetScanCapable();
        scanCapable.Should().ContainSingle().Which.ServiceName.Should().Be("scanner");
    }

    [Fact]
    public void GetScanCapable_Empty_ReturnsEmpty()
    {
        _registry.GetScanCapable().Should().BeEmpty();
    }

    // ── GetAll ──

    [Fact]
    public void GetAll_ReturnsAllServices()
    {
        _registry.Register(MakeEntry("a"));
        _registry.Register(MakeEntry("b"));
        _registry.GetAll().Should().HaveCount(2);
    }

    // ── IsServiceRegistered ──

    [Fact]
    public void IsServiceRegistered_True()
    {
        _registry.Register(MakeEntry("sentinel"));
        _registry.IsServiceRegistered("sentinel").Should().BeTrue();
    }

    [Fact]
    public void IsServiceRegistered_False()
    {
        _registry.IsServiceRegistered("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void IsServiceRegistered_CaseInsensitive()
    {
        _registry.Register(MakeEntry("Sentinel"));
        _registry.IsServiceRegistered("sentinel").Should().BeTrue();
    }

    // ── Clear ──

    [Fact]
    public void Clear_RemovesAll()
    {
        _registry.Register(MakeEntry("sentinel", subcommands: ["sentinel"], slashCommands: ["ping"], supportsScan: true));

        _registry.Clear();

        _registry.GetAll().Should().BeEmpty();
        _registry.GetScanCapable().Should().BeEmpty();
        _registry.TryGetSubcommandService("sentinel", out _).Should().BeFalse();
        _registry.TryGetSlashCommandService("ping", out _).Should().BeFalse();
    }

    // ── Multiple subcommands/commands per service ──

    [Fact]
    public void Register_MultipleSubcommands_AllIndexed()
    {
        _registry.Register(MakeEntry("sentinel", subcommands: ["sentinel", "watch"]));

        _registry.TryGetSubcommandService("sentinel", out _).Should().BeTrue();
        _registry.TryGetSubcommandService("watch", out _).Should().BeTrue();
    }
}

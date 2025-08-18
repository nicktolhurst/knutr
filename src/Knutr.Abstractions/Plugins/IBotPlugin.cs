namespace Knutr.Abstractions.Plugins;

public interface IBotPlugin
{
    string Name { get; }
    void Configure(ICommandBuilder commands);
}

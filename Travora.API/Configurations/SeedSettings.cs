namespace Travora.API.Configurations;

public class SeedSettings
{
    public bool AutoSeedOnStartup { get; set; } = true;
    public string[] SeedOrder { get; set; } = ["countries", "cities", "airports", "airlines", "aircraft"];
}

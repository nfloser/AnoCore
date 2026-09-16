using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;

namespace AnoCore.Plugin;

[MinimumApiVersion(260)]
public sealed class AnoCorePlugin : BasePlugin
{
    public override string ModuleName => "AnoCore";

    public override string ModuleDescription => "Modular CS2 server framework for Ano modules.";

    public override string ModuleAuthor => "AnoMeme contributors";

    public override string ModuleVersion => "0.1.0-dev";

    public override void Load(bool hotReload)
    {
        // Runtime composition is intentionally introduced incrementally.
        // This first slice only establishes the stable plugin entry point.
    }

    public override void Unload(bool hotReload)
    {
    }
}

namespace AnoCore.Abstractions.Modules;

public sealed record ModuleSnapshot(
    ModuleDescriptor Descriptor,
    ModuleState State,
    Exception? Failure = null);

namespace AnoCore.Abstractions.Modules;

public enum ModuleState
{
    Created = 0,
    Loading = 1,
    Loaded = 2,
    Unloading = 3,
    Unloaded = 4,
    Faulted = 5,
}

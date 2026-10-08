namespace AnoCore.Abstractions.Menus;

/// <summary>Read-only personal summary displayed above dashboard actions.</summary>
public sealed record MenuDashboard(string Profile, string Progression, string Statistics,
    string Playtime, string Challenge1, string Challenge2);

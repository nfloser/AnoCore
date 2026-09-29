namespace AnoCore.Abstractions.Settings;

public sealed record PlayerSettingUpdate<T>(PlayerSettingKey<T> Key, T Value);

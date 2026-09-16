namespace AnoCore.Modules.AnoVeto;

public interface IAnoVetoRandomSource
{
    IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count);
}

public sealed class AnoVetoRandomSource : IAnoVetoRandomSource
{
    public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count < 0 || count > source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var working = source.ToArray();
        for (var index = working.Length - 1; index > 0; index--)
        {
            var other = Random.Shared.Next(index + 1);
            (working[index], working[other]) = (working[other], working[index]);
        }

        return working.Take(count).ToArray();
    }
}

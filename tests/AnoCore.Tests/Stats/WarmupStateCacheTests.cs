using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class WarmupStateCacheTests
{
    [TestMethod]
    public void ValidEntity_IsResolvedOnceButWarmupIsReadEveryTime()
    {
        var entity = new Rules { Warmup = true };
        var lookups = 0;
        var cache = Create(() => { lookups++; return entity; });
        Assert.AreEqual(true, cache.Read(1));
        entity.Warmup = false;
        Assert.AreEqual(false, cache.Read(1));
        for (var tick = 2; tick < 10002; tick++) Assert.AreEqual(false, cache.Read(tick));
        Assert.AreEqual(1, lookups);
    }

    [TestMethod]
    public void MissingEntity_IsRetriedNextTickWithoutRepeatedSameTickSearches()
    {
        Rules? entity = null;
        var lookups = 0;
        var cache = Create(() => { lookups++; return entity; });
        Assert.IsNull(cache.Read(10));
        Assert.IsNull(cache.Read(10));
        Assert.AreEqual(1, lookups);
        entity = new Rules { Warmup = false };
        Assert.AreEqual(false, cache.Read(11));
        Assert.AreEqual(2, lookups);
    }

    [TestMethod]
    public void InvalidEntity_IsReplacedWithoutReadingItsState()
    {
        var entity = new Rules { Warmup = true };
        var cache = Create(() => entity);
        Assert.AreEqual(true, cache.Read(10));
        entity.Valid = false;
        entity = new Rules { Warmup = false };
        Assert.AreEqual(false, cache.Read(10));
    }

    [TestMethod]
    public void Reset_ReleasesEvenValidEntityAndRetriesSameTickMiss()
    {
        Rules? entity = new Rules { Warmup = true };
        var cache = Create(() => entity);
        Assert.AreEqual(true, cache.Read(10));
        entity = null;
        cache.Reset();
        Assert.IsNull(cache.Read(10));
        entity = new Rules { Warmup = false };
        cache.Reset();
        Assert.AreEqual(false, cache.Read(10));
    }

    [TestMethod]
    public void MissingRulesState_IsRetriedNextTick()
    {
        var entity = new Rules { Warmup = null };
        var lookups = 0;
        var cache = Create(() => { lookups++; return entity; });
        Assert.IsNull(cache.Read(10));
        Assert.IsNull(cache.Read(10));
        entity = new Rules { Warmup = true };
        Assert.AreEqual(true, cache.Read(11));
        Assert.AreEqual(2, lookups);
    }

    [TestMethod]
    public void FailedNativeRead_DoesNotRetainEntityAndPropagatesError()
    {
        var entity = new Rules { Warmup = true };
        var cache = new WarmupStateCache<Rules>(() => entity, value => value.Valid,
            value => value.Throw ? throw new InvalidOperationException("native read") : value.Warmup);
        Assert.AreEqual(true, cache.Read(10));
        entity.Throw = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => cache.Read(11));
        entity = new Rules { Warmup = false };
        Assert.AreEqual(false, cache.Read(12));
    }

    private static WarmupStateCache<Rules> Create(Func<Rules?> resolve)
        => new(resolve, value => value.Valid,
            value => value.Valid ? value.Warmup : throw new AssertFailedException("Read invalid entity"));

    private sealed class Rules
    {
        public bool Valid { get; set; } = true;
        public bool? Warmup { get; set; }
        public bool Throw { get; set; }
    }
}

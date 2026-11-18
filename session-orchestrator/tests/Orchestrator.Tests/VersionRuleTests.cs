using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>
/// The version rule of the store (docs/store.md): a 64-bit FNV-1a hash of the file text. The plugin hooks
/// (plugin/hooks/backend.ts, versionOf) check the same values in backend.test.ts, so both sides agree.
/// </summary>
public class VersionRuleTests
{
    [TestCase("", -3750763034362895579L)]
    [TestCase("{\"title\":\"a\"}", 7837972144070839554L)]
    [TestCase("é", 775207407765167617L)]
    public void Version_is_the_hash_of_the_text(string text, long expected) =>
        Assert.That(Versions.Of(text), Is.EqualTo(expected));
}

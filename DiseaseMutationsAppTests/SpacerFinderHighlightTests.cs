using gRNA;

namespace DiseaseMutationsAppTests;

public class SpacerFinderHighlightTests
{
    [Test]
    public void GetMutationHighlightSpan_SubstitutionOverlap_ReturnsIntersection()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(5, 10, 8, 2);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DeletionZeroLength_UsesAnchorInsideWindow()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(10, 8, 13, 0);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DeletionAnchorAtWindowEnd_UsesLastBase()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(2, 5, 7, 0);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(4));
            Assert.That(length, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DuplicationLikeLongRegion_CropsToWindow()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(4, 6, 2, 5);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(0));
            Assert.That(length, Is.EqualTo(3));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DelinsLikeRegionInsideWindow_ReturnsExactSpan()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(20, 10, 23, 4);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(4));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_NoOverlap_ReturnsNoHighlight()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(0, 5, 20, 3);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(-1));
            Assert.That(length, Is.EqualTo(0));
        });
    }
}

using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Well addressing. The 96-well expectations reproduce column D of the workbook sheet
/// "Distribución_2D_frag", which fills a plate row-major: A1..A12, then B1..B12.
/// </summary>
public class WellAddressTests
{
    [TestCase(1, "Plate 1 - A1")]
    [TestCase(6, "Plate 1 - A6")]
    [TestCase(11, "Plate 1 - A11")]
    [TestCase(12, "Plate 1 - A12")]
    [TestCase(13, "Plate 1 - B1")]
    [TestCase(24, "Plate 1 - B12")]
    [TestCase(25, "Plate 1 - C1")]
    [TestCase(96, "Plate 1 - H12")]
    [TestCase(97, "Plate 2 - A1")]
    [TestCase(192, "Plate 2 - H12")]
    [TestCase(193, "Plate 3 - A1")]
    public void WellAddress_Plate96_FillsRowMajor(int poolId, string expected)
    {
        Assert.That(Pooling.wellAddress(Pooling.plate96, poolId).Label, Is.EqualTo(expected));
    }

    [TestCase(1, "Plate 1 - A1")]
    [TestCase(24, "Plate 1 - A24")]
    [TestCase(25, "Plate 1 - B1")]
    [TestCase(384, "Plate 1 - P24")]
    [TestCase(385, "Plate 2 - A1")]
    public void WellAddress_Plate384_FillsRowMajor(int poolId, string expected)
    {
        Assert.That(Pooling.wellAddress(Pooling.plate384, poolId).Label, Is.EqualTo(expected));
    }

    [Test]
    public void WellAddress_ExposesComponentParts()
    {
        var address = Pooling.wellAddress(Pooling.plate96, 110);

        Assert.Multiple(() =>
        {
            Assert.That(address.Plate, Is.EqualTo(2));
            Assert.That(address.Row, Is.EqualTo('B'));
            Assert.That(address.Column, Is.EqualTo(2));
            Assert.That(address.Label, Is.EqualTo("Plate 2 - B2"));
        });
    }

    [Test]
    public void WellAddress_RejectsNonPositiveId()
    {
        Assert.Throws<ArgumentException>(() => Pooling.wellAddress(Pooling.plate96, 0));
    }

    [Test]
    public void WellAddress_RejectsPlateWithMoreRowsThanLetters()
    {
        var tooTall = new Pooling.PlateFormat(27, 12);
        Assert.Throws<ArgumentException>(() => Pooling.wellAddress(tooTall, 1));
    }
}

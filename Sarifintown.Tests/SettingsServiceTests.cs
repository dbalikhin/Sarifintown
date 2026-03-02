using Sarifintown.Services;

namespace Sarifintown.Tests;

[TestFixture]
public class SettingsServiceTests
{
    [Test]
    public void SurroundingLines_Default_IsThree()
    {
        var service = new SettingsService();

        Assert.That(service.SurroundingLines, Is.EqualTo(3));
    }

    [Test]
    public void SurroundingLines_WhenSetBelowMin_ClampsToMin()
    {
        var service = new SettingsService();

        service.SurroundingLines = 0;

        Assert.That(service.SurroundingLines, Is.EqualTo(1));
    }

    [Test]
    public void SurroundingLines_WhenSetAboveMax_ClampsToMax()
    {
        var service = new SettingsService();

        service.SurroundingLines = 42;

        Assert.That(service.SurroundingLines, Is.EqualTo(10));
    }
}

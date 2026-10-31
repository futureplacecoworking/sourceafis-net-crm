using System.Reflection;
using System.Threading;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
public class ProductIdentityTests
{
    [Test]
    public void ReceptionistCanIdentifyTheInstalledProductAndVersion()
    {
        using var ui = new UiFixture();
        Assert.That(ui.Form.Text, Is.EqualTo("Central Biométrica"));
        var version = ui.Find<Control>("versionLabel");
        Assert.That(version.Visible, Is.True);
        Assert.That(version.Text, Does.Contain("1.4.0"));
        Assert.That(ui.Form.GetType().Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product, Is.EqualTo("Central Biométrica"));
    }
}

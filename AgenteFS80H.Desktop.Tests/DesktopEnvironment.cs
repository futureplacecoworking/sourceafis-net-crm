using System.Threading;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

// WinForms rendering defaults must be set before the first window in any fixture.
[SetUpFixture]
[Apartment(ApartmentState.STA)]
public class DesktopEnvironment
{
    [OneTimeSetUp]
    public void InitializeDesktop()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}

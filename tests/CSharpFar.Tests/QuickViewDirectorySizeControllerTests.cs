using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class QuickViewDirectorySizeControllerTests
{
    [Fact]
    public void InactiveQuickView_RepeatedUpdatesDoNotWakeInputLoop()
    {
        int wakes = 0;
        using var controller = new QuickViewDirectorySizeController(() => wakes++);

        controller.Update(quickViewEnabled: false, item: null);
        controller.Update(quickViewEnabled: false, item: null);
        controller.Update(quickViewEnabled: false, item: null);

        Assert.Equal(0, wakes);
    }
}

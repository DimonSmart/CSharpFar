using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PanelRangeSelectionTests
{
    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    [InlineData(2, 2)]
    public void SelectRange_AddsBothEndpointsInLogicalOrderWithoutMovingCursor(int anchor, int target)
    {
        var (controller, state) = CreatePanel(6);
        state.CursorIndex = 3;
        state.ScrollOffset = 1;
        state.SelectedPaths.Add(state.Items[0].FullPath);
        state.SelectedLocations.Add(state.Items[0].Location);

        controller.SelectRange(state, anchor, target);

        FilePanelItem[] expected = state.Items
            .Where((_, index) => index == 0 || index >= Math.Min(anchor, target) && index <= Math.Max(anchor, target))
            .ToArray();
        Assert.True(state.SelectedPaths.SetEquals(expected.Select(item => item.FullPath)));
        Assert.True(state.SelectedLocations.SetEquals(expected.Select(item => item.Location)));
        Assert.Equal(expected.Length, state.Summary!.SelectedCount);
        Assert.Equal(expected.Sum(item => item.Size ?? 0), state.Summary.SelectedFileSize);
        Assert.Equal(3, state.CursorIndex);
        Assert.Equal(1, state.ScrollOffset);
    }

    [Fact]
    public void SelectRange_IsIdempotentAndPreservesSelectionOutsideRange()
    {
        var (controller, state) = CreatePanel(6);
        state.SelectedPaths.Add(state.Items[0].FullPath);
        state.SelectedLocations.Add(state.Items[0].Location);
        controller.SelectRange(state, 1, 4);
        PanelSummary summary = state.Summary!;

        controller.SelectRange(state, 4, 1);

        Assert.Same(summary, state.Summary);
        Assert.Equal(5, state.SelectedPaths.Count);
        Assert.Contains(state.Items[0].FullPath, state.SelectedPaths);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(1, -1)]
    [InlineData(6, 2)]
    [InlineData(1, 6)]
    public void SelectRange_InvalidEndpointsHaveNoEffect(int anchor, int target)
    {
        var (controller, state) = CreatePanel(6);
        state.SelectedPaths.Add(state.Items[0].FullPath);
        state.SelectedLocations.Add(state.Items[0].Location);
        controller.RefreshSelectionSummary(state);
        PanelSummary summary = state.Summary!;

        controller.SelectRange(state, anchor, target);

        Assert.Same(summary, state.Summary);
        Assert.Single(state.SelectedPaths);
        Assert.Single(state.SelectedLocations);
    }

    [Fact]
    public void SelectRange_EmptyItemsHaveNoEffect()
    {
        var (controller, state) = CreatePanel(0);
        controller.SelectRange(state, 0, 0);
        Assert.Empty(state.SelectedPaths);
        Assert.Empty(state.SelectedLocations);
        Assert.Null(state.Summary);
    }

    [Theory]
    [InlineData(false, 2, 30)]
    [InlineData(true, 3, 30)]
    public void SelectRange_RespectsParentAndFolderSelectionOption(bool selectFolders, int expectedCount, long expectedSize)
    {
        var (controller, state) = CreatePanel(0);
        state.Items.Add(new FilePanelItem
        {
            Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true,
        });
        state.Items.Add(new FilePanelItem
        {
            Name = "one.txt", FullPath = @"C:\work\one.txt", IsDirectory = false, Size = 10,
        });
        state.Items.Add(new FilePanelItem
        {
            Name = "folder", FullPath = @"C:\work\folder", IsDirectory = true,
        });
        state.Items.Add(new FilePanelItem
        {
            Name = "two.txt", FullPath = @"C:\work\two.txt", IsDirectory = false, Size = 20,
        });

        controller.SelectRange(state, 3, 0, new AppSettings.PanelOptionsSettings { SelectFolders = selectFolders });

        Assert.Equal(expectedCount, state.Summary!.SelectedCount);
        Assert.Equal(expectedSize, state.Summary.SelectedFileSize);
        Assert.DoesNotContain(state.Items[0].FullPath, state.SelectedPaths);
        Assert.DoesNotContain(state.Items[0].Location, state.SelectedLocations);
        Assert.Equal(selectFolders, state.SelectedLocations.Contains(state.Items[2].Location));
    }

    [Fact]
    public void SelectRange_RepairsPartialMembershipInBothSelectionSets()
    {
        var (controller, state) = CreatePanel(3);
        state.SelectedPaths.Add(state.Items[0].FullPath);
        state.SelectedLocations.Add(state.Items[1].Location);

        controller.SelectRange(state, 0, 1);

        Assert.Equal(2, state.SelectedPaths.Count);
        Assert.Equal(2, state.SelectedLocations.Count);
        Assert.True(state.Items.Take(2).All(item =>
            state.SelectedPaths.Contains(item.FullPath) &&
            state.SelectedLocations.Contains(item.Location)));
        Assert.Equal(2, state.Summary!.SelectedCount);
    }

    [Fact]
    public void SelectRange_OnlyNonSelectableItemsLeavesSummaryUnchanged()
    {
        var (controller, state) = CreatePanel(0);
        state.Items.Add(new FilePanelItem
        {
            Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true,
        });
        state.Items.Add(new FilePanelItem
        {
            Name = "folder", FullPath = @"C:\work\folder", IsDirectory = true,
        });
        controller.RefreshSelectionSummary(state);
        PanelSummary summary = state.Summary!;

        controller.SelectRange(state, 0, 1, new AppSettings.PanelOptionsSettings { SelectFolders = false });

        Assert.Same(summary, state.Summary);
        Assert.Empty(state.SelectedPaths);
        Assert.Empty(state.SelectedLocations);
    }

    private static (PanelController Controller, FilePanelState State) CreatePanel(int count)
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\work" };
        for (int i = 0; i < count; i++)
        {
            state.Items.Add(new FilePanelItem
            {
                Name = $"file{i}.txt",
                FullPath = $@"C:\work\file{i}.txt",
                IsDirectory = false,
                Size = (i + 1) * 10L,
            });
        }

        var controller = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService()));
        return (controller, state);
    }
}

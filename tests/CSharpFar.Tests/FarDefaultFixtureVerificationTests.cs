namespace CSharpFar.Tests;

public class FarDefaultFixtureVerificationTests
{
    [Fact(Skip = "Requires FarDefault fixture generated from a clean current Far build. See Spec/006.md.")]
    public void FarDefault_Preset_Matches_Exported_Far_Fixture()
    {
        throw new NotImplementedException(
            "Capture tests/Fixtures/Far/FarDefaultHighlight.farconfig and " +
            "tests/Fixtures/Far/FarDefaultMaskGroups.json, then compare them with FarDefaultHighlightPreset.");
    }
}

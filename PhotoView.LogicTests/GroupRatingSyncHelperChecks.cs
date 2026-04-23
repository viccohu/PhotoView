using PhotoView.Helpers;

namespace PhotoView.LogicTests;

internal static class GroupRatingSyncHelperChecks
{
    public static void Run()
    {
        MissingRating_UsesExistingRating();
        ConflictingRatings_UsesHigherRating();
        MatchingRatings_DoNotRequireSynchronization();
    }

    private static void MissingRating_UsesExistingRating()
    {
        var needsSync = GroupRatingSyncHelper.NeedsSynchronization(new uint[] { 99, 0 }, out var canonicalRating);

        TestAssert.True(needsSync, "Missing group rating should require synchronization.");
        TestAssert.Equal(99u, canonicalRating, "Missing group rating should inherit the existing rating.");
    }

    private static void ConflictingRatings_UsesHigherRating()
    {
        var needsSync = GroupRatingSyncHelper.NeedsSynchronization(new uint[] { 25, 99 }, out var canonicalRating);

        TestAssert.True(needsSync, "Conflicting group ratings should require synchronization.");
        TestAssert.Equal(99u, canonicalRating, "Conflicting group ratings should resolve to the higher rating.");
    }

    private static void MatchingRatings_DoNotRequireSynchronization()
    {
        var needsSync = GroupRatingSyncHelper.NeedsSynchronization(new uint[] { 50, 50 }, out var canonicalRating);

        TestAssert.False(needsSync, "Matching group ratings should not require synchronization.");
        TestAssert.Equal(50u, canonicalRating, "Matching group ratings should keep their shared value.");
    }
}

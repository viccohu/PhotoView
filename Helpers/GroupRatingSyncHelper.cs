namespace PhotoView.Helpers;

public static class GroupRatingSyncHelper
{
    public static uint GetCanonicalRating(IEnumerable<uint> ratings)
    {
        var canonical = 0u;

        foreach (var rating in ratings)
        {
            if (rating > canonical)
            {
                canonical = rating;
            }
        }

        return canonical;
    }

    public static bool NeedsSynchronization(IEnumerable<uint> ratings, out uint canonicalRating)
    {
        var seenAny = false;
        canonicalRating = 0u;

        foreach (var rating in ratings)
        {
            if (!seenAny)
            {
                canonicalRating = rating;
                seenAny = true;
                continue;
            }

            if (rating != canonicalRating)
            {
                canonicalRating = GetCanonicalRating(ratings);
                return true;
            }

            if (rating > canonicalRating)
            {
                canonicalRating = rating;
            }
        }

        return false;
    }
}

namespace JobLinkv2.Services.Apply
{
    // One applicant to a job, as far as ranking is concerned. Score is the suitability score (0-100)
    // worked out the same way for every user, whatever their plan.
    public sealed record RankedApplicant(int ApplicationId, int UserId, int Score, bool IsPriority, DateTime AppliedAt);

    // The order an employer sees applicants in.
    public static class ApplicantRanker
    {
        // Best first:
        //   1. suitability score - always decides, a higher score always ranks higher;
        //   2. Priority Application - only breaks a TIE on the score, never lifts someone over a
        //      better-scoring applicant (and never changes anyone's score);
        //   3. the earlier application;
        //   4. the application id, so the order never depends on the order they were passed in.
        public static IReadOnlyList<RankedApplicant> Rank(IEnumerable<RankedApplicant> applicants) =>
            applicants
                .OrderByDescending(a => a.Score)
                .ThenByDescending(a => a.IsPriority)
                .ThenBy(a => a.AppliedAt)
                .ThenBy(a => a.ApplicationId)
                .ToList();
    }
}

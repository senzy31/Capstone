using System.ComponentModel.DataAnnotations;

namespace Joblink.Services.Tracker
{
    // What a client may send to the application tracker. These classes are the allow-list:
    // anything else in the JSON body (a user id, an application type, is_deleted, an
    // apply link...) is ignored - who the user is comes from the login token, and what
    // an application or a hand-added job is allowed to be is decided by the tracker rules.

    // "Log application": a job the user typed in by hand and applied to elsewhere.
    public sealed class LogApplicationRequest
    {
        [Required] public int? JobId { get; set; }

        public string? Status { get; set; }

        public DateTime? AppliedAt { get; set; }
    }

    // Change the status of an application you logged by hand.
    public sealed class ChangeApplicationStatusRequest
    {
        [Required] public int? ApplicationId { get; set; }

        public string? Status { get; set; }
    }

    // A job added by hand from the tracker: just what was typed.
    public sealed class LogJobRequest
    {
        [StringLength(300)] public string? Title { get; set; }

        [StringLength(300)] public string? Company { get; set; }

        [StringLength(300)] public string? Location { get; set; }
    }
}

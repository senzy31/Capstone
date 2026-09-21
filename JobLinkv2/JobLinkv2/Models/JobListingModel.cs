using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Job_Listings")]
    public class JoblistingModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("job_id")]
        public int JobId { get; set; }

        [Column("external_job_id")]
        public string? ExternalJobId { get; set; }

        [Column("title")]
        public string? Title { get; set; }

        [Column("company")]
        public string? Company { get; set; }

        [Column("location")]
        public string? Location { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("source_api")]
        public string? SourceApi { get; set; }

        [Column("posted_date")]
        public DateTime? PostedDate { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        // ----- Apply flow -----------------------------------------------------

        // "Internal" = posted by a JobLink employer, "External" = imported from
        // JSearch (or logged by hand) - no employer on JobLink to receive it.
        [Column("source")]
        public string Source { get; set; } = "External";

        // Only set for Internal jobs.
        [Column("employer_id")]
        public int? EmployerId { get; set; }

        [Column("apply_url")]
        public string? ApplyUrl { get; set; }

        [Column("apply_is_direct")]
        public bool ApplyIsDirect { get; set; }

        // e.g. LinkedIn, Indeed
        [Column("publisher")]
        public string? Publisher { get; set; }

        // JSearch's apply_options array, as JSON.
        [Column("apply_options")]
        public string? ApplyOptions { get; set; }

        // Set when every apply link turned out to be dead.
        [Column("is_expired")]
        public bool IsExpired { get; set; }

        [Column("latitude")]
        public decimal? Latitude { get; set; }

        [Column("longitude")]
        public decimal? Longitude { get; set; }

        //public ICollection<ApplicationModel> Applications { get; set; }
        //public ICollection<JobMatchModel> JobMatches { get; set; }
        //public ICollection<SavedJobsModel> SavedJobs { get; set; }
    }
}

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
        public string ExternalJobId { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("company")]
        public string Company { get; set; }

        [Column("location")]
        public string Location { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("source_api")]
        public string SourceApi { get; set; }

        [Column("posted_date")]
        public DateTime? PostedDate { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        //public ICollection<ApplicationModel> Applications { get; set; }
        //public ICollection<JobMatchModel> JobMatches { get; set; }
        //public ICollection<SavedJobsModel> SavedJobs { get; set; }
    }
}

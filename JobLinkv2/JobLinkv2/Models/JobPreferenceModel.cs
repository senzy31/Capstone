using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobLinkv2.Models
{
    // What a jobseeker prefers in a job (one row per user). Salaries are
    // monthly, in PHP. Used to score how well a job suits the user.
    [Table("Job_Preferences")]
    public class JobPreferenceModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("preference_id")]
        public int PreferenceId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("preferred_location")]
        public string? PreferredLocation { get; set; }

        // onsite | remote | hybrid
        [Column("work_arrangement")]
        public string? WorkArrangement { get; set; }

        [Column("min_salary")]
        public decimal? MinSalary { get; set; }

        [Column("max_salary")]
        public decimal? MaxSalary { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }
    }
}

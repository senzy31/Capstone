using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Applications")]
    public class ApplicationModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("application_id")]
        public int ApplicationId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("job_id")]
        public int JobId { get; set; }

        // Nullable: the DB column allows NULL for applications logged
        // before the applicant has built a resume in Resume Builder.
        [Column("resume_id")]
        public int? ResumeId { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("applied_at")]
        public DateTime? AppliedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        // Note: a "deleted_at" DateTime property used to be here, mapped via
        // [Column("deleted_at")] - but the Applications table has no such
        // column, only is_deleted (bit), like every other table. It was dead,
        // broken code (blew up every INSERT/UPDATE). Removed.

        // Navigation property only - nullable, see ResumeModel.User for why.
        [ForeignKey("ResumeId")]
        public ResumeModel? Resume { get; set; }

        // Note: a "Skill" nav property with [ForeignKey("SkillId")] used to be
        // here, but ApplicationModel has no SkillId column/property for it to
        // point at - it was dead, broken code. Removed.
    }
}

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

        [Column("resume_id")]
        public int ResumeId { get; set; }

        [Column("status")]
        public string Status { get; set; }

        [Column("applied_at")]
        public DateTime AppliedAt { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [ForeignKey("ResumeId")]
        public ResumeModel Resume { get; set; }

        [ForeignKey("SkillId")]
        public SkillsModel Skill { get; set; }
    }
}

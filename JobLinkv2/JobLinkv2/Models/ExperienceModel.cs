using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Experience")]
    public class ExperienceModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("experience_id")]
        public int ExperienceId { get; set; }

        [Column("resume_id")]
        public int ResumeId { get; set; }

        [Column("company_name")]
        public string CompanyName { get; set; }

        [Column("position")]
        public string Position { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("start_date")]
        public DateTime StartDate { get; set; }

        [Column("end_date")]
        public DateTime EndDate { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey("ResumeId")]
        public ResumeModel Resume { get; set; }
    }
}

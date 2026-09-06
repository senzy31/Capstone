using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Education")]
    public class EducationModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("education_id")]
        public int EducationId { get; set; }

        [Column("resume_id")]
        public int ResumeId { get; set; }

        [Column("school_name")]
        public string SchoolName { get; set; }

        [Column("degree")]
        public string Degree { get; set; }

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

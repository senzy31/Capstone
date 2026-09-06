using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Resume_Skills")]
    public class ResumeSkillsModel
    {
        [Column("resume_id")]
        public int ResumeId { get; set; }

        [Column("skill_id")]
        public int SkillId { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey("ResumeId")]
        public ResumeModel Resume { get; set; }

        [ForeignKey("SkillId")]
        public SkillsModel Skill { get; set; }
    }
}

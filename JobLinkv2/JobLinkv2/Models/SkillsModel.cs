using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Skills")]
    public class SkillsModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("skill_id")]
        public int SkillId { get; set; }

        [Column("skill_name")]
        public string SkillName { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        //public ICollection<ResumeSkillsModel> ResumeSkills { get; set; }
    }
}

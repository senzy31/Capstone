using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Resumes")]
    public class ResumeModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("resume_id")]
        public int ResumeId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("template_type")]
        public string TemplateType { get; set; }

        [Column("ai_generated_content")]
        public string AiGeneratedContent { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey("UserId")]
        public UserModel User { get; set; }

        //public ICollection<EducationModel> Educations { get; set; }
        //public ICollection<ExperienceModel> Experiences { get; set; }
        //public ICollection<ResumeSkillsModel> ResumeSkills { get; set; }
        //public ICollection<ApplicationModel> Applications { get; set; }
    }
}

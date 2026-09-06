using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Users")]
    public class UserModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("user_id")]
        public int UserId { get; set; }

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("password_hash")]
        public string PasswordHash { get; set; }

        [Column("role")]
        public string Role { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        //public ProfileModel Profile { get; set; }
        //public ICollection<ResumeModel> Resumes { get; set; }
        //public ICollection<ApplicationModel> Applications { get; set; }
        //public ICollection<JobMatchModel> JobMatches { get; set; }
        //public ICollection<SavedJobsModel> SavedJobs { get; set; }
        //public ICollection<NotificationModel> Notifications { get; set; }
    }
}

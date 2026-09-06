using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Profiles")]
    public class ProfileModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("profile_id")]
        public int ProfileId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("phone")]
        public string Phone { get; set; }

        [Column("address")]
        public string Address { get; set; }

        [Column("linkedin_url")]
        public string LinkedinUrl { get; set; }

        [Column("github_url")]
        public string GithubUrl { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey("UserId")]
        public UserModel User { get; set; }
    }
}

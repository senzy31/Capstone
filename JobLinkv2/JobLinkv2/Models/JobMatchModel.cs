
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Job_Match")]
    public class JobMatchModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("match_id")]
        public int MatchId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("job_id")]
        public int JobId { get; set; }

        [Column("match_score")]
        public decimal MatchScore { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey("UserId")]
        public UserModel User { get; set; }

        [ForeignKey("JobId")]
        public JoblistingModel JobListing { get; set; }
    }
}

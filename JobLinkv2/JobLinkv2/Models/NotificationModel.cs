using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Models
{
    [Table("Notifications")]
    public class NotificationModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("notification_id")]
        public int NotificationId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("message")]
        public string Message { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        // One of the literal values in NotificationTypes (JobLinkv2.Services.Notifications).
        // Null for old rows created before this column existed.
        [Column("type")]
        public string? Type { get; set; }

        // Where clicking the notification should take you - a path into the front end, not a
        // full URL. Null when there's nowhere in particular to go.
        [Column("link")]
        public string? Link { get; set; }

        [ForeignKey("UserId")]
        public UserModel User { get; set; }
    }
}

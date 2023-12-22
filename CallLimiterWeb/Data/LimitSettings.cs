using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace CallLimiterWeb.Data
{
    [Table("CLM_LimitSettings")]
    public class LimitSettings
    {
        [Key]
        [Column(TypeName = "varchar(36)")]
        public string? LimitSettingsId { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? Description { get; set; }


        [Column(TypeName = "varchar(20)")]
        public string? DNIS { get; set; }

        public bool IsAllBusy { get; set; }

        [Column(TypeName = "varchar(30)")]
        public string? Type { get; set; }

        public int MaxLimitValue { get; set; }
        public int AvailableAgentsLimitValue { get; set; }

        [Column(TypeName = "varchar(3)")]
        public string? Conditions { get; set; }

        public ICollection<DNISListLMS>? DNISListLMS { get; set; }
        public ICollection<QueueListLMS>? QueueListLMS { get; set; }

        [Column(TypeName = "varchar(36)")]
        public string? DivisionId { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string? DivisionName { get; set; }


        public bool IsScheduled { get; set; }
        public DateTime ScheduleStart { get; set; }
        public DateTime ScheduleEnd { get; set; }
        public string? ScheduleStartString { get; set; }
        public string? ScheduleENDPT { get; set; }

        public bool IsEnabled { get; set; }
        public bool IsSettingsError { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string? CreatedBy { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string? LastModifiedBy { get; set; }

    }

    [Table("CLM_DNISListLMS")]
    public class DNISListLMS
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DNISListId { get; set; }

        [Column(TypeName = "varchar(20)")]
        public string? DNIS { get; set; }
        [Column(TypeName = "varchar(36)")]
        public string? LimitSettingsId { get; set; }
        [ForeignKey("LimitSettingsId")]
        public LimitSettings? LimitSettings { get; set; }

    }

    [Table("CLM_QueueListLMS")]
    public class QueueListLMS
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int QueueListId { get; set; }

        [Column(TypeName = "varchar(36)")]
        public string? LimitSettingsId { get; set; }


        [Column(TypeName = "varchar(36)")]
        public string? QueueId { get; set; }
        [Column(TypeName = "varchar(255)")]
        public string? QueueName { get; set; }

        [ForeignKey("LimitSettingsId")]
        public LimitSettings? LimitSettings { get; set; }

    }

    [Table("CLM_ANIList")]
    public class ANIList
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        [Column(TypeName = "varchar(20)")]
        public string? ANI { get; set; }
        public DateTime DateCreated { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string? CreatedBy { get; set; }

    }


    public class LimitSettingsDTO
    {
        public LimitSettingsDTO()
        {
            LimitSettingsId = string.Empty;
            Description = string.Empty;
            DNIS = string.Empty;
            IsAllBusy = false;
            Type = "Concurrent Calls";
            MaxLimitValue = 1;
            AvailableAgentsLimitValue = 0;
            DNISList = new List<DNISListDTO>();
            QueueList = new List<QueueListDTO>();
            Conditions = "SUM";
            IsScheduled = false;
            ScheduleStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
            ScheduleEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 59).AddMinutes(59);
            IsEnabled = false;
            IsSettingsError = false;
            CreatedBy = string.Empty;
            LastModifiedBy = string.Empty;
            DivisionId = string.Empty;
            DivisionName = string.Empty;
        }

        public string? LimitSettingsId { get; set; }
        public string? Description { get; set; }
        public string? DNIS { get; set; }
        public bool IsAllBusy { get; set; }
        public string? Type { get; set; }
        public int MaxLimitValue { get; set; }
        public int AvailableAgentsLimitValue { get; set; }
        public string? Conditions { get; set; }
        public List<DNISListDTO> DNISList { get; set; }
        public List<QueueListDTO> QueueList { get; set; }
        public bool IsScheduled { get; set; }
        public DateTime ScheduleStart { get; set; }
        public DateTime ScheduleEnd { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsSettingsError { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public string? CreatedBy { get; set; }
        public string? LastModifiedBy { get; set; }
        public string? DivisionId { get; set; }
        public string? DivisionName { get; set; }

    }

    public class DNISListDTO
    {
        public string? LimitSettingsId { get; set; }
        public string? DNIS { get; set; }

    }

    public class QueueListDTO
    {
        public string? LimitSettingsId { get; set; }
        public string? QueueId { get; set; }
        public string? QueueName { get; set; }
        public string? DivisionId { get; set; }
        public string? DivisionName { get;set; }

    }

    public class Acc
    {
		public int Id { get; set; }
		public DateTime? Time { get; set; }
        public string? Src_user { get; set; } //ANI
        public string? Dst_user { get; set; } //DNIS
        public string? Sip_code { get; set; }
        public string? Sip_reason { get; set; }
		public string? LimitSettingsId { get; set; }
        public string? DivisionId { get; set; }
        

    }

    public class AccDTO
    {
        public AccDTO()
        {
            Acc = new Acc();
        }

        public Acc Acc {  get; set; }
        public string? Description { get; set; } //LimitSetting Description

    }

    public class ANIDTO
    {
        public string? ANI { get; set; }
        //public DateTime DateCreated { get; set; }
        //public string? CreatedBy { get; set; }

    }
}

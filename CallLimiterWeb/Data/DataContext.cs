using Microsoft.EntityFrameworkCore;

namespace CallLimiterWeb.Data
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options){}
        public DbSet<LimitSettings> limitSettings { get; set; }
        public DbSet<DNISListLMS> DNISListLMS { get; set; }
        public DbSet<QueueListLMS> QueueListLMS { get; set; }
		public DbSet<Acc> acc { get; set; }
        public DbSet<ANIList> ANIList { get; set; }

	}
}



using Microsoft.EntityFrameworkCore;

using Messanger.Classes;

namespace Messanger.Tabels
{
    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> options) : base(options) { }

        public DbSet<User> Users { get; set; }
    }
}

using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<ParentDevice> ParentDevices => Set<ParentDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.MacAddress).IsUnique();
            e.HasIndex(d => d.ApiKey).IsUnique();
        });

        modelBuilder.Entity<Child>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Device).WithMany().HasForeignKey(c => c.DeviceId);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Device).WithMany(d => d.Conversations).HasForeignKey(c => c.DeviceId);
            e.HasOne(c => c.Child).WithMany(ch => ch.Conversations).HasForeignKey(c => c.ChildId);
            e.HasIndex(c => c.DeviceId);
            e.HasIndex(c => c.StartedAt);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId);
            e.HasIndex(m => m.ConversationId);
            e.Property(m => m.Role).HasConversion<string>();
            e.Property(m => m.SafetyFlag).HasConversion<string>();
        });

        modelBuilder.Entity<Parent>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Email).IsUnique();
        });

        modelBuilder.Entity<ParentDevice>(e =>
        {
            e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
            e.HasOne(pd => pd.Parent).WithMany(p => p.ParentDevices).HasForeignKey(pd => pd.ParentId);
            e.HasOne(pd => pd.Device).WithMany(d => d.ParentDevices).HasForeignKey(pd => pd.DeviceId);
        });
    }
}

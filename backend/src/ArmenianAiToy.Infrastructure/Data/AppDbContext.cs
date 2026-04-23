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
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ParentPasswordResetToken> ParentPasswordResetTokens => Set<ParentPasswordResetToken>();
    public DbSet<ParentEmailVerificationToken> ParentEmailVerificationTokens => Set<ParentEmailVerificationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.MacAddress).IsUnique();
            e.HasIndex(d => d.ApiKey).IsUnique();
            // B4: NOT NULL column with default backfills existing devices at
            // migration time, so the new non-nullable property never sees a
            // null in-memory and existing rows get the Armenia-first default.
            e.Property(d => d.TimeZone).HasDefaultValue("Asia/Yerevan");
            // B5: per-mode flags default true — existing devices remain
            // behaviorally unchanged after migration (all four modes allowed).
            e.Property(d => d.StoryEnabled).HasDefaultValue(true);
            e.Property(d => d.GameEnabled).HasDefaultValue(true);
            e.Property(d => d.RiddleEnabled).HasDefaultValue(true);
            e.Property(d => d.CuriosityEnabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<Child>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Device).WithMany().HasForeignKey(c => c.DeviceId);
            e.Property(c => c.Gender).HasConversion<string>();
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

        // AuditEvent is deliberately foreign-key-free (see entity xmldoc):
        // audit rows outlive their subjects. EventType stored as string,
        // consistent with other enums (Gender, MessageRole, SafetyFlag).
        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Timestamp);
            e.Property(a => a.EventType).HasConversion<string>();
        });

        // ParentPasswordResetToken — single-use forgot-password state.
        // FK cascade to Parent: a deleted parent takes their pending
        // reset tokens with them, by design (pending tokens are not
        // audit material and should not linger as orphans). Unique
        // index on TokenHash so the completion endpoint can look up by
        // hash alone.
        modelBuilder.Entity<ParentPasswordResetToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Parent)
                .WithMany()
                .HasForeignKey(t => t.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.ParentId);
        });

        // ParentEmailVerificationToken — single-use email-verification
        // state. Same shape as ParentPasswordResetToken (FK cascade,
        // unique TokenHash, ParentId index).
        modelBuilder.Entity<ParentEmailVerificationToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Parent)
                .WithMany()
                .HasForeignKey(t => t.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.ParentId);
        });
    }
}

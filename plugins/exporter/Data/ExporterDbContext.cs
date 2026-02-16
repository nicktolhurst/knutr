using Microsoft.EntityFrameworkCore;

namespace Knutr.Plugins.Exporter.Data;

public sealed class ExporterDbContext(DbContextOptions<ExporterDbContext> options) : DbContext(options)
{
    public DbSet<ChannelExport> ChannelExports => Set<ChannelExport>();
    public DbSet<ExportedMessage> ExportedMessages => Set<ExportedMessage>();
    public DbSet<ExportedUser> ExportedUsers => Set<ExportedUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChannelExport>(entity =>
        {
            entity.ToTable("channel_exports");
            entity.HasKey(e => e.ChannelId);

            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.LastSyncTs).HasColumnName("last_sync_ts");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ExportedMessage>(entity =>
        {
            entity.ToTable("exported_messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.MessageTs).HasColumnName("message_ts");
            entity.Property(e => e.ThreadTs).HasColumnName("thread_ts");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Text).HasColumnName("text");
            entity.Property(e => e.EditedTs).HasColumnName("edited_ts");
            entity.Property(e => e.ImportedAt).HasColumnName("imported_at");

            entity.HasIndex(e => new { e.ChannelId, e.MessageTs })
                .IsUnique()
                .HasDatabaseName("ix_exported_messages_channel_message");
        });

        modelBuilder.Entity<ExportedUser>(entity =>
        {
            entity.ToTable("exported_users");
            entity.HasKey(e => e.UserId);

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.RealName).HasColumnName("real_name");
            entity.Property(e => e.IsBot).HasColumnName("is_bot");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}

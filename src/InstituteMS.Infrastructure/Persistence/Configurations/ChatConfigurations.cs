using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> b)
    {
        b.ToTable("ChatConversation");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.Type).HasColumnName("Type").HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.Title).HasColumnName("Title").HasMaxLength(150);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(150);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.LastMessageOn).HasColumnName("LastMessageOn");
        b.Property(x => x.StaffLastReadMessageId).HasColumnName("StaffLastReadMessageId");
        b.Property(x => x.IsActive).HasColumnName("IsActive");

        b.HasMany(x => x.Participants)
            .WithOne(p => p.Conversation!)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Messages)
            .WithOne(m => m.Conversation!)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ChatParticipantConfiguration : IEntityTypeConfiguration<ChatParticipant>
{
    public void Configure(EntityTypeBuilder<ChatParticipant> b)
    {
        b.ToTable("ChatParticipant");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.ConversationId).HasColumnName("ConversationId");
        b.Property(x => x.UserEmail).HasColumnName("UserEmail").HasMaxLength(150);
        b.Property(x => x.DisplayName).HasColumnName("DisplayName").HasMaxLength(150).IsRequired();
        b.Property(x => x.Role).HasColumnName("Role").HasMaxLength(30).IsRequired();
        b.Property(x => x.GuestToken).HasColumnName("GuestToken").HasMaxLength(64);
        b.Property(x => x.Contact).HasColumnName("Contact").HasMaxLength(150);
        b.Property(x => x.JoinedOn).HasColumnName("JoinedOn");
        b.Property(x => x.LastReadMessageId).HasColumnName("LastReadMessageId");

        b.HasIndex(x => new { x.ConversationId, x.UserEmail });
        b.HasIndex(x => x.GuestToken);
    }
}

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("ChatMessage");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.ConversationId).HasColumnName("ConversationId");
        b.Property(x => x.SenderEmail).HasColumnName("SenderEmail").HasMaxLength(150);
        b.Property(x => x.SenderDisplayName).HasColumnName("SenderDisplayName").HasMaxLength(150).IsRequired();
        b.Property(x => x.SenderRole).HasColumnName("SenderRole").HasMaxLength(30).IsRequired();
        b.Property(x => x.Body).HasColumnName("Body").HasMaxLength(2000).IsRequired();
        b.Property(x => x.SentOn).HasColumnName("SentOn");

        b.Property(x => x.AttachmentFileName).HasColumnName("AttachmentFileName").HasMaxLength(150);
        b.Property(x => x.AttachmentContentType).HasColumnName("AttachmentContentType").HasMaxLength(100);
        // No max length: Base64 file content, sized in code against MaxAttachmentBytes.
        b.Property(x => x.AttachmentContent).HasColumnName("AttachmentContent");
        b.Property(x => x.AttachmentSize).HasColumnName("AttachmentSize");

        b.HasIndex(x => new { x.ConversationId, x.Id });
    }
}

public class ChatPushSubscriptionConfiguration : IEntityTypeConfiguration<ChatPushSubscription>
{
    public void Configure(EntityTypeBuilder<ChatPushSubscription> b)
    {
        b.ToTable("ChatPushSubscription");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(150).IsRequired();
        b.Property(x => x.Endpoint).HasColumnName("Endpoint").HasMaxLength(500).IsRequired();
        b.Property(x => x.P256dh).HasColumnName("P256dh").HasMaxLength(200).IsRequired();
        b.Property(x => x.Auth).HasColumnName("Auth").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => x.Endpoint).IsUnique();
        b.HasIndex(x => x.Email);
    }
}

public class ChatFcmSubscriptionConfiguration : IEntityTypeConfiguration<ChatFcmSubscription>
{
    public void Configure(EntityTypeBuilder<ChatFcmSubscription> b)
    {
        b.ToTable("ChatFcmSubscription");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(150).IsRequired();
        b.Property(x => x.Token).HasColumnName("Token").HasMaxLength(300).IsRequired();
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.Email);
    }
}

using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("Name").HasMaxLength(250);
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(50);
        b.Property(x => x.Roles).HasColumnName("Roles").HasMaxLength(150);
        b.Property(x => x.LoginId).HasColumnName("Loginid").HasMaxLength(20);
        b.Property(x => x.PhoneNumber).HasColumnName("Phn_no").HasMaxLength(50);
        b.Property(x => x.DateOfBirth).HasColumnName("User_date_of_birth");
        b.Property(x => x.RegisterDate).HasColumnName("Registerdate");
        b.Property(x => x.EndDate).HasColumnName("End_date");
        b.Property(x => x.IsActive).HasColumnName("IsActive");

        b.Property(x => x.PasswordHash).HasColumnName("PasswordHash").HasMaxLength(255);
        b.Property(x => x.RefreshToken).HasColumnName("RefreshToken").HasMaxLength(255);
        b.Property(x => x.RefreshTokenExpiry).HasColumnName("RefreshTokenExpiry");
        b.Property(x => x.FailedLoginCount).HasColumnName("FailedLoginCount");
        b.Property(x => x.LockoutEnd).HasColumnName("LockoutEnd");
        b.Property(x => x.LastLoginOn).HasColumnName("LastLoginOn");

        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
    }
}

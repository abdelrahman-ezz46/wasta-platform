using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasta.Domain.Catalog;

namespace Wasta.Infrastructure.Persistence.Configurations;

public class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> b)
    {
        b.ToTable("track");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Slug).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> b)
    {
        b.ToTable("skill");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    public void Configure(EntityTypeBuilder<Industry> b)
    {
        b.ToTable("industry");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("location");
        b.HasKey(x => x.Id);
        b.Property(x => x.City).IsRequired();
        b.Property(x => x.CountryCode).IsRequired().HasMaxLength(2).IsFixedLength();
    }
}

public class EmploymentTypeConfiguration : IEntityTypeConfiguration<EmploymentType>
{
    public void Configure(EntityTypeBuilder<EmploymentType> b)
    {
        b.ToTable("employment_type");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

public class WorkTypeConfiguration : IEntityTypeConfiguration<WorkType>
{
    public void Configure(EntityTypeBuilder<WorkType> b)
    {
        b.ToTable("work_type");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

public class ApplicationStatusConfiguration : IEntityTypeConfiguration<ApplicationStatus>
{
    public void Configure(EntityTypeBuilder<ApplicationStatus> b)
    {
        b.ToTable("application_status");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("payment_method");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

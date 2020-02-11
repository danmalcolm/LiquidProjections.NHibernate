using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    public class ProjectorState : IProjectorState
    {
        public virtual string Id { get; set; }
        public virtual long Checkpoint { get; set; }
        public virtual DateTime LastUpdateUtc { get; set; }

        public virtual string LastStreamId { get; set; }
    }

    internal class ProjectorStateConfiguration : IEntityTypeConfiguration<ProjectorState>
    {
        public void Configure(EntityTypeBuilder<ProjectorState> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Checkpoint).IsRequired();
        }
    }

//    internal sealed class ProjectorStateConfiguration : ClassMap<ProjectorState>
//    {
//        public ProjectorStateConfiguration()
//        {
//            Id(x => x.Id).Not.Nullable().Length(150);
//            Map(x => x.Checkpoint);
//            Map(x => x.LastUpdateUtc);
//            Map(x => x.LastStreamId);
//        }
//    }
}
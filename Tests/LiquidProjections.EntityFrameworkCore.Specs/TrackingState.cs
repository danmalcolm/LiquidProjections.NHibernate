using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    public class TrackingState : ITrackingState
    {
        public long Id { get; set; }
        public string ProjectorId { get; set; }
        public long Checkpoint { get; set; }
        public DateTime LastUpdateUtc { get; set; }
    }

    internal class TrackingStateConfiguration : IEntityTypeConfiguration<TrackingState>
    {
        public void Configure(EntityTypeBuilder<TrackingState> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedOnAdd();
            builder.Property(x => x.ProjectorId).IsRequired();
            builder.HasIndex(x => x.ProjectorId).IsUnique();
        }
    }
//    internal sealed class TrackingStateClassMap : ClassMap<TrackingState>
//    {
//        public TrackingStateClassMap()
//        {
//            Id(x => x.Id).GeneratedBy.Identity();
//            
//            Map(x => x.ProjectorId)
//                .Not.Nullable()
//                .Length(150)
//                .Index("IX_TrackingState_ProjectorId");
//            
//            Map(x => x.Checkpoint);
//            Map(x => x.LastUpdateUtc);
//        }
//    }
}
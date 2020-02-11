using System;

namespace LiquidProjections.EntityFrameworkCore
{
    public interface ITrackingState
    {
        string ProjectorId { get; set; }
        long Checkpoint { get; set; }
        DateTime LastUpdateUtc { get; set; }
    }
}
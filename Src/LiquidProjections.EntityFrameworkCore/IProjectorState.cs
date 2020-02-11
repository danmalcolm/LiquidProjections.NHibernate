using System;

namespace LiquidProjections.EntityFrameworkCore
{
    public interface IProjectorState
    {
        string Id { get; set; }
        long Checkpoint { get; set; }
        DateTime LastUpdateUtc { get; set; }
    }
}
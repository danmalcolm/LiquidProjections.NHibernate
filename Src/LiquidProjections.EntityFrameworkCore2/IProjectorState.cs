using System;

namespace LiquidProjections.EFCore
{
    public interface IProjectorState
    {
        string Id { get; set; }
        long Checkpoint { get; set; }
        DateTime LastUpdateUtc { get; set; }
    }
}
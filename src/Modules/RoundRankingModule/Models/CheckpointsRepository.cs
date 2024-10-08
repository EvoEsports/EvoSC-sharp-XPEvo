﻿namespace EvoSC.Modules.Official.RoundRankingModule.Models;

public class CheckpointsRepository : Dictionary<string, CheckpointData>
{
    public List<CheckpointData> GetSortedData()
    {
        return this.Values
            .OrderByDescending(cpData => cpData.CheckpointId)
            .ThenBy(cpData => cpData.Time.TotalMilliseconds)
            .ToList();
    }
}

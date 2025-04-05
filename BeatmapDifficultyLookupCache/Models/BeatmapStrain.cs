using System;
using System.Diagnostics.CodeAnalysis;

namespace BeatmapDifficultyLookupCache.Models
{
    [Serializable]
    public class BeatmapStrain
    {
        public double time { get; set; }
        public double star_rating { get; set; }

        public BeatmapStrain(double time, double star_rating)
        {
            this.time = time;
            this.star_rating = star_rating;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Dapper.Contrib.Extensions;
using Newtonsoft.Json.Linq;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace BeatmapDifficultyLookupCache.Models
{
    [Serializable]
    public class BeatmapStrains
    {
        public int beatmap_id { get; set; }
        public JArray? mods { get; set; }
        public Dictionary<string, double[]> strains { get; set; } = new Dictionary<string, double[]>();

        public BeatmapStrains(int beatmap_id, JArray? mods)
        {
            this.beatmap_id = beatmap_id;
            this.mods = mods;
            //this.skills = skills;
        }

        public void AddStrains(string skill, double[] strains)
        {
            this.strains[skill] = strains;
        }
    }
}

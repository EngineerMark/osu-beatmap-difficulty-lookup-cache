﻿using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Game.Rulesets;
using System.Linq;

namespace BeatmapDifficultyLookupCache
{
    public class ExtendedTaikoDifficultyCalculator : TaikoDifficultyCalculator, IExtendedDifficultyCalculator
    {
        private Skill[] skills;

        public ExtendedTaikoDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        public Skill[] GetSkills() => skills;
        public DifficultyHitObject[] GetDifficultyHitObjects(IBeatmap beatmap, double clockRate) => CreateDifficultyHitObjects(beatmap, clockRate).ToArray();

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        {
            this.skills = skills;
            return base.CreateDifficultyAttributes(beatmap, mods, skills, clockRate);
        }
    }
}

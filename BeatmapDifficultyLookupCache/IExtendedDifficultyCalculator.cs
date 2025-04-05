using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;

namespace BeatmapDifficultyLookupCache
{
    public interface IExtendedDifficultyCalculator
    {
        Skill[] GetSkills();
        DifficultyHitObject[] GetDifficultyHitObjects(IBeatmap beatmap, double clockRate);
    }
}
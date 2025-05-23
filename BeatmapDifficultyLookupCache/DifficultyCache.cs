// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatmapDifficultyLookupCache.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch.Difficulty;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Server.QueueProcessor;

namespace BeatmapDifficultyLookupCache
{
    public class DifficultyCache
    {
        private static readonly List<Ruleset> available_rulesets = getRulesets();
        private static readonly DifficultyAttributes empty_attributes = new DifficultyAttributes(Array.Empty<Mod>(), -1);

        private readonly Dictionary<DifficultyRequest, Task<DifficultyAttributes>> attributesCache = new Dictionary<DifficultyRequest, Task<DifficultyAttributes>>();
        private readonly Dictionary<DifficultyRequest, Task<BeatmapStrains>> strainsCache = new Dictionary<DifficultyRequest, Task<BeatmapStrains>>();
        private readonly ILogger logger;

        private readonly bool useDatabase;

        public DifficultyCache(ILogger<DifficultyCache> logger)
        {
            this.logger = logger;

            useDatabase = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_DATABASE_LOOKUPS"));
        }

        private static long totalLookups;

        public async Task<double> GetDifficultyRating(DifficultyRequest request)
        {
            if (request.BeatmapId == 0)
                return 0;

            if (useDatabase)
                return await getDatabasedDifficulty(request);

            return (await computeAttributes(request)).StarRating;
        }

        public async Task<DifficultyAttributes> GetAttributes(DifficultyRequest request)
        {
            if (request.BeatmapId == 0)
                return empty_attributes;

            if (useDatabase)
            {
                try
                {
                    return await getDatabasedAttributes(request);
                }
                catch
                {
                    // Databased attribute retrieval can fail if the database doesn't contain all attributes for a given beatmap.
                    // If such a case occurs, fall back to providing just the star rating rather than outputting exceptions.
                    return new DifficultyAttributes { StarRating = await GetDifficultyRating(request) };
                }
            }

            return await computeAttributes(request);
        }

        private async Task<DifficultyAttributes> getDatabasedAttributes(DifficultyRequest request)
        {
            int mods = getModBitwise(request.RulesetId, request.GetMods());

            beatmap_difficulty_attribute[] rawDifficultyAttributes;

            using (var conn = await DatabaseAccess.GetConnectionAsync())
            {
                rawDifficultyAttributes = (await conn.QueryAsync<beatmap_difficulty_attribute>(
                    "SELECT * FROM osu_beatmap_difficulty_attribs WHERE beatmap_id = @BeatmapId AND mode = @RulesetId AND mods = @ModValue", new
                    {
                        BeatmapId = request.BeatmapId,
                        RulesetId = request.RulesetId,
                        ModValue = mods
                    })).ToArray();
            }

            DifficultyAttributes attributes;

            switch (request.RulesetId)
            {
                case 0:
                    attributes = new OsuDifficultyAttributes();
                    break;

                case 1:
                    attributes = new TaikoDifficultyAttributes();
                    break;

                case 2:
                    attributes = new CatchDifficultyAttributes();
                    break;

                case 3:
                    attributes = new ManiaDifficultyAttributes();
                    break;

                default:
                    throw new InvalidOperationException($"Invalid ruleset: {request.RulesetId}");
            }

            attributes.FromDatabaseAttributes(
                rawDifficultyAttributes.ToDictionary(a => (int)a.attrib_id, e => (double)e.value),
                // Empty beatmap since its values aren't serialised out.
                new APIBeatmap());

            return attributes;
        }

        private async Task<float> getDatabasedDifficulty(DifficultyRequest request)
        {
            int mods = getModBitwise(request.RulesetId, request.GetMods());

            if (Interlocked.Increment(ref totalLookups) % 1000 == 0)
            {
                logger.LogInformation("difficulty lookup for (beatmap: {BeatmapId}, ruleset: {RulesetId}, mods: {Mods})",
                    request.BeatmapId,
                    request.RulesetId,
                    mods);
            }

            using (var conn = await DatabaseAccess.GetConnectionAsync())
            {
                return await conn.QueryFirstOrDefaultAsync<float>("SELECT diff_unified from osu.osu_beatmap_difficulty WHERE beatmap_id = @BeatmapId AND mode = @RulesetId and mods = @ModValue", new
                {
                    BeatmapId = request.BeatmapId,
                    RulesetId = request.RulesetId,
                    ModValue = mods
                });
            }
        }

        private async Task<DifficultyAttributes> computeAttributes(DifficultyRequest request)
        {
            Task<DifficultyAttributes>? task;

            lock (attributesCache)
            {
                if (!attributesCache.TryGetValue(request, out task))
                {
                    attributesCache[request] = task = Task.Run(async () =>
                    {
                        var apiMods = request.GetMods();

                        logger.LogInformation("Computing difficulty (beatmap: {BeatmapId}, ruleset: {RulesetId}, mods: {Mods})",
                            request.BeatmapId,
                            request.RulesetId,
                            apiMods.Select(m => m.ToString()));

                        try
                        {
                            var ruleset = available_rulesets.First(r => r.RulesetInfo.OnlineID == request.RulesetId);
                            var mods = apiMods.Select(m => m.ToMod(ruleset)).ToArray();
                            var beatmap = await getBeatmap(request.BeatmapId);

                            var difficultyCalculator = ruleset.CreateDifficultyCalculator(beatmap);
                            var attributes = difficultyCalculator.Calculate(mods);

                            // Trim a few members which we don't consume and only take up RAM.
                            // attributes.Mods = Array.Empty<Mod>();

                            return attributes;
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning("Request failed with \"{Message}\"", e.Message);
                            return empty_attributes;
                        }
                    });
                }
            }

            return await task;
        }

        public async Task<BeatmapStrains> GetStrains(DifficultyRequest request)
        {
            Task<BeatmapStrains>? task;
            lock (strainsCache)
            {
                if (!strainsCache.TryGetValue(request, out task))
                {
                    strainsCache[request] = task = Task.Run(async () =>
                    {
                        var apiMods = request.GetMods();
                        logger.LogInformation("Computing strains (beatmap: {BeatmapId}, ruleset: {RulesetId}, mods: {Mods})",
                            request.BeatmapId,
                            request.RulesetId,
                            apiMods.Select(m => m.ToString()));
                        try
                        {
                            var ruleset = available_rulesets.First(r => r.RulesetInfo.OnlineID == request.RulesetId);
                            var mods = apiMods.Select(m => m.ToMod(ruleset)).ToArray();
                            var beatmap = await getBeatmap(request.BeatmapId);
                            //var difficultyCalculator = ruleset.CreateDifficultyCalculator(beatmap);
                            var difficultyCalculator = RulesetHelper.GetExtendedDifficultyCalculator(ruleset.RulesetInfo, beatmap);
                            difficultyCalculator.Calculate(mods); //forces to calculate clockrate
                            //var attributes = difficultyCalculator.Calculate(mods);
                            //var skills = difficultyCalculator.CreateSkills();

                            Skill[] skills = ((IExtendedDifficultyCalculator)difficultyCalculator).GetSkills();
                            var strainSkills = skills.Where(x => x is StrainSkill or StrainDecaySkill).ToArray();

                            BeatmapStrains strains = new BeatmapStrains(request.BeatmapId, request.Mods);

                            foreach (var skill in strainSkills)
                            {
                                double[] _strains = ((StrainSkill)skill).GetCurrentStrainPeaks().ToArray();
                                //get name of the skill object
                                string name = skill.GetType().Name;

                                strains.AddStrains(name, _strains);
                            }

                            return strains;
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning("Request failed with \"{Message}\"", e.Message);
                            return new BeatmapStrains(request.BeatmapId, request.Mods);
                        }
                    });
                }
            }
            return await task;
        }
        public void Purge(int beatmapId)
        {
            logger.LogInformation("Purging (beatmap: {BeatmapId})", beatmapId);

            lock (attributesCache)
            {
                foreach (var req in attributesCache.Keys.ToArray())
                {
                    if (req.BeatmapId == beatmapId)
                        attributesCache.Remove(req);
                }
            }
        }

        private async Task<WorkingBeatmap> getBeatmap(int beatmapId)
        {
            //get executable path
            string executablePath = Assembly.GetExecutingAssembly().Location;
            string path = Path.Combine(Path.GetDirectoryName(executablePath) ?? string.Empty, "beatmaps", $"{beatmapId}.osu");
            // Check if the file is stored locally already (relative to executable)
            if (File.Exists(path))
            {
                logger.LogInformation("Loading beatmap from local cache ({BeatmapId})", beatmapId);
                // Read file as a StreamReader
                using (var fileStream
                    = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return new LoaderWorkingBeatmap(fileStream);
                }
            }

            logger.LogInformation("Downloading beatmap ({BeatmapId})", beatmapId);

            var req = new WebRequest(string.Format(Environment.GetEnvironmentVariable("DOWNLOAD_PATH") ?? "https://osu.ppy.sh/osu/{0}", beatmapId))
            {
                AllowInsecureRequests = true
            };

            await req.PerformAsync();

            if (req.ResponseStream.Length == 0)
                throw new Exception($"Retrieved zero-length beatmap ({beatmapId})!");

            LoaderWorkingBeatmap workingBeatmap = new LoaderWorkingBeatmap(req.ResponseStream);

            if(workingBeatmap.BeatmapInfo.Status == BeatmapOnlineStatus.Ranked || workingBeatmap.BeatmapInfo.Status == BeatmapOnlineStatus.Approved || workingBeatmap.BeatmapInfo.Status == BeatmapOnlineStatus.Loved)
            {
                req.ResponseStream.Seek(0, SeekOrigin.Begin);
                // Save the file locally
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            
                await File.WriteAllLinesAsync(path, new StreamReader(req.ResponseStream).ReadToEnd().Split('\n'));
            }

            return workingBeatmap;
        }

        private static List<Ruleset> getRulesets()
        {
            const string ruleset_library_prefix = "osu.Game.Rulesets";

            var rulesetsToProcess = new List<Ruleset>();

            foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, $"{ruleset_library_prefix}.*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    Type type = assembly.GetTypes().First(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset)));
                    rulesetsToProcess.Add((Ruleset)Activator.CreateInstance(type)!);
                }
                catch
                {
                    throw new Exception($"Failed to load ruleset ({file})");
                }
            }

            return rulesetsToProcess;
        }

        private static int getModBitwise(int rulesetId, List<APIMod> mods)
        {
            int val = 0;

            foreach (var mod in mods)
                val |= (int)getLegacyMod(mod);

            return val;

            LegacyMods getLegacyMod(APIMod mod)
            {
                switch (mod.Acronym)
                {
                    case "EZ":
                        return LegacyMods.Easy;

                    case "HR":
                        return LegacyMods.HardRock;

                    case "NC":
                        return LegacyMods.DoubleTime;

                    case "DT":
                        return LegacyMods.DoubleTime;

                    case "HT":
                        return LegacyMods.HalfTime;

                    case "4K":
                        return LegacyMods.Key4;

                    case "5K":
                        return LegacyMods.Key5;

                    case "6K":
                        return LegacyMods.Key6;

                    case "7K":
                        return LegacyMods.Key7;

                    case "8K":
                        return LegacyMods.Key8;

                    case "9K":
                        return LegacyMods.Key9;

                    case "FL" when rulesetId == 0:
                        return LegacyMods.Flashlight;

                    case "HD" when rulesetId == 0 && mods.Any(m => m.Acronym == "FL"):
                        return LegacyMods.Hidden;

                    case "TD" when rulesetId == 0:
                        return LegacyMods.TouchDevice;
                }

                return 0;
            }
        }
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Rulesets.Difficulty;

namespace BeatmapDifficultyLookupCache.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AttributesController : Controller
    {
        private readonly DifficultyCache cache;

        public AttributesController(DifficultyCache cache)
        {
            this.cache = cache;
        }

        //[HttpPost]
        //public Task<DifficultyAttributes> Post([FromBody] DifficultyRequest request)
        //{
        //    return cache.GetAttributes(request);
        //}

        private readonly int AsyncProcesses = 8;

        [HttpPost] //allow 2nd endpoint for multiple requests
        public async Task<DifficultyAttributes[]> Post([FromBody] DifficultyRequest[] requests)
        {
            //return await Task.WhenAll(requests.Select(cache.GetAttributes));

            //Run them async

            var tasks = new Task<DifficultyAttributes>[requests.Length];
            var results = new DifficultyAttributes[requests.Length];

            for (int i = 0; i < requests.Length; i++)
            {
                tasks[i] = cache.GetAttributes(requests[i]);
            }

            for (int i = 0; i < tasks.Length; i += AsyncProcesses)
            {
                await Task.WhenAll(tasks.Skip(i).Take(AsyncProcesses).ToArray());
            }

            for (int i = 0; i < tasks.Length; i++)
            {
                results[i] = tasks[i].Result;
            }

            return results;
        }

        //public Task<DifficultyAttributes> Post([FromBody] DifficultyRequest request) => cache.GetAttributes(request);
    }
}

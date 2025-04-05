using BeatmapDifficultyLookupCache.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Rulesets.Difficulty.Skills;
using System.Threading.Tasks;

namespace BeatmapDifficultyLookupCache.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class StrainController : Controller
    {
        private readonly DifficultyCache cache;

        public StrainController(DifficultyCache cache)
        {
            this.cache = cache;
        }

        [HttpPost]
        public Task<BeatmapStrains> Post([FromBody] DifficultyRequest request) => cache.GetStrains(request);
    }
}

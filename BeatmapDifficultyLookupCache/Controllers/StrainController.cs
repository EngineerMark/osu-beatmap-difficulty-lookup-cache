using BeatmapDifficultyLookupCache.Models;
using Microsoft.AspNetCore.Mvc;
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

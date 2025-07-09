using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Goals;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers
{
    /// <summary>
    /// RESTful API controller for managing administrator-defined high-level goals.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GoalsController : ControllerBase
    {
        private readonly IGoalStore _store;

        public GoalsController(IGoalStore store) => _store = store;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Goal>>> GetGoals() => Ok(await _store.GetAllAsync());

        [HttpGet("{id}")]
        public async Task<ActionResult<Goal>> GetGoal(Guid id)
        {
            var goal = await _store.GetAsync(id);
            return goal is null ? NotFound() : Ok(goal);
        }

        [HttpPost]
        public async Task<ActionResult<Goal>> CreateGoal([FromBody] Goal goal)
        {
            var created = await _store.CreateAsync(goal);
            return CreatedAtAction(nameof(GetGoal), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateGoal(Guid id, [FromBody] Goal goal)
        {
            if (id != goal.Id)
                return BadRequest("Payload id mismatch");

            try
            {
                await _store.UpdateAsync(goal);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteGoal(Guid id)
        {
            await _store.DeleteAsync(id);
            return NoContent();
        }
    }
} 
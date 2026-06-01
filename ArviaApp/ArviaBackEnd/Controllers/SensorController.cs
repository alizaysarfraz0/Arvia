using ArviaBackEnd.Data;
using ArviaBackEnd.Models;
using Microsoft.AspNetCore.Mvc;

namespace ArviaBackEnd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SensorsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        // Inject the database context so we can save and read data
        public SensorsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: api/sensors/data (Hardware sends data here)
        [HttpPost("data")]
        public async Task<IActionResult> ReceiveSensorData([FromBody] SensorReading reading)
        {
            // Add the reading to the database and save
            _context.SensorReadings.Add(reading);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Sensor data saved successfully!" });
        }

        // GET: api/sensors/latest (Blazor frontend fetches data from here)
        [HttpGet("latest")]
        public IActionResult GetLatestReading()
        {
            var latest = _context.SensorReadings
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();

            if (latest == null) return NotFound(new { Message = "No data available." });

            return Ok(latest);
        }
    }
}
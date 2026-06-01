using ArviaBackEnd.Models;
using System.Text;
using System.Text.Json;

namespace ArviaBackEnd.Services
{
    public class SensorSimulatorService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SensorSimulatorService> _logger;

        public SensorSimulatorService(IHttpClientFactory httpClientFactory, ILogger<SensorSimulatorService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Sensor Simulator started.");

            // Creating an HttpClient to talk to our own API
            var client = _httpClientFactory.CreateClient();
            var apiUrl = "http://localhost:5154/api/sensors/data"; 

            var random = new Random();

            while (!stoppingToken.IsCancellationRequested)
            {
                // Generating fake data
                var fakeReading = new SensorReading
                {
                    DeviceId = "SIM-001",
                    Temperature = Math.Round(20.0 + (random.NextDouble() * 15.0), 1), // 20.0 to 35.0 C
                    Humidity = Math.Round(40.0 + (random.NextDouble() * 40.0), 1),    // 40.0 to 80.0 %
                    SoilMoisture = Math.Round(10.0 + (random.NextDouble() * 60.0), 1), // 10.0 to 70.0 %
                    PhLevel = Math.Round(5.5 + (random.NextDouble() * 2.0), 1)         // 5.5 to 7.5
                };

                // 2. Packaging it as JSON
                var json = JsonSerializer.Serialize(fakeReading);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    // 3. Sending it to the API
                    var response = await client.PostAsync(apiUrl, content, stoppingToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Sent fake data: Temp={fakeReading.Temperature}°C, Moisture={fakeReading.SoilMoisture}%");
                    }
                    else
                    {
                         _logger.LogWarning($"Failed to send data. Status code: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error sending fake data: {ex.Message}");
                }

                // 4. Waiting 10 seconds before sending the next one
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
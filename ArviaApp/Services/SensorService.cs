namespace ArviaApp.Services
{
    public class SensorReading
    {
        public double PHLevel { get; set; }
        public double HumidityPercent { get; set; }
        public double SoilMoisturePercent { get; set; }
        public double TemperatureCelsius { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    // Interface for dependency injection
    public interface ISensorService
    {
        Task<SensorReading> GetLatestReadingAsync();
    }

    public class MockSensorService : ISensorService
    {
        public async Task<SensorReading> GetLatestReadingAsync()
        {
            // Simulating sensor read delay
            await Task.Delay(500);

            // Returning realistic mock data for healthy soil
            return new SensorReading
            {
                PHLevel = 6.5,
                HumidityPercent = 55.2,
                SoilMoisturePercent = 30.5,
                TemperatureCelsius = 22.1,
                Recommendations = new List<string>
                {
                    "pH level is optimal for most vegetables.",
                    "Moisture is slightly low. Consider watering soon.",
                    "Temperature is ideal for current growth stage."
                }
            };
        }
    }
}
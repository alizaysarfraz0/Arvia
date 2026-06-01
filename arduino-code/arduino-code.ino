#include "esp_camera.h"
#include <WiFi.h>
#include <WebServer.h>
#include <Wire.h>
#include <Adafruit_ADS1X15.h>
#include <OneWire.h>
#include <DallasTemperature.h>
#include <HTTPClient.h>

// -------- Configuration --------
const char* ssid = "AndroidAPdc34";               // Update with your WiFi SSID
const char* password = "12345678";       // Update with your WiFi Password
const char* backendUrl = "http://192.168.43.230:5154/api/sensors/data"; // Update with your PC's local IP address

// -------- Pins --------
#define FLASH_LED_PIN 4
#define ONE_WIRE_BUS 13

// -------- Sensors --------
OneWire oneWire(ONE_WIRE_BUS);
DallasTemperature tempSensor(&oneWire);
Adafruit_ADS1115 ads;

// -------- Web --------
WebServer server(80);

// -------- Variables --------
float temperature = 0;
float phVoltage = 0;
float phValue = 0;
int soilPercent = 0;
float humidity = 0; // Keeping for backend schema compatibility

// smoothing
int lastSoil = 0;
float lastPH = 0;

// Timing for POST requests
unsigned long lastPostTime = 0;
const unsigned long postInterval = 5000; // POST data every 5 seconds

// -------- Camera config --------
camera_config_t config;

// -------- Read average ADC --------
int readAvg(int ch) {
  long sum = 0;
  for (int i = 0; i < 10; i++) {
    sum += ads.readADC_SingleEnded(ch);
    delay(5);
  }
  return sum / 10;
}

// -------- pH conversion --------
float convertPH(float voltage) {
  float ph = 7 + ((3.30 - voltage) / 0.18);
  return constrain(ph, 0, 14);
}

// -------- Setup --------
void setup() {
  Serial.begin(115200);

  pinMode(FLASH_LED_PIN, OUTPUT);
  digitalWrite(FLASH_LED_PIN, LOW);

  Wire.begin(14, 15);
  ads.begin();
  tempSensor.begin();

  // -------- Camera pins --------
  config.ledc_channel = LEDC_CHANNEL_0;
  config.ledc_timer = LEDC_TIMER_0;
  config.pin_d0 = 5;
  config.pin_d1 = 18;
  config.pin_d2 = 19;
  config.pin_d3 = 21;
  config.pin_d4 = 36;
  config.pin_d5 = 39;
  config.pin_d6 = 34;
  config.pin_d7 = 35;
  config.pin_xclk = 0;
  config.pin_pclk = 22;
  config.pin_vsync = 25;
  config.pin_href = 23;
  config.pin_sscb_sda = 26;
  config.pin_sscb_scl = 27;
  config.pin_pwdn = 32;
  config.pin_reset = -1;
  config.xclk_freq_hz = 20000000;
  config.pixel_format = PIXFORMAT_JPEG;

  config.frame_size = FRAMESIZE_QVGA;   // stable
  config.jpeg_quality = 12;
  config.fb_count = 2;

  if (esp_camera_init(&config) != ESP_OK) {
    Serial.println("Camera init failed");
    return;
  }

  // -------- WiFi --------
  WiFi.begin(ssid, password);
  Serial.print("Connecting");

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }

  Serial.println("\nConnected!");
  Serial.println(WiFi.localIP());

  // -------- Routes --------
  server.on("/", handleRoot);
  server.on("/stream", HTTP_GET, handleStream);
  server.on("/capture", HTTP_GET, handleCapture);

  server.begin();
}

// -------- Loop --------
void loop() {
  server.handleClient();
  readSensors();

  if (millis() - lastPostTime >= postInterval) {
    lastPostTime = millis();
    sendDataToBackend();
  }
}

// -------- Read Sensors --------
void readSensors() {
  // ---- Temperature ----
  tempSensor.requestTemperatures();
  float t = tempSensor.getTempCByIndex(0);
  if (t != -127.0) {
    temperature = t;
  }

  // ---- pH ----
  int adc0 = readAvg(0);
  phVoltage = (adc0 * 0.1875) / 1000.0;
  float phRaw = convertPH(phVoltage);
  phValue = (phRaw + lastPH) / 2;
  lastPH = phValue;

  // ---- Soil ----
  int adc1 = readAvg(1);
  int soilRaw = map(adc1, 11090, 5200, 0, 100);
  soilRaw = constrain(soilRaw, 0, 100);

  soilPercent = (soilRaw + lastSoil) / 2;
  lastSoil = soilPercent;
  
  // Note: Humidity is set to a constant 0 or can be implemented later.
  humidity = 0; 
}

// -------- Send Data to C# Backend --------
void sendDataToBackend() {
  if (WiFi.status() == WL_CONNECTED) {
    HTTPClient http;
    http.begin(backendUrl);
    http.addHeader("Content-Type", "application/json");

    // Construct JSON payload
    String payload = "{";
    payload += "\"DeviceId\": \"ESP32\", ";
    payload += "\"Temperature\": " + String(temperature) + ", ";
    payload += "\"Humidity\": " + String(humidity) + ", ";
    payload += "\"PhLevel\": " + String(phValue) + ", ";
    payload += "\"SoilMoisture\": " + String(soilPercent);
    payload += "}";

    int httpResponseCode = http.POST(payload);

    if (httpResponseCode > 0) {
      Serial.print("POST Response code: ");
      Serial.println(httpResponseCode);
    } else {
      Serial.print("Error sending POST: ");
      Serial.println(httpResponseCode);
    }
    http.end();
  } else {
    Serial.println("Error in WiFi connection");
  }
}

// -------- Web Page --------
void handleRoot() {
  String html = "<html><head><meta name='viewport' content='width=device-width, initial-scale=1.0'></head><body>";
  html += "<h2>ESP32 Smart Farm</h2>";
  html += "<img src='/stream' style='width:100%; max-width:400px;'><br><br>";
  html += "Temperature: " + String(temperature) + " C<br>";
  html += "pH: " + String(phValue) + "<br>";
  html += "Soil Moisture: " + String(soilPercent) + "%<br>";
  html += "</body></html>";
  server.send(200, "text/html", html);
}

// -------- Still Capture Endpoint --------
void handleCapture() {
  camera_fb_t * fb = esp_camera_fb_get();
  if (!fb) {
    server.send(500, "text/plain", "Camera capture failed");
    return;
  }
  
  WiFiClient client = server.client();
  String response = "HTTP/1.1 200 OK\r\n";
  response += "Content-Type: image/jpeg\r\n";
  response += "Content-Disposition: inline; filename=capture.jpg\r\n";
  response += "Access-Control-Allow-Origin: *\r\n"; // Useful for fetching via JS
  response += "\r\n";
  
  server.sendContent(response);
  client.write(fb->buf, fb->len);
  
  esp_camera_fb_return(fb);
}

// -------- Stream Endpoint --------
void handleStream() {
  WiFiClient client = server.client();
  String response = "HTTP/1.1 200 OK\r\n";
  response += "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n";
  response += "Access-Control-Allow-Origin: *\r\n"; // Enable cross-origin for frontend
  response += "\r\n";
  server.sendContent(response);

  while (client.connected()) {
    camera_fb_t * fb = esp_camera_fb_get();
    if (!fb) continue;

    server.sendContent("--frame\r\n");
    server.sendContent("Content-Type: image/jpeg\r\n\r\n");

    client.write(fb->buf, fb->len);
    server.sendContent("\r\n");

    esp_camera_fb_return(fb);

    delay(60);
  }
}
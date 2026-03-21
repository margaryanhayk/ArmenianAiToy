/*
 * Armenian AI Toy — Phase 1: Text Chat via Browser
 *
 * ESP32 serves a local web page over Wi-Fi.
 * User types an Armenian question in the browser.
 * ESP32 forwards the question to the .NET backend.
 * Backend calls OpenAI GPT-4o and returns an Armenian answer.
 * Answer is displayed in the browser.
 *
 * Hardware: ESP32 DevKit (CH340 / Type-C)
 * No microphone or speaker needed for Phase 1.
 */

#include <WiFiManager.h>
#include <ESPAsyncWebServer.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include "config.h"

// ========================
// Globals
// ========================
AsyncWebServer server(WEB_SERVER_PORT);

// Backend URL — configured via WiFiManager custom parameter
char backendUrl[BACKEND_URL_MAX_LEN] = "http://192.168.1.100:5000";

// WiFiManager custom parameter for backend URL
WiFiManagerParameter customBackendUrl("backend_url", "Backend URL", backendUrl, BACKEND_URL_MAX_LEN);

// ========================
// HTML Page (stored in flash via PROGMEM)
// ========================
const char index_html[] PROGMEM = R"rawliteral(
<!DOCTYPE html>
<html lang="hy">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Արեգ — AI Խdelays delays</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      min-height: 100vh;
      display: flex;
      justify-content: center;
      align-items: center;
      padding: 16px;
    }
    .container {
      background: white;
      border-radius: 24px;
      padding: 24px;
      width: 100%;
      max-width: 480px;
      box-shadow: 0 20px 60px rgba(0,0,0,0.3);
    }
    .header {
      text-align: center;
      margin-bottom: 20px;
    }
    .header h1 {
      font-size: 28px;
      color: #333;
    }
    .header p {
      color: #888;
      font-size: 14px;
      margin-top: 4px;
    }
    .chat-box {
      height: 360px;
      overflow-y: auto;
      border: 2px solid #eee;
      border-radius: 16px;
      padding: 16px;
      margin-bottom: 16px;
      background: #fafafa;
    }
    .msg {
      margin-bottom: 12px;
      padding: 10px 14px;
      border-radius: 16px;
      max-width: 85%;
      word-wrap: break-word;
      line-height: 1.5;
      font-size: 15px;
    }
    .msg.user {
      background: #667eea;
      color: white;
      margin-left: auto;
      border-bottom-right-radius: 4px;
    }
    .msg.bot {
      background: #e8e8e8;
      color: #333;
      border-bottom-left-radius: 4px;
    }
    .msg.error {
      background: #ffe0e0;
      color: #c00;
      border-bottom-left-radius: 4px;
    }
    .msg.thinking {
      background: #e8e8e8;
      color: #888;
      border-bottom-left-radius: 4px;
      font-style: italic;
    }
    .input-row {
      display: flex;
      gap: 8px;
    }
    .input-row input {
      flex: 1;
      padding: 14px 16px;
      border: 2px solid #ddd;
      border-radius: 16px;
      font-size: 16px;
      outline: none;
      transition: border-color 0.2s;
    }
    .input-row input:focus {
      border-color: #667eea;
    }
    .input-row button {
      padding: 14px 24px;
      background: #667eea;
      color: white;
      border: none;
      border-radius: 16px;
      font-size: 16px;
      cursor: pointer;
      transition: background 0.2s;
    }
    .input-row button:hover { background: #5a6fd6; }
    .input-row button:disabled {
      background: #ccc;
      cursor: not-allowed;
    }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <h1>&#x2600; Areg</h1>
      <p>Armenian AI Toy — Phase 1</p>
    </div>
    <div class="chat-box" id="chat"></div>
    <div class="input-row">
      <input type="text" id="msg" placeholder="Type your question..." autocomplete="off" />
      <button id="btn" onclick="sendMsg()">Send</button>
    </div>
  </div>
  <script>
    const chat = document.getElementById('chat');
    const msgInput = document.getElementById('msg');
    const btn = document.getElementById('btn');

    msgInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !btn.disabled) sendMsg();
    });

    function addMsg(text, cls) {
      const div = document.createElement('div');
      div.className = 'msg ' + cls;
      div.textContent = text;
      chat.appendChild(div);
      chat.scrollTop = chat.scrollHeight;
      return div;
    }

    async function sendMsg() {
      const text = msgInput.value.trim();
      if (!text) return;

      addMsg(text, 'user');
      msgInput.value = '';
      btn.disabled = true;

      const thinkingDiv = addMsg('Thinking...', 'thinking');

      try {
        const res = await fetch('/api/chat', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ message: text })
        });

        chat.removeChild(thinkingDiv);

        if (res.ok) {
          const data = await res.json();
          addMsg(data.response, 'bot');
        } else {
          const err = await res.text();
          addMsg('Error: ' + err, 'error');
        }
      } catch (e) {
        chat.removeChild(thinkingDiv);
        addMsg('Connection error: ' + e.message, 'error');
      }

      btn.disabled = false;
      msgInput.focus();
    }
  </script>
</body>
</html>
)rawliteral";

// ========================
// Setup
// ========================
void setup() {
  Serial.begin(115200);
  Serial.println("\n\n=== Armenian AI Toy — Phase 1 ===");

  // LED setup
  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, HIGH); // LED on during setup

  // WiFiManager setup
  WiFiManager wm;

  // Add custom parameter for backend URL
  wm.addParameter(&customBackendUrl);

  // Generate unique AP name from MAC
  String apName = AP_NAME_PREFIX + String((uint32_t)ESP.getEfuseMac(), HEX);
  Serial.println("AP name: " + apName);

  // Auto-connect or start config portal
  // Timeout: 180 seconds, then restart
  wm.setConfigPortalTimeout(180);

  if (!wm.autoConnect(apName.c_str())) {
    Serial.println("WiFi connection failed. Restarting...");
    ESP.restart();
  }

  // Save the backend URL from the config portal
  strncpy(backendUrl, customBackendUrl.getValue(), BACKEND_URL_MAX_LEN - 1);
  backendUrl[BACKEND_URL_MAX_LEN - 1] = '\0';

  Serial.println("WiFi connected!");
  Serial.print("IP address: ");
  Serial.println(WiFi.localIP());
  Serial.print("Backend URL: ");
  Serial.println(backendUrl);
  Serial.print("Free heap: ");
  Serial.println(ESP.getFreeHeap());

  // ========================
  // Web Server Routes
  // ========================

  // Serve the chat HTML page
  server.on("/", HTTP_GET, [](AsyncWebServerRequest *request) {
    request->send(200, "text/html", index_html);
  });

  // Chat API endpoint — proxies to backend
  server.on("/api/chat", HTTP_POST,
    [](AsyncWebServerRequest *request) {},
    NULL,
    [](AsyncWebServerRequest *request, uint8_t *data, size_t len, size_t index, size_t total) {
      // Accumulate body
      if (index == 0) {
        request->_tempObject = new String();
      }
      String *body = (String *)request->_tempObject;
      body->concat((char *)data, len);

      // When all data received, process it
      if (index + len == total) {
        Serial.println("Chat request: " + *body);

        // Parse the incoming JSON
        JsonDocument reqDoc;
        DeserializationError err = deserializeJson(reqDoc, *body);
        delete body;
        request->_tempObject = nullptr;

        if (err) {
          request->send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
          return;
        }

        const char* message = reqDoc["message"];
        if (!message || strlen(message) == 0) {
          request->send(400, "application/json", "{\"error\":\"Message is required\"}");
          return;
        }

        // Forward to backend
        String response = forwardToBackend(message);

        if (response.length() == 0) {
          request->send(502, "application/json", "{\"error\":\"Backend unavailable\"}");
          return;
        }

        request->send(200, "application/json", response);
      }
    }
  );

  // Status endpoint
  server.on("/api/status", HTTP_GET, [](AsyncWebServerRequest *request) {
    JsonDocument doc;
    doc["wifi"] = WiFi.isConnected();
    doc["ip"] = WiFi.localIP().toString();
    doc["rssi"] = WiFi.RSSI();
    doc["freeHeap"] = ESP.getFreeHeap();
    doc["backendUrl"] = backendUrl;
    doc["uptime"] = millis() / 1000;

    String json;
    serializeJson(doc, json);
    request->send(200, "application/json", json);
  });

  server.begin();
  Serial.println("Web server started on port " + String(WEB_SERVER_PORT));
  Serial.println("Open http://" + WiFi.localIP().toString() + " in your browser");

  // LED off — setup complete
  digitalWrite(LED_PIN, LOW);
}

// ========================
// Loop
// ========================
void loop() {
  // Blink LED slowly to show device is alive
  static unsigned long lastBlink = 0;
  if (millis() - lastBlink > 3000) {
    digitalWrite(LED_PIN, HIGH);
    delay(100);
    digitalWrite(LED_PIN, LOW);
    lastBlink = millis();
  }

  // Monitor heap
  static unsigned long lastHeapLog = 0;
  if (millis() - lastHeapLog > 30000) {
    Serial.printf("[Heap] Free: %d bytes\n", ESP.getFreeHeap());
    lastHeapLog = millis();
  }
}

// ========================
// Backend Communication
// ========================
String forwardToBackend(const char* message) {
  HTTPClient http;
  String url = String(backendUrl) + "/api/chat";

  Serial.println("Forwarding to: " + url);

  http.begin(url);
  http.addHeader("Content-Type", "application/json");
  http.setTimeout(HTTP_TIMEOUT_MS);

  // Build request JSON
  JsonDocument reqDoc;
  reqDoc["message"] = message;
  String requestBody;
  serializeJson(reqDoc, requestBody);

  // Send POST request
  int httpCode = http.POST(requestBody);

  String result = "";

  if (httpCode == 200) {
    result = http.getString();
    Serial.println("Backend response: " + result);
  } else {
    Serial.printf("Backend error: HTTP %d\n", httpCode);
    if (httpCode > 0) {
      Serial.println("Response: " + http.getString());
    }
  }

  http.end();
  return result;
}

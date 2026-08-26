#include "ETS2Telemetry.h"
ETS2Telemetry ets2;
int minR = 400, maxR = 623, targetMinR = 400, targetMaxR = 623;
const int POT_PIN = A0;

void setup() {
  Serial.begin(9600);
  pinMode(LED_BUILTIN, OUTPUT);
  for(int i=0;i<3;i++) { digitalWrite(LED_BUILTIN, HIGH); delay(100); digitalWrite(LED_BUILTIN, LOW); delay(100); }
  Joystick.setSteeringRange(minR, maxR);
}

void loop() {
  if (Serial.available() > 0) {
    String data = Serial.readStringUntil('\n');
    data.trim();
    digitalWrite(LED_BUILTIN, HIGH);
    if (data == "waiting") {
      ets2.conn = false; targetMinR = 400; targetMaxR = 623;
      Serial.print("RANGE:"); Serial.print(minR); Serial.print(","); Serial.println(maxR);
    } else {
      ets2.parse(data);
      if (ets2.conn) {
        float s = ets2.spd_f;
        if (s < 10) { targetMinR = 282; targetMaxR = 742; }
        else if (s < 30) { targetMinR = 256; targetMaxR = 768; }
        else if (s < 60) { targetMinR = 179; targetMaxR = 844; }
        else if (s < 80) { targetMinR = 128; targetMaxR = 896; }
        else if (s < 100) { targetMinR = 77; targetMaxR = 947; }
        else if (s < 140) { targetMinR = 38; targetMaxR = 986; }
        else { targetMinR = 0; targetMaxR = 1023; }
        if (ets2.pb) { targetMinR = 450; targetMaxR = 573; }
        if (!ets2.eng) { targetMinR = 400; targetMaxR = 623; }
      }
      Serial.print("RANGE:"); Serial.print(minR); Serial.print(","); Serial.println(maxR);
    }
    digitalWrite(LED_BUILTIN, LOW);
  }
  if (minR != targetMinR || maxR != targetMaxR) {
    int diffMin = targetMinR - minR; int diffMax = targetMaxR - maxR;
    minR += diffMin * 0.05; maxR += diffMax * 0.05;
    if (abs(diffMin) < 2) minR = targetMinR; if (abs(diffMax) < 2) maxR = targetMaxR;
    Joystick.setSteeringRange(minR, maxR);
  }
  delay(10);
}
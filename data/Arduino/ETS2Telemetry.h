#ifndef ETS2TELEMETRY_H
#define ETS2TELEMETRY_H

#include <Arduino.h>

class ETS2Telemetry {
private:
  int findInt(String d, String k) {
    int i = d.indexOf(k);
    if (i < 0) return 0;
    i += k.length();
    int e = d.indexOf(',', i);
    if (e < 0) e = d.length();
    return d.substring(i, e).toInt();
  }
  float findFloat(String d, String k) {
    int i = d.indexOf(k);
    if (i < 0) return 0;
    i += k.length();
    int e = d.indexOf(',', i);
    if (e < 0) e = d.length();
    return d.substring(i, e).toFloat();
  }
public:
  float spd_f; bool pb; bool eng; int fuel; bool conn;
  ETS2Telemetry() { conn = false; spd_f = 0; pb = false; eng = false; fuel = 0; }
  void parse(String d) {
    if (d == "waiting") { conn = false; return; }
    if (d.startsWith("connected,S:")) {
      conn = true; String dataPart = d.substring(10);
      spd_f = findFloat(dataPart, "S:");
      pb = (findInt(dataPart, "PB:") == 1);
      eng = (findInt(dataPart, "EO:") == 1);
      fuel = findInt(dataPart, "F:");
      return;
    }
    if (!conn) return;
    spd_f = findFloat(d, "S:");
    pb = (findInt(d, "PB:") == 1);
    eng = (findInt(d, "EO:") == 1);
    fuel = findInt(d, "F:");
  }
};
#endif
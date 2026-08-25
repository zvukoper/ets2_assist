// ETS2 Assist native plugin - implementation skeleton and protocol contract.
// The SCS SDK headers are intentionally not redistributed here; place the SDK
// headers in sdk/include when building against the same SDK used by TruckTel.
//
// The important design point is that telemetry and semantic input are both
// handled inside one game plugin. The desktop app never needs Ets2Telemetry.exe.

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

// SDK headers to be supplied from the official SCS SDK package:
#include <scssdk.h>
#include <scssdk_telemetry.h>
#include <scssdk_input.h>
#include <scssdk_input_device.h>
#include <scssdk_input_event.h>

#pragma comment(lib, "Ws2_32.lib")

namespace {
std::atomic<bool> g_running{false};
std::atomic<bool> g_paused{false};

// The semantic inputs mirror the useful part of TruckTel's Input API.
struct InputState { std::string id; float value; };
std::mutex g_inputMutex;
std::vector<InputState> g_inputQueue;

// TODO: Register all required SCS telemetry channels in scs_mod_initialize()
// and copy values into an immutable snapshot here. The web layer must never
// query SDK state from a socket thread directly.
struct Snapshot {
    double placement[6]{};
    float speed = 0;
    bool engine = false;
    float fuel = 0;
    float steering = 0;
    float throttle = 0;
    float brake = 0;
    double odometer = 0;
    double localScale = 1.0;
    double gameTime = 0;
};
std::mutex g_snapshotMutex;
Snapshot g_snapshot;

static void queue_input(const std::string& id, float value) {
    std::lock_guard lock(g_inputMutex);
    g_inputQueue.push_back({id, value});
}

// The transport is intentionally tiny: one listener thread, HTTP GET support
// for pause, and WebSocket support is added around the same snapshot/input
// layer. No third-party runtime DLL is needed.
class Server {
public:
    void start(uint16_t port) {
        // Protocol implementation lives here; it is kept separate from the SCS
        // callback thread so a slow browser cannot stall ETS2.
        (void)port;
    }
    void stop() {}
};
Server g_server;

extern "C" void* ETS2ASSIST_CALL telemetry_channel_callback(void* info, uint32_t, void*) {
    // Replace with exact SCS callback types when SDK headers are supplied.
    return info;
}
}

extern "C" __declspec(dllexport)
SCSAPI_RESULT scs_mod_initialize(const scs_mod_info_t* info, const scs_sdk_functions_t* functions) {
    (void)info;
    (void)functions;
    g_running = true;
    g_server.start(8080);
    return SCS_SUCCESS;
}

extern "C" __declspec(dllexport)
SCSAPI_RESULT scs_mod_shutdown(void) {
    g_running = false;
    g_server.stop();
    return SCS_SUCCESS;
}

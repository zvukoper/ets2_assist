using System;
using System.IO.MemoryMappedFiles;

namespace ETS2_Assist_GUI
{
    public static class SCSController
    {
        private const string MapName = "Local\\SCSControls";
        private const int FloatCount = 4;
        private const int BoolCount = 38;
        private const int FloatSize = 4;
        private const int BoolSize = 1;
        private const int TotalSize = FloatCount * FloatSize + BoolCount * BoolSize;

        public const int ActionPause = 4;
        public const int ActionParkingBrake = 5;

        private static MemoryMappedFile? _mmf;
        private static MemoryMappedViewAccessor? _accessor;
        private static bool _initialized = false;
        private static bool _lastPauseState = false;

        // Событие для логирования
        public static event Action<string>? OnLog;

        private static void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        public static bool Initialize()
        {
            try
            {
                Log("[SCS] Attempting to create/open shared memory: " + MapName);
                _mmf = MemoryMappedFile.CreateOrOpen(MapName, TotalSize);
                Log("[SCS] Shared memory opened successfully.");

                _accessor = _mmf.CreateViewAccessor(0, TotalSize);
                Log("[SCS] View accessor created.");

                // Тестовая запись
                try
                {
                    _accessor.Write(0, (byte)123);
                    byte test = _accessor.ReadByte(0);
                    if (test == 123)
                    {
                        Log("[SCS] Test write successful.");
                    }
                    else
                    {
                        Log("[SCS] Test write FAILED (read back " + test + ").");
                    }
                    _accessor.Write(0, (byte)0);
                }
                catch (Exception ex)
                {
                    Log("[SCS] Test write EXCEPTION: " + ex.Message);
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Log("[SCS] Init error: " + ex.Message);
                return false;
            }
        }

        public static bool SetPause(bool enabled)
        {
            if (!_initialized && !Initialize())
            {
                Log("[SCS] SetPause failed: not initialized.");
                return false;
            }
            Log($"[SCS] SetPause({enabled}) - writing to index {ActionPause}");
            bool result = SetBool(ActionPause, enabled);
            if (result)
            {
                _lastPauseState = enabled;
                Log($"[SCS] SetPause({enabled}) - SUCCESS.");
            }
            else
            {
                Log($"[SCS] SetPause({enabled}) - FAILED.");
            }
            return result;
        }

        public static bool SetParkingBrake(bool enabled)
        {
            if (!_initialized && !Initialize())
                return false;
            return SetBool(ActionParkingBrake, enabled);
        }

        private static bool SetBool(int index, bool value)
        {
            if (!_initialized || _accessor == null)
            {
                Log("[SCS] SetBool: not initialized or accessor null.");
                return false;
            }
            try
            {
                long offset = FloatCount * FloatSize + index * BoolSize;
                Log($"[SCS] SetBool: writing {(value ? 1 : 0)} to offset {offset} (index {index})");
                _accessor.Write(offset, (byte)(value ? 1 : 0));

                byte readback = _accessor.ReadByte(offset);
                if (readback == (value ? 1 : 0))
                {
                    Log($"[SCS] SetBool: readback verified.");
                    return true;
                }
                else
                {
                    Log($"[SCS] SetBool: readback mismatch! Expected {(value ? 1 : 0)}, got {readback}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[SCS] SetBool: EXCEPTION: {ex.Message}");
                return false;
            }
        }

        public static void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _initialized = false;
            Log("[SCS] Disposed.");
        }
    }
}
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;   // WRITE_EXTERNAL_STORAGE permission
#endif

namespace Study
{
    public enum VehicleKind { EScooter, Segway, Unicycle, Skateboard }
    public enum DifficultyLabel { Easy, Medium, Hard }

    [Serializable]
    public class RideSessionInfo
    {
        public string sessionId;
        public VehicleKind vehicle;
        public string participantId;
        public DateTime startTimeUtc;
        public DateTime? endTimeUtc;
        public int totalCoinsPlanned;
        public int totalCoinsCollected;
        public float durationSec;
    }

    public class StudyLogger : MonoBehaviour
    {
        public static StudyLogger Instance { get; private set; }

        [Header("Session")]
        public string participantId = "";

        [Header("Telemetry")]
        [Tooltip("Samples per second for path/inputs logging.")]
        public float telemetryHz = 10f;

        [Header("Android Folder")]
        [Tooltip("Top-level folder name under /storage/emulated/0/Documents/. (not used if you want flat in Documents)")]
        public string androidDocumentsSubfolder = ""; // leave empty to write flat into /Documents

        [Header("Foot Sensors (fallback source)")]
        [Tooltip("If VehicleDataReceiver.footSensors is empty, values will be read from this component.")]
        public FootSensorInput footInput;  // drag your FootSensorInput here

        [Header("Foot Sensors (key hints)")]
        [Tooltip("Used to build fsn_/fsr_ columns immediately, before first feet packet arrives.")]
        public string[] footKeysHint = new string[] {
            "left_heel_norm","left_toe_norm","left_mid_l_norm","left_mid_r_norm",
            "right_heel_norm","right_toe_norm","right_mid_l_norm","right_mid_r_norm"
        };

        // Files
        string _rootDir;
        string _rideId;
        string _eventsJsonlPath;
        string _summaryCsvPath;
        string _telemetryCsvPath;
        string _coinsCsvPath;
        StreamWriter _eventsWriter;
        StreamWriter _telemetryWriter;
        bool _telemetryHeaderWritten = false;
        bool _coinsHeaderWritten = false;

        // Session
        RideSessionInfo _ride;
        float _telemetryAccum = 0f;
        float _rideStartTimeRealtime;
        Vector3 _lastRigPos;
        bool _haveLastRigPos = false;

        // Feet keys (normalized source keys)
        string[] _footKeys = null;                // e.g., left_heel_norm...
  

        // Cached sensors for event context
        float _imuYawDeg = 0f, _imuPitchDeg = 0f, _imuRollDeg = 0f;
        Dictionary<string, float> _lastFootNorm = new Dictionary<string, float>(); // baseName -> norm
        Dictionary<string, float> _lastFootRaw = new Dictionary<string, float>(); // baseName -> raw

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
                    Permission.RequestUserPermission(Permission.ExternalStorageWrite);
            }
            catch { }
#endif
        }

        void OnDisable()
        {
            try { _eventsWriter?.Flush(); _eventsWriter?.Dispose(); } catch { }
            try { _telemetryWriter?.Flush(); _telemetryWriter?.Dispose(); } catch { }
        }

        // ============ PUBLIC API ============

        public void StartRide(VehicleKind vehicle, int totalCoinsPlanned)
        {
            string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _rideId = Guid.NewGuid().ToString("N");

            string baseDir = GetQuestDocumentsDir(); // /storage/emulated/0/Documents
            // If you do NOT want a subfolder per session, uncomment this line:
            // _rootDir = baseDir;
            // If you DO want per-session folder:
            string sessionFolder = string.IsNullOrEmpty(participantId)
                ? $"{ts}_{vehicle}_{_rideId}"
                : $"{ts}_{vehicle}_{participantId}_{_rideId}";
            _rootDir = string.IsNullOrEmpty(androidDocumentsSubfolder) ? Path.Combine(baseDir, sessionFolder) :
                       Path.Combine(baseDir, androidDocumentsSubfolder, sessionFolder);
            TryCreateDirectory(_rootDir);

            _eventsJsonlPath = Path.Combine(_rootDir, $"events_{ts}.jsonl");
            _summaryCsvPath = Path.Combine(_rootDir, $"summary_{ts}.csv");
            _telemetryCsvPath = Path.Combine(_rootDir, $"telemetry_{ts}.csv");
            _coinsCsvPath = Path.Combine(_rootDir, $"coins_{ts}.csv");

            _eventsWriter = new StreamWriter(_eventsJsonlPath, true, Encoding.UTF8);
            _telemetryWriter = new StreamWriter(_telemetryCsvPath, false, Encoding.UTF8);
            _telemetryHeaderWritten = false;
            _coinsHeaderWritten = false;
            _telemetryAccum = 0f;
            _haveLastRigPos = false;
            _footKeys = null;
            _lastFootNorm.Clear();
            _lastFootRaw.Clear();

            _ride = new RideSessionInfo
            {
                sessionId = _rideId,
                vehicle = vehicle,
                participantId = participantId,
                startTimeUtc = DateTime.UtcNow,
                endTimeUtc = null,
                totalCoinsPlanned = Mathf.Max(0, totalCoinsPlanned),
                totalCoinsCollected = 0,
                durationSec = 0f
            };
            _rideStartTimeRealtime = Time.realtimeSinceStartup;

            // Auto-bind FootSensorInput if it exists (Start scene persists it)
            if (footInput == null && FootSensorInput.Instance != null)
            {
                footInput = FootSensorInput.Instance;
                Debug.Log("[StudyLogger] Auto-bound FootSensorInput.Instance for logging.");
            }


            if (!File.Exists(_summaryCsvPath))
            {
                File.WriteAllText(_summaryCsvPath,
                    "sessionId,participant,vehicle,startUtc,endUtc,durationSec,totalCoinsPlanned,totalCoinsCollected,folder\n");
            }

            LogEvent("ride_start", new Dictionary<string, object> {
                {"vehicle", vehicle.ToString()},
                {"totalCoinsPlanned", totalCoinsPlanned},
                {"startUtc", _ride.startTimeUtc.ToString("o")},
                {"folder", _rootDir}
            });
        }

        public void FinishRide()
        {
            if (_ride == null) return;

            _ride.endTimeUtc = DateTime.UtcNow;
            _ride.durationSec = (float)(_ride.endTimeUtc.Value - _ride.startTimeUtc).TotalSeconds;

            var sb = new StringBuilder();
            sb.Append(_ride.sessionId).Append(",")
              .Append(EscapeCsv(_ride.participantId)).Append(",")
              .Append(_ride.vehicle).Append(",")
              .Append(_ride.startTimeUtc.ToString("o")).Append(",")
              .Append(_ride.endTimeUtc.Value.ToString("o")).Append(",")
              .Append(_ride.durationSec.ToString("F3")).Append(",")
              .Append(_ride.totalCoinsPlanned).Append(",")
              .Append(_ride.totalCoinsCollected).Append(",")
              .Append(EscapeCsv(_rootDir)).Append("\n");
            File.AppendAllText(_summaryCsvPath, sb.ToString());

            LogEvent("ride_finish", new Dictionary<string, object> {
                {"durationSec", _ride.durationSec},
                {"totalCoinsCollected", _ride.totalCoinsCollected}
            });

            try { _eventsWriter?.Flush(); _telemetryWriter?.Flush(); } catch { }
        }

        public void RegisterCoinsCatalog(List<CoinMeta> coins)
        {
            if (coins == null) return;
            if (!_coinsHeaderWritten)
            {
                File.WriteAllText(_coinsCsvPath,
                    "sessionId,index,label,lateral,deltaLateral,segIndex,worldX,worldY,worldZ\n");
                _coinsHeaderWritten = true;
            }
            var sb = new StringBuilder();
            foreach (var c in coins)
            {
                Vector3 p = c.transform.position;
                sb.Append(_rideId).Append(",")
                  .Append(c.index).Append(",")
                  .Append(c.label).Append(",")
                  .Append(c.lateral.ToString("F3")).Append(",")
                  .Append(c.deltaLateral.ToString("F3")).Append(",")
                  .Append(c.segmentIndex).Append(",")
                  .Append(p.x.ToString("F3")).Append(",")
                  .Append(p.y.ToString("F3")).Append(",")
                  .Append(p.z.ToString("F3")).Append("\n");
            }
            File.AppendAllText(_coinsCsvPath, sb.ToString());
            LogEvent("coins_registered", new Dictionary<string, object> { { "count", coins.Count } });
        }

        public void LogCoinCollected(CoinMeta meta)
        {
            _ride.totalCoinsCollected++;
            var payload = new Dictionary<string, object>{
                {"coinIndex", meta.index},
                {"label", meta.label.ToString()},
                {"deltaLateral", meta.deltaLateral},
                {"timeSinceStart", TimeSinceRideStart()},
                {"imuYawDeg", _imuYawDeg},
                {"imuPitchDeg", _imuPitchDeg},
                {"imuRollDeg", _imuRollDeg}
            };
            if (_lastFootNorm.Count > 0) payload["foot_norm"] = new Dictionary<string, object>(_lastFootNorm.Count);
            if (_lastFootRaw.Count > 0) payload["foot_raw"] = new Dictionary<string, object>(_lastFootRaw.Count);
            if (payload.TryGetValue("foot_norm", out var fn))
            {
                var dict = (Dictionary<string, object>)fn;
                foreach (var kv in _lastFootNorm) dict[kv.Key] = kv.Value;
            }
            if (payload.TryGetValue("foot_raw", out var fr))
            {
                var dict = (Dictionary<string, object>)fr;
                foreach (var kv in _lastFootRaw) dict[kv.Key] = kv.Value;
            }
            LogEvent("coin_collected", payload);
        }

        public void LogCoinMissed(CoinMeta meta)
        {
            LogEvent("coin_missed", new Dictionary<string, object> {
                {"coinIndex", meta.index},
                {"label", meta.label.ToString()},
                {"deltaLateral", meta.deltaLateral},
                {"timeSinceStart", TimeSinceRideStart()}
            });
        }

        public void LogCollision(string withTag, string withLayer, Vector3 point, Vector3 relativeVel)
        {
            var payload = new Dictionary<string, object>{
                {"withTag", withTag},
                {"withLayer", withLayer},
                {"x", point.x}, {"y", point.y}, {"z", point.z},
                {"relV", relativeVel.magnitude},
                {"timeSinceStart", TimeSinceRideStart()},
                {"imuYawDeg", _imuYawDeg},
                {"imuPitchDeg", _imuPitchDeg},
                {"imuRollDeg", _imuRollDeg}
            };
            if (_lastFootNorm.Count > 0) payload["foot_norm"] = new Dictionary<string, object>(_lastFootNorm.Count);
            if (_lastFootRaw.Count > 0) payload["foot_raw"] = new Dictionary<string, object>(_lastFootRaw.Count);
            if (payload.TryGetValue("foot_norm", out var fn))
            {
                var dict = (Dictionary<string, object>)fn;
                foreach (var kv in _lastFootNorm) dict[kv.Key] = kv.Value;
            }
            if (payload.TryGetValue("foot_raw", out var fr))
            {
                var dict = (Dictionary<string, object>)fr;
                foreach (var kv in _lastFootRaw) dict[kv.Key] = kv.Value;
            }
            LogEvent("collision", payload);
        }

        public void LogFall(string reason, Vector3 atPos)
        {
            var payload = new Dictionary<string, object>{
                {"reason", reason},
                {"x", atPos.x}, {"y", atPos.y}, {"z", atPos.z},
                {"timeSinceStart", TimeSinceRideStart()},
                {"imuYawDeg", _imuYawDeg},
                {"imuPitchDeg", _imuPitchDeg},
                {"imuRollDeg", _imuRollDeg}
            };
            if (_lastFootNorm.Count > 0) payload["foot_norm"] = new Dictionary<string, object>(_lastFootNorm.Count);
            if (_lastFootRaw.Count > 0) payload["foot_raw"] = new Dictionary<string, object>(_lastFootRaw.Count);
            if (payload.TryGetValue("foot_norm", out var fn))
            {
                var dict = (Dictionary<string, object>)fn;
                foreach (var kv in _lastFootNorm) dict[kv.Key] = kv.Value;
            }
            if (payload.TryGetValue("foot_raw", out var fr))
            {
                var dict = (Dictionary<string, object>)fr;
                foreach (var kv in _lastFootRaw) dict[kv.Key] = kv.Value;
            }
            LogEvent("fall", payload);
        }

        // ---------- Telemetry ----------
        public void LogTelemetry(float dt, Transform rig, VehicleDataReceiver rx, float commandedSpeed, float commandedTurn)
        {
            if (rig == null) return;
            _telemetryAccum += dt;
            float period = (telemetryHz <= 0f) ? 0.1f : 1f / telemetryHz;
            if (_telemetryAccum < period) return;
            _telemetryAccum = 0f;

            // approx speed from rig motion
            float speedMps = 0f;
            if (_haveLastRigPos)
                speedMps = (rig.position - _lastRigPos).magnitude / Mathf.Max(period, 1e-4f);
            _lastRigPos = rig.position;
            _haveLastRigPos = true;

            // IMU / throttle
            float imuYaw = (rx != null) ? rx.imuYaw : 0f;
            float imuPitch = (rx != null) ? rx.imuPitch : 0f;
            float imuRoll = (rx != null) ? rx.imuRoll : 0f;
            float thr = (rx != null) ? Mathf.Clamp01(rx.throttle) : 0f;

            

            // header (once)
            if (!_telemetryHeaderWritten)
            {
                var head = new StringBuilder();
                head.Append("t,worldX,worldY,worldZ,yawDegRig,pitchDegRig,rollDegRig,")
                    .Append("imuYawDeg,imuPitchDeg,imuRollDeg,")
                    .Append("throttle,cmdSpeed,cmdTurn,speedMps,")
                    .Append("fsn_left_heel,fsn_left_toe,fsn_left_mid_l,fsn_left_mid_r,")
                    .Append("fsn_right_heel,fsn_right_toe,fsn_right_mid_l,fsn_right_mid_r\n");

                _telemetryWriter.Write(head.ToString());
                _telemetryHeaderWritten = true;
            }


            // build row
            float tNow = TimeSinceRideStart();
            var row = new StringBuilder();
            row.Append(tNow.ToString("F3")).Append(",")
               .Append(rig.position.x.ToString("F3")).Append(",")
               .Append(rig.position.y.ToString("F3")).Append(",")
               .Append(rig.position.z.ToString("F3")).Append(",")
               .Append(rig.rotation.eulerAngles.y.ToString("F2")).Append(",")
               .Append(rig.rotation.eulerAngles.x.ToString("F2")).Append(",")
               .Append(rig.rotation.eulerAngles.z.ToString("F2")).Append(",")
               .Append(imuYaw.ToString("F2")).Append(",")
               .Append(imuPitch.ToString("F2")).Append(",")
               .Append(imuRoll.ToString("F2")).Append(",")
               .Append(thr.ToString("F3")).Append(",")
               .Append(commandedSpeed.ToString("F3")).Append(",")
               .Append(commandedTurn.ToString("F3")).Append(",")
               .Append(speedMps.ToString("F3"));

            // feet values: normalized then raw
            _lastFootNorm.Clear();
            _lastFootRaw.Clear();

            // Gather values the same way SegwayController does
            float lHeel = 0, lToe = 0, lMidL = 0, lMidR = 0, rHeel = 0, rToe = 0, rMidL = 0, rMidR = 0;

            // Gather values: prefer FootSensorInput (because Start scene has it and it persists)
            if (footInput != null)
            {
                lHeel = footInput.leftHeel;
                lToe = footInput.leftToe;
                lMidL = footInput.leftMidL;
                lMidR = footInput.leftMidR;

                rHeel = footInput.rightHeel;
                rToe = footInput.rightToe;
                rMidL = footInput.rightMidL;
                rMidR = footInput.rightMidR;
            }
            else if (rx != null && rx.footSensors != null && rx.footSensors.Count > 0)
            {
                rx.footSensors.TryGetValue("left_heel_norm", out lHeel);
                rx.footSensors.TryGetValue("left_toe_norm", out lToe);
                rx.footSensors.TryGetValue("left_mid_l_norm", out lMidL);
                rx.footSensors.TryGetValue("left_mid_r_norm", out lMidR);

                rx.footSensors.TryGetValue("right_heel_norm", out rHeel);
                rx.footSensors.TryGetValue("right_toe_norm", out rToe);
                rx.footSensors.TryGetValue("right_mid_l_norm", out rMidL);
                rx.footSensors.TryGetValue("right_mid_r_norm", out rMidR);
            }



            // Append normalized feet into CSV row
            row.Append($",{lHeel:F3},{lToe:F3},{lMidL:F3},{lMidR:F3}")
               .Append($",{rHeel:F3},{rToe:F3},{rMidL:F3},{rMidR:F3}");

            // Save into dictionaries for event logging
            _lastFootNorm["left_heel"] = lHeel;
            _lastFootNorm["left_toe"] = lToe;
            _lastFootNorm["left_mid_l"] = lMidL;
            _lastFootNorm["left_mid_r"] = lMidR;
            _lastFootNorm["right_heel"] = rHeel;
            _lastFootNorm["right_toe"] = rToe;
            _lastFootNorm["right_mid_l"] = rMidL;
            _lastFootNorm["right_mid_r"] = rMidR;


            row.Append("\n");
            _telemetryWriter.Write(row.ToString());
            _telemetryWriter.Flush();

            // cache latest IMU for events
            _imuYawDeg = imuYaw; _imuPitchDeg = imuPitch; _imuRollDeg = imuRoll;
        }

        // ============ INTERNALS ============

        string GetQuestDocumentsDir()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string path = "/storage/emulated/0/Documents";
            TryCreateDirectory(path);
            return path;
#else
            string fallback = Application.persistentDataPath;
            TryCreateDirectory(fallback);
            return fallback;
#endif
        }

        void TryCreateDirectory(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (Exception e) { Debug.LogWarning($"[StudyLogger] CreateDirectory failed: {dir}\n{e}"); }
        }

        void LogEvent(string type, Dictionary<string, object> payload)
        {
            if (_eventsWriter == null) return;
            var sb = new StringBuilder(256);
            sb.Append("{\"t\":").Append(TimeSinceRideStart().ToString("F3"))
              .Append(",\"type\":\"").Append(type).Append("\"");

            if (payload != null)
            {
                foreach (var kv in payload)
                {
                    sb.Append(",\"").Append(EscapeJson(kv.Key)).Append("\":");
                    AppendJsonValue(sb, kv.Value);
                }
            }
            sb.Append("}\n");
            _eventsWriter.Write(sb.ToString());
            _eventsWriter.Flush();
        }

        float TimeSinceRideStart()
        {
            return Mathf.Max(0f, Time.realtimeSinceStartup - _rideStartTimeRealtime);
        }

        static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static void AppendJsonValue(StringBuilder sb, object val)
        {
            if (val == null) { sb.Append("null"); return; }

            switch (val)
            {
                case string str:
                    sb.Append("\"").Append(EscapeJson(str)).Append("\""); break;
                case bool b:
                    sb.Append(b ? "true" : "false"); break;
                case int i:
                    sb.Append(i); break;
                case long l:
                    sb.Append(l); break;
                case float f:
                    sb.Append(f.ToString("G9")); break;
                case double d:
                    sb.Append(d.ToString("G17")); break;
                case Enum e:
                    sb.Append("\"").Append(e.ToString()).Append("\""); break;
                case Vector3 v:
                    sb.Append("{\"x\":").Append(v.x.ToString("G9"))
                      .Append(",\"y\":").Append(v.y.ToString("G9"))
                      .Append(",\"z\":").Append(v.z.ToString("G9")).Append("}");
                    break;
                case Dictionary<string, object> dict:
                    sb.Append("{");
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(",");
                        sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":");
                        AppendJsonValue(sb, kv.Value);
                        first = false;
                    }
                    sb.Append("}");
                    break;
                default:
                    sb.Append("\"").Append(EscapeJson(val.ToString())).Append("\"");
                    break;
            }
        }

        static readonly string[] _defaultFootKeys = new string[]
{
            "left_heel","left_toe","left_mid_l","left_mid_r",
            "right_heel","right_toe","right_mid_l","right_mid_r"
};

        /// Reads normalized values from FootSensorInput
        bool TryReadFootFromInput(out float[] values)
        {
            values = null;
            if (footInput == null) return false;

            values = new float[8];
            values[0] = footInput.leftHeel;
            values[1] = footInput.leftToe;
            values[2] = footInput.leftMidL;
            values[3] = footInput.leftMidR;
            values[4] = footInput.rightHeel;
            values[5] = footInput.rightToe;
            values[6] = footInput.rightMidL;
            values[7] = footInput.rightMidR;
            return true;
        }

        /// Maps a normalized value to the correct array index
        float MapDefaultKeyToArray(string[] keys, string keyOrNorm, float[] vals)
        {
            string baseName = keyOrNorm.EndsWith("_norm") ? keyOrNorm.Substring(0, keyOrNorm.Length - 5) : keyOrNorm;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == baseName) return vals[i];
            }
            return 0f;
        }

        /// Converts int dictionary (if returned by FootSensorInput) into float dictionary
        Dictionary<string, float> ToFloatDict(Dictionary<string, int> src)
        {
            var dst = new Dictionary<string, float>();
            if (src == null) return dst;
            foreach (var kv in src)
                dst[kv.Key] = kv.Value; // implicit int->float
            return dst;
        }

    }
}

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ETS2_Assist_GUI.Quests
{
    internal sealed class QuestResolvedPoint
    {
        public string Uid = "";
        public string OriginalUid = "";
        public string Category = "";
        public double X;
        public double Y;
        public double Z;
        public bool IsGenerated;
    }

    internal sealed class QuestPointResolver
    {
        private sealed class RoadSegment { public double X1, Z1, X2, Z2; }
        private readonly QuestStore _store;
        private readonly Dictionary<string, QuestResolvedPoint> _staticCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _roadSync = new();
        private List<RoadSegment> _roads = new();
        private Task? _roadLoadTask;

        public QuestPointResolver(QuestStore store) { _store = store; _roadLoadTask = Task.Run(LoadRoads); }
        public void ClearCaches() { _staticCache.Clear(); }

        public bool TryResolve(QuestDefinition def, QuestInteractionDefinition interaction, out QuestResolvedPoint point)
        {
            point = new QuestResolvedPoint();
            if (interaction.Source?.Selector != null && string.IsNullOrWhiteSpace(interaction.Source.Uid))
                return TryResolveGenerated(def, interaction, interaction.Source.Selector, out point);
            return TryGetStatic(interaction.Source?.Category ?? "", interaction.Source?.Uid ?? "", out point);
        }

        private bool TryResolveGenerated(QuestDefinition def, QuestInteractionDefinition interaction, QuestPointSelector selector, out QuestResolvedPoint point)
        {
            point = new QuestResolvedPoint();
            string key = def.Id + ":" + interaction.Id;
            if (_store.State.GeneratedPoints.TryGetValue(key, out QuestGeneratedPoint? generated) && generated != null && !string.IsNullOrWhiteSpace(generated.Uid))
            {
                point = new QuestResolvedPoint { Uid = generated.Uid, OriginalUid = generated.SourceUid, Category = generated.Category, X = generated.X, Y = generated.Y, Z = generated.Z, IsGenerated = true };
                return true;
            }

            QuestResolvedPoint anchor;
            QuestInteractionDefinition? anchorDefinition = null;
            if (!string.IsNullOrWhiteSpace(selector.AnchorInteractionId))
                anchorDefinition = def.Interactions.FirstOrDefault(i => string.Equals(i.Id, selector.AnchorInteractionId, StringComparison.OrdinalIgnoreCase));
            if (anchorDefinition == null)
                anchorDefinition = def.Interactions.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Source?.Uid));
            if (anchorDefinition == null || !TryResolveStaticSource(anchorDefinition.Source, out anchor))
            {
                Logger.Current?.Data($"[QUEST] Не найден anchor для генерации точки {key}.");
                return false;
            }

            var candidates = LoadStaticCategory(selector.Category);
            if (candidates.Count == 0) { Logger.Current?.Data($"[QUEST] SDO-категория для генерации не найдена: {selector.Category}"); return false; }
            double minDist = Math.Max(0, selector.MinDistanceM);
            double maxDist = selector.MaxDistanceM <= 0 ? double.MaxValue : selector.MaxDistanceM;
            double maxRoad = selector.MaxRoadDistanceM;
            EnsureRoadsReadyForSelection();
            bool roadsReady; lock (_roadSync) roadsReady = _roads.Count > 0;

            var ranged = candidates.Where(c => !string.Equals(c.Uid, anchor.Uid, StringComparison.OrdinalIgnoreCase))
                .Select(c => new { Candidate = c, Direct = Math.Sqrt(DistanceSquared(c.X, c.Z, anchor.X, anchor.Z)) })
                .Where(x => x.Direct >= minDist && x.Direct <= maxDist).ToList();
            if (ranged.Count == 0) { Logger.Current?.Data($"[QUEST] Для {key} нет SDO-точек в диапазоне {minDist:0}..{maxDist:0} м от anchor."); return false; }

            var ranked = ranged.Select(x => new { x.Candidate, x.Direct, Road = roadsReady ? NearestRoadDistance(x.Candidate.X, x.Candidate.Z) : double.PositiveInfinity }).ToList();
            if (roadsReady && maxRoad > 0)
            {
                var roadCandidates = ranked.Where(x => x.Road <= maxRoad).OrderBy(x => x.Road).ThenBy(x => x.Direct).ToList();
                if (roadCandidates.Count > 0) ranked = roadCandidates;
                else Logger.Current?.Data($"[QUEST] В диапазоне {minDist:0}..{maxDist:0} м не найден объект ближе {maxRoad:0} м к дороге; используем ближайший по прямой.");
            }

            var chosen = ranked.OrderBy(x => roadsReady && maxRoad > 0 ? x.Road : x.Direct).ThenBy(x => x.Direct).First();
            if (!selector.CreateCustomPoint) { point = chosen.Candidate; point.OriginalUid = chosen.Candidate.Uid; return true; }

            string uid = "quest_" + SanitizeId(def.Id) + "_" + SanitizeId(interaction.Id) + "_" + Guid.NewGuid().ToString("N");
            generated = new QuestGeneratedPoint
            {
                QuestId = def.Id, InteractionId = interaction.Id, SourceCategory = selector.Category, SourceUid = chosen.Candidate.Uid,
                Uid = uid, Category = selector.Category, Name = interaction.Name, X = chosen.Candidate.X, Y = chosen.Candidate.Y, Z = chosen.Candidate.Z, Known = false, CreatedUtc = DateTime.UtcNow
            };
            _store.State.GeneratedPoints[key] = generated;
            _store.SaveState();
            Logger.Current?.Workflow($"[QUEST] Создана постоянная квестовая SDO-точка: {uid}; reference={chosen.Candidate.Uid}; category={selector.Category}; direct={chosen.Direct:0}m; road={(double.IsInfinity(chosen.Road) ? "n/a" : chosen.Road.ToString("0") + "m")}.");
            point = new QuestResolvedPoint { Uid = uid, OriginalUid = chosen.Candidate.Uid, Category = selector.Category, X = chosen.Candidate.X, Y = chosen.Candidate.Y, Z = chosen.Candidate.Z, IsGenerated = true };
            return true;
        }

        private bool TryResolveStaticSource(QuestPointSource? source, out QuestResolvedPoint point)
        {
            if (source == null) { point = new QuestResolvedPoint(); return false; }
            return TryGetStatic(source.Category, source.Uid, out point);
        }

        private bool TryGetStatic(string category, string uid, out QuestResolvedPoint point)
        {
            string key = category + ":" + uid;
            if (_staticCache.TryGetValue(key, out point!)) return true;
            point = new QuestResolvedPoint();
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(uid)) return false;
            string path = Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data", "model_" + category + ".json");
            try
            {
                if (!File.Exists(path)) return false;
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (JObject token in (root["objects"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string candidateUid = token["uid"]?.Value<string>() ?? "";
                    if (!string.Equals(candidateUid, uid, StringComparison.OrdinalIgnoreCase)) continue;
                    point = new QuestResolvedPoint { Uid = candidateUid, OriginalUid = candidateUid, Category = category, X = token["x"]?.Value<double>() ?? 0, Y = token["y"]?.Value<double>() ?? 0, Z = token["z"]?.Value<double>() ?? 0, IsGenerated = false };
                    _staticCache[key] = point; return true;
                }
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] load static point {category}/{uid}: {ex.Message}"); }
            return false;
        }

        private List<QuestResolvedPoint> LoadStaticCategory(string category)
        {
            var list = new List<QuestResolvedPoint>(); if (string.IsNullOrWhiteSpace(category)) return list;
            string path = Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data", "model_" + category + ".json");
            try
            {
                if (!File.Exists(path)) return list;
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (JObject token in (root["objects"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string uid = token["uid"]?.Value<string>() ?? ""; if (string.IsNullOrWhiteSpace(uid)) continue;
                    var point = new QuestResolvedPoint { Uid=uid, OriginalUid=uid, Category=category, X=token["x"]?.Value<double>() ?? 0, Y=token["y"]?.Value<double>() ?? 0, Z=token["z"]?.Value<double>() ?? 0 };
                    list.Add(point); _staticCache[category+":"+uid]=point;
                }
            }
            catch(Exception ex){Logger.Current?.Data($"[QUEST] load category {category}: {ex.Message}");}
            return list;
        }

        private void EnsureRoadsReadyForSelection()
        {
            Task? task; lock(_roadSync) task=_roadLoadTask; if(task==null||task.IsCompleted)return; try{task.Wait(TimeSpan.FromSeconds(2));}catch{}
        }

        private void LoadRoads()
        {
            try
            {
                string path=Path.Combine(AppDataPaths.StaticDataDirectory,"GeoJson","roads.geojson"); if(!File.Exists(path))return;
                JObject root=JObject.Parse(File.ReadAllText(path)); var result=new List<RoadSegment>();
                foreach(JObject feature in(root["features"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    JObject? geometry=feature["geometry"] as JObject; if(geometry==null)continue; string type=geometry["type"]?.Value<string>() ?? ""; JToken? coordinates=geometry["coordinates"]; if(coordinates==null)continue;
                    foreach(var segment in ExtractSegments(coordinates,type))result.Add(segment);
                }
                lock(_roadSync)_roads=result; Logger.Current?.Data($"[QUEST] Road index loaded: {result.Count} segments.");
            }
            catch(Exception ex){Logger.Current?.Data($"[QUEST] Road index load failed: {ex.Message}");}
        }

        private static IEnumerable<RoadSegment> ExtractSegments(JToken coords,string geometryType)
        {
            if(geometryType.Equals("LineString",StringComparison.OrdinalIgnoreCase)){var points=ReadPoints(coords);for(int i=1;i<points.Count;i++)yield return new RoadSegment{X1=points[i-1].X,Z1=points[i-1].Z,X2=points[i].X,Z2=points[i].Z};yield break;}
            if(geometryType.Equals("MultiLineString",StringComparison.OrdinalIgnoreCase)){foreach(JToken line in coords as JArray ?? new JArray()){var points=ReadPoints(line);for(int i=1;i<points.Count;i++)yield return new RoadSegment{X1=points[i-1].X,Z1=points[i-1].Z,X2=points[i].X,Z2=points[i].Z};}yield break;}
            var direct=ReadPoints(coords);for(int i=1;i<direct.Count;i++)yield return new RoadSegment{X1=direct[i-1].X,Z1=direct[i-1].Z,X2=direct[i].X,Z2=direct[i].Z};
        }

        private static List<(double X,double Z)> ReadPoints(JToken token)
        {
            var result=new List<(double X,double Z)>(); foreach(JToken p in token as JArray ?? new JArray()){if(p is not JArray pair||pair.Count<2)continue;if(pair[0].Type==JTokenType.Array)break;double x=pair[0].Value<double>(),z=pair[1].Value<double>();if(double.IsFinite(x)&&double.IsFinite(z))result.Add((x,z));}return result;
        }
        private double NearestRoadDistance(double x,double z){List<RoadSegment> roads;lock(_roadSync)roads=_roads;if(roads.Count==0)return double.PositiveInfinity;double best2=double.PositiveInfinity;foreach(var s in roads){double d2=PointSegmentDistanceSquared(x,z,s.X1,s.Z1,s.X2,s.Z2);if(d2<best2)best2=d2;}return Math.Sqrt(best2);}
        private static double PointSegmentDistanceSquared(double px,double pz,double x1,double z1,double x2,double z2){double vx=x2-x1,vz=z2-z1,wx=px-x1,wz=pz-z1,len2=vx*vx+vz*vz;if(len2<=1e-12)return wx*wx+wz*wz;double t=Math.Clamp((wx*vx+wz*vz)/len2,0,1);double dx=px-(x1+t*vx),dz=pz-(z1+t*vz);return dx*dx+dz*dz;}
        private static double DistanceSquared(double x1,double z1,double x2,double z2){double dx=x1-x2,dz=z1-z2;return dx*dx+dz*dz;}
        private static string SanitizeId(string value){if(string.IsNullOrWhiteSpace(value))return "quest";string s=new(value.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());return s.Length>48?s[..48]:s;}
    }
}

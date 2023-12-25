using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core;
using System;
//using System.Reflection;
using Rust;
using Network;
using Oxide.Plugins.BGradeExt;

namespace Oxide.Plugins
{
    [Info("PowerWalk", "RFC1920", "1.0.3")]
    [Description("Walk the power lines like a boss.")]
    internal class PowerWalk : CovalencePlugin
    {
        public Dictionary<string, PowerLine> powerlines = new Dictionary<string, PowerLine>();
        public Dictionary<int, string> idToLine = new Dictionary<int, string>();
        //private readonly FieldInfo meshLookupField = typeof(MeshColliderLookup).GetField("meshLookup", BindingFlags.Instance | BindingFlags.NonPublic);
        private ConfigData configData;
        public static PowerWalk Instance;
        private const string permUse = "powerwalk.use";
        private const string permTP = "powerwalk.tp";

        public class PowerLine
        {
            public List<int> id;
            public List<Vector3> points = new List<Vector3>();
            public List<PowerLineWireSpan> spans = new List<PowerLineWireSpan>();
        }

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        private void DoLog(string message)
        {
            if (configData.Options.debug)
            {
                Interface.Oxide.LogInfo(message);
            }
        }

        private void OnNewSave()
        {
            powerlines = new Dictionary<string, PowerLine>();
            FindPowerLines();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                { "NoPermission", "You don't have permission to use this command." },
                { "IncorrectUsage", "Incorrect usage! /powerline [list/name]" },
                { "DoesnotExist", "The powerline '{0}' doesn't exist. Use '/powerline list' for a list of powerlines." },
                { "PowerLines", "<color=#00ff00>PowerLines:</color>\n{0}" },
                { "start", "Start" },
                { "end", "End" },
                { "TeleportingTo", "Teleporting to the {1} of : {0} in {2} seconds..." },
                { "TeleportedTo", "Teleported to the {1} of : {0}" }
            }, this);
        }

        private bool HasPerm(IPlayer player) => player.IsAdmin || permission.UserHasPermission(player.Id, permUse);

        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permTP, this);
            LoadConfigVariables();
            FindPowerLines();

            AddCovalenceCommand("pwalk", "WalkLines");
            Instance = this;
        }

        private void WalkLines(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!player.HasPermission(permUse)) return;

            string chosenLine = "";
            if (args.Length > 0) chosenLine = $"Powerline {args[0]}";
            if (args.Length == 2 && powerlines.ContainsKey(chosenLine) && args[1] == "tp")
            {
                if (!player.HasPermission(permTP)) return;
                GenericPosition pos = ToGeneric(powerlines[chosenLine].points[0]);
                Message(iplayer, "TeleportingTo", chosenLine, "start", "5");
                pos.Y = TerrainMeta.HeightMap.GetHeight(powerlines[chosenLine].points[0]);
                timer.Once(5f, () => { iplayer.Teleport(pos.X, pos.Y, pos.Z); Message(iplayer, "TeleportedTo", "start", chosenLine); });
            }
            else if (args.Length == 2 && args[1] == "show")
            {
                int i = 0;
                foreach (Vector3 point in powerlines[chosenLine].points)
                {
                    player?.SendConsoleCommand("ddraw.text", configData.Options.ShowAllTextTime, Color.green, point + new Vector3(0, 1.5f, 0), $"<size=20>{i}</size>");
                    i++;
                }
            }
            else if (args.Length == 1 && powerlines.ContainsKey(chosenLine))
            {
                DoLog("Creating object and setting powerline");
                PowerWalker pw = player.gameObject.AddComponent<PowerWalker>();
                pw.chosenLine = chosenLine;
            }
            else if (args.Length == 1 && args[0] == "show")
            {
                foreach (KeyValuePair<string, PowerLine> pl in powerlines)
                {
                    player?.SendConsoleCommand("ddraw.text", configData.Options.ShowAllTextTime, Color.green, pl.Value.points[0] + new Vector3(0, 1.5f, 0), $"<size=20>{pl.Key} {Lang("start", null)}</size>");
                    player?.SendConsoleCommand("ddraw.text", configData.Options.ShowAllTextTime, Color.blue, pl.Value.points.Last() + new Vector3(0, 1.5f, 0), $"<size=20>{pl.Key} {Lang("end", null)}</size>");
                }
            }
            else if (args.Length == 1 && (args[0] == "off" || args[0] == "none"))
            {
                PowerWalker go = player.gameObject.GetComponent<PowerWalker>();
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            else if (args.Length == 0)
            {
                PowerWalker pw = player.gameObject.AddComponent<PowerWalker>();
                pw.SetNearestLine();
            }
        }

        private void Unload()
        {
            DestroyAll<PowerPlatform>();
            DestroyAll<PowerWalker>();
        }

        private void DestroyAll<T>() where T : MonoBehaviour
        {
            foreach (T type in UnityEngine.Object.FindObjectsOfType<T>())
            {
                UnityEngine.Object.Destroy(type);
            }
        }

        private void SaveData()
        {
            // Save the data file as we add/remove minicopters.
            Interface.Oxide.DataFileSystem.WriteObject(Name, powerlines);
        }

        private void LoadData()
        {
            powerlines = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, PowerLine>>(Name);
        }

        private void FindPowerLines()
        {
            int x = 0;
            foreach (PowerLineWire pwire in UnityEngine.Object.FindObjectsOfType<PowerLineWire>())
            {
                //DoLog("Found a powerlinewire");
                string nom = $"Powerline {x}";
                powerlines.Add(nom, new PowerLine());
                foreach (Transform pole in pwire.poles)
                {
                    //DoLog($"-- Found a power pole at {pole.transform.position.ToString()}");
                    powerlines[nom].points.Add(pole.transform.position);
                }

                powerlines[nom].spans = pwire.spans;
                idToLine.Add(x, nom);
                //foreach (PowerLineWireSpan span in pwire.spans)
                //{
                //    DoLog($"-- Found a wire span running from {span.start.transform.position.ToString()} to {span.end.transform.position.ToString()}.  It is {span.WireLength.ToString()}m long.");
                //}
                x++;
            }
        }

        private object OnEntityGroundMissing(BaseEntity entity)
        {
            PowerPlatform platform = entity.GetComponentInParent<PowerPlatform>();
            if (platform != null)
            {
                return false;
            }

            return null;
        }

        private GenericPosition ToGeneric(Vector3 vec) => new GenericPosition(vec.x, vec.y, vec.z);

        #region config
        protected override void LoadDefaultConfig()
        {
            DoLog("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options
                {
                    ShowAllTextTime = 30,
                    ShowOneTextTime = 60,
                    ShowOneAllPoints = true
                },
                Version = Version
            };
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        public class ConfigData
        {
            public Options Options = new Options();
            public VersionNumber Version;
        }

        public class Options
        {
            public float ShowAllTextTime;
            public float ShowOneTextTime;
            public bool ShowOneAllPoints;
            public bool ShowPlatformsToAll;
            public bool debug;
        }
        #endregion

        #region Classes
        public class PowerWalker : MonoBehaviour
        {
            private static List<Vector3> points = new List<Vector3>();
            private static List<PowerLineWireSpan> spans = new List<PowerLineWireSpan>();
            public BasePlayer player;
            public string chosenLine;
            public Vector3 lastPoint;
            public Vector3 nearPoint;
            public Vector3 ybump = new Vector3(0, 12, 0);
            public Vector3 ybump2 = new Vector3(0, 20, 0);

            public Vector3 spawnPos;
            public Quaternion spawnRot;
            public List<BaseEntity> ladders = new List<BaseEntity>();

            public PowerPlatform pp1;
            public PowerPlatform pp2;

            private const string prefabladder = "assets/prefabs/building/ladder.wall.wood/ladder.wooden.wall.prefab";

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                //player.EnablePlayerCollider();
            }

            public void FixedUpdate()
            {
                //if (points.Count == 0) return;
                int nearPt = 0;
                if (chosenLine.Length == 0) return;

                if (Instance.powerlines.ContainsKey(chosenLine))
                {
                    points = Instance.powerlines[chosenLine].points;
                    spans = Instance.powerlines[chosenLine].spans;
                    if (Vector3.Distance(player.transform.position, nearPoint) > 20f)
                    {
                        //Instance.DoLog("Finding new nearest point");
                        nearPt = FindNearestPoint();
                        if (nearPt > -1)
                        {
                            nearPoint = points[nearPt];// + hbump;
                        }
                    }
                }
                if (nearPoint != lastPoint)
                {
                    lastPoint = nearPoint;
                    Instance.Message(player.IPlayer, $"Near point {nearPt} at {nearPoint}");
                    player.SendConsoleCommand("ddraw.text", 15, Color.yellow, nearPoint, $"<size=20>Point {nearPt}</size>");

                    KillPlatforms();
                    KillLadders();

                    spawnPos = nearPoint + new Vector3(0, 1.5f, 0);
                    spawnRot = new Quaternion();

                    SpawnLadders();
                    SpawnPlatforms();
                }
            }

            public void SetNearestLine()
            {
                float lowMag = 99999;
                string linechoice = chosenLine;
                foreach (KeyValuePair<string, PowerLine> line in Instance.powerlines)
                {
                    foreach (Vector3 point in line.Value.points)
                    {
                        if ((point - player.transform.position).magnitude < lowMag)
                        {
                            lowMag = (point - player.transform.position).magnitude;
                            linechoice = line.Key;
                            Instance.DoLog($"Current closest line(point) is {linechoice}({point})");
                        }
                    }
                }
                chosenLine = linechoice;
                Instance.DoLog($"Setting nearest powerline to {chosenLine}");
                FindNearestPoint();
            }

            public string SetNearestPoint(int choice = 0)
            {
                if (choice == 0)
                {
                    choice = FindNearestPoint();
                }

                chosenLine = Instance.idToLine[choice];
                Instance.DoLog($"Setting nearest powerline to {chosenLine}({choice})");
                return chosenLine;
            }

            private int FindNearestPoint(int index=0)
            {
                float lowMag = 99999;
                int rtrn = -1;
                int i = 0;
                foreach (Vector3 point in points)
                {
                    if ((point - player.transform.position).magnitude < lowMag)
                    {
                        lowMag = (point - player.transform.position).magnitude;
                        rtrn = i;
                    }
                    i++;
                }
                return rtrn;
            }

            private int FindNearestSpanStart(int index=0)
            {
                float lowMag = 99999;
                int rtrn = -1;
                int i = 0;
                foreach (PowerLineWireSpan span in spans)
                {
                    if ((span.start.position - player.transform.position).magnitude < lowMag)
                    {
                        lowMag = (span.start.position - player.transform.position).magnitude;
                        rtrn = i;
                    }
                    i++;
                }
                return rtrn;
            }

            private int FindNearestSpanEnd(int index=0)
            {
                float lowMag = 99999;
                int rtrn = -1;
                int i = 0;
                foreach (PowerLineWireSpan span in spans)
                {
                    if ((span.end.position - player.transform.position).magnitude < lowMag)
                    {
                        lowMag = (span.end.position - player.transform.position).magnitude;
                        rtrn = i;
                    }
                    i++;
                }
                return rtrn;
            }

            private void SpawnPlatforms()
            {
                int st1 = FindNearestSpanStart();
                int en1 = FindNearestSpanEnd();

                // FIXME - check for super-tall wires and bump accordingly
                Vector3 start = spans[st1].start.position + ybump;
                Vector3 end = spans[st1].end.position + ybump;

                //if (Instance.configData.Options.debug)
                //{
                //    player.SendConsoleCommand("ddraw.text", 15, Color.white, start + new Vector3(0, 0.2f, 0), $"WireStart\n{start.ToString()}");
                //    player.SendConsoleCommand("ddraw.text", 15, Color.white, end + new Vector3(0, -0.2f, 0), $"WireEnd\n{end.ToString()}");
                //}

                Quaternion rotation = Quaternion.LookRotation(start - end, Vector3.up);
                Instance.DoLog("Attaching platform to pole1");
                GameObject go = Instantiate(new GameObject(), (start + end) / 2, rotation);
                pp1 = go.AddComponent<PowerPlatform>();
                pp1.Setup(start, end, rotation, spans[st1].WireLength / 2);
                pp1.player = player;

                start = spans[en1].start.position + ybump;
                end = spans[en1].end.position + ybump;

                //if (Instance.configData.Options.debug)
                //{
                //    player.SendConsoleCommand("ddraw.text", 15, Color.white, start + new Vector3(0, 0.2f, 0), $"WireStart\n{start.ToString()}");
                //    player.SendConsoleCommand("ddraw.text", 15, Color.white, end + new Vector3(0, -0.2f, 0), $"WireEnd\n{end.ToString()}");
                //}

                rotation = Quaternion.LookRotation(start - end, Vector3.up);
                Instance.DoLog("Attaching platform to pole2");
                go = Instantiate(new GameObject(), (start + end) / 2, rotation);
                pp2 = go.AddComponent<PowerPlatform>();
                pp2.Setup(start, end, rotation, spans[en1].WireLength / 2);
                pp2.player = player;
            }

            private void OnCollisionEnter(Collision col)
            {
                Instance.DoLog($"PowerWalker Collision Enter: {col.gameObject.name}");
            }

            private void OnCollisionExit(Collision col)
            {
                Instance.DoLog($"PowerWalker Collision Exit: {col.gameObject.name}");
            }

            private void OnTriggerEnter(Collider col)
            {
                Instance.DoLog($"PowerWalker Trigger Enter: {col.gameObject.name}");
            }

            private void OnTriggerExit(Collider col)
            {
                Instance.DoLog($"PowerWalker Trigger Exit: {col.gameObject.name}");
            }

            private void SpawnLadders(bool tall = false)
            {
                int start = FindNearestSpanStart();
                spawnRot = spans[start].start.rotation * Quaternion.Euler(0, 0, 0);

                int lcount = 5;
                if (spans[start].start.position.y - TerrainMeta.HeightMap.GetHeight(spans[start].start.position) > 13f)
                {
                    // So far, this never happens.  There are low wires, and they are always the closest.  Also, these undamaged poles are rare.
                    lcount = 20;
                }

                Instance.DoLog($"Building {lcount} ladders...");
                for (int i = 0; i < lcount; i++)
                {
                    GameObject go = SpawnPrefab(prefabladder, spawnPos, spawnRot, true);
                    BaseEntity ladder = go?.GetComponent<BaseEntity>();
                    RemoveComps(ladder);
                    ladder?.Spawn();
                    ladder.SetFlag(BaseEntity.Flags.Busy, true, true); // Blocks pickup

                    ladders.Add(ladder);
                    spawnPos += new Vector3(0, 3, 0);
                }
            }

            private void KillPlatforms()
            {
                DestroyImmediate(pp1);
                DestroyImmediate(pp2);
            }

            private void KillLadders()
            {
                if (ladders.Count == 0) return;
                for (int i = 0; i < ladders.Count; i++)
                {
                    ladders[i]?.Kill();
                }
                ladders = new List<BaseEntity>();
            }

            private static GameObject SpawnPrefab(string prefabname, Vector3 pos, Quaternion angles, bool active)
            {
                GameObject prefab = GameManager.server.CreatePrefab(prefabname, pos, angles, active);

                if (prefab == null) return null;

                prefab.transform.position = pos;
                prefab.transform.rotation = angles;
                prefab.gameObject.SetActive(active);

                return prefab;
            }

            public void RemoveComps(BaseEntity obj)
            {
                StabilityEntity hasstab = obj.GetComponent<StabilityEntity>();
                if (hasstab != null)
                {
                    hasstab.grounded = true;
                }
                GroundWatch hasgw = obj.GetComponent<GroundWatch>();
                if (hasgw != null)
                {
                    DestroyImmediate(hasgw);
                }
                foreach (MeshCollider mesh in obj.GetComponentsInChildren<MeshCollider>())
                {
                    DestroyImmediate(mesh);
                }
            }

            private void OnDestroy()
            {
                KillPlatforms();
                KillLadders();
            }
        }

        public class PowerPlatform : MonoBehaviour
        {
            public BasePlayer player;

            public Vector3 start;
            public Vector3 end;
            public Quaternion rotation;
            public Vector3 distance;
            public Vector3 direction;
            public float length;
            public float count;
            public float segment;

            public List<BuildingBlock> floors;

            private const string stdprefab = "assets/prefabs/building core/floor/floor.prefab";
            private const string triprefab = "assets/prefabs/building core/floor.triangle/floor.triangle.prefab";

            public void Setup(Vector3 st, Vector3 en, Quaternion rot, float len)
            {
                start = st; end = en;
                rotation = rot;
                distance = (start + end) / 2;
                direction = (end - start).normalized;
                length = len;
                //count = (float)Math.Floor(len / 1.689346) + 1;
                count = (float)Math.Floor(len / 1.5);// + 1;
                segment = len / count;
                floors = new List<BuildingBlock>();

                Instance.DoLog($"Need to build {count} floors from {start} to {end} in {segment}m segments");
                //Instance.DoLog($"Direction = {direction.ToString()}");
                SpawnPlatform();
            }

            private void OnDestroy()
            {
                if (floors.Count == 0) return;
                foreach (BuildingBlock floor in floors)
                {
                    floor.Kill();
                }
                floors = new List<BuildingBlock>();
            }

            private void SpawnPlatform()
            {
                List<Connection> connections = Net.sv.connections.Where(con => con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player).ToList();
                //Vector3 pos = start + (direction * 0.844673f);
                Vector3 pos = start + (direction * 1.7f);
                for (int i = 0; i < count; i++)
                {
                    string prefab = stdprefab;
                    bool counterrot = true;
                    float pwidth = 3f;
                    if (i == 0)
                    {
                        pos += direction * pwidth / 2;
                        //if (i == 0 ) pos += (direction * 1f);
                        prefab = triprefab;
                        pwidth = 1.5f;
                        counterrot = false;
                    }
                    else if (i == count - 1)
                    {
                        pos -= direction * pwidth / 2;
                        prefab = triprefab;
                        pwidth = 1.5f;
                    }
                    Instance.DoLog($"Spawning floor {i} at {pos}");
                    BaseEntity be = SpawnPart(prefab, null, pos, rotation, 0, counterrot);
                    BuildingBlock block = be as BuildingBlock;
                    if (!Instance.configData.Options.ShowPlatformsToAll)
                    {
                        block?.OnNetworkSubscribersLeave(connections);
                    }
                    floors.Add(block);

                    //pos += direction * 1.689346f;
                    pos += direction * pwidth;
                }
            }

            private BaseEntity SpawnPart(string prefab, BaseEntity entitypart, Vector3 position, Quaternion rotation, ulong skin = 0UL, bool counterrot = false)
            {
                if (entitypart == null)
                {
                    entitypart = GameManager.server.CreateEntity(prefab, position, rotation);
                    if (counterrot)
                    {
                        entitypart.transform.RotateAround(position, transform.up, 180f);
                    }
                    entitypart.skinID = skin;
                    entitypart.Spawn();

                    if (entitypart != null)
                    {
                        SpawnRefresh(entitypart);
                    }
                }

                return entitypart;
            }

            private void SpawnRefresh(BaseEntity entity)
            {
                StabilityEntity hasstab = entity.GetComponent<StabilityEntity>();
                if (hasstab != null)
                {
                    hasstab.grounded = true;
                }
                DecayEntity hasdecay = entity.GetComponent<DecayEntity>();
                if (hasdecay != null)
                {
                    hasdecay.decay = null;
                }
                BuildingBlock hasblock = entity.GetComponent<BuildingBlock>();
                if (hasblock != null)
                {
                    hasblock.SetGrade(BuildingGrade.Enum.Wood);
                    hasblock.SetHealthToMax();
                    hasblock.UpdateSkin();
                    hasblock.ClientRPC(null, "RefreshSkin");
                }
                //List<Connection> connections = Net.sv.connections.Where(con => con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player).ToList();
                List<Connection> connections = Net.sv.connections.Where(con => con.connected && con.isAuthenticated && con.player is BasePlayer).ToList();
                foreach (Connection c in connections)
                {
                    Instance.DoLog($"Found connection authstatus:{c.authStatus} connected:{c.connected}");
                }
                entity.OnNetworkSubscribersLeave(connections);
            }
        }

        public class PowerPlatformOld : MonoBehaviour
        {
            public BoxCollider coll;
            public Rigidbody rigidbody;
            public BasePlayer player;

            public void Setup(Vector3 pos, Quaternion rot, Vector3 size)
            {
                gameObject.name = "PowerlinePlatform";
                gameObject.layer = (int)Layer.Deployed;
                gameObject.transform.position = pos;
                gameObject.transform.rotation = rot;
                gameObject.transform.localScale = size;

                Instance.DoLog("Adding rigidbody");
                rigidbody = gameObject.AddComponent<Rigidbody>();
                //rigidbody.MovePosition(gameObject.transform.position);
                //rigidbody.drag = 1f;
                //rigidbody.maxAngularVelocity = 7;
                rigidbody.mass = 1000;
                //rigidbody.centerOfMass = gameObject.transform.position;
                //rigidbody.AddForce(Vector3.up, ForceMode.Acceleration);
                rigidbody.useGravity = false;
                rigidbody.isKinematic = false; // if false and gravity true, it falls, so i know it's there...
                rigidbody.detectCollisions = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rigidbody.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;// X | RigidbodyConstraints.FreezeRotationY;
                //rigidbody.constraints = RigidbodyConstraints.FreezePosition;// & RigidbodyConstraints.FreezeRotation;// X & RigidbodyConstraints.FreezeRotationZ;
                //rigidbody.constraints = RigidbodyConstraints.FreezeRotation;// & RigidbodyConstraints.FreezeRotation;// X & RigidbodyConstraints.FreezeRotationZ;
                //rigidbody.freezeRotation = true;
                rigidbody.SetDensity(10);
                rigidbody.transform.localScale = size;
                //rigidbody.gameObject.SetActive(true);
                //rigidbody.WakeUp();

                Instance.DoLog("Adding box collider");
                coll = gameObject.AddComponent<BoxCollider>();
                //coll.transform.localPosition = gameObject.transform.position;
                //coll.center = gameObject.transform.position;
                coll.size = gameObject.transform.localScale;
                coll.isTrigger = false;
                coll.IsOnLayer(0);
                coll.material = new PhysicMaterial()
                {
                    bounciness = 1,
                    dynamicFriction = 5,
                    frictionCombine = PhysicMaterialCombine.Maximum
                };
                coll.
                //coll.IsOnLayer((int)Layer.Reserved1);
                //coll.IsOnLayer((int)Layer.Transparent);
                //coll.enabled = true;
                //coll.gameObject.SetActive(true);

                gameObject.SetActive(true);
            }

            private void OnDestroy()
            {
                Destroy(coll);
                //Destroy(rigidbody);
            }

            public void OnCollisionEnter(Collision collision)
            {
                GameObject go = collision.gameObject;
                if (go.GetComponent<BasePlayer>())
                {
                    Instance.DoLog($"PowerlinePlatform Collision Enter: {go.name} {go.GetInstanceID()} on layer {collision.collider.gameObject.layer}");
                    return;
                }
                Physics.IgnoreCollision(collision.collider, coll);
            }

            public void OnCollisionExit(Collision collision)
            {
                GameObject go = collision.gameObject;
                if (go.GetComponent<BasePlayer>())
                {
                    Instance.DoLog($"PowerlinePlatform Collision Exit: {go.name} {go.GetInstanceID()}");
                    return;
                }
                Physics.IgnoreCollision(collision.collider, coll);
            }

            public void OnTriggerEnter(Collider coll)
            {
                GameObject go = coll.gameObject;
                //Instance.DoLog($"PowerlineCollider Trigger Exit: {go.name} {go.GetType().Name}");

                if (go.GetComponent<BasePlayer>())
                {
                    Instance.DoLog($"PowerlinePlatform Exit: {go.GetComponent<BasePlayer>().displayName}");
                }
            }

            public void OnTriggerExit(Collider coll)
            {
                GameObject go = coll.gameObject;
                //Instance.DoLog($"PowerlineCollider Trigger Enter: {go.name} {go.GetType().Name}");
                if (go.GetComponent<BasePlayer>())
                {
                    Instance.DoLog($"PowerlinePlatform Enter: {go.GetComponent<BasePlayer>().displayName}");
                }
            }

            public void FixedUpdate()
            {
                if (Instance.configData.Options.debug)
                {
                    Debug(player);
                }
            }

            public void Debug(BasePlayer player)
            {
                if (player == null) return;
                //BoxCollider coll = gameObject.GetComponent<BoxCollider>();
                //Rigidbody rigidbody = gameObject.GetComponent<Rigidbody>();

                //Instance.DoLog($"gameObject size {gameObject.transform.localScale.ToString()}");
                //Instance.DoLog($"Collider center {coll.center.ToString()}");
                //Instance.DoLog($"Collider size {coll.size.ToString()}");
                //Instance.DoLog($"Collider bounds {coll.bounds.ToString()}");
                //Instance.DoLog($"Collider isTrigger {coll.isTrigger.ToString()}");
                ////Instance.DoLog($"New collider instanceid: {coll.gameObject.GetInstanceID()}");

                Vector3 center = coll.transform.position;
                Vector3 front = coll.transform.TransformPoint(Vector3.forward);// * (spans[st1].WireLength / 2));
                Vector3 back = coll.transform.TransformPoint(-Vector3.forward);// * -(spans[st1].WireLength / 2));
                Vector3 left = coll.transform.TransformPoint(-Vector3.right);
                Vector3 right = coll.transform.TransformPoint(Vector3.right);
                Vector3 top = coll.transform.TransformPoint(Vector3.up);
                Vector3 bot = coll.transform.TransformPoint(-Vector3.up);

                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, center, $"<size=20>CENTER {center}</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, front, $"<size=20>FRONT</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, front, 0.25f);
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, back, $"<size=20>BACK</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, back, 0.25f);
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, left, $"<size=20>LEFT</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, left, 0.25f);
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, right, $"<size=20>RIGHT</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, right, 0.25f);
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, top, $"<size=20>TOP</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, top, 0.25f);
                player.SendConsoleCommand("ddraw.text", 1, Color.yellow, bot, $"<size=20>BOTTOM</size>");
                player.SendConsoleCommand("ddraw.arrow", 1, Color.white, center, bot, 0.25f);

                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.position, $"<size=20>RB CTR {rigidbody.transform.position}</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(Vector3.forward), $"<size=20>RB FRT</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(-Vector3.forward), $"<size=20>RB BAK</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(Vector3.right), $"<size=20>RB LT</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(-Vector3.right), $"<size=20>RB RT</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(Vector3.up), $"<size=20>RB TOP</size>");
                player.SendConsoleCommand("ddraw.text", 1, Color.red, rigidbody.transform.TransformPoint(-Vector3.up), $"<size=20>RB BOT</size>");

                //Instance.DoLog($"Rigidbody.center: {rigidbody.transform.position}");
                //Instance.DoLog($"Rigidbody.rotation: {rigidbody.transform.rotation}");
                //Instance.DoLog($"Rigidbody.centerOfMass: {rigidbody.centerOfMass}");
            }
        }
        #endregion Classes
    }
}

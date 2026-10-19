using Oculus.Interaction.DebugTree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
//using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using Meta.WitAi.Utilities;
using UnityEngine.XR.Interaction.Toolkit;
using System.Threading.Tasks;
using TMPro;
using static System.Net.Mime.MediaTypeNames;
using UnityEngine.Experimental.GlobalIllumination;
using System.Text.RegularExpressions;
using System.Globalization;
using JetBrains.Annotations;
using XCharts.Runtime;
using UnityEngine.SceneManagement;
using static NewBehaviourScript;
using static GLTFast.Schema.AnimationChannelBase;



public class NewBehaviourScript : MonoBehaviour
{
    [SerializeField] string name;
    [SerializeField] float geo_scale; // only to be used for example scenes. allowed to vary across scenes.
    public float scale; // only to be used for custom scene

    [SerializeField] public float scene_time;
    [SerializeField] public float step_time;

    //string fileName = @"/data/local/tmp/exampleB5_tracks - Copy.csv";
    [SerializeField] public static Material lineMaterial ;
    [SerializeField] Mesh markerMesh;
    [SerializeField] Material markerMat;
    [SerializeField] public Transform player; 
    
    public static List<Matrix4x4> markerMatrices = new List<Matrix4x4>();
    bool drawMeshes = true;

    // Run-scoped string table shared by every Track. The binary format already ships one (each step
    // references a name by a ushort index into it), and the CSV reader interns into the same table, so
    // no Track ever stores per-step strings of its own -- see Track.ProcessAt / Track.VolumeAt.
    public static string[] stringTable = System.Array.Empty<string>();

    // Interning state, alive only while a file is being parsed; dropped by FinishStringTable().
    private static Dictionary<string, ushort> stringIds;
    private static List<string> stringScratch;

    public static List<GameObject> tracks = new List<GameObject>();
    // Inner key is (eventID << 32 | trackID), NOT trackID alone: GEANT4 restarts trackID at 1 for
    // every event, and a multi-event run's tracks all land in the same binary file, so trackID by
    // itself is not unique across the whole run.
    public static Dictionary<string, Dictionary<long, Track>> trackInfo = new Dictionary<string, Dictionary<long, Track>>();

    // True when the loaded run carries a per-step GEANT4 volumeID (binary/Custom scenes).
    // False for the bundled CSV example scenes, which have no such column.
    public static bool hasVolumeInfo = false;
    public double time_scale;
    private Dictionary<Track, double> orderedtime = new Dictionary<Track, double>();
    // NOTE: the old `time_control` (Dictionary<string, List<GameObject>> keyed by time.ToString())
    // used to live here. It cost one heap string + one List object per unique step time -- millions of
    // each on a large run -- and drove nothing visible, since the objects it toggled are the invisible
    // per-step colliders. Time slicing is owned by TrackMeshRenderer, which indexes a sorted segment
    // array directly; SimTracks/SteppedTracks now go through it.
    //private static Dictionary<int, List<Vector3>> trackPoints = new Dictionary<int, List<Vector3>>();

    public static List<ColliderEntry> colliders = new List<ColliderEntry>();

    // SETTINGS AND STATES TO MANAGE COLLIDERS

    // Raised well above the old 1000 / 2.0 / 2.5 / 5s now that Recompute no longer scans every segment
    // in the run on every tick (see BuildColliderGrid). The limiting factor is no longer the search but
    // the number of live XRSimpleInteractables, so maxActive is the knob to turn if the headset
    // struggles -- the radii only decide how far ahead tracks become grabbable.
    public int maxActive = 5000;
    public float recomputeInterval = 1.5f;
    public float activateRadius = 6.0f;
    public float releaseRadius = 7.5f;
    // Debug escape hatch: activates EVERY segment collider at once. Do not turn this on for a real
    // run -- it means millions of active GameObjects, each with a Rigidbody, a CapsuleCollider and an
    // XRSimpleInteractable that the XR Interaction Manager then walks every frame. Edep mode used to
    // set it; it now calls SuspendTrackColliders instead.
    public bool overriden = false;

    // While true the proximity system is parked and no segment collider is active at all.
    private bool trackCollidersSuspended = false;


    private HashSet<int> activeSet = new HashSet<int>();
    private HashSet<int> nextActiveSet = new HashSet<int>();
    private float timer;

    // --- spatial index over the track colliders ---------------------------------------------------
    // Recompute used to test the camera against every segment in the run -- millions of point-to-segment
    // distances -- on every tick. That cost is what kept activateRadius and maxActive pinned low, and it
    // arrived as a visible hitch every recomputeInterval seconds.
    //
    // Tracks are bucketed into a uniform grid once at load, so a tick only visits the handful of cells
    // around the camera. Indexing is per *track*, not per segment: a track's segments are contiguous in
    // `colliders` (DrawTrack appends them in one run), and a track covers far fewer cells than its
    // segments do individually, so the index is ~10x smaller for the same effect.
    //
    // The grid lives in the scale root's local space -- the same space Recompute compares in -- so the
    // user scale slider never invalidates it.
    private struct TrackRange { public int first, count; }
    private struct OversizedTrack { public int track; public Vector3 min, max; }

    // A track spanning more cells than this is held aside and tested against its bounding box instead,
    // so one stray long-flying particle cannot smear itself across the whole grid.
    private const int MaxCellsPerTrack = 128;

    private float gridCellSize = 1f;
    private readonly Dictionary<long, int> gridBuckets = new Dictionary<long, int>(); // cell key -> bucket
    private int[] gridBucketStart = System.Array.Empty<int>();                        // CSR offsets, buckets + 1
    private int[] gridEntries = System.Array.Empty<int>();                            // indices into gridTracks
    private readonly List<TrackRange> gridTracks = new List<TrackRange>();
    private readonly List<OversizedTrack> gridOversized = new List<OversizedTrack>();

    // Visit stamps, so a track reachable through several cells is only considered once per query.
    private int[] gridStamp = System.Array.Empty<int>();
    private int gridStampCounter;
    private readonly List<int> gridQueryResult = new List<int>();

    List<Track> trackInstances = new List<Track>();
    //private Vector3 fixedStartPoint = new Vector3(0, -63.5f, -127f);

    public Slider time_controller;

    public GameObject time;
    public GameObject start_time;
    public GameObject stop_time;
    public GameObject status;
    public GameObject toggle_prefab;
    public GameObject movie_prefab;

    public GameObject AnalysisBoard;
    public GameObject CutsBoard;
    public GameObject EdepBoard;
    public GameObject Controls;
    public GameObject Menus;

    HashSet<string> particles_in_scene = new HashSet<string>();


    public float startTime = 0.0f;

    public double maxT, minT;

    public Button playButton;

    private bool playbool = false;

    public TextAsset file;
    public string customFile;

    public bool checkScale = false;
    public float checkedScale = 1f;
    public bool appliedScaleToGeometry = false;

    // ---- User-controlled scale (the "Scale" slider) ----------------------------------------------------
    // Discrete log-scale values; index 3 == 1.0 is the default (no change from current behaviour).
    public static readonly float[] userScaleValues = { 0.001f, 0.01f, 0.1f, 1f, 10f };
    // Static so it survives run switches (a new track_renderer clone is instantiated per run).
    public static int userScaleIndex = 3;
    // Shared parent that holds the geometry ("Scene"), this track-renderer clone (with the unified track
    // mesh) and every track's collider GameObject, so they all scale together about the origin. Its
    // localScale is the user scale, multiplied on top of the auto-fit checkedScale (which stays on the
    // geometry's own localScale and is baked into track positions). DontDestroyOnLoad, like the geometry.
    public static Transform scaleRoot;
    // The "Scale" slider itself (cloned from the time slider at runtime, Custom scene only).
    public Slider scale_controller;

    // The "Scale : Nx" readout above that slider, and the value it currently shows (NaN = never drawn).
    private const string ScaleLabelName = "ScaleLabel";

    // Points added on top of whatever the surrounding panel labels use, so the scale readout reads
    // clearly at arm's length in the headset.
    private const float ScaleLabelFontBoost = 6f;
    private TextMeshProUGUI scaleLabel;
    private float lastLabelledScale = float.NaN;

    // The scale the experiment is actually displayed at: the auto-fit factor chosen from the data's
    // own coordinate magnitudes (checkedScale) times the slider's multiplier. The slider position alone
    // would be misleading -- with an auto-fit of 0.01 and the slider at 10x, the experiment is being
    // shown at 0.1x, not 10x.
    public float TotalScale => checkedScale * userScaleValues[userScaleIndex];

    // warning in case CSV could not be fully read or understood:
    bool csvWarning = false;

    // Drops everything belonging to the currently-loaded run: the per-step arrays (the bulk of the
    // memory), the collider bookkeeping and the string table. Safe to call when nothing is loaded.
    public static void ReleaseRunData()
    {
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
            {
                // Destroy before releasing: ReleaseStepData nulls trackObj, so anything still standing
                // (a scene torn down without going through RunManager) would be orphaned otherwise.
                if (track.trackObj != null)
                    Destroy(track.trackObj);

                track.ReleaseStepData();
            }
        }

        trackInfo.Clear();
        tracks.Clear();
        colliders.Clear();
        colliders.TrimExcess();
        markerMatrices.Clear();
        markerMatrices.TrimExcess();

        stringTable = System.Array.Empty<string>();
        stringIds = null;
        stringScratch = null;
        hasVolumeInfo = false;
    }

    // Full teardown for leaving an analysis scene entirely. On top of the run data this drops the
    // statics that would otherwise survive into the next scene: the scale root (a destroyed Transform),
    // the leaked line material, and the user's scale choice, which is only meaningful within a run.
    public static void ResetStaticState()
    {
        ReleaseRunData();

        scaleRoot = null;
        userScaleIndex = 3;

        if (movieTrailMaterial != null)
        {
            Destroy(movieTrailMaterial);
            movieTrailMaterial = null;
        }

        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }
    }

    // --- run-scoped string interning -------------------------------------------------------------

    private static void BeginStringTable()
    {
        stringIds = new Dictionary<string, ushort>();
        stringScratch = new List<string>();
    }

    // Returns a stable ushort id for `value`, adding it to the table on first sight. Used by the CSV
    // reader; the binary reader gets its table verbatim from the file instead.
    private static ushort InternString(string value)
    {
        value = value ?? "";
        if (stringIds.TryGetValue(value, out ushort existing))
            return existing;

        if (stringScratch.Count > ushort.MaxValue)
        {
            UnityEngine.Debug.LogWarning($"[NEW-BEHAVIOUR-SCRIPT] String table exceeded {ushort.MaxValue} entries; '{value}' will be reported as unknown.");
            return 0;
        }

        ushort id = (ushort)stringScratch.Count;
        stringScratch.Add(value);
        stringIds[value] = id;
        return id;
    }

    private static void FinishStringTable()
    {
        stringTable = stringScratch != null ? stringScratch.ToArray() : System.Array.Empty<string>();
        stringIds = null;
        stringScratch = null;
    }

    // Resolves ids[i] against the run's string table. Returns "" for a missing/out-of-range entry
    // rather than throwing, since a malformed file should degrade to a blank label, not a crash.
    public static string LookupString(ushort[] ids, int i)
    {
        if (ids == null || i < 0 || i >= ids.Length)
            return "";
        ushort id = ids[i];
        return id < stringTable.Length ? stringTable[id] : "";
    }

    IEnumerator waiting()
    {
        int F = 5;
        yield return new WaitForSeconds(F);
        movie_init();

    }
    void Start()
    {
        // In a custom scene, this function is automatically called when NewBehaviourScript is instantiated
        // This will create your tracks from the csv file. 

        // Set  up the scene 

        // Release the outgoing run before reading the next one, so the two never coexist in memory.
        // (Clearing the dictionary alone is not enough: RunManager may still be holding Track objects
        // while it tears down their GameObjects.)
        ReleaseRunData();
        hasVolumeInfo = false; // set true by ReadBIN if this run actually carries per-step volume IDs

        //time = GameObject.Find("Time");
        start_time = GameObject.Find("Start");
        stop_time = GameObject.Find("Stop");
        status = GameObject.Find("Status");

        // the below must all be references to the PANELs of the boards; NEVER TURN OFF THE BOARDS!
        AnalysisBoard = GameObject.Find("Analysis Board").transform.GetChild(0).gameObject;
        CutsBoard = GameObject.Find("Cuts Board").transform.GetChild(0).gameObject;
        EdepBoard = GameObject.Find("Edep Board").transform.GetChild(0).gameObject;
        Controls = GameObject.Find("CPanel");
        Menus = GameObject.Find("MPanel");

        player = GameObject.Find("Main Camera").transform;

        time_controller = GameObject.Find("TSlider").GetComponent<Slider>();
        //playButton = GameObject.Find("TPlay").GetComponent<Button>();

        Shader shader = Shader.Find("Standard");
        string fileName = @$"C:\Users\uclav\Documents\B's Sandbox\G4VR\Assets\{name}\{name}_tracks.csv";
        string filePath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, fileName);
        // Renderer.material hands back a *new* instance each time it is read, and a run switch builds a
        // fresh track_renderer clone, so the previous instance has to be destroyed or it is stranded.
        if (lineMaterial != null)
            Destroy(lineMaterial);
        lineMaterial = GameObject.Find("line_mat").GetComponent<Renderer>().material;
        lineMaterial.shader = shader;
        lineMaterial.EnableKeyword("_EMISSION");

        // read csv and draw tracks
        if (SceneManager.GetActiveScene().name == "Custom" || file == null)
        { 
            // Note: the file is NOT deleted after reading any more. It lives in RunCache, which is what
            // lets a second visit to the same run skip the download entirely; RunCache.Prepare evicts
            // the previous address's files, so this does not grow without bound.
            ReadBIN(customFile, true);
        }
        else
            ReadCSV(file, true);

        DrawTracks(1f);

        BuildColliderGrid();


        // additional functions (if any)

        Format_Cuts(); // THIS HAS BEEN COMMENTED OUT JUST TO TEST THE G4VR VIS STUFF
                       //TrackAnalyser.Starter();
                       //SimTracks();
       
        // only for custom scene since settings are not initialized in the inspector
        if (SceneManager.GetActiveScene().name=="Custom")
        {
            UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Configuring settings for Custom scene");
            configureSettings();

            // Set up the "Scale" slider's shared root: this track-renderer clone (with the track mesh) and all
            // track colliders go under it; the geometry is scaled to match directly in Update(). Together the
            // slider resizes the whole experiment uniformly.
            EnsureScaleRoot();
            ReparentIntoScaleRoot();
        }

    }

    // Creates (once per Custom-scene load) the shared scale root for the tracks. It is intentionally
    // scene-scoped (NOT DontDestroyOnLoad): the track-renderer clone and track colliders parented under it
    // must die with the scene. The geometry ("Scene") is DontDestroyOnLoad and is NOT reparented -- instead
    // Update() scales it directly by checkedScale * userScale, which keeps the geometry<->track relative
    // scale identical while avoiding the lifetime pitfalls of reparenting a persistent object under a
    // scene-scoped one. A run switch (same scene) reuses this root; new clones re-parent into it.
    private void EnsureScaleRoot()
    {
        if (scaleRoot == null)
        {
            GameObject existing = GameObject.Find("WorldScaleRoot");
            scaleRoot = existing != null ? existing.transform : new GameObject("WorldScaleRoot").transform;
        }

        scaleRoot.localScale = Vector3.one * userScaleValues[userScaleIndex];
    }

    // Parents this clone (carrying the unified track mesh) and every track's collider GameObject under the
    // scale root. worldPositionStays=false preserves the baked local positions, so colliders stay coincident
    // with the mesh vertices once the root scales them.
    private void ReparentIntoScaleRoot()
    {
        if (scaleRoot == null)
            return;

        transform.SetParent(scaleRoot, false);

        foreach (var typeEntry in trackInfo)
            foreach (var track in typeEntry.Value)
                if (track.Value.trackObj != null && track.Value.trackObj.transform.parent != scaleRoot)
                    track.Value.trackObj.transform.SetParent(scaleRoot, false);
    }

    // Called by the Scale slider. Applies the discrete user scale to the shared root; the k-nearest collider
    // search reads positions in root-local space (see Recompute), so its effective world radius tracks this
    // scale automatically -- no separate radius adjustment needed.
    public void SetUserScale(int index)
    {
        userScaleIndex = Mathf.Clamp(index, 0, userScaleValues.Length - 1);
        if (scaleRoot != null)
            scaleRoot.localScale = Vector3.one * userScaleValues[userScaleIndex];

        UpdateScaleLabel();
    }

    private void Update()
    {
        GameObject Geometry = GameObject.Find("Scene");
        if (Geometry != null)
        {
            // Auto-fit scale (checkedScale) times the user-chosen scale. The tracks live under scaleRoot
            // (localScale = userScale) and their vertices are baked with checkedScale, so applying the same
            // product here keeps the geometry and tracks at an identical, invariant relative scale.
            float geometryScale = checkedScale * userScaleValues[userScaleIndex];
            Geometry.transform.localScale = new Vector3(geometryScale, geometryScale, geometryScale);
            //appliedScaleToGeometry = true;
            //UnityEngine.Debug.Log("[NewBehaviourScript] set scene scale to "+checkedScale);
        }
        //else 
        //UnityEngine.Debug.Log("[NewBehaviourScript] Could not find Scene");

        UpdateScaleLabel();

        if (!trackCollidersSuspended)
        {
            timer += Time.deltaTime;
            if (timer >= recomputeInterval)
            {
                timer = 0f;
                Recompute(overriden);
            }
        }

        if (movieClockRunning)
            UpdateMovie();
    }

    void LateUpdate()
    {
        if (drawMeshes == true)
        {
            for (int i = 0; i < markerMatrices.Count; i += 1023)
            {
                int count = Mathf.Min(1023, markerMatrices.Count - i);

                Graphics.DrawMeshInstanced(
                    markerMesh,
                    0,
                    markerMat,
                    markerMatrices.GetRange(i, count),
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false
                );
            }
        }
    }
    string ReadCString(BinaryReader reader)
    {
        ushort len = reader.ReadUInt16();
        byte[] bytes = reader.ReadBytes(len);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    void ReadBIN(string file, bool dummy)
    {
        using (BinaryReader reader = new BinaryReader(File.Open(file, FileMode.Open)))
        {
            byte[] magicBytes = reader.ReadBytes(4);
            string magic = System.Text.Encoding.ASCII.GetString(magicBytes);
            uint version = reader.ReadUInt32();
            uint stringCount = reader.ReadUInt32();
            uint trackCount = reader.ReadUInt32();

            Debug.Log($"Magic={magic} version={version} strings={stringCount} tracks={trackCount}");

            // Binary v2+ stores an explicit eventID per track. GEANT4 restarts trackID at 1 for every
            // event, and a multi-event run's tracks all land in this same file, so trackID alone is not
            // a unique key across the run. v1 files predate this field; fall back to detecting event
            // boundaries the same way the exporter itself does (trackID==1 marks a new event).
            bool hasExplicitEventID = version >= 2;
            if (!hasExplicitEventID)
                Debug.LogWarning($"[NEW-BEHAVIOUR-SCRIPT] Binary version {version} predates per-track eventID; falling back to a trackID==1 heuristic to separate events. Re-export with the current exporter for exact event attribution.");

            int fallbackEventIndex = 0;
            bool sawFirstTrack = false;

            minT = double.PositiveInfinity;
            maxT = double.NegativeInfinity;

            // Reusable per-step read buffers. These used to be eleven fresh arrays per track, i.e. ~4.5
            // million allocations on a 400k-track run, all of which had to be collected. They are grown
            // to the largest step count seen and then reused for every subsequent track.
            float[] x = null, y = null, z = null, time = null, edep = null;
            float[] px = null, py = null, pz = null, energy = null;
            ushort[] processID = null, volumeID = null;
            int bufferCapacity = 0;

            for (int t = 0; t < trackCount; t++)
            {
                int trackID = reader.ReadInt32();

                int eventID;
                if (hasExplicitEventID)
                {
                    eventID = reader.ReadInt32();
                }
                else
                {
                    if (trackID == 1)
                    {
                        if (sawFirstTrack) fallbackEventIndex++;
                        sawFirstTrack = true;
                    }
                    eventID = fallbackEventIndex;
                }

                long uniqueKey = ((long)eventID << 32) | (uint)trackID;

                ushort nameID = reader.ReadUInt16();
                double charge = reader.ReadDouble();

                float r = reader.ReadSingle();
                float g = reader.ReadSingle();
                float b = reader.ReadSingle();

                uint n = reader.ReadUInt32();

                if (n > bufferCapacity)
                {
                    bufferCapacity = (int)n;
                    x = new float[bufferCapacity];
                    y = new float[bufferCapacity];
                    z = new float[bufferCapacity];
                    time = new float[bufferCapacity];
                    edep = new float[bufferCapacity];
                    px = new float[bufferCapacity];
                    py = new float[bufferCapacity];
                    pz = new float[bufferCapacity];
                    energy = new float[bufferCapacity];
                    processID = new ushort[bufferCapacity];
                    volumeID = new ushort[bufferCapacity];
                }

                for (int i = 0; i < n; i++) x[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) y[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) z[i] = reader.ReadSingle();

                for (int i = 0; i < n; i++) time[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) edep[i] = reader.ReadSingle();

                for (int i = 0; i < n; i++) px[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) py[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) pz[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) energy[i] = reader.ReadSingle();

                for (int i = 0; i < n; i++) processID[i] = reader.ReadUInt16();
                for (int i = 0; i < n; i++) volumeID[i] = reader.ReadUInt16();

                string pname = null;
                string type = charge.ToString(CultureInfo.InvariantCulture);

                bool colorByRGB = !(r == 0f && g == 0f && b == 0f);

                // No logging here: this runs once per track, and a 400k-track run would emit 400k log
                // lines, each with a captured stack trace (see m_StackTraceTypes in ProjectSettings).
                Color trackColor = colorByRGB
                    ? new Color(r * 255f, g * 255f, b * 255f)
                    : GetColor(type);

                if (!trackInfo.ContainsKey(type))
                {
                    trackInfo[type] = new Dictionary<long, Track>();
                }

                if (!trackInfo[type].ContainsKey(uniqueKey))
                {
                    trackInfo[type][uniqueKey] = new Track();
                    trackInfo[type][uniqueKey].ID = trackID;
                    trackInfo[type][uniqueKey].eventID = eventID;
                    trackInstances.Add(trackInfo[type][uniqueKey]);
                }

                Track tr = trackInfo[type][uniqueKey];
                tr.BeginSteps((int)n); // exact capacity: the file tells us the step count up front

                tr.type = type;
                tr.particleName = pname;
                tr.particleNameID = nameID;
                tr.colorByRGB = colorByRGB;
                tr.color = trackColor;

                for (int i = 0; i < n; i++)
                {
                    float posX = -x[i];
                    float posY = y[i];
                    float posZ = z[i];

                    List<float> poss = new List<float>() { Mathf.Abs(posX), Mathf.Abs(posY), Mathf.Abs(posZ)};

                    if (!checkScale)
                    {
                        checkedScale = checkScaleFromPosition(poss.Max());
                        GameObject[] geometries = { GameObject.Find("Scene"), GameObject.Find("exampleB3a_scene"), GameObject.Find("exampleB4a_scene"), GameObject.Find("exampleB5_scene") };
                        foreach (GameObject geo in geometries)
                        {
                            if (geo != null)
                                geo.transform.localScale = Vector3.one * checkedScale;
                        }

                        checkScale = true;
                    }

                    posX *= checkedScale;
                    posY *= checkedScale;
                    posZ *= checkedScale;
                    Vector3 position = new Vector3(posX, posY, posZ);

                    float tracktime = time[i];
                    float e = energy[i];
                    float d = edep[i];
                    //UnityEngine.Debug.Log("EDEP value:"+ d);

                    if (tracktime < minT) minT = tracktime;
                    if (tracktime > maxT) maxT = tracktime;

                    tr.AppendStep(position, tracktime, e, d, px[i], py[i], pz[i], processID[i], volumeID[i]);
                }

                // Freeze this track's steps immediately, so the growable lists for one track are the
                // only scratch alive at any moment rather than the whole run's worth.
                tr.Compact();
            }

            // The file's own string table becomes the run's table verbatim.
            string[] table = new string[stringCount];
            for (int sIdx = 0; sIdx < stringCount; sIdx++)
            {
                ushort len = reader.ReadUInt16();
                byte[] bytes = reader.ReadBytes(len);
                table[sIdx] = System.Text.Encoding.UTF8.GetString(bytes);
            }
            stringTable = table;

            // Only the per-track particle name is materialised. The per-step process and volume names
            // used to be expanded into two List<string> per track -- two more references per step on
            // top of the ushort ids that already encode them. Track.ProcessAt/VolumeAt resolve those
            // ids against the table on demand instead, which is what the UI needs anyway (one step at
            // a time, on hover or click).
            foreach (var typeDict in trackInfo.Values)
            {
                foreach (Track tr in typeDict.Values)
                {
                    tr.particleName = tr.particleNameID < stringTable.Length
                        ? stringTable[tr.particleNameID]
                        : "";
                    particles_in_scene.Add(tr.particleName);
                }
            }

            hasVolumeInfo = true; // binary tracks always carry a per-step volumeID; CSV example scenes do not
            Debug.Log("Finished reading BIN file.");
        }

        // The run's time range comes straight from the minT/maxT accumulated while parsing. It used to
        // be recovered by string-sorting millions of stringified step times -- which is what forced
        // time_control to exist at all. (The old `tracks.Add(tval.trackObj)` here also added nothing but
        // nulls, since trackObj is not created until DrawTracks runs; that list is filled there now.)
        if (double.IsInfinity(minT) || double.IsInfinity(maxT))
        {
            minT = 0.0;
            maxT = 0.0;
        }

        stop_time.GetComponent<TextMeshProUGUI>().text = maxT.ToString();
        start_time.GetComponent<TextMeshProUGUI>().text = minT.ToString();
        status.transform.GetComponent<TextMeshProUGUI>().text = "Complete";
        status.transform.GetComponent<TextMeshProUGUI>().color = Color.green;

        AnalysisBoard.SetActive(false);


        // initializing the unified mesh and the colliders

        TrackMeshRenderer trackMeshRenderer = gameObject.AddComponent<TrackMeshRenderer>();
        trackMeshRenderer.trackInstances = trackInstances;
        trackMeshRenderer.time_slider = time_controller;
        trackMeshRenderer.BuildMesh();

        trackMeshRenderer.SliderSetup();

        //trackMeshRenderer.SetTimeIndex(currentTimeIndex);

    }


    void ReadCSV(TextAsset file, bool dummy) 
    {
        // As of 2/2/2026, the expected input format of the csv file is as follows:
        // track/hit , ID, particle, charge, step, x, y, z, time of step, edep, process, px, py, pz, energy, R, G, B  
        string[] lines = Regex.Split(file.text, "\r\n|\r|\n");
        bool headerSkipped = false;
        maxT = double.NegativeInfinity;
        minT = double.PositiveInfinity;

        // The CSV carries process/particle names as text on every row. Interning them into the same
        // ushort-indexed table the binary format uses means a Track stores 2 bytes per step instead of
        // an 8-byte reference (plus the string itself, freshly allocated by Split on every single row).
        BeginStringTable();

        float T_range = 1f;

        foreach (string line in lines)
        {
            if (!headerSkipped)
            {
                headerSkipped = true;
                continue;
            }
            try
            {
                string[] values = line.Split(',');

                if (values[0] == "track") // process tracks; TODO: logic to process hits (future work)
                {
                    //Debug.Log("Parsing CSV");
                    int trackID = int.Parse(values[1], CultureInfo.InvariantCulture);
                    float posX, posY, posZ;
                    posX = -float.Parse(values[5], CultureInfo.InvariantCulture);
                    posY = float.Parse(values[6], CultureInfo.InvariantCulture);
                    posZ = float.Parse(values[7], CultureInfo.InvariantCulture);
                    List<float> poss = new List<float>() { Math.Abs(posX), Math.Abs(posY), Math.Abs(posZ) };
                    if (!checkScale)
                    {
                        checkedScale = checkScaleFromPosition(poss.Max());
                        GameObject[] geometries = { GameObject.Find("Scene"), GameObject.Find("exampleB3a_scene"), GameObject.Find("exampleB4a_scene"), GameObject.Find("exampleB5_scene") };

                        foreach (GameObject geo in geometries)
                        {
                            if (geo != null)
                            {
                                geo.transform.localScale =
                                    Vector3.one * checkedScale;
                            }
                        }

                        checkScale = true;
                    }
                    posX = -float.Parse(values[5], CultureInfo.InvariantCulture) * checkedScale;
                    posY = float.Parse(values[6], CultureInfo.InvariantCulture) * checkedScale;
                    posZ = float.Parse(values[7], CultureInfo.InvariantCulture) * checkedScale;

                    double energy = ParseHelper.ParseEnergy(values[14]);
                    double time = ParseHelper.ParseTime(values[8]);
                    string pname = values[2];

                    float px = float.Parse(values[11], CultureInfo.InvariantCulture);
                    float py = float.Parse(values[12], CultureInfo.InvariantCulture);
                    float pz = float.Parse(values[13], CultureInfo.InvariantCulture);

                    // COLORING 
                    bool colorByRGB = false;
                    Color trackColor = new Color();
                    string type = values[3];// type == charge. it is used to color tracks by GEANT4 convention; alternatively, coloured if RGB specified.
                    try
                    {
                        float r = float.Parse(values[15], CultureInfo.InvariantCulture);
                        float g = float.Parse(values[16], CultureInfo.InvariantCulture);
                        float b = float.Parse(values[17], CultureInfo.InvariantCulture);

                        colorByRGB = true;
                        trackColor = new Color(r * 255f, g * 255f, b * 255f);
                        UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Setting RGB values for track coloring");
                    }
                    catch
                    {
                        colorByRGB = false;
                        trackColor = GetColor(type);
                        UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Setting type values for track coloring");
                    }
                    string process = values[10];

                    double edep = ParseHelper.ParseEnergy(values[9]);

                    if (time < minT) { minT = time; }
                    if (time > maxT) { maxT = time; }

                    Vector3 position = new Vector3(posX, posY, posZ);

                    if (!trackInfo.ContainsKey(type))
                    {
                        trackInfo[type] = new Dictionary<long, Track>();
                        //Debug.Log("HELLO: added type to dictionary");
                    }

                    if (!trackInfo[type].ContainsKey(trackID))
                    {
                        trackInfo[type][trackID] = new Track();
                        trackInfo[type][trackID].ID = trackID;
                        //Debug.Log("HELLO: initialized track");
                        trackInstances.Add(trackInfo[type][trackID]);
                    }

                    Track csvTrack = trackInfo[type][trackID];
                    csvTrack.type = type;
                    csvTrack.particleName = pname;
                    csvTrack.particleNameID = InternString(pname);
                    csvTrack.colorByRGB = colorByRGB;
                    csvTrack.color = trackColor;

                    // volumeID 0: the CSV format has no per-step volume column, which is why
                    // hasVolumeInfo stays false for the example scenes and TrackAnalyser falls back to
                    // its collider-overlap edep accounting.
                    csvTrack.AppendStep(position, (float)time, (float)energy, (float)edep,
                                        px, py, pz, InternString(process), 0);

                    particles_in_scene.Add(pname);
                    //Debug.Log("Track Info Count " + trackInfo.Count);

                }
            }
            catch (Exception e)
            {
                csvWarning = true;
                UnityEngine.Debug.LogError("[READ-CSV] Errro in parsing track; skipping... (" + e.Message + ")");
                continue;
            }
        }

        //time_scale = 10.0f / (maxT - minT); // this needs an associated slider. 
        //Debug.Log("trackinfo length: " + trackInfo.Count);

        FinishStringTable();

        // Steps arrived interleaved across tracks, so freezing has to wait until the whole file is read.
        foreach (var typeDict in trackInfo.Values)
            foreach (Track tr in typeDict.Values)
                tr.Compact();

        // The run's time range comes straight from the minT/maxT accumulated while parsing. It used to
        // be recovered by string-sorting millions of stringified step times -- which is what forced
        // time_control to exist at all. (The old `tracks.Add(tval.trackObj)` here also added nothing but
        // nulls, since trackObj is not created until DrawTracks runs; that list is filled there now.)
        if (double.IsInfinity(minT) || double.IsInfinity(maxT))
        {
            minT = 0.0;
            maxT = 0.0;
        }

        stop_time.GetComponent<TextMeshProUGUI>().text = maxT.ToString();
        start_time.GetComponent<TextMeshProUGUI>().text = minT.ToString();
        status.transform.GetComponent<TextMeshProUGUI>().text = "Complete";
        status.transform.GetComponent<TextMeshProUGUI>().color = Color.green;

        AnalysisBoard.SetActive(false);


        // initializing the unified mesh and the colliders

        TrackMeshRenderer trackMeshRenderer = gameObject.AddComponent<TrackMeshRenderer>();
        trackMeshRenderer.trackInstances = trackInstances;
        trackMeshRenderer.time_slider = time_controller;
        trackMeshRenderer.BuildMesh();

        trackMeshRenderer.SliderSetup();

        //trackMeshRenderer.SetTimeIndex(currentTimeIndex);

    }


    // COLLIDER STUFF
    public struct ColliderEntry
    {
        public int id;
        public Vector3 start;
        public Vector3 end;
        public GameObject obj;    // reference to actual collider
    }

    // Buckets every track that produced collider segments into the uniform grid. Runs once per loaded
    // run, straight after DrawTracks, and is what makes a larger activateRadius and maxActive affordable.
    private void BuildColliderGrid()
    {
        gridBuckets.Clear();
        gridTracks.Clear();
        gridOversized.Clear();
        gridBucketStart = System.Array.Empty<int>();
        gridEntries = System.Array.Empty<int>();
        gridStamp = System.Array.Empty<int>();
        gridStampCounter = 0;

        if (colliders.Count == 0)
            return;

        // One cell per activation radius, so a query touches a 3x3x3 neighbourhood.
        gridCellSize = Mathf.Max(activateRadius, 0.01f);

        List<Vector3> mins = new List<Vector3>();
        List<Vector3> maxs = new List<Vector3>();

        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
            {
                if (track.colliderCount <= 0 || track.positions.Length == 0)
                    continue;

                Vector3 min = track.positions[0];
                Vector3 max = min;
                for (int i = 1; i < track.positions.Length; i++)
                {
                    min = Vector3.Min(min, track.positions[i]);
                    max = Vector3.Max(max, track.positions[i]);
                }

                gridTracks.Add(new TrackRange { first = track.firstColliderIndex, count = track.colliderCount });
                mins.Add(min);
                maxs.Add(max);
            }
        }

        gridStamp = new int[gridTracks.Count];

        // Pass 1: count how many tracks land in each cell, assigning bucket indices as cells appear.
        List<int> bucketCounts = new List<int>();

        for (int t = 0; t < gridTracks.Count; t++)
        {
            if (!TryCellRange(mins[t], maxs[t], out Vector3Int lo, out Vector3Int hi))
            {
                gridOversized.Add(new OversizedTrack { track = t, min = mins[t], max = maxs[t] });
                continue;
            }

            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        long key = CellKey(x, y, z);
                        if (!gridBuckets.TryGetValue(key, out int bucket))
                        {
                            bucket = bucketCounts.Count;
                            gridBuckets[key] = bucket;
                            bucketCounts.Add(0);
                        }

                        bucketCounts[bucket]++;
                    }
        }

        // Pass 2: prefix-sum the counts into CSR offsets -- one flat int[] rather than a List per
        // occupied cell, which would mean an object per cell.
        gridBucketStart = new int[bucketCounts.Count + 1];
        for (int b = 0; b < bucketCounts.Count; b++)
            gridBucketStart[b + 1] = gridBucketStart[b] + bucketCounts[b];

        gridEntries = new int[gridBucketStart[bucketCounts.Count]];
        int[] cursor = new int[bucketCounts.Count];

        // Pass 3: fill. TryCellRange is deterministic, so oversized tracks skip out here too.
        for (int t = 0; t < gridTracks.Count; t++)
        {
            if (!TryCellRange(mins[t], maxs[t], out Vector3Int lo, out Vector3Int hi))
                continue;

            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        int bucket = gridBuckets[CellKey(x, y, z)];
                        gridEntries[gridBucketStart[bucket] + cursor[bucket]++] = t;
                    }
        }

        UnityEngine.Debug.Log($"[NBS][Grid] {gridTracks.Count} tracks / {colliders.Count} segments indexed into {bucketCounts.Count} cells (cell size {gridCellSize:0.###}); {gridOversized.Count} oversized.");
    }

    private int CellCoord(float v) => Mathf.FloorToInt(v / gridCellSize);

    // False when the box covers more cells than MaxCellsPerTrack: the track is too spread out to bucket
    // usefully and belongs in the oversized list instead.
    private bool TryCellRange(Vector3 min, Vector3 max, out Vector3Int lo, out Vector3Int hi)
    {
        lo = new Vector3Int(CellCoord(min.x), CellCoord(min.y), CellCoord(min.z));
        hi = new Vector3Int(CellCoord(max.x), CellCoord(max.y), CellCoord(max.z));

        long cells = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
        return cells > 0 && cells <= MaxCellsPerTrack;
    }

    // Packs a cell coordinate into one long: 21 bits per axis, biased so negatives stay positive. That
    // covers +/-1,048,575 cells per axis, far beyond any detector's extent at a sane cell size.
    private static long CellKey(int x, int y, int z)
    {
        const long Bias = 1L << 20;
        const long Mask = (1L << 21) - 1;

        long lx = (x + Bias) & Mask;
        long ly = (y + Bias) & Mask;
        long lz = (z + Bias) & Mask;

        return (lx << 42) | (ly << 21) | lz;
    }

    // Collects every indexed track within `radius` of `center` (both in scale-root local space) into
    // `result`, without duplicates.
    private void GatherNearbyTracks(Vector3 center, float radius, List<int> result)
    {
        result.Clear();

        if (gridTracks.Count == 0)
            return;

        gridStampCounter++;

        int reach = Mathf.Max(1, Mathf.CeilToInt(radius / gridCellSize));
        int cx = CellCoord(center.x);
        int cy = CellCoord(center.y);
        int cz = CellCoord(center.z);

        for (int x = cx - reach; x <= cx + reach; x++)
            for (int y = cy - reach; y <= cy + reach; y++)
                for (int z = cz - reach; z <= cz + reach; z++)
                {
                    if (!gridBuckets.TryGetValue(CellKey(x, y, z), out int bucket))
                        continue;

                    for (int e = gridBucketStart[bucket]; e < gridBucketStart[bucket + 1]; e++)
                    {
                        int t = gridEntries[e];
                        if (gridStamp[t] == gridStampCounter)
                            continue;

                        gridStamp[t] = gridStampCounter;
                        result.Add(t);
                    }
                }

        // Oversized tracks are few; test each against its bounding box.
        float radiusSqr = radius * radius;
        foreach (OversizedTrack o in gridOversized)
        {
            if (gridStamp[o.track] == gridStampCounter)
                continue;

            Vector3 closest = Vector3.Min(Vector3.Max(center, o.min), o.max);
            if ((closest - center).sqrMagnitude > radiusSqr)
                continue;

            gridStamp[o.track] = gridStampCounter;
            result.Add(o.track);
        }
    }

    // Parks (or restarts) the proximity collider system.
    //
    // Track segment colliders exist only for hover/click inspection, which edep mode does not offer --
    // it inspects geometry pieces instead. Edep mode used to express that by setting `overriden`, whose
    // branch in Recompute ACTIVATES every segment GameObject in the run: millions of objects switched
    // on, each registering an XRSimpleInteractable that the XR Interaction Manager then iterates every
    // single frame. That was both the long stall on entering edep mode and the permanent lag after it.
    //
    // Suspending is nearly free: at most `maxActive` (1000) segments are ever active, so only those
    // have to be switched off.
    public void SuspendTrackColliders(bool suspend)
    {
        trackCollidersSuspended = suspend;

        if (!suspend)
        {
            timer = recomputeInterval; // recompute on the next frame rather than up to 5s later
            return;
        }

        foreach (int idx in activeSet)
        {
            if (idx >= 0 && idx < colliders.Count && colliders[idx].obj != null)
                colliders[idx].obj.SetActive(false);
        }

        activeSet.Clear();
        nextActiveSet.Clear();
    }

    // Single sweep that switches every segment collider off, for the one caller that has to turn them
    // on in bulk: the CSV example scenes' collider-overlap edep accounting. Without this, those scenes
    // would come out of LogEdep with every segment left active, which is exactly what suspending was
    // meant to avoid.
    public void ForceAllTrackCollidersOff()
    {
        for (int i = 0; i < colliders.Count; i++)
            if (colliders[i].obj != null && colliders[i].obj.activeSelf)
                colliders[i].obj.SetActive(false);

        activeSet.Clear();
        nextActiveSet.Clear();
    }

    void Recompute(bool overriden = false)
    {
        if (!overriden)
        {
            // Work in the scale root's local space: the stored seg.start/end are baked (root-local) positions,
            // so comparing against the camera transformed into that space keeps activate/releaseRadius measured
            // in the unscaled coordinate system. The effective *world* radius then scales with the user scale
            // (shrinking the search as the experiment shrinks), which is the intended behaviour.
            Vector3 camPos = scaleRoot != null ? scaleRoot.InverseTransformPoint(player.position) : player.position;

            float activateR2 = activateRadius * activateRadius;
            float releaseR2 = releaseRadius * releaseRadius;

            nextActiveSet.Clear();

            // Release pass: bounded by maxActive, so this one can stay exhaustive.
            foreach (int idx in activeSet)
            {
                var seg = colliders[idx];
                if (seg.obj == null)
                    continue;

                float d2 = SqrDistancePointToSegment(
                    camPos,
                    seg.start,
                    seg.end
                );

                if (d2 <= releaseR2)
                {
                    nextActiveSet.Add(idx);
                }
                else
                {
                    seg.obj.SetActive(false);
                }
            }

            // Acquire pass: only the segments of tracks the grid says are near the camera. This used to
            // walk all `colliders` -- millions of point-to-segment tests every tick -- which is what
            // made the whole scheme expensive and forced the small radius and low maxActive.
            GatherNearbyTracks(camPos, activateRadius, gridQueryResult);

            for (int q = 0; q < gridQueryResult.Count && nextActiveSet.Count < maxActive; q++)
            {
                TrackRange range = gridTracks[gridQueryResult[q]];
                int end = range.first + range.count;

                for (int i = range.first; i < end && nextActiveSet.Count < maxActive; i++)
                {
                    if (nextActiveSet.Contains(i))
                        continue;

                    var seg = colliders[i];
                    if (seg.obj == null)
                        continue;

                    float d2 = SqrDistancePointToSegment(
                        camPos,
                        seg.start,
                        seg.end
                    );

                    if (d2 <= activateR2)
                    {
                        seg.obj.SetActive(true);
                        nextActiveSet.Add(i);
                    }
                }
            }

            var temp = activeSet;
            activeSet = nextActiveSet;
                nextActiveSet = temp;
        }
        else
        {
            // activate all colliders
            UnityEngine.Debug.Log("[NBS][Recompute] Collider deactivation overriden");
            activeSet.Clear();
            nextActiveSet.Clear();

            for (int i = 0; i < colliders.Count; i++)
            {
                var seg = colliders[i];

                if (!seg.obj.activeSelf)
                    seg.obj.SetActive(true);

                activeSet.Add(i);
            }
        }
    }


    private void configureSettings()
    {
        GameObject cutsButton = GameObject.Find("CButton");
        GameObject movieButton = GameObject.Find("MButton");
        GameObject edepButton = GameObject.Find("EButton");

        cutsButton.GetComponent<Button>().onClick.AddListener(ShowCutsBoard);
        movieButton.GetComponent<Button>().onClick.AddListener(movie_init);

        TrackAnalyser runningInstance = GetComponent<TrackAnalyser>();

        Button edepBtn = edepButton.GetComponent<Button>();
        edepBtn.onClick.RemoveAllListeners();
        edepBtn.onClick.AddListener(() => runningInstance.ModeSwitch(edepButton));

        SetupScaleSlider();
    }

    // Clones the time slider (TSlider) into a "Scale" slider (SSlider) placed just below it, and wires it to
    // the discrete user scale. Guarded so a run switch (which re-runs configureSettings on the new clone)
    // reuses the existing slider instead of stacking duplicates.
    private void SetupScaleSlider()
    {
        GameObject ssliderGO = GameObject.Find("SSlider");
        if (ssliderGO == null)
        {
            GameObject tsliderGO = GameObject.Find("TSlider");
            if (tsliderGO == null)
            {
                UnityEngine.Debug.LogWarning("[NEW-BEHAVIOUR-SCRIPT] TSlider not found; cannot create Scale slider.");
                return;
            }

            ssliderGO = Instantiate(tsliderGO, tsliderGO.transform.parent);
            ssliderGO.name = "SSlider";

            // Place it directly below the time slider.
            RectTransform trt = tsliderGO.GetComponent<RectTransform>();
            RectTransform srt = ssliderGO.GetComponent<RectTransform>();
            if (trt != null && srt != null)
                srt.anchoredPosition = trt.anchoredPosition + new Vector2(0f, -(Mathf.Abs(trt.rect.height) + 20f));

        }

        scaleLabel = EnsureScaleLabel(ssliderGO);

        scale_controller = ssliderGO.GetComponent<Slider>();
        if (scale_controller == null)
            return;

        // Strip listeners copied from the time slider. RemoveAllListeners only clears runtime listeners, so
        // also disable any inspector-wired (persistent) ones that Instantiate copied over, otherwise moving
        // the Scale slider would also fire the time slider's callbacks.
        scale_controller.onValueChanged.RemoveAllListeners();
        for (int i = scale_controller.onValueChanged.GetPersistentEventCount() - 1; i >= 0; i--)
            scale_controller.onValueChanged.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);

        scale_controller.wholeNumbers = true;
        scale_controller.minValue = 0;
        scale_controller.maxValue = userScaleValues.Length - 1;
        scale_controller.value = userScaleIndex; // reflects the persisted choice across run switches
        scale_controller.onValueChanged.AddListener(v => SetUserScale((int)v));

        UpdateScaleLabel();
    }

    // Builds (or re-finds) the "Scale : Nx" readout sitting just above the Scale slider.
    //
    // It has to be built from scratch: TSlider is a plain UGUI slider with no TextMeshProUGUI anywhere
    // in it, so the previous attempt to relabel an existing child silently did nothing. Font settings
    // are copied from a label already on the same panel where one exists, so the readout matches the
    // surrounding controls rather than guessing a size that may be wrong on a world-space VR canvas.
    private TextMeshProUGUI EnsureScaleLabel(GameObject ssliderGO)
    {
        Transform existing = ssliderGO.transform.Find(ScaleLabelName);
        if (existing != null)
            return existing.GetComponent<TextMeshProUGUI>();

        RectTransform sliderRect = ssliderGO.GetComponent<RectTransform>();
        if (sliderRect == null)
        {
            UnityEngine.Debug.LogWarning("[NEW-BEHAVIOUR-SCRIPT] Scale slider has no RectTransform; skipping its label.");
            return null;
        }

        GameObject labelGO = new GameObject(ScaleLabelName, typeof(RectTransform));
        labelGO.transform.SetParent(ssliderGO.transform, false);

        float height = Mathf.Max(Mathf.Abs(sliderRect.rect.height), 1f);

        // Stretched across the slider's width, pivoted on its own bottom edge and anchored to the
        // slider's top, so it sits immediately above the track at any slider size.
        RectTransform rect = labelGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, height * 0.25f);

        TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = false;
        label.raycastTarget = false; // a readout only; must not steal XR ray hits from the slider

        TextMeshProUGUI template = ssliderGO.transform.parent != null
            ? ssliderGO.transform.parent.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;

        if (template != null)
        {
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.fontSize = template.fontSize + ScaleLabelFontBoost;
            label.color = template.color;
        }
        else
        {
            label.fontSize = height * 0.7f + ScaleLabelFontBoost;
            label.color = Color.white;
        }

        // The label box is sized from the slider, which can be shorter than the boosted text; let it
        // overflow upward rather than shrink or clip.
        label.overflowMode = TextOverflowModes.Overflow;

        return label;
    }

    // Refreshed from Update(), so the readout is also correct when checkedScale is decided after the
    // slider was built. Guarded on the value, so it is one float compare per frame in the steady state.
    private void UpdateScaleLabel()
    {
        if (scaleLabel == null)
            return;

        float total = TotalScale;
        if (!float.IsNaN(lastLabelledScale) && Mathf.Approximately(total, lastLabelledScale))
            return;

        lastLabelledScale = total;
        scaleLabel.text = "Scale : " + FormatScale(total) + "x";
    }

    // Plain decimals where they stay readable (10, 1, 0.01, 0.001), scientific below that -- the
    // auto-fit factor can already be 0.001, which the slider can take down to 1e-6.
    private static string FormatScale(float value)
    {
        if (value >= 1f)
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        if (value >= 0.001f)
            return value.ToString("0.#####", CultureInfo.InvariantCulture);

        return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
    }

    private float checkScaleFromPosition(float val)
    {
        if (val > 1000 && val < 10000)
        {
            return 0.01f;
        }
        else if (val > 10000)
        {
            return 0.001f;
        }
        else
            return 1;
    }
    public void ShowCutsBoard() // show this when cuts is clicked
    {
        CutsBoard.SetActive(true);
        AnalysisBoard.SetActive(false);
        EdepBoard.SetActive(false);

    }
  
    public void DrawTracks(float speed)
    {
        Dictionary<Track, double> temp = new Dictionary<Track, double>(); //useless
        foreach (var typeEntry in trackInfo) // initializing orderedtime thingie
        {
            var tracksByType = typeEntry.Value;
            foreach (var track in tracksByType)
            {
                if (track.Value.StepCount == 0)
                    continue;
                temp[track.Value] = track.Value.times[0];
            }
        }

        orderedtime = temp.OrderBy(entry => entry.Value).ToDictionary(entry => entry.Key, entry => entry.Value); //useless

     

        startTime = Time.time;
        //StartCoroutine(ExecuteTracks(orderedtime));
        // uncomment this for immediate track drawing
        foreach (var typeEntry in trackInfo) // initializing orderedtime thingie. this draws all tracks immediately.
        {
            var tracksByType = typeEntry.Value;
            foreach (var track in tracksByType)
            {
                track.Value.DrawTrack();
                if (track.Value.trackObj != null)
                    tracks.Add(track.Value.trackObj); // real references, so teardown can destroy them
            }
        }

    }

    private IEnumerator ExecuteTracks(Dictionary<Track, double> orderedTime)
    {
        foreach (var entry in orderedTime)
        {

            double targetTime = entry.Value; 
            float waitTime = (float)(targetTime - (Time.time - startTime)); 

            if (waitTime > 0)
                yield return new WaitForSeconds(waitTime);

            //Debug.Log($"Drawing {entry.Key.ID}");
            entry.Key.DrawTrack();
        }
    }

    //[DEPRECATED]
    // Plays the run back by advancing the same time index the slider drives, so the unified track mesh
    // reveals itself segment by segment.
    //
    // This used to walk time_control and SetActive the per-step collider GameObjects. Those have no
    // renderer, so it changed nothing on screen; the only visible effect was the time/status readout.
    public async void SimTracks()
    {
        TrackMeshRenderer meshRenderer = GetComponent<TrackMeshRenderer>();
        if (meshRenderer == null)
            return;

        if (playButton.name != "Play")
        {
            playButton.name = "Play";
            playbool = false;
            Debug.Log("PAUSING TRACK SIMULATION");
            return;
        }

        playbool = true;
        playButton.name = "Pause";
        Debug.Log("PROCEEDING TO TRACK SIMULATION");

        int stepCount = meshRenderer.TimeStepCount;
        TextMeshProUGUI timeLabel = time != null ? time.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI statusLabel = status != null ? status.GetComponent<TextMeshProUGUI>() : null;

        for (int i = 0; i < stepCount && playbool; i++)
        {
            meshRenderer.SetTimeIndex(i);

            if (time_controller != null)
                time_controller.SetValueWithoutNotify(i);

            if (timeLabel != null)
                timeLabel.text = meshRenderer.TimeAt(i).ToString();

            if (statusLabel != null)
            {
                bool finished = i == stepCount - 1;
                statusLabel.text = finished ? "Complete" : "Ongoing";
                statusLabel.color = finished ? Color.green : Color.red;
            }

            await PauseForTime(1);

            // The await can resume after the scene (and this component) has been torn down.
            if (this == null)
                return;
        }

        playbool = false;
        playButton.name = "Play";
    }

    public float movieDuration = 30f;  // seconds of total playback

    // How real simulation time gets compressed into the movie window.
    // Linear preserves relative speeds and is what most runs should use.
    // Log spreads a wide dynamic range (ps bursts followed by a long, sparse
    // tail) across the window instead, at the cost of screen-space speed no
    // longer being literal. Auto picks Linear unless the run's time range is
    // wide enough that linear would hide most of the action.
    public enum TimeScaleMode { Linear, Log, Auto }
    public TimeScaleMode movieTimeScaleMode = TimeScaleMode.Auto;

    // Used only when movieTimeScaleMode == Auto. If the run's (maxT - minT)
    // exceeds this many ps, the time range is considered "very large" and
    // Log is selected; otherwise Linear is used. Default is 1e4 ps (10 ns).
    public double autoLogRangeThresholdPs = 1e4;

    // Ceiling on how many particles may be alive at once. Playback cost is dominated by live
    // TrailRenderers (each one is a dynamic mesh rebuilt every frame), so this is the knob to turn if a
    // dense run still stutters. Tracks that would start while the ceiling is full are skipped.
    public int maxConcurrentMovieTracks = 1500;

    private TimeScaleMode effectiveTimeScaleMode;
    private bool movieClockRunning = false;
    private float movieClock;

    // Every eligible track, sorted by start time. Sorting is what lets playback admit tracks with one
    // advancing cursor rather than testing the whole run every frame.
    private List<Track> movieTracksCache;
    private int movieNextToStart;
    private readonly List<Track> movieLive = new List<Track>();
    private bool movieCeilingWarned;

    // Particles are pooled: a dense run spawns and retires thousands of them, and Instantiate/Destroy
    // churn on that scale is its own source of hitching.
    private readonly Stack<GameObject> moviePool = new Stack<GameObject>();

    // One material shared by every trail. Previously each spawn did `new Material(Shader.Find(...))` --
    // a string lookup across all loaded shaders plus a fresh material, per track. Trail colour comes
    // from startColor/endColor, which Sprites/Default reads as vertex colour, so sharing is free.
    private static Material movieTrailMaterial;

    private static Material MovieTrailMaterial
    {
        get
        {
            if (movieTrailMaterial == null)
                movieTrailMaterial = new Material(Shader.Find("Sprites/Default"));

            return movieTrailMaterial;
        }
    }

    // --- Initialize movie mode ---
    public void movie_init()
    {
        Debug.Log("[MOVIE] Initializing...");

        // Disable static track meshes
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
                if (track.trackObj != null)
                    track.trackObj.SetActive(false);
        }

        drawMeshes = false;
        Menus.SetActive(false);
        Controls.SetActive(false);
        var component = GetComponent<MeshRenderer>();
        component.enabled = false;

        // clean up any particles left over from a movie run already in progress
        movieClockRunning = false;
        ReleaseLiveMovieTracks();

        effectiveTimeScaleMode = ResolveTimeScaleMode();
        PrecomputeMovieTimes();

        movieTracksCache = trackInfo.Values
            .SelectMany(d => d.Values)
            .Where(t => t.positions != null && t.positions.Length >= 2 && t.movieTimes.Length == t.positions.Length)
            .ToList();

        movieTracksCache.Sort((a, b) => a.movieTimes[0].CompareTo(b.movieTimes[0]));

        foreach (var track in movieTracksCache)
        {
            track.movieCursor = 0;
            track.movieSpawned = false;
        }

        movieNextToStart = 0;
        movieCeilingWarned = false;

        Debug.Log($"[MOVIE] Start Time: {minT} ps, End Time: {maxT} ps, mode: {effectiveTimeScaleMode}");

        movieClock = 0f;
        movieClockRunning = true;
    }

    // --- Cleanup ---
    public void movie_deinit()
    {
        Debug.Log("[MOVIE] Deinitializing movie...");

        movieClockRunning = false;
        ReleaseLiveMovieTracks();

        // Reactivate original track objects
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
                if (track.trackObj != null)
                    track.trackObj.SetActive(true);
        }

        Menus.SetActive(true);
        Controls.SetActive(true);
        drawMeshes = true;
        var component = GetComponent<MeshRenderer>();
        component.enabled = true;
    }

    // When movieTimeScaleMode is Auto, decides Linear vs Log from the run's actual
    // time range instead of a fixed guess. Explicit Linear/Log selections pass through.
    private TimeScaleMode ResolveTimeScaleMode()
    {
        if (movieTimeScaleMode != TimeScaleMode.Auto)
            return movieTimeScaleMode;

        double range = maxT - minT;
        TimeScaleMode resolved = range > autoLogRangeThresholdPs ? TimeScaleMode.Log : TimeScaleMode.Linear;
        Debug.Log($"[MOVIE] Auto time-scale: range={range:E2} ps vs threshold={autoLogRangeThresholdPs:E2} ps -> {resolved}");
        return resolved;
    }

    // Maps a physical time (ps) into [0, movieDuration] once per track, up front,
    // so playback never has to re-derive the mapping (and can't drift out of sync
    // with the window it was scaled for).
    private void PrecomputeMovieTimes()
    {
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
            {
                int n = track.StepCount;
                if (track.movieTimes.Length != n)
                    track.movieTimes = new float[n];
                for (int i = 0; i < n; i++)
                    track.movieTimes[i] = (float)RemapTime(track.times[i]);
            }
        }
    }

    private double RemapTime(double t)
    {
        double range = maxT - minT;
        if (range <= 0)
            return 0;

        if (effectiveTimeScaleMode == TimeScaleMode.Log)
        {
            double epsilon = range * 1e-6;
            double logMin = Math.Log10(epsilon);
            double logMax = Math.Log10(range + epsilon);
            double logT = Math.Log10((t - minT) + epsilon);
            double frac = (logT - logMin) / (logMax - logMin);
            frac = Math.Max(0.0, Math.Min(1.0, frac));
            return movieDuration * frac;
        }
        else
        {
            double frac = (t - minT) / range;
            frac = Math.Max(0.0, Math.Min(1.0, frac));
            return movieDuration * frac;
        }
    }

    // --- Advance the single authoritative movie clock and place every particle ---
    private void UpdateMovie()
    {
        movieClock += Time.deltaTime;

        if (movieClock >= movieDuration)
        {
            movieClock = movieDuration;
            EvaluateMovieTracks();
            movie_deinit(); // guarantees playback never runs past movieDuration
            return;
        }

        EvaluateMovieTracks();
    }

    // Advances playback by touching only the particles that are actually on screen.
    //
    // This used to call EvaluateTrack for every track in the run, every frame -- hundreds of thousands
    // of iterations per frame on a dense run, almost all of them on tracks that had not started yet or
    // had long since finished. The cache is sorted by start time and the clock only moves forward, so
    // admission is a single advancing cursor and the per-frame work is proportional to what is visible.
    private void EvaluateMovieTracks()
    {
        while (movieNextToStart < movieTracksCache.Count)
        {
            Track candidate = movieTracksCache[movieNextToStart];
            if (candidate.movieTimes[0] > movieClock)
                break;

            movieNextToStart++;

            // Its whole lifetime elapsed within one frame (or during a hitch): never worth spawning.
            if (candidate.movieTimes[candidate.movieTimes.Length - 1] < movieClock)
                continue;

            if (movieLive.Count >= maxConcurrentMovieTracks)
            {
                if (!movieCeilingWarned)
                {
                    movieCeilingWarned = true;
                    UnityEngine.Debug.LogWarning($"[MOVIE] More than {maxConcurrentMovieTracks} tracks are live at once; the excess is being skipped. Raise maxConcurrentMovieTracks if the headset can take it.");
                }

                continue;
            }

            SpawnMovieTrack(candidate);
            movieLive.Add(candidate);
        }

        // Backwards, so finished tracks can be removed in place.
        for (int i = movieLive.Count - 1; i >= 0; i--)
        {
            Track track = movieLive[i];
            if (EvaluateTrack(track))
                continue;

            DespawnMovieTrack(track);
            movieLive.RemoveAt(i);
        }
    }

    // Returns false once the track has played out and should be retired.
    private bool EvaluateTrack(Track track)
    {
        float[] mt = track.movieTimes;
        int n = mt.Length;

        if (movieClock > mt[n - 1] || !track.movieSpawned)
            return false;

        // movieClock only moves forward, so the cursor only ever advances.
        while (track.movieCursor < n - 2 && mt[track.movieCursor + 1] < movieClock)
            track.movieCursor++;

        int i = track.movieCursor;
        float segStart = mt[i];
        float segEnd = mt[i + 1];
        float t = segEnd > segStart ? Mathf.Clamp01((movieClock - segStart) / (segEnd - segStart)) : 1f;

        // positions are baked (root-local); place the marker in the same space as the scaled tracks.
        Vector3 lerped = Vector3.Lerp(track.positions[i], track.positions[i + 1], t);
        if (scaleRoot != null)
            track.movieObj.transform.localPosition = lerped;
        else
            track.movieObj.transform.position = lerped;

        return true;
    }

    private void SpawnMovieTrack(Track track)
    {
        GameObject sph;
        TrailRenderer trail;

        if (moviePool.Count > 0)
        {
            sph = moviePool.Pop();
            trail = sph.GetComponent<TrailRenderer>();
            sph.SetActive(true);
        }
        else
        {
            sph = Instantiate(movie_prefab);

            trail = sph.GetComponent<TrailRenderer>();
            if (trail == null)
                trail = sph.AddComponent<TrailRenderer>();

            trail.time = 1f;
            trail.startWidth = 0.1f;
            trail.endWidth = 0f;
            trail.material = MovieTrailMaterial;
        }

        if (scaleRoot != null)
        {
            sph.transform.SetParent(scaleRoot, false);
            sph.transform.localPosition = track.positions[0];
        }
        else
        {
            sph.transform.SetParent(null, false);
            sph.transform.position = track.positions[0];
        }

        sph.transform.localScale = Vector3.one * 0.08f;

        // Reading Renderer.material instantiates a copy, but only the first time for a given pooled
        // object -- so the number of particle materials is the pool size, not the track count.
        Renderer rend = sph.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = track.color;

        trail.startColor = track.color;
        trail.endColor = new Color(1, 1, 1, 0);
        trail.emitting = true;
        trail.Clear(); // after positioning, so the trail does not streak in from the last user

        track.movieObj = sph;
        track.movieTrail = trail;
        track.movieSpawned = true;
        track.movieCursor = 0;
    }

    // Returns the particle to the pool. (The old version asked for a fade-out -- detach the trail and
    // Destroy it after trail.time -- but then destroyed the very same GameObject immediately on the
    // next line, so no fade ever happened. Retiring instantly matches what was actually on screen.)
    private void DespawnMovieTrack(Track track)
    {
        if (!track.movieSpawned)
            return;

        GameObject sph = track.movieObj;

        if (sph != null)
        {
            if (track.movieTrail != null)
            {
                track.movieTrail.emitting = false;
                track.movieTrail.Clear();
            }

            sph.SetActive(false);
            moviePool.Push(sph);
        }

        track.movieObj = null;
        track.movieTrail = null;
        track.movieSpawned = false;
    }

    private void ReleaseLiveMovieTracks()
    {
        for (int i = movieLive.Count - 1; i >= 0; i--)
            DespawnMovieTrack(movieLive[i]);

        movieLive.Clear();
    }

    // Pooled particles are parented to the shared scale root rather than to this clone, so a run switch
    // would otherwise leave them behind as orphaned inactive objects under WorldScaleRoot. They belong
    // to the component that owns the pool, so they go when it does.
    private void OnDestroy()
    {
        ReleaseLiveMovieTracks();

        while (moviePool.Count > 0)
        {
            GameObject pooled = moviePool.Pop();
            if (pooled != null)
                Destroy(pooled);
        }
    }







    // Slider-driven time scrub. Kept as the public entry point (a persistent UnityEvent in Custom.unity
    // still names it), but it now moves the unified mesh's time index rather than rebuilding a sorted
    // list of stringified times on every single slider movement.
    public void SteppedTracks(Slider slider)
    {
        TrackMeshRenderer meshRenderer = GetComponent<TrackMeshRenderer>();
        if (meshRenderer == null || slider == null)
            return;

        int index = (int)slider.value;
        meshRenderer.SetTimeIndex(index);

        if (time != null)
            time.GetComponent<TextMeshProUGUI>().text = meshRenderer.TimeAt(index).ToString();
    }

    async Task PauseForTime(int t)
    {
        await Task.Delay(t * 100);
    }

    public void SetSpeed() // deprecated
    {
        //float speed = speedsl.value;
        foreach (GameObject track in tracks)
        {
            Destroy(track.gameObject);
        }
        //DrawTracks(speed);
    }

    public void Format_Cuts()
    {
        Transform content = CutsBoard.transform.GetChild(1).GetChild(0).GetChild(0);
        foreach (Transform child in content)
            Destroy(child.gameObject);
        
        foreach (string pname in particles_in_scene)
        {
            UnityEngine.Debug.Log("[FORMAT-CUTS] Adding cut option for "+ pname);
            GameObject temp = Instantiate(toggle_prefab);
            temp.GetComponentInChildren<UnityEngine.UI.Text>().text = pname;
            temp.GetComponent<Toggle>().isOn = true;
            temp.GetComponent<Toggle>().onValueChanged.AddListener((interactor) => Manage_Cuts(pname, temp.GetComponent<Toggle>().isOn));

            temp.transform.SetParent(content, false);
            
        }
    }
    
    private void Manage_Cuts(string pname, bool active)
    {
        var mesh = GetComponent<TrackMeshRenderer>();
        mesh.ApplyCuts(pname, active);
            }

    static float SqrDistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Vector3.Dot(ab, ab);

        t = Mathf.Clamp01(t);

        Vector3 closest = a + t * ab;
        return (p - closest).sqrMagnitude;
    }

    // Per-step track data, held as exact-sized arrays rather than List<double>.
    //
    // Why: a large run is ~400k tracks / several million steps, and every per-step quantity in the
    // binary format is written as a 32-bit float (BinaryReader.ReadSingle), so storing them as double
    // doubled the footprint without adding a single bit of precision -- the widening was lossless in
    // both directions. Exact-sized arrays also avoid List<T>'s doubling growth, which wastes up to 2x.
    //
    // Process and volume names are not stored per step either: the file ships a string table and a
    // per-step ushort index into it, so we keep the index and resolve on demand (ProcessAt/VolumeAt).
    //
    // Net effect per step: ~160 B -> ~46 B.
    public class Track
    {
        private static readonly Vector3[] NoPositions = System.Array.Empty<Vector3>();
        private static readonly float[] NoFloats = System.Array.Empty<float>();
        private static readonly ushort[] NoUShorts = System.Array.Empty<ushort>();

        public int ID;
        public int eventID; // which GEANT4 event this track belongs to; only meaningful for binary/custom scenes
        public string type;
        public string particleName;
        public bool colorByRGB;
        public Color color;
        public ushort particleNameID;

        // --- per-step data; every array below has length StepCount ---
        public Vector3[] positions = NoPositions;
        public float[] times = NoFloats;
        public float[] energies = NoFloats;
        public float[] edeps = NoFloats;
        public float[] px = NoFloats;
        public float[] py = NoFloats;
        public float[] pz = NoFloats;
        public ushort[] processIDs = NoUShorts;
        public ushort[] volumeIDs = NoUShorts; // binary/custom scenes only; empty for the CSV examples

        public int StepCount => positions.Length;

        // Step i's GEANT4 process / physical-volume name, resolved against the run's string table.
        public string ProcessAt(int i) => LookupString(processIDs, i);
        public string VolumeAt(int i) => LookupString(volumeIDs, i);

        public GameObject trackObj; // parent gameobejct of each track
        public List<GameObject> segments = new List<GameObject>(); // segments of the track 

        // Where this track's entries sit in the shared `colliders` list. DrawTrack appends them in one
        // uninterrupted run, so the range is contiguous and the spatial index can name a whole track
        // with two ints instead of listing every segment separately.
        public int firstColliderIndex = -1;
        public int colliderCount;
        private LayerMask raycastLayerMask; // Set this to ensure only relevant objects are hit

        // movie mode: times[] remapped into [0, movieDuration], plus per-track playback state
        public float[] movieTimes = NoFloats;
        public int movieCursor;
        public bool movieSpawned;
        public GameObject movieObj;
        public TrailRenderer movieTrail;

        // --- step accumulation ------------------------------------------------------------------
        // Steps arrive one at a time (and, in the CSV case, interleaved between tracks), so they land
        // in growable lists first and are frozen into the arrays above by Compact(). The scratch is
        // dropped at that point, so the transient List overhead never outlives parsing.
        private sealed class Scratch
        {
            public List<Vector3> positions;
            public List<float> times, energies, edeps, px, py, pz;
            public List<ushort> processIDs, volumeIDs;
        }

        private Scratch scratch;

        // `expected` is the step count when it is known up front (the binary format stores it), so the
        // lists can be allocated exactly once with no growth reallocations at all.
        //
        // Reopening an already-compacted track seeds the scratch from its existing arrays rather than
        // starting empty. That only happens when one key is written twice -- the v1 binary fallback
        // separates events by a trackID==1 heuristic, which can collide -- and without the seeding the
        // second batch of steps would silently replace the first instead of extending it.
        public void BeginSteps(int expected)
        {
            if (scratch != null)
                return;

            int existing = positions.Length;
            int c = Mathf.Max(expected, 0) + existing;

            scratch = new Scratch
            {
                positions = new List<Vector3>(c),
                times = new List<float>(c),
                energies = new List<float>(c),
                edeps = new List<float>(c),
                px = new List<float>(c),
                py = new List<float>(c),
                pz = new List<float>(c),
                processIDs = new List<ushort>(c),
                volumeIDs = new List<ushort>(c),
            };

            if (existing == 0)
                return;

            scratch.positions.AddRange(positions);
            scratch.times.AddRange(times);
            scratch.energies.AddRange(energies);
            scratch.edeps.AddRange(edeps);
            scratch.px.AddRange(px);
            scratch.py.AddRange(py);
            scratch.pz.AddRange(pz);
            scratch.processIDs.AddRange(processIDs);
            scratch.volumeIDs.AddRange(volumeIDs);
        }

        public void AppendStep(Vector3 position, float time, float energy, float edep,
                               float momentumX, float momentumY, float momentumZ,
                               ushort processID, ushort volumeID)
        {
            if (scratch == null)
                BeginSteps(0);

            scratch.positions.Add(position);
            scratch.times.Add(time);
            scratch.energies.Add(energy);
            scratch.edeps.Add(edep);
            scratch.px.Add(momentumX);
            scratch.py.Add(momentumY);
            scratch.pz.Add(momentumZ);
            scratch.processIDs.Add(processID);
            scratch.volumeIDs.Add(volumeID);
        }

        // Freezes the accumulated steps into exact-sized arrays and releases the scratch lists.
        // Idempotent: calling it on an already-compacted track does nothing.
        public void Compact()
        {
            if (scratch == null)
                return;

            positions = scratch.positions.ToArray();
            times = scratch.times.ToArray();
            energies = scratch.energies.ToArray();
            edeps = scratch.edeps.ToArray();
            px = scratch.px.ToArray();
            py = scratch.py.ToArray();
            pz = scratch.pz.ToArray();
            processIDs = scratch.processIDs.ToArray();
            volumeIDs = scratch.volumeIDs.ToArray();

            scratch = null;
        }

        // Drops every per-step array so a finished run stops holding hundreds of MB once its scene is
        // torn down. The track's identity fields are left intact -- they are a few bytes each.
        public void ReleaseStepData()
        {
            scratch = null;
            firstColliderIndex = -1;
            colliderCount = 0;
            positions = NoPositions;
            times = NoFloats;
            energies = NoFloats;
            edeps = NoFloats;
            px = NoFloats;
            py = NoFloats;
            pz = NoFloats;
            processIDs = NoUShorts;
            volumeIDs = NoUShorts;
            movieTimes = NoFloats;
            segments.Clear();
            trackObj = null;
            movieObj = null;
            movieTrail = null;
        }

        public void DrawTrack()
        {
            if (positions == null || positions.Length < 2)
                return;

            if (times == null || times.Length == 0)
                return;

            trackObj = new GameObject($"Track_{ID}");
            firstColliderIndex = colliders.Count;

            for (int i = 0; i < positions.Length - 1; i++)
            {
                GameObject trackSegment = new GameObject($"{ID}_TrackSegment_{i + 1}");
                trackSegment.transform.SetParent(trackObj.transform, false);

                Vector3 start = positions[i];
                Vector3 end = positions[i + 1];
                Vector3 mid = (start + end) * 0.5f;

                trackSegment.transform.position = mid;
                trackSegment.transform.rotation = Quaternion.LookRotation(end - start);

                float length = Vector3.Distance(start, end);

                Rigidbody rb = trackSegment.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                CapsuleCollider capsule = trackSegment.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                capsule.height = length + 0.05f;
                capsule.radius = 0.025f;

                XRSimpleInteractable interactable = trackSegment.AddComponent<XRSimpleInteractable>();
                int temp = i;
                interactable.selectEntered.AddListener(_ => onHit(temp));
                interactable.hoverEntered.AddListener(_ => OnHoverEntered(temp));
                interactable.hoverExited.AddListener(_ => OnHoverExited());

                // fill in collider struct
                ColliderEntry entry = new ColliderEntry();
                entry.id = colliders.Count;
                entry.start = positions[i];
                entry.end = positions[i + 1];
                entry.obj = trackSegment;
                colliders.Add(entry);

                trackSegment.SetActive(false);

                segments.Add(trackSegment);
            }

            colliderCount = colliders.Count - firstColliderIndex;
        }


        public void OnHoverExited()
        {
            GameObject gobj = GameObject.Find("XR Origin (XR Rig)");
            GameObject panel = gobj.transform.GetChild(0).GetChild(0).GetChild(0).gameObject;
            panel.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = null;
            panel.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = null; // energies
        }

        public void OnHoverEntered(int i)
        {
            GameObject gobj = GameObject.Find("XR Origin (XR Rig)");
            GameObject panel = gobj.transform.GetChild(0).GetChild(0).GetChild(0).gameObject;
            panel.SetActive(true);
            panel.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = particleName; // particle type
            panel.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = string.Format("{0:N4}", energies[i]) + " MeV"; // energies
        }

        public void onHit(int i)
        {
            //Debug.Log(i);
            GameObject board = GameObject.Find("Analysis Board");
            GameObject analysis = board.transform.GetChild(0).gameObject;
            analysis.SetActive(true);
            GameObject.Find("Cuts Board").transform.GetChild(0).gameObject.SetActive(false); // set cuts board to be inactive
            analysis.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + particleName //particle
                : "Colon not found";
            analysis.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + $"{ID}"//id
                : "Colon not found";
            analysis.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + $"{i + 1}"//step
                : "Colon not found";
            analysis.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + string.Format("{0:N4}", energies[i]) + " MeV"//energy
                : "Colon not found";
            analysis.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + string.Format("{0:N4}", px[i]) + " MeV/c" // px
                : "Colon not found";
            analysis.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + string.Format("{0:N4}", py[i]) + " MeV/c" // py
                : "Colon not found";
            analysis.transform.GetChild(6).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(6).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(6).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(6).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + string.Format("{0:N4}", pz[i]) + " MeV/c" // pz
                : "Colon not found";
            analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + ProcessAt(i) // process
                : "Colon not found";
            analysis.transform.GetChild(8).GetComponent<TextMeshProUGUI>().text = analysis.transform.GetChild(8).GetComponent<TextMeshProUGUI>().text.IndexOf(':') != -1
                ? analysis.transform.GetChild(8).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(8).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + string.Format("{0:N4}", edeps[i]) + " MeV"// edep
                : "Colon not found";
        }

        

    }

    public static Color GetColor(string type)
        {
            if (float.Parse(type, CultureInfo.InvariantCulture) > 0)
                return Color.blue;
            else if (float.Parse(type, CultureInfo.InvariantCulture) < 0)
                return Color.red;
            return Color.green;
        }

    public static void Close(GameObject obj) { obj.SetActive(false); }

        
}


public class ParseHelper : MonoBehaviour
{
    private static Dictionary<string, float> TUnits = new Dictionary<string, float>()
    {
        ["fs"] = 1e-3f,
        ["ps"] = 1f,
        ["ns"] = 1e3f,
        ["us"] = 1e6f,
        ["ms"] = 1e9f,
        ["s"] = 1e12f,
        ["min"] = 60f * 1e12f,   
        ["h"] = 3600f * 1e12f  
    };

    private static Dictionary<string, float> EUnits = new Dictionary<string, float>() // express all energy values in MeV
    {
        ["meV"] = 1e-9f,
        ["eV"] = 1e-6f,
        ["keV"] = 1e-3f,
        ["MeV"] = 1f,
        ["GeV"] = 1e3f,
        ["TeV"] = 1e6f,
    };

    public static double ParseTime(string fullstr)
    {
        fullstr = fullstr.Trim();
        int spaceIndex = fullstr.IndexOf(' ');
        if (spaceIndex < 0)
            throw new FormatException($"Invalid time string: {fullstr}");

        string valueStr = fullstr.Substring(0, spaceIndex);
        string unit = fullstr.Substring(spaceIndex + 1);

        if (!TUnits.ContainsKey(unit))
            throw new KeyNotFoundException($"Unknown time unit: {unit}");

        return double.Parse(valueStr, CultureInfo.InvariantCulture) * TUnits[unit];
    }

    public static double ParseEnergy(string fullstr)
    {
        fullstr = fullstr.Trim();
        int spaceIndex = fullstr.IndexOf(' ');
        if (spaceIndex < 0)
            throw new FormatException($"Invalid energy string: {fullstr}");

        string valueStr = fullstr.Substring(0, spaceIndex);
        string unit = fullstr.Substring(spaceIndex + 1);

        if (!EUnits.ContainsKey(unit))
            throw new KeyNotFoundException($"Unknown energy unit: {unit}");

        return double.Parse(valueStr, CultureInfo.InvariantCulture) * EUnits[unit];
    }
}

// One drawable segment of the unified track mesh, kept sorted by time.
//
// Deliberately kept to 12 bytes: on a multi-million-step run this array is one of the largest single
// allocations in the scene. It therefore stores a particle *id* rather than a Track reference, and
// only the first vertex index -- the second is always indexA + 1, because a track's vertices are
// written consecutively by BuildMesh.
struct TimedSegment
{
    public float time;
    public int indexA;
    public ushort particleId;
}


public class TrackMeshRenderer : MonoBehaviour
{
    public Material trackMaterial;
    public List<Track> trackInstances;
    public Slider time_slider;

    Mesh mesh;
    Material ownedMaterial; // instantiated here, so this component is responsible for destroying it

    readonly List<Vector3> vertices = new();
    readonly List<Color32> colors = new();
    readonly List<int> indices = new();
    readonly List<TimedSegment> allSegments = new();

    // Time of each currently-visible segment, in the same order as `indices`. Replaces the old
    // timeToIndexCount list, whose entries were always just (i + 1) * 2 and so carried no information.
    readonly List<float> visibleTimes = new();

    // Visibility per particle, keyed by the string table id rather than by name, so TimedSegment can
    // hold a ushort instead of an object reference.
    readonly Dictionary<ushort, bool> cutStates = new Dictionary<ushort, bool>();
    readonly Dictionary<string, ushort> idByParticle = new Dictionary<string, ushort>();

    // Number of time steps the slider can scrub through.
    public int TimeStepCount => visibleTimes.Count;

    // Time (in the run's own units) at a given slider index.
    public float TimeAt(int timeIndex)
    {
        if (visibleTimes.Count == 0)
            return 0f;
        return visibleTimes[Mathf.Clamp(timeIndex, 0, visibleTimes.Count - 1)];
    }

    void Awake()
    {
        mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        var mf = gameObject.AddComponent<MeshFilter>();
        var mr = gameObject.AddComponent<MeshRenderer>();

        mf.sharedMesh = mesh;
        ownedMaterial = new Material(Shader.Find("Sprites/Default"));
        mr.sharedMaterial = ownedMaterial;
    }

    // The mesh and material are created with `new`, so Unity will never collect them on its own --
    // without this, every run switch (which builds a fresh track_renderer clone) stranded a whole
    // run's worth of vertex and index buffers plus a material.
    void OnDestroy()
    {
        if (mesh != null)
        {
            mesh.Clear();
            Destroy(mesh);
            mesh = null;
        }

        if (ownedMaterial != null)
        {
            Destroy(ownedMaterial);
            ownedMaterial = null;
        }

        vertices.Clear();
        vertices.TrimExcess();
        colors.Clear();
        colors.TrimExcess();
        indices.Clear();
        indices.TrimExcess();
        allSegments.Clear();
        allSegments.TrimExcess();
        visibleTimes.Clear();
        visibleTimes.TrimExcess();
        cutStates.Clear();
        idByParticle.Clear();
    }

    public void BuildMesh()
    {
        vertices.Clear();
        colors.Clear();
        indices.Clear();
        visibleTimes.Clear();
        allSegments.Clear();
        cutStates.Clear();
        idByParticle.Clear();

        // Size the buffers once up front; growing them by doubling would transiently hold two copies of
        // a multi-million-element array.
        int totalSteps = 0;
        foreach (var track in trackInstances)
            totalSteps += track.StepCount;

        if (vertices.Capacity < totalSteps) vertices.Capacity = totalSteps;
        if (colors.Capacity < totalSteps) colors.Capacity = totalSteps;
        if (allSegments.Capacity < totalSteps) allSegments.Capacity = totalSteps;

        foreach (var track in trackInstances)
        {
            if (track.positions == null || track.positions.Length < 2)
                continue;

            int baseVertex = vertices.Count;

            // Color32 rather than Color: 4 bytes per vertex instead of 16, for the same 8-bit-per-channel
            // result the display can actually show.
            Color32 trackColor = track.color;
            ushort particleId = track.particleNameID;

            if (!string.IsNullOrEmpty(track.particleName))
                idByParticle[track.particleName] = particleId;

            for (int i = 0; i < track.positions.Length; i++)
            {
                vertices.Add(track.positions[i]);
                colors.Add(trackColor);
            }

            for (int i = 0; i < track.positions.Length - 1; i++)
            {
                allSegments.Add(new TimedSegment
                {
                    time = track.times[i + 1],
                    indexA = baseVertex + i,
                    particleId = particleId
                });
            }

            if (!cutStates.ContainsKey(particleId))
                cutStates[particleId] = true;
        }

        allSegments.Sort((a, b) => a.time.CompareTo(b.time));

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();

        // The mesh owns its own copy of the vertex data now, so release ours instead of holding a
        // second multi-million-element copy for the lifetime of the scene.
        vertices.Clear();
        vertices.TrimExcess();
        colors.Clear();
        colors.TrimExcess();

        ApplyCuts();
    }

    public void SliderSetup()
    {
        time_slider.maxValue = Mathf.Max(0, visibleTimes.Count - 1);
        time_slider.minValue = 0;
        time_slider.wholeNumbers = true;
        time_slider.onValueChanged.AddListener((interactor) => SetTimeIndex((int) time_slider.value));
    }

    public void SetTimeIndex(int timeIndex)
    {
        if (visibleTimes.Count == 0)
            return;

        // Each visible segment contributes exactly two indices, so the prefix count is positional.
        int indexCount = (Mathf.Clamp(timeIndex, 0, visibleTimes.Count - 1) + 1) * 2;

        mesh.SetIndices(
            indices,
            0,
            indexCount,
            MeshTopology.Lines,
            0
        );
    }

    public void ApplyCuts(string pname=null, bool active=true)
    {
        if (pname != null && idByParticle.TryGetValue(pname, out ushort id))
            cutStates[id] = active;

        RebuildIndices();
    }

    void RebuildIndices()
    {
        indices.Clear();
        visibleTimes.Clear();

        foreach (var seg in allSegments)
        {
            if (cutStates.TryGetValue(seg.particleId, out bool visible) && !visible)
                continue;

            indices.Add(seg.indexA);
            indices.Add(seg.indexA + 1); // a track's vertices are consecutive; see TimedSegment
            visibleTimes.Add(seg.time);
        }

        mesh.SetIndices(indices, MeshTopology.Lines, 0);

        if (time_slider != null)
            time_slider.maxValue = Mathf.Max(0, visibleTimes.Count - 1);
    }
}

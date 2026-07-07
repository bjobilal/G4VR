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

    List<string> stringTable = new List<string>(); // LUT for binary

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
    public Dictionary<string, List<GameObject>> time_control = new Dictionary<string, List<GameObject>>(); // string is time and list<gameobject> is all the tracks which appear at that time.
    //private static Dictionary<int, List<Vector3>> trackPoints = new Dictionary<int, List<Vector3>>();

    public static List<ColliderEntry> colliders = new List<ColliderEntry>();

    // SETTINGS AND STATES TO MANAGE COLLIDERS

    public int maxActive = 1000;
    public float recomputeInterval = 5f;
    public float activateRadius = 2.0f;
    public float releaseRadius = 2.5f;
    public bool overriden = false;


    private HashSet<int> activeSet = new HashSet<int>();
    private HashSet<int> nextActiveSet = new HashSet<int>();
    private float timer;

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

    List<string> sortedKeys; // of time_control
    
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

    // warning in case CSV could not be fully read or understood:
    bool csvWarning = false;

    string GetString(ushort id)
    {
        return (id < stringTable.Count) ? stringTable[id] : "";
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

        trackInfo.Clear();
        tracks.Clear();
        markerMatrices.Clear();
        colliders.Clear();
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
        lineMaterial = GameObject.Find("line_mat").GetComponent<Renderer>().material;
        lineMaterial.shader = shader;
        lineMaterial.EnableKeyword("_EMISSION");

        // read csv and draw tracks
        if (SceneManager.GetActiveScene().name == "Custom" || file == null)
        { 
            ReadBIN(customFile, true);
            if (File.Exists(customFile))
                File.Delete(customFile);

        }
        else
            ReadCSV(file, true);

        DrawTracks(1f);


        // additional functions (if any)

        Format_Cuts(); // THIS HAS BEEN COMMENTED OUT JUST TO TEST THE G4VR VIS STUFF
                       //TrackAnalyser.Starter();
                       //SimTracks();
       
        // only for custom scene since settings are not initialized in the inspector
        if (SceneManager.GetActiveScene().name=="Custom")
        {
            UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Configuring settings for Custom scene");
            configureSettings();
        }
        
    }

    private void Update()
    {
        GameObject Geometry = GameObject.Find("Scene");
        if (Geometry != null)
        {
            Geometry.transform.localScale = new Vector3(checkedScale, checkedScale, checkedScale);
            //appliedScaleToGeometry = true;
            //UnityEngine.Debug.Log("[NewBehaviourScript] set scene scale to "+checkedScale);
        }
        //else 
        //UnityEngine.Debug.Log("[NewBehaviourScript] Could not find Scene");

        timer += Time.deltaTime;
        if (timer >= recomputeInterval)
        {
            timer = 0f;
            Recompute(overriden);
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

                float[] x = new float[n];
                float[] y = new float[n];
                float[] z = new float[n];
                float[] time = new float[n];
                float[] edep = new float[n];
                float[] px = new float[n];
                float[] py = new float[n];
                float[] pz = new float[n];
                float[] energy = new float[n];
                ushort[] processID = new ushort[n];
                ushort[] volumeID = new ushort[n];

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

                Color trackColor;
                if (colorByRGB)
                {
                    UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] (Binary) Setting track color by RGB");
                    trackColor = new Color(r * 255f, g * 255f, b * 255f);
                }
                else
                {
                    UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] (Binary) Setting track color by type");
                    trackColor = GetColor(type);
                }

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

                    double tracktime = time[i];
                    double e = energy[i];
                    double d = edep[i];
                    //UnityEngine.Debug.Log("EDEP value:"+ d);

                    if (tracktime < minT) minT = tracktime;
                    if (tracktime > maxT) maxT = tracktime;

                    tr.positions.Add(position);
                    tr.energies.Add(e);
                    tr.times.Add(tracktime);
                    tr.processIDs.Add(processID[i]);
                    tr.volumeIDs.Add(volumeID[i]);
                    tr.px.Add(px[i]);
                    tr.py.Add(py[i]);
                    tr.pz.Add(pz[i]);
                    tr.edeps.Add(d);
                }
            }

            List<string> stringTable = new List<string>();

            for (int s = 0; s < stringCount; s++)
            {
                ushort len = reader.ReadUInt16();
                byte[] bytes = reader.ReadBytes(len);
                stringTable.Add(System.Text.Encoding.UTF8.GetString(bytes));
            }

            foreach (var typeDict in trackInfo.Values)
            {
                foreach (Track tr in typeDict.Values)
                {
                    tr.particleName = stringTable[tr.particleNameID];
                    particles_in_scene.Add(tr.particleName);
                    foreach (ushort id in tr.processIDs)
                        tr.processes.Add(stringTable[id]);
                    foreach (ushort id in tr.volumeIDs)
                        tr.volumeNames.Add(stringTable[id]);
                }
            }

            hasVolumeInfo = true; // binary tracks always carry a per-step volumeID; CSV example scenes do not
            Debug.Log("Finished reading BIN file.");
        }

        foreach (var typeEntry in trackInfo)
        {
            foreach (var kv in typeEntry.Value)
            {
                var tval = kv.Value;

                foreach (var ti in tval.times)
                {
                    if (!time_control.ContainsKey(ti.ToString()))
                        time_control[ti.ToString()] = new List<GameObject>();
                }

                tracks.Add(tval.trackObj); // keep list of all track GameObjects
            }
        }

        sortedKeys = time_control.Keys.OrderBy(key => key).ToList();

        Debug.Log($"Sorted Keys length: {sortedKeys.Count}");

        // time slider details
        stop_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Max().ToString();
        start_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Min().ToString();
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
        maxT = 0.0;
        minT = 99999999999999999999999999999.0;

        float T_range = 1f;

        double logMinT = Math.Log10(maxT - minT + 1);

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

                    double px = float.Parse(values[11], CultureInfo.InvariantCulture);

                    double py = float.Parse(values[12], CultureInfo.InvariantCulture);
                    double pz = float.Parse(values[13], CultureInfo.InvariantCulture);

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
                    trackInfo[type][trackID].positions.Add(position);
                    trackInfo[type][trackID].energies.Add(energy);
                    trackInfo[type][trackID].times.Add(time);
                    trackInfo[type][trackID].type = type;
                    trackInfo[type][trackID].particleName = pname;
                    trackInfo[type][trackID].processes.Add(process);
                    trackInfo[type][trackID].px.Add(px);
                    trackInfo[type][trackID].py.Add(py);
                    trackInfo[type][trackID].pz.Add(pz);
                    trackInfo[type][trackID].edeps.Add(edep);
                    trackInfo[type][trackID].colorByRGB = colorByRGB;
                    trackInfo[type][trackID].color = trackColor;

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

        foreach (var typeEntry in trackInfo)
        {
            foreach (var kv in typeEntry.Value)
            {
                var tval = kv.Value;

                foreach (var ti in tval.times)
                {
                    if (!time_control.ContainsKey(ti.ToString()))
                        time_control[ti.ToString()] = new List<GameObject>();
                }

                tracks.Add(tval.trackObj); // keep list of all track GameObjects
            }
        }

        sortedKeys = time_control.Keys.OrderBy(key => key).ToList();

        Debug.Log($"Sorted Keys length: {sortedKeys.Count}");

        // time slider details
        stop_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Max().ToString();
        start_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Min().ToString();
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

    void Recompute(bool overriden = false)
    {
        if (!overriden)
        {
            Vector3 camPos = player.position;

            float activateR2 = activateRadius * activateRadius;
            float releaseR2 = releaseRadius * releaseRadius;

            nextActiveSet.Clear();

            foreach (int idx in activeSet)
            {
                var seg = colliders[idx];

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

            for (int i = 0; i < colliders.Count && nextActiveSet.Count < maxActive; i++)
            {
                if (nextActiveSet.Contains(i))
                    continue;

                var seg = colliders[i];

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
        edepBtn.onClick.RemoveAllListeners(); // 🔹 clear existing listeners
        edepBtn.onClick.AddListener(() => runningInstance.ModeSwitch(edepButton));
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
                track.Value.DrawTrack(time_control);
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
            entry.Key.DrawTrack(time_control); 
        }
    }

    //[DEPRECATED]
    public async void SimTracks()
    {

        if (playButton.name == "Play")
        {
            playbool = true;
            playButton.name = "Pause";
            foreach (string key in time_control.Keys)
            {
                var tracks = time_control[key];
                foreach (var track in tracks)
                    track.SetActive(false);
            }
            sortedKeys = time_control.Keys
                              .Select(key => new { Key = key, Value = double.Parse(key) })
                              .OrderBy(item => item.Value)
                              .Select(item => item.Key)
                              .ToList();
            Debug.Log("PROCEEDING TO TRACK SIMULATION");
            foreach (string key in sortedKeys)
            {
                if (playbool)
                {
                    var tracks = time_control[key];
                    //Debug.Log($"Current Time: {key}");
                    foreach (var track in tracks)
                    {
                        track.SetActive(true);
                        Debug.Log($"Current Time: {key} || {track.name} activated");
                    }

                    time.transform.GetComponent<TextMeshProUGUI>().text = key;
                    // SIM STATUS CONTROL
                    if ((time.transform.GetComponent<TextMeshProUGUI>().text == stop_time.transform.GetComponent<TextMeshProUGUI>().text))
                    {
                        status.transform.GetComponent<TextMeshProUGUI>().text = "Complete";
                        status.transform.GetComponent<TextMeshProUGUI>().color = Color.green;
                    }
                    else {
                        status.transform.GetComponent<TextMeshProUGUI>().text = "Ongoing";
                        status.transform.GetComponent<TextMeshProUGUI>().color = Color.red;
                    }
                        await PauseForTime(1);
                }
            }
        }
        else
        {
            playButton.name = "Play";
            playbool = false;
            Debug.Log("PAUSING TRACK SIMULATION");

        }



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

    private TimeScaleMode effectiveTimeScaleMode;
    private bool movieClockRunning = false;
    private float movieClock;
    private List<Track> movieTracksCache;

    // --- Initialize movie mode ---
    public void movie_init()
    {
        Debug.Log("[MOVIE] Initializing...");

        // Disable static track meshes
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
                track.trackObj.SetActive(false);
        }

        drawMeshes = false;
        Menus.SetActive(false);
        Controls.SetActive(false);
        var component = GetComponent<MeshRenderer>();
        component.enabled = false;

        // clean up any particles left over from a movie run already in progress
        movieClockRunning = false;
        if (movieTracksCache != null)
        {
            foreach (var track in movieTracksCache)
                DespawnMovieTrack(track);
        }

        effectiveTimeScaleMode = ResolveTimeScaleMode();
        PrecomputeMovieTimes();

        movieTracksCache = trackInfo.Values
            .SelectMany(d => d.Values)
            .Where(t => t.positions != null && t.positions.Count >= 2 && t.movieTimes.Count == t.positions.Count)
            .ToList();

        foreach (var track in movieTracksCache)
            track.movieCursor = 0;

        Debug.Log($"[MOVIE] Start Time: {minT} ps, End Time: {maxT} ps, mode: {effectiveTimeScaleMode}");

        movieClock = 0f;
        movieClockRunning = true;
    }

    // --- Cleanup ---
    public void movie_deinit()
    {
        Debug.Log("[MOVIE] Deinitializing movie...");

        movieClockRunning = false;

        if (movieTracksCache != null)
        {
            foreach (var track in movieTracksCache)
                DespawnMovieTrack(track);
        }

        // Reactivate original track objects
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
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
                track.movieTimes.Clear();
                foreach (double t in track.times)
                    track.movieTimes.Add(RemapTime(t));
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

    private void EvaluateMovieTracks()
    {
        foreach (var track in movieTracksCache)
            EvaluateTrack(track);
    }

    private void EvaluateTrack(Track track)
    {
        List<double> mt = track.movieTimes;
        int n = mt.Count;

        if (movieClock < mt[0] || movieClock > mt[n - 1])
        {
            if (track.movieSpawned)
                DespawnMovieTrack(track);
            return;
        }

        if (!track.movieSpawned)
            SpawnMovieTrack(track);

        // movieClock only moves forward, so the cursor only ever advances.
        while (track.movieCursor < n - 2 && mt[track.movieCursor + 1] < movieClock)
            track.movieCursor++;

        int i = track.movieCursor;
        double segStart = mt[i];
        double segEnd = mt[i + 1];
        float t = segEnd > segStart ? Mathf.Clamp01((float)((movieClock - segStart) / (segEnd - segStart))) : 1f;

        track.movieObj.transform.position = Vector3.Lerp(track.positions[i], track.positions[i + 1], t);
    }

    private void SpawnMovieTrack(Track track)
    {
        GameObject sph = Instantiate(movie_prefab, track.positions[0], Quaternion.identity);
        sph.GetComponent<Renderer>().material.color = track.color;
        sph.transform.localScale = Vector3.one * 0.08f;

        TrailRenderer trail = sph.AddComponent<TrailRenderer>();
        trail.time = 1f;
        trail.startWidth = 0.1f;
        trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = track.color;
        trail.endColor = new Color(1, 1, 1, 0);
        trail.Clear();

        track.movieObj = sph;
        track.movieTrail = trail;
        track.movieSpawned = true;
        track.movieCursor = 0;
    }

    private void DespawnMovieTrack(Track track)
    {
        if (!track.movieSpawned)
            return;

        if (track.movieTrail != null)
        {
            track.movieTrail.transform.SetParent(null, true);
            track.movieTrail.emitting = false;
            Destroy(track.movieTrail.gameObject, track.movieTrail.time);
        }

        if (track.movieObj != null)
            Destroy(track.movieObj);

        track.movieObj = null;
        track.movieTrail = null;
        track.movieSpawned = false;
    }







    public void SteppedTracks(Slider slider) 
    {
        foreach (string key in time_control.Keys)
        {
            var tracks = time_control[key];
            foreach (var track in tracks)
                track.SetActive(false);
        }
        sortedKeys = time_control.Keys
                          .Select(key => new { Key = key, Value = double.Parse(key) })
                          .OrderBy(item => item.Value)
                          .Select(item => item.Key)
                          .ToList();
        int t_slider = (int) slider.value;
        string max = sortedKeys[t_slider];
        time.transform.GetComponent<TextMeshProUGUI>().text = max;
        foreach (string key in sortedKeys)
        {
            if (sortedKeys.IndexOf(key) <= t_slider)
            {
                var tracks = time_control[key];
                foreach (var track in tracks)
                    track.SetActive(true);
            }
            else break;
        }
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

    public class Track
    {
        public int ID;
        public int eventID; // which GEANT4 event this track belongs to; only meaningful for binary/custom scenes
        public List<Vector3> positions = new List<Vector3>();
        public List<double> times = new List<double>();
        public List<double> energies = new List<double>();
        public string type;
        public string particleName;
        public bool colorByRGB;
        public Color color;

        public List<string> processes = new List<string>();
        public List<double> edeps = new List<double>();
        public List<double> px = new List<double>();
        public List<double> py = new List<double>();
        public List<double> pz = new List<double>();


        public GameObject trackObj; // parent gameobejct of each track
        public List<GameObject> segments = new List<GameObject>(); // segments of the track 
        private LayerMask raycastLayerMask; // Set this to ensure only relevant objects are hit

        // only for binary/custom scene.

        public List<ushort> processIDs = new List<ushort>();
        public List<ushort> volumeIDs = new List<ushort>();
        public List<string> volumeNames = new List<string>(); // resolved from volumeIDs; empty for CSV example scenes
        public ushort particleNameID;

        // movie mode: times[] remapped into [0, movieDuration], plus per-track playback state
        public List<double> movieTimes = new List<double>();
        public int movieCursor;
        public bool movieSpawned;
        public GameObject movieObj;
        public TrailRenderer movieTrail;

        public void DrawTrack(Dictionary<string, List<GameObject>> list)
        {
            if (positions == null || positions.Count < 2)
                return;

            if (times == null || times.Count == 0)
                return;

            trackObj = new GameObject($"Track_{ID}");

            for (int i = 0; i < positions.Count - 1; i++)
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
                list[Convert.ToString(times[i])].Add(trackSegment);
            }
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
                ? analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text.Substring(0, analysis.transform.GetChild(7).GetComponent<TextMeshProUGUI>().text.IndexOf(':')+1) + processes[i] // process
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

struct TimedSegment
{
    public double time;
    public int indexA;
    public int indexB;
    public Track track; 
}



public class TrackMeshRenderer : MonoBehaviour
{
    public Material trackMaterial;
    public List<Track> trackInstances;
    public Slider time_slider;

    Mesh mesh;

    readonly List<Vector3> vertices = new();
    readonly List<Color> colors = new();
    readonly List<int> indices = new();
    readonly List<TimedSegment> allSegments = new();
    readonly List<int> timeToIndexCount = new();

    readonly Dictionary<string, bool> cutStates = new Dictionary<string, bool>(); // tracks the visibility of given string names


    void Awake()
    {
        mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        var mf = gameObject.AddComponent<MeshFilter>();
        var mr = gameObject.AddComponent<MeshRenderer>();

        mf.sharedMesh = mesh;
        mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    public void BuildMesh()
    {
        vertices.Clear();
        colors.Clear();
        indices.Clear();
        timeToIndexCount.Clear();
        allSegments.Clear();

        foreach (var track in trackInstances)
        {
            if (track.positions == null || track.positions.Count < 2)
                continue;

            int baseVertex = vertices.Count;
            Color trackColor = track.color;

            for (int i = 0; i < track.positions.Count; i++)
            {
                vertices.Add(track.positions[i]);
                colors.Add(trackColor);
            }

            for (int i = 0; i < track.positions.Count - 1; i++)
            {
                allSegments.Add(new TimedSegment
                {
                    time = track.times[i + 1],
                    indexA = baseVertex + i,
                    indexB = baseVertex + i + 1,
                    track = track
                });
            }
        }

        allSegments.Sort((a, b) => a.time.CompareTo(b.time));

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();

        foreach (var track in trackInstances)
        {
            if (!cutStates.ContainsKey(track.particleName))
                cutStates[track.particleName] = true;
        }

        ApplyCuts();
    }



    public void SliderSetup()
    {
        time_slider.maxValue = timeToIndexCount.Count - 1;
        time_slider.minValue = 0;
        time_slider.wholeNumbers = true;
        time_slider.onValueChanged.AddListener((interactor) => SetTimeIndex((int) time_slider.value));
    }

    public void SetTimeIndex(int timeIndex)
    {
        if (timeToIndexCount.Count == 0)
            return;

        int indexCount = timeToIndexCount[
            Mathf.Clamp(timeIndex, 0, timeToIndexCount.Count - 1)
        ];

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
        if (pname != null)
            cutStates[pname] = active;

        RebuildIndices();
    }

    void RebuildIndices()
    {
        indices.Clear();
        timeToIndexCount.Clear();

        foreach (var seg in allSegments)
        {
            if (cutStates.TryGetValue(seg.track.particleName, out bool visible)
                && !visible)
            {
                continue;
            }

            indices.Add(seg.indexA);
            indices.Add(seg.indexB);
            timeToIndexCount.Add(indices.Count);
        }

        mesh.SetIndices(indices, MeshTopology.Lines, 0);
    }


}
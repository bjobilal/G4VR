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
using JetBrains.Annotations;
using XCharts.Runtime;
using UnityEngine.SceneManagement;
using static NewBehaviourScript;



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
    
    public static List<Matrix4x4> markerMatrices = new List<Matrix4x4>();
    bool drawMeshes = true;

    public static List<GameObject> tracks = new List<GameObject>();
    public static Dictionary<string, Dictionary<int, Track>> trackInfo = new Dictionary<string, Dictionary<int, Track>>(); // the int in the dictionary is the track id
    public double time_scale;
    private Dictionary<Track, double> orderedtime = new Dictionary<Track, double>();
    public Dictionary<string, List<GameObject>> time_control = new Dictionary<string, List<GameObject>>(); // string is time and list<gameobject> is all the tracks which appear at that time.
    //private static Dictionary<int, List<Vector3>> trackPoints = new Dictionary<int, List<Vector3>>();

    //public Dictionary<string, List<Track>> trackOriginTimes = new Dictionary<string, List<Track>>(); // stores information about the tracks that appear at the string time
    SortedDictionary<double, List<Track>> trackOriginTimes;

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

    public bool checkScale = false;
    public float checkedScale = 1f;
    public bool appliedScaleToGeometry = false;

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

        time = GameObject.Find("Time");
        start_time = GameObject.Find("Start");
        stop_time = GameObject.Find("Stop");
        status = GameObject.Find("Status");

        // the below must all be references to the PANELs of the boards; NEVER TURN OFF THE BOARDS!
        AnalysisBoard = GameObject.Find("Analysis Board").transform.GetChild(0).gameObject;
        CutsBoard = GameObject.Find("Cuts Board").transform.GetChild(0).gameObject;
        EdepBoard = GameObject.Find("Edep Board").transform.GetChild(0).gameObject;
        Controls = GameObject.Find("CPanel");
        Menus = GameObject.Find("MPanel");

        time_controller = GameObject.Find("TSlider").GetComponent<Slider>();
        //playButton = GameObject.Find("TPlay").GetComponent<Button>();

        Shader shader = Shader.Find("Standard");
        string fileName = @$"C:\Users\uclav\Documents\B's Sandbox\G4VR\Assets\{name}\{name}_tracks.csv";
        string filePath = Path.Combine(UnityEngine.Application.streamingAssetsPath, fileName);
        lineMaterial = GameObject.Find("line_mat").GetComponent<Renderer>().material;
        lineMaterial.shader = shader;
        lineMaterial.EnableKeyword("_EMISSION");

        // read csv and draw tracks

        ReadCSV(file, true);
        DrawTracks(1f);

        // time slider settings

        time_controller.maxValue = sortedKeys.Count - 1;
        time_controller.minValue = 0;
        time_controller.SetValueWithoutNotify(sortedKeys.Count - 1);
        time.transform.GetComponent<TextMeshProUGUI>().text = null;

        time_controller.onValueChanged.AddListener((interactor) => SteppedTracks(time_controller));

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
           
            string[] values = line.Split(',');

            if (values[0]=="track") // process tracks; TODO: logic to process hits (future work)
            {
                //Debug.Log("Parsing CSV");
                int trackID = int.Parse(values[1]);
                float posX, posY, posZ;
                posX = -float.Parse(values[5]);
                posY = float.Parse(values[6]);
                posZ = float.Parse(values[7]);
                List<float> poss = new List<float>() { Math.Abs(posX), Math.Abs(posY), Math.Abs(posZ) };
                if (!checkScale)
                {
                    checkedScale = checkScaleFromPosition(poss.Max());
                    GameObject[] geometries ={ GameObject.Find("Scene"),GameObject.Find("exampleB3a_scene"),GameObject.Find("exampleB4a_scene"), GameObject.Find("exampleB5_scene") };

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
                posX = -float.Parse(values[5])*checkedScale;
                posY = float.Parse(values[6])*checkedScale;
                posZ = float.Parse(values[7])*checkedScale;

                double energy = ParseHelper.ParseEnergy(values[14]);
                double time = ParseHelper.ParseTime(values[8]);
                string pname = values[2];

                double px = float.Parse(values[11]);

                double py = float.Parse(values[12]);
                double pz = float.Parse(values[13]);

                // COLORING 
                bool colorByRGB = false;
                Color trackColor = new Color();
                try
                {
                    float r = float.Parse(values[15]);
                    float g = float.Parse(values[16]);
                    float b = float.Parse(values[17]);

                    colorByRGB = true;
                    trackColor = new Color(r, g, b);
                    UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Setting RGB values for track coloring");
                    throw new Exception("Manual Error");
                }
                catch
                {
                    colorByRGB = false;
                    UnityEngine.Debug.Log("[NEW-BEHAVIOUR-SCRIPT] Setting type values for track coloring");
                }
                string process = values[10];

                double edep = ParseHelper.ParseEnergy(values[9]);

                if (time < minT) { minT = time; }
                if (time > maxT) { maxT = time; }

                string type = values[3];// type == charge. it is used to color tracks by GEANT4 convention; alternatively, coloured if RGB specified.

                Vector3 position = new Vector3(posX, posY, posZ);

                if (!trackInfo.ContainsKey(type))
                {
                    trackInfo[type] = new Dictionary<int, Track>();
                    //Debug.Log("HELLO: added type to dictionary");
                }

                if (!trackInfo[type].ContainsKey(trackID))
                {
                    trackInfo[type][trackID] = new Track(); 
                    trackInfo[type][trackID].ID = trackID;
                    //Debug.Log("HELLO: initialized track");
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

                trackInstances.Add(trackInfo[type][trackID]);

                //Debug.Log("Track Info Count " + trackInfo.Count);

            }
        }

        //time_scale = 10.0f / (maxT - minT); // this needs an associated slider. 
        //Debug.Log("trackinfo length: " + trackInfo.Count);

        // Step 1: keep raw times as double keys
        int F = 30;
        var trackOriginTimesRaw = new SortedDictionary<double, List<Track>>();

        foreach (var typeEntry in trackInfo)
        {
            foreach (var kv in typeEntry.Value)
            {
                var tval = kv.Value;
                double t0 = tval.times[0]; // first step of this track
                if (!trackOriginTimesRaw.ContainsKey(t0))
                    trackOriginTimesRaw[t0] = new List<Track>();
                trackOriginTimesRaw[t0].Add(tval);

                // --- time_control logic ---
                foreach (var ti in tval.times)
                {
                    if (!time_control.ContainsKey(ti.ToString()))
                        time_control[ti.ToString()] = new List<GameObject>();
                }

                tracks.Add(tval.trackObj); // keep list of all track GameObjects
            }
        }
        //foreach (var typeEntry in trackInfo) { var tracksByType = typeEntry.Value; foreach (var track in tracksByType) { var tval = track.Value; if (tval == null) { //Debug.LogError($"track.Value IS NULL for key {track.Key} in type {typeEntry.Key}"); continue; } //Debug.Log($"Adding track: ID={tval.ID}, times0={tval.times?[0] ?? double.NaN}, obj={tval.trackObj}, hash={tval.GetHashCode()}"); tracks.Add(tval.trackObj); string originKey = Convert.ToString(tval.times[0]); if (!trackOriginTimes.ContainsKey(originKey)) trackOriginTimes.Add(originKey, new List<Track> { tval }); else trackOriginTimes[originKey].Add(tval); for (int i = 0; i < tval.times.Count; i++) { double ti = tval.times[i]; if (!time_control.ContainsKey(Convert.ToString(ti))) time_control.Add(Convert.ToString(ti), new List<GameObject>()); } //Debug.Log($"time of {tval.ID} is {tval.times[0]}"); } }


        // Step 2: scale all times into movie seconds
        trackOriginTimes = new SortedDictionary<double, List<Track>>();

        foreach (var kv in trackOriginTimesRaw)
        {
            double rawTime = kv.Key;
            double scaledTime = F * ((rawTime - minT) / (maxT - minT)); // 0..F seconds
            trackOriginTimes[scaledTime] = kv.Value;
        }

        // Now trackOriginTimes.Keys are numeric floats/doubles, no collisions
        Debug.Log($"[TRACK ORIGINS] Scaled range: {trackOriginTimes.First().Key:F3}s → {trackOriginTimes.Last().Key:F3}s");





        sortedKeys = time_control.Keys.OrderBy(key => key).ToList();

        Debug.Log($"Sorted Keys length: {sortedKeys.Count}");

        // time slider details
        stop_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Max().ToString();
        start_time.GetComponent<TextMeshProUGUI>().text = sortedKeys.Select(s => Convert.ToDouble(s)).Min().ToString();
        status.transform.GetComponent<TextMeshProUGUI>().text = "Complete";
        status.transform.GetComponent<TextMeshProUGUI>().color = Color.green;

        AnalysisBoard.SetActive(false);


        // initializing the unified mesh and the colliders

        /*foreach (var track in trackInstances)
            track.DrawTrack(time_control);*/

        TrackMeshRenderer trackMeshRenderer = gameObject.AddComponent<TrackMeshRenderer>();
        trackMeshRenderer.trackInstances = trackInstances;
        trackMeshRenderer.BuildMesh();

        //trackMeshRenderer.SetTimeIndex(currentTimeIndex);

    }

    private void configureSettings()
    {
        GameObject cutsButton = GameObject.Find("CButton");
        GameObject movieButton = GameObject.Find("MButton");
        GameObject edepButton = GameObject.Find("Edep");
        cutsButton.GetComponent<Button>().onClick.AddListener(ShowCutsBoard);
        movieButton.GetComponent<Button>().onClick.AddListener(movie_init);

        TrackAnalyser runningInstance = GetComponent<TrackAnalyser>(); 
        edepButton.GetComponent<Button>().onClick.AddListener(() => runningInstance.ModeSwitch(edepButton));

        //UnityEngine.Debug.Log(movieButton);
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
    private Coroutine movieRoutine;

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

        if (movieRoutine != null)
            StopCoroutine(movieRoutine);
        movieRoutine = StartCoroutine(movie());
    }

    // --- Cleanup ---
    public void movie_deinit()
    {
        Debug.Log("[MOVIE] Deinitializing movie...");

        // Reactivate original track objects
        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value.Values)
                track.trackObj.SetActive(true);
        }

        Menus.SetActive(true);
        Controls.SetActive(true);
        drawMeshes = true;
    }

    // --- Main playback controller ---
    private IEnumerator movie()
    {
        Debug.Log($"[MOVIE] Start Time: {minT} ps, End Time: {maxT} ps");

        var orderedTrackOrigins = trackOriginTimes;

        float startSceneTime = Time.time;

        float movieStartTime = Time.time;

        foreach (var kvp in orderedTrackOrigins)
        {
            foreach (var track in kvp.Value)
            {
            }

            float trackStart = (float)kvp.Key;  // movie seconds
            float wait = trackStart - (Time.time - movieStartTime);
            if (wait > 0f)
                yield return new WaitForSeconds(wait);

            foreach (Track track in kvp.Value)
                StartCoroutine(Move(track, trackStart));
        }


        // Wait until full movie length has passed
        float remaining = Mathf.Max(0f, movieDuration - (Time.time - startSceneTime));
        yield return new WaitForSeconds(remaining);

        movie_deinit();
    }



    // --- Animate a single track ---
    private IEnumerator Move(Track track, float originOffset)
    {
        if (track.positions == null || track.positions.Count < 2)
            yield break;

        // Instantiate particle at first position
        GameObject sph = Instantiate(movie_prefab, track.positions[0], Quaternion.identity);
        sph.GetComponent<Renderer>().material.color = GetColor(track.type);
        sph.transform.localScale = Vector3.one * 0.08f;

        // Trail setup
        TrailRenderer trail = sph.AddComponent<TrailRenderer>();
        trail.time = 1f;
        trail.startWidth = 0.1f;
        trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = GetColor(track.type);
        trail.endColor = new Color(1, 1, 1, 0);
        trail.Clear();

        // Precompute scaled times (absolute movie seconds)
        List<double> scaledTimes = track.times
            .Select(t => ((t - minT) / (maxT - minT) * movieDuration))
            .ToList();

        float sceneStart = Time.time;

        for (int i = 0; i < track.positions.Count - 1; i++)
        {
            Vector3 start = track.positions[i];
            Vector3 end = track.positions[i + 1];

            double startTime = scaledTimes[i];
            double endTime = scaledTimes[i + 1];

            if (endTime <= startTime)
                continue;

            // Wait until segment start
            float elapsed = Time.time - sceneStart;
            float waitTime = (float)(startTime - originOffset - elapsed);
            if (waitTime > 0f)
                yield return new WaitForSeconds(waitTime);

            // Animate this segment smoothly
            float segmentElapsed = 0f;

            // applying a small boost (set to 1f to remove entirely)
            const float speedBoost = 1.1f;  // 1% faster
            float segmentDuration = (float)(endTime - startTime) / speedBoost;

            while (segmentElapsed < segmentDuration)
            {
                segmentElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(segmentElapsed / segmentDuration);
                sph.transform.position = Vector3.Lerp(start, end, t);
                yield return null;
            }

            sph.transform.position = end;
        }

        // Detach and fade trail
        if (trail != null)
        {
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time);
        }

        Destroy(sph);
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
            //UnityEngine.Debug.Log("[FORMAT-CUTS] Adding cut option for "+ pname);
            GameObject temp = Instantiate(toggle_prefab);
            temp.GetComponentInChildren<UnityEngine.UI.Text>().text = pname;
            temp.GetComponent<Toggle>().isOn = true;
            temp.GetComponent<Toggle>().onValueChanged.AddListener((interactor) => Manage_Cuts(pname, temp.GetComponent<Toggle>().isOn));

            temp.transform.SetParent(content, false);
        }
    }
    
    private void Manage_Cuts(string pname, bool active)
    {
        foreach (var typeEntry in trackInfo) 
        {
            var tracksByType = typeEntry.Value;
            foreach (var track in tracksByType)
            {
                if (track.Value.particleName == pname && !active)
                    track.Value.trackObj.SetActive(false);
                else if (track.Value.particleName == pname && active)
                    track.Value.trackObj.SetActive(true);
            }
        }
    }

    public class Track 
    {
        public int ID;
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
            if (float.Parse(type) > 0)
                return Color.blue;
            else if (float.Parse(type) < 0)
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

        return double.Parse(valueStr) * TUnits[unit];
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

        return double.Parse(valueStr) * EUnits[unit];
    }
}

public class TrackMeshRenderer : MonoBehaviour
{
    public Material trackMaterial;
    public List<Track> trackInstances;

    Mesh mesh;

    readonly List<Vector3> vertices = new();
    readonly List<Color> colors = new();
    readonly List<int> indices = new();

    readonly List<int> timeToIndexCount = new();

    void Awake()
    {
        mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        var mf = gameObject.AddComponent<MeshFilter>();
        var mr = gameObject.AddComponent<MeshRenderer>();

        mf.sharedMesh = mesh;
        mr.sharedMaterial = trackMaterial;
    }

    public void BuildMesh()
    {
        if (mesh == null)
        {
            Debug.LogWarning("Mesh is null — creating a new one.");
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        vertices.Clear();
        colors.Clear();
        indices.Clear();
        timeToIndexCount.Clear();

        foreach (var track in trackInstances)
        {
            if (track.positions == null || track.positions.Count < 2)
                continue;

            int baseVertex = vertices.Count;

            Color trackColor = track.colorByRGB ? track.color : GetColor(track.type);

            for (int i = 0; i < track.positions.Count; i++)
            {
                vertices.Add(track.positions[i]);
                colors.Add(trackColor);
            }

            for (int i = 0; i < track.positions.Count - 1; i++)
            {
                indices.Add(baseVertex + i);
                indices.Add(baseVertex + i + 1);
                timeToIndexCount.Add(indices.Count);
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
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


}
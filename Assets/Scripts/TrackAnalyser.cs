using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using Meta.XR.MRUtilityKit.SceneDecorator;
using OVR.OpenVR;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
//using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class TrackAnalyser : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] string name;
    static GameObject geometry;

    public GameObject SceneSwitchButton;
    public GameObject time_board;

    public GameObject EdepBoard;
    public GameObject CutsBoard;
    public GameObject movie_button;
    public GameObject carousel;
    //public GameObject DebugBoard;  // DEBUG ONLY

    public static Transform DebugBoardTextTransform;

    public NewBehaviourScript NBS_instance;

    //private static bool on_analysis; // if on_analysis is true -> you show edeps. if on_analysis is false -> you go back to tracks


    // A property, not a copied field: a static field initialiser captures the dictionary reference
    // exactly once at class-load time, which silently desynchronises the moment the other side assigns
    // a new dictionary rather than clearing the existing one.
    public static Dictionary<string, Dictionary<long, NewBehaviourScript.Track>> trackInfo
        => NewBehaviourScript.trackInfo;

    public static Dictionary<GameObject, List<double>> edep_bypiece = new Dictionary<GameObject, List<double>>();

    // Original per-piece meshes captured at GLB import time, BEFORE StaticBatchingUtility.Combine
    // rewrites each MeshFilter.sharedMesh to point into the shared combined buffer. MeshColliders must
    // use these originals for collision/click-to-inspect; the combined buffer would give every piece
    // the geometry of the whole scene. Populated by ServerGET/CustomManager; survives the scene change
    // because the geometry lives under the DontDestroyOnLoad "Scene" object.
    public static Dictionary<GameObject, Mesh> originalMeshes = new Dictionary<GameObject, Mesh>();

    private static Dictionary<GameObject, UnityEngine.Color> originalColors = new Dictionary<GameObject, UnityEngine.Color>();

    // Global dictionary to store original materials
    private static Dictionary<GameObject, Material> originalMaterials = new Dictionary<GameObject, Material>();

    // Every Material this class instantiates for edep coloring. Unity never reclaims these on its own,
    // and edep mode creates one per geometry piece (~11.6k on the detector geometries), so each
    // entry/exit cycle used to strand that many materials. Destroyed by ReleaseColoringMaterials().
    private static readonly List<Material> coloringMaterials = new List<Material>();

    // The one shared material the instancer draws the un-hit detector with while in edep mode.
    private static Material faintMaterial;

    // Builds a transparent edep material and records it for later destruction.
    private static Material CreateColoringMaterial(UnityEngine.Color color)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Mode", 3f); // 3 = transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        coloringMaterials.Add(mat);
        return mat;
    }

    private static void ReleaseColoringMaterials()
    {
        foreach (Material mat in coloringMaterials)
            if (mat != null)
                Destroy(mat);

        coloringMaterials.Clear();
    }

    // Run-scoped reset, for switching runs inside one scene. Deliberately keeps originalMeshes: the
    // detector geometry outlives a run switch, and those meshes are what its colliders are built from.
    public static void ReleaseRunState()
    {
        faintMaterial = null;
        ReleaseColoringMaterials();
        edep_bypiece.Clear();
        originalColors.Clear();
        originalMaterials.Clear();
    }

    // Full reset, for leaving the scene. originalMeshes holds a reference to every mesh of the loaded
    // GLB, so until it is cleared Resources.UnloadUnusedAssets can never reclaim the old geometry.
    public static void ResetStaticState()
    {
        ReleaseRunState();
        originalMeshes.Clear();
        geometry = null;
    }

    // Call this before coloring
    private static void PrepareForColoring(GameObject obj, UnityEngine.Color targetColor)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend == null) return;

        // sharedMaterial, not material: *reading* Renderer.material instantiates a private copy, so
        // capturing the original through it created a second throwaway material per piece on top of the
        // coloring one. Writing through sharedMaterial also keeps the pieces sharing the GLB's original
        // materials on restore, which is what InstancedGeometryDrawer groups by.
        if (!originalMaterials.ContainsKey(obj))
        {
            originalMaterials[obj] = rend.sharedMaterial;
        }

        rend.sharedMaterial = CreateColoringMaterial(targetColor);
    }

    // Call this to restore the original material
    private static void RestoreOriginalMaterial(GameObject obj)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend == null) return;

        if (originalMaterials.ContainsKey(obj))
        {
            rend.sharedMaterial = originalMaterials[obj];
            originalMaterials.Remove(obj);
        }
    }



    public void Awake()
    {
        //on_analysis = true;
        NBS_instance = gameObject.transform.GetComponent<NewBehaviourScript>();
    }
    public void Starter()
    {
        //DebugBoardTextTransform = DebugBoard.transform.GetChild(0);
        Debug.Log("[TRACK-ANALYSER] Initiating start sequence for edep mode");
        SceneSwitchButton = GameObject.Find("EButton");
        time_board = GameObject.Find("Controls(R: 0, 25, 0)");
        EdepBoard = GameObject.Find("Edep Board");
        CutsBoard = GameObject.Find("Cuts Board");
        movie_button = GameObject.Find("MButton");
        carousel = GameObject.Find("NumberGallery");

        geometry = GameObject.Find($"{name}_scene");
        if (geometry == null)
            geometry = GameObject.Find("G4Scene");
        EdepBoard.transform.GetChild(0).gameObject.SetActive( false );
        AddGeometries();
        SceneSwitch(true);
        LogEdep();

        // The CSV example scenes attribute edep by collider overlap, which means LogEdep had to switch
        // the segment colliders back on. Put them away again before entering the mode proper.
        if (!NewBehaviourScript.hasVolumeInfo)
            NBS_instance.ForceAllTrackCollidersOff();

        Coloring();

        // next time you call this button, 
        //on_analysis = !on_analysis;
    }

    public void ModeSwitch(GameObject button)
    {
        string text = button.transform.GetComponentInChildren<TextMeshProUGUI>().text;
        if (text == "Edep")
        {
            Starter();
        }
        if (text == "Tracks") { SceneSwitch(false); }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private static void AddGeometries() //  function to add children of input geom to edep_bypiece as keys
    {
        edep_bypiece.Clear();

        // The collider work is left entirely to PrepareGeometryColliders. This used to call
        // AddComponent<MeshCollider> unconditionally, guarded only by edep_bypiece -- which is cleared
        // on the line above -- so every re-entry into edep mode stacked ANOTHER convex MeshCollider on
        // every piece. Convex hull cooking is expensive, so entering edep got slower each time.
        Transform root = geometry.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            GameObject piece = root.GetChild(i).gameObject;
            if (piece.activeSelf && !edep_bypiece.ContainsKey(piece))
                edep_bypiece[piece] = new List<double>();
        }
    }

    public static void LogEdep()
    {
        Physics.SyncTransforms();

        foreach (var geo in edep_bypiece)
            geo.Value.Clear();

        Dictionary<GameObject, Collider> colliderByGeo = PrepareGeometryColliders();

        // Custom/binary scenes carry the actual GEANT4 volume each step occurred in, so use that
        // directly instead of guessing geometrically. CSV example scenes have no such data, so they
        // keep the old collider-overlap approximation (which double-counts edep in nested volumes,
        // since GEANT4 itself never produces overlapping volumes to begin with).
        if (NewBehaviourScript.hasVolumeInfo)
            LogEdepByVolumeID();
        else
            LogEdepByOverlap(colliderByGeo);
    }

    // Ensures every geometry piece has a (convex) MeshCollider + XRSimpleInteractable for click-to-inspect,
    // regardless of which edep-accounting method is used below.
    private static Dictionary<GameObject, Collider> PrepareGeometryColliders()
    {
        Dictionary<GameObject, Collider> colliderByGeo = new Dictionary<GameObject, Collider>();

        foreach (var geo in edep_bypiece)
        {
            GameObject temp = geo.Key;
            MeshCollider tempCollider = temp.GetComponent<MeshCollider>();
            if (tempCollider == null)
            {
                tempCollider = temp.AddComponent<MeshCollider>();
                // Use the pre-batch original mesh; MeshFilter.sharedMesh may now be the combined buffer.
                if (originalMeshes.TryGetValue(temp, out Mesh originalMesh))
                    tempCollider.sharedMesh = originalMesh;
                tempCollider.convex = true;
            }
            if (!tempCollider.enabled)
                tempCollider.enabled = true;

            XRSimpleInteractable interactable = temp.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable = temp.AddComponent<XRSimpleInteractable>();
                interactable.selectEntered.AddListener((interactor) => onHit(temp));
            }

            colliderByGeo[temp] = tempCollider;
        }

        return colliderByGeo;
    }

    // Custom/binary scenes: GEANT4's navigator already resolved each step to exactly one physical
    // volume (it never allows overlapping volumes), so match by the recorded volume name instead of
    // testing collider overlap. volumeNames[i] and edeps[i] come from the same trajectory point, so
    // they're aligned by index. GEANT4 copy numbers aren't preserved in the exported name, so a
    // segmented volume (calorimeter cells, tracker layers, ...) produces several geometry pieces that
    // all share one name -- those get disambiguated geometrically in ResolveInstance.
    private static void LogEdepByVolumeID()
    {
        // Colliders are resolved once here, not per step. ResolveInstance runs inside a loop over every
        // step of every track, so a GetComponent<Collider>() in there was millions of lookups.
        Dictionary<string, List<Candidate>> geoGroupsByName = new Dictionary<string, List<Candidate>>();
        foreach (GameObject g in edep_bypiece.Keys)
        {
            if (!geoGroupsByName.TryGetValue(g.name, out List<Candidate> group))
            {
                group = new List<Candidate>();
                geoGroupsByName[g.name] = group;
            }
            group.Add(new Candidate { piece = g, collider = g.GetComponent<Collider>() });
        }

        HashSet<string> unresolved = new HashSet<string>();
        HashSet<string> fallbackWarned = new HashSet<string>();

        foreach (var typeEntry in trackInfo)
        {
            foreach (var track in typeEntry.Value)
            {
                NewBehaviourScript.Track currTrack = track.Value;
                int count = currTrack.StepCount;

                for (int i = 0; i < count; i++)
                {
                    // Resolved from the run's string table on demand; the per-step name lists this used
                    // to read are gone (see NewBehaviourScript.Track).
                    string volumeName = currTrack.VolumeAt(i);
                    if (string.IsNullOrEmpty(volumeName))
                        continue;

                    if (!geoGroupsByName.TryGetValue(volumeName, out List<Candidate> candidates))
                    {
                        if (unresolved.Add(volumeName))
                            Debug.LogWarning($"[TRACK-ANALYSER] Step reports volume '{volumeName}' with no matching geometry piece; edep not attributed.");
                        continue;
                    }

                    // Track positions are baked in the scale root's local space; the geometry colliders live
                    // under that (possibly user-scaled) root, so compare in world space.
                    GameObject target = candidates.Count == 1
                        ? candidates[0].piece
                        : ResolveInstance(candidates, ToWorld(currTrack.positions[i]), volumeName, fallbackWarned);

                    if (target != null)
                        edep_bypiece[target].Add(currTrack.edeps[i]);
                }
            }
        }
    }

    // Transforms a baked (scale-root-local) track position into world space, matching the geometry colliders
    // which are parented under the user-scalable root. Falls back to identity when no root exists.
    private static Vector3 ToWorld(Vector3 rootLocalPosition)
    {
        return NewBehaviourScript.scaleRoot != null
            ? NewBehaviourScript.scaleRoot.TransformPoint(rootLocalPosition)
            : rootLocalPosition;
    }

    // Disambiguates several same-named geometry pieces (e.g. the many identical cells of a segmented
    // calorimeter) by testing which instance's collider actually contains the step's recorded
    // position. Colliders here are always convex (see PrepareGeometryColliders), so Collider.ClosestPoint
    // returns the point itself when it's inside -- the standard Unity idiom for point-in-collider.
    private struct Candidate
    {
        public GameObject piece;
        public Collider collider;
    }

    private static GameObject ResolveInstance(List<Candidate> candidates, Vector3 position, string volumeName, HashSet<string> fallbackWarned)
    {
        const float containmentSqrTolerance = 1e-6f;

        // Fast path: an AABB check is far cheaper than Collider.ClosestPoint, and only candidates
        // whose bounds contain the point can possibly be the true containing instance.
        foreach (Candidate candidate in candidates)
        {
            Collider collider = candidate.collider;
            if (collider == null || !collider.bounds.Contains(position))
                continue;

            Vector3 closestPoint = collider.ClosestPoint(position);
            if ((closestPoint - position).sqrMagnitude <= containmentSqrTolerance)
                return candidate.piece;
        }

        // Fallback: no instance claimed exact containment (float precision at a shared boundary, or a
        // coordinate mismatch worth investigating) -- attribute to whichever instance is closest.
        if (fallbackWarned.Add(volumeName))
            Debug.LogWarning($"[TRACK-ANALYSER] No instance of '{volumeName}' exactly contains a step position; falling back to nearest instance. If this recurs often, check for a coordinate/scale mismatch between GEANT4 and the imported geometry.");

        GameObject closest = null;
        float closestSqrDist = float.PositiveInfinity;

        foreach (Candidate candidate in candidates)
        {
            if (candidate.collider == null)
                continue;

            float sqrDist = (candidate.collider.ClosestPoint(position) - position).sqrMagnitude;
            if (sqrDist < closestSqrDist)
            {
                closestSqrDist = sqrDist;
                closest = candidate.piece;
            }
        }

        return closest;
    }

    // Example (CSV) scenes: no per-step volume info is available, so fall back to geometric overlap
    // between each step's capsule collider and every geometry piece. This double-counts edep for
    // nested volumes, but that's acceptable for the bundled example scenes.
    private static void LogEdepByOverlap(Dictionary<GameObject, Collider> colliderByGeo)
    {
        foreach (var geo in edep_bypiece)
        {
            GameObject temp = geo.Key;
            Collider tempCollider = colliderByGeo[temp];

            foreach (var typeEntry in trackInfo) // type is charge.
            {
                var tracksByType = typeEntry.Value; // dictionary with trackIDs and tracks
                foreach (var track in tracksByType)
                {
                    // track.Value is an instance of the Track class
                    NewBehaviourScript.Track currTrack = track.Value;

                    // Null at reduced fidelity, where no per-step segments were built. Custom scenes
                    // never reach this method anyway -- they attribute edep by volume ID instead.
                    if (currTrack.segments == null)
                        continue;

                    for (int i = 0; i < currTrack.segments.Count; i++)
                    {
                        GameObject segmentObject = currTrack.segments[i];
                        if (!segmentObject.activeSelf)
                        {
                            segmentObject.SetActive(true);
                        }
                        Collider segmentCollider = segmentObject.GetComponent<CapsuleCollider>();
                        if (segmentCollider == null)
                        {
                            Debug.LogError($"No CapsuleCollider on {segmentObject.name}");
                            continue;
                        }
                        if (!segmentCollider.enabled)
                            segmentCollider.enabled = true;

                        bool isOverlapping = Physics.ComputePenetration(
                                segmentCollider, segmentCollider.transform.position, segmentCollider.transform.rotation,
                                tempCollider, tempCollider.transform.position, tempCollider.transform.rotation,
                                out Vector3 direction, out float distance);

                        if (isOverlapping)
                        {
                            geo.Value.Add(currTrack.edeps[i]);
                        }
                    }
                }
            }
        }
    }


    public static void onHit(GameObject obj)
    {
        GameObject edep_board = GameObject.Find("Edep Board");
        GameObject edep_logging = edep_board.transform.GetChild(0).gameObject;
        edep_logging.SetActive(true);
        GameObject.Find("Cuts Board").transform.GetChild(0).gameObject.SetActive(false); // set cuts board to be inactive


        List<double> edeps = edep_bypiece[obj];
        double total = edep_bypiece[obj].Sum();

        edep_logging.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Edep: "+total.ToString("0.0000000E+0") + " MeV";

        edep_logging.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Geometry: " + obj.name;

    }


    private static void Coloring()
    {
        const double edepThreshold = 1e-4; // MeV threshold for faint coloring

        if (edep_bypiece.Count == 0)
            return;

        // Compute min/max of totals above threshold
        var allTotalEdeps = edep_bypiece.Values
            .Select(list => list.Sum())
            .Where(total => total >= edepThreshold)
            .ToList();

        bool anyAboveThreshold = allTotalEdeps.Count > 0;

        double minEdep = anyAboveThreshold ? allTotalEdeps.Min() : 0.0;
        double maxEdep = anyAboveThreshold ? allTotalEdeps.Max() : 0.0;
        double range = maxEdep - minEdep;
        if (range == 0) range = 1e-6; // prevent division by zero

        // Pieces that need a material of their own, i.e. those that actually received energy. The rest
        // keep the shared faint look the instancer is already drawing, so both the material allocations
        // and the individual draw calls now scale with how much of the detector the beam touched rather
        // than with how many pieces the detector has.
        HashSet<GameObject> lit = new HashSet<GameObject>();

        // Iterate through each geometry
        foreach (var geo in edep_bypiece)
        {
            GameObject obj = geo.Key;
            double totaledep = geo.Value.Sum();

            // No deposition: left to the instancer's faint material. Nothing to allocate, nothing to draw
            // separately.
            if (totaledep <= 0)
                continue;

            UnityEngine.Color targetColor;

            if (totaledep < edepThreshold)
            {
                // Very small deposition → slightly less faint white
                targetColor = new UnityEngine.Color(1f, 1f, 1f, 0.15f);
            }
            else
            {
                // Normal deposition → color by gradient
                float normalized = (float)((totaledep - edepThreshold) / range);
                normalized = Mathf.Clamp01(normalized);

                if (normalized < 0.25f)
                {
                    float t = Mathf.InverseLerp(0f, 0.25f, normalized);
                    targetColor = UnityEngine.Color.Lerp(UnityEngine.Color.blue, UnityEngine.Color.cyan, t);
                }
                else if (normalized < 0.45f)
                {
                    float t = Mathf.InverseLerp(0.25f, 0.45f, normalized);
                    targetColor = UnityEngine.Color.Lerp(UnityEngine.Color.cyan, UnityEngine.Color.green, t);
                }
                else if (normalized < 0.65f)
                {
                    float t = Mathf.InverseLerp(0.45f, 0.65f, normalized);
                    targetColor = UnityEngine.Color.Lerp(UnityEngine.Color.green, UnityEngine.Color.yellow, t);
                }
                else if (normalized < 0.80f)
                {
                    float t = Mathf.InverseLerp(0.65f, 0.80f, normalized);
                    targetColor = UnityEngine.Color.Lerp(UnityEngine.Color.yellow, new UnityEngine.Color(1f, 0.5f, 0f), t); // orange
                }
                else
                {
                    float t = Mathf.InverseLerp(0.80f, 1f, normalized);
                    targetColor = UnityEngine.Color.Lerp(new UnityEngine.Color(1f, 0.5f, 0f), UnityEngine.Color.red, t);
                }

                targetColor.a = 0.3f;
            }

            // Apply the color by swapping to a Standard shader material
            PrepareForColoring(obj, targetColor);
            lit.Add(obj);
        }

        // Hand just these pieces back to their own MeshRenderers; the detector stays instanced.
        if (InstancedGeometryDrawer.Active != null)
            InstancedGeometryDrawer.Active.SetExcluded(lit);

        Debug.Log($"[TRACK-ANALYSER] Edep mode: {lit.Count} of {edep_bypiece.Count} pieces recolored; the rest stay instanced.");

        // Update color gradient UI
        GameObject color_board = GameObject.Find("Edep Gradient");
        if (color_board != null && anyAboveThreshold)
        {
            color_board.transform.GetChild(0).gameObject.SetActive(true);
            color_board.transform.GetChild(0).GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text = maxEdep.ToString("0.000E+0") + " MeV";
            color_board.transform.GetChild(0).GetChild(1).GetComponent<TMPro.TextMeshProUGUI>().text = ((maxEdep + minEdep) / 2).ToString("0.000E+0") + " MeV";
            color_board.transform.GetChild(0).GetChild(2).GetComponent<TMPro.TextMeshProUGUI>().text = minEdep.ToString("0.000E+0") + " MeV";
        }
    }

    private void SceneSwitch(bool to_edeps)
    {
        if (to_edeps)
        {
            // --- Switching TO Edep scene ---

            // Keep the instancer running. Edep mode only needs an individual material on the pieces that
            // actually received energy -- usually a small fraction of the detector. Everything else can
            // share one faint material, which is precisely the case instancing handles best. This used
            // to call SetInstancingActive(false), waking ~11.6k MeshRenderers and taking the idle scene
            // from ~a dozen draw calls to over twenty thousand, every frame, for the whole session.
            //
            // Nothing is excluded yet: until LogEdep has run we do not know which pieces were hit, so
            // the whole detector draws faint. Coloring() narrows the set once the edeps are known.
            if (InstancedGeometryDrawer.Active != null)
            {
                faintMaterial = CreateColoringMaterial(new UnityEngine.Color(1f, 1f, 1f, 0.05f));
                InstancedGeometryDrawer.Active.SetOverrideMaterial(faintMaterial);
                InstancedGeometryDrawer.Active.SetExcluded(null);
            }

            originalMaterials.Clear();

            // Park the track-collider proximity system for the duration of edep mode. Track segments
            // are not inspectable here -- geometry pieces are -- so the colliders have no job.
            //
            // This used to set NBS_instance.overriden = true, whose branch in Recompute ACTIVATES every
            // segment GameObject in the run, then re-ran that loop every 5 seconds. The two loops that
            // used to follow this comment then called GetComponent<Collider>() on each of those
            // millions of segments to switch the collider off again. Between them, that was both the
            // stall on entering edep mode and the permanent lag once inside it.
            NBS_instance.SuspendTrackColliders(true);

            SceneSwitchButton.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Tracks";

            if (carousel != null) carousel.SetActive(false);
            time_board.SetActive(false);
            movie_button.SetActive(false);
        }
        else
        {
            // --- Switching BACK from Edep scene ---

            // Restart the proximity collider system. It re-activates the segments near the camera by
            // itself on the next frame, so there is no need to walk every segment in the run.
            NBS_instance.SuspendTrackColliders(false);

            // Hand the whole detector back to the instancer with its own materials.
            if (InstancedGeometryDrawer.Active != null)
            {
                InstancedGeometryDrawer.Active.SetExcluded(null);
                InstancedGeometryDrawer.Active.SetOverrideMaterial(null);
                InstancedGeometryDrawer.Active.SetInstancingActive(true);
            }

            faintMaterial = null; // destroyed below with the rest of this session's materials

            // Update Scene button label
            SceneSwitchButton = GameObject.Find("EButton");
            //UnityEngine.Debug.Log("[TRACK-ANALYSER] Scene Switch Button is set to "+SceneSwitchButton);
            SceneSwitchButton.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Edep";

            // Show UI elements
            if (carousel != null) carousel.SetActive(true);
            time_board.SetActive(true);
            movie_button.SetActive(true);

            // Hide Edep board and gradient
            GameObject edep_board = GameObject.Find("Edep Board");
            if (edep_board != null)
                edep_board.transform.GetChild(0).gameObject.SetActive(false);

            GameObject edep_grad = GameObject.Find("Edep Gradient");
            if (edep_grad != null)
                edep_grad.transform.GetChild(0).gameObject.SetActive(false);

            // Detach the materials built for *this* edep session before restoring. The restore loop can
            // itself create fallback materials, and those must not be in the batch we destroy below.
            Material[] spentMaterials = coloringMaterials.ToArray();
            coloringMaterials.Clear();

            // Restore the pieces that were actually recolored. Only those were touched -- the rest kept
            // their own materials throughout and were simply drawn faint by the instancer, so the old
            // "fallback faint white" branch here would now *introduce* a wrong material rather than
            // repair one.
            foreach (var kv in originalMaterials)
            {
                Renderer rend = kv.Key != null ? kv.Key.GetComponent<Renderer>() : null;
                if (rend != null)
                    rend.sharedMaterial = kv.Value;
            }

            // De-interact the geometry: pieces are only clickable inside edep mode.
            foreach (var geo in edep_bypiece)
            {
                Collider col = geo.Key != null ? geo.Key.GetComponent<Collider>() : null;
                if (col != null)
                    col.enabled = false;
            }

            // Clear dictionaries. The originals are back on the renderers, so this session's coloring
            // materials are now unreferenced and can be destroyed outright.
            edep_bypiece.Clear();
            originalMaterials.Clear();

            foreach (Material mat in spentMaterials)
                if (mat != null)
                    Destroy(mat);
        }
    }


    private static void SetRendererColor(Renderer renderer, UnityEngine.Color color)
    {
        if (renderer == null || renderer.sharedMaterial == null)
            return;

        Material mat = renderer.material;

        // --- Base color for glTF PBR ---
        if (mat.HasProperty("_BaseColorFactor"))
            mat.SetColor("_BaseColorFactor", color);

        // Optionally still try _Color (for Standard fallback)
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);

        // --- Remove emissive effect ---
        if (mat.HasProperty("emissiveFactor"))
            mat.SetColor("emissiveFactor", UnityEngine.Color.black);

        if (mat.HasProperty("_EmissiveFactor"))
            mat.SetColor("_EmissiveFactor", UnityEngine.Color.black);

        // --- Remove emissive texture ---
        if (mat.HasProperty("emissiveTexture"))
            mat.SetTexture("emissiveTexture", null);

        if (mat.HasProperty("_EmissionMap"))
            mat.SetTexture("_EmissionMap", null);

        // --- Tone down metallic / roughness for bleak white ---
        if (mat.HasProperty("metallicFactor"))
            mat.SetFloat("metallicFactor", 0f);

        if (mat.HasProperty("roughnessFactor"))
            mat.SetFloat("roughnessFactor", 1f);

        // --- Disable keyword that may force emission ---
        mat.DisableKeyword("_EMISSION");
    }


}

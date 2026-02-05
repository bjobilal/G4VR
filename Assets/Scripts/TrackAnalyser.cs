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

    //private static bool on_analysis; // if on_analysis is true -> you show edeps. if on_analysis is false -> you go back to tracks


    public static Dictionary<string, Dictionary<int, NewBehaviourScript.Track>> trackInfo = NewBehaviourScript.trackInfo;

    public static Dictionary<GameObject, List<double>> edep_bypiece = new Dictionary<GameObject, List<double>>();

    private static Dictionary<GameObject, UnityEngine.Color> originalColors = new Dictionary<GameObject, UnityEngine.Color>();

    // Global dictionary to store original materials
    private static Dictionary<GameObject, Material> originalMaterials = new Dictionary<GameObject, Material>();

    // Call this before coloring
    private static void PrepareForColoring(GameObject obj, UnityEngine.Color targetColor)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend == null) return;

        // Store original material if not already stored
        if (!originalMaterials.ContainsKey(obj))
        {
            originalMaterials[obj] = rend.material;
        }

        // Create a new material based on Standard shader
        Material colorMat = new Material(Shader.Find("Standard"));
        colorMat.color = targetColor;

        // Optional: Make it slightly transparent
        colorMat.SetFloat("_Mode", 3f); // 3 = transparent
        colorMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        colorMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        colorMat.SetInt("_ZWrite", 0);
        colorMat.DisableKeyword("_ALPHATEST_ON");
        colorMat.EnableKeyword("_ALPHABLEND_ON");
        colorMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        colorMat.renderQueue = 3000;

        // Assign new material
        rend.material = colorMat;
    }

    // Call this to restore the original material
    private static void RestoreOriginalMaterial(GameObject obj)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend == null) return;

        if (originalMaterials.ContainsKey(obj))
        {
            rend.material = originalMaterials[obj];
            originalMaterials.Remove(obj);
        }
    }



    public void Awake()
    {
        //on_analysis = true;
    }
    public void Starter()
    {
        //DebugBoardTextTransform = DebugBoard.transform.GetChild(0);
        Debug.Log("[TRACK-ANALYSER] Initiating start sequence for edep mode");
        SceneSwitchButton = GameObject.Find("Edep");
        time_board = GameObject.Find("Controls(R: 0, 25, 0)");
        EdepBoard = GameObject.Find("Edep Board");
        CutsBoard = GameObject.Find("Cuts Board");
        movie_button = GameObject.Find("MButton");
        carousel = GameObject.Find("NumberGallery");

        geometry = GameObject.Find($"{name}_scene");
        if (geometry == null)
            geometry = GameObject.Find("G4Scene");
        EdepBoard.transform.GetChild(0).gameObject.SetActive( false );
        SceneSwitch(true);
        if (true){
            AddGeometries();
            LogEdep();
            Coloring();
        }

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
        for (int i =0; i<geometry.transform.childCount; i++)
        {
            if (geometry.transform.GetChild(i).gameObject.activeSelf && !edep_bypiece.ContainsKey(geometry.transform.GetChild(i).gameObject))
            {
                edep_bypiece[geometry.transform.GetChild(i).gameObject] = new List<double>();
                geometry.transform.GetChild(i).AddComponent<MeshCollider>();
                geometry.transform.GetChild(i).GetComponent<MeshCollider>().convex = true;
            }
        }

    }

    public static void LogEdep()
    {
        //DebugBoardTextTransform.GetComponent<TextMeshProUGUI>().text += "Entered LogEdep \n "; // DEBUG ONLY
        Physics.SyncTransforms();
        foreach (var geo in edep_bypiece)
        {
            GameObject temp = geo.Key;
            Collider tempCollider = null;
            try { tempCollider = temp.GetComponent<MeshCollider>(); }
            catch { }
            if (tempCollider == null)
            {
                tempCollider = temp.AddComponent<MeshCollider>();
                temp.GetComponent<MeshCollider>().convex = true; 
            }

            try
            {
                XRSimpleInteractable interactable = temp.AddComponent<XRSimpleInteractable>();

                interactable.enabled = true;
                interactable.selectEntered.AddListener((interactor) => onHit(temp));
            }
            catch { }
            double totaledep = 0;
            foreach (var typeEntry in trackInfo) // type is charge. 
            {
                var tracksByType = typeEntry.Value; // dictionary with trackIDs and tracks
                foreach (var track in tracksByType)
                {
                    // track.Value is an instance of the Track class
                    NewBehaviourScript.Track currTrack = track.Value;
                    for (int i = 0; i<currTrack.segments.Count; i++)
                    {
                        GameObject segmentObject = currTrack.segments[i];
                        Collider segmentCollider = segmentObject.GetComponent<Collider>();
                        //DebugBoardTextTransform.GetComponent<TextMeshProUGUI>().text += "NO ISSUES \n "; // DEBUG ONLY
                        if (!tempCollider.enabled)
                            tempCollider.enabled = true;
                        if (!segmentCollider.enabled)
                            segmentCollider.enabled = true;
                        //Physics.SyncTransforms();
                        //Coroutine coroutine = StartCoroutine(DelayedBoundsCheck());

                        //tempCollider.gameObject.SetActive(true);
                        //MeshCollider mc = tempCollider as MeshCollider;
                        //var mesh = mc.sharedMesh;


                        /*if (segmentCollider.bounds.Intersects(tempCollider.bounds)) // this code used to be fine until i realized that the bounds checking is too coarse and is AABB. physics may be better - BJ
                        {
                            geo.Value.Add(currTrack.edeps[i]); // add the edep to the edep list associated with temp
                            totaledep += currTrack.edeps[i];
                            Debug.Log($"{tempCollider.gameObject.name} has edep: {currTrack.edeps[i]} from {segmentCollider.gameObject.name} and step {i}");
                        }*/
                        bool isOverlapping = Physics.ComputePenetration(
                                segmentCollider, segmentCollider.transform.position, segmentCollider.transform.rotation,
                                tempCollider, tempCollider.transform.position, tempCollider.transform.rotation,
                                out Vector3 direction, out float distance);

                        if (isOverlapping)
                        {
                            geo.Value.Add(currTrack.edeps[i]);
                            totaledep += currTrack.edeps[i];
                            //Debug.Log($"{tempCollider.gameObject.name} has edep: {currTrack.edeps[i]} from {segmentCollider.gameObject.name} and step {i}");
                        }

                    }
                }
            }

            //Debug.Log($"{temp.name} has total edep: {totaledep}");
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

        if (allTotalEdeps.Count == 0)
            return;

        double minEdep = allTotalEdeps.Min();
        double maxEdep = allTotalEdeps.Max();
        double range = maxEdep - minEdep;
        if (range == 0) range = 1e-6; // prevent division by zero

        // Iterate through each geometry
        foreach (var geo in edep_bypiece)
        {
            GameObject obj = geo.Key;
            double totaledep = geo.Value.Sum();

            UnityEngine.Color targetColor;

            if (totaledep <= 0)
            {
                // No deposition == faint white
                targetColor = new UnityEngine.Color(1f, 1f, 1f, 0.05f);
            }
            else if (totaledep < edepThreshold)
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
        }

        // Update color gradient UI
        GameObject color_board = GameObject.Find("Edep Gradient");
        if (color_board != null)
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

            originalMaterials.Clear();

            foreach (var geo in edep_bypiece)
            {
                GameObject obj = geo.Key;
                Renderer rend = obj.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (!originalMaterials.ContainsKey(obj))
                        originalMaterials[obj] = rend.material;

                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = new UnityEngine.Color(1f, 1f, 1f, 0.05f);
                    mat.color = new UnityEngine.Color(1f, 1f, 1f, 0.05f);
                    mat.SetFloat("_Mode", 3f); // Transparent mode
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    rend.material = mat;
                }
            }

            SceneSwitchButton.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Tracks";

            foreach (var typeEntry in trackInfo)
            {
                foreach (var track in typeEntry.Value)
                {
                    NewBehaviourScript.Track currTrack = track.Value;
                    foreach (var segment in currTrack.segments)
                    {
                        segment.GetComponent<Collider>().enabled = false;
                    }
                }
            }

            if (carousel != null) carousel.SetActive(false);
            time_board.SetActive(false);
            movie_button.SetActive(false);
        }
        else
        {
            // --- Switching BACK from Edep scene ---

            // Update Scene button label
            SceneSwitchButton = GameObject.Find("Edep");
            //UnityEngine.Debug.Log("[TRACK-ANALYSER] Scene Switch Button is set to "+SceneSwitchButton);
            SceneSwitchButton.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Edep";

            // Enable line segment colliders
            foreach (var typeEntry in trackInfo)
            {
                foreach (var track in typeEntry.Value)
                {
                    NewBehaviourScript.Track currTrack = track.Value;
                    foreach (var segment in currTrack.segments)
                    {
                        segment.GetComponent<Collider>().enabled = true;
                    }
                }
            }

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

            // Restore original materials and disable colliders for Edep geometries
            foreach (var geo in edep_bypiece)
            {
                GameObject obj = geo.Key;
                Renderer rend = obj.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (originalMaterials.ContainsKey(obj))
                    {
                        rend.material = originalMaterials[obj];
                    }
                    else
                    {
                        // fallback: faint white
                        Material mat = new Material(Shader.Find("Standard"));
                        mat.color = new UnityEngine.Color(1f, 1f, 1f, 0.05f);
                        mat.SetFloat("_Mode", 3f);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                        rend.material = mat;
                    }
                }

                // De-interact geometry
                Collider col = obj.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }

            // Clear dictionaries
            edep_bypiece.Clear();
            originalMaterials.Clear();
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

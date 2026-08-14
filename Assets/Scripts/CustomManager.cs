using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;
//using static UnityEditor.Experimental.GraphView.Port;
using UnityEngine.Networking;
using TMPro;

public class CustomManager : MonoBehaviour
{
    // Start is called before the first frame update

    GameObject Geometry;

    TextMeshProUGUI error_header;
    TextMeshProUGUI error_message;

    void Start()
    {

        try
        {
            Geometry = GameObject.Find("Scene");
            string URL = GameManager.instance.URL;
            string name = GameManager.instance.customscene;

            Geometry.transform.GetChild(0).gameObject.SetActive(true);
            StartCoroutine(WaitForLoadAndSetOpacity());


        }
        catch
        {
            Geometry.SetActive(true);
            SetOpacity(GameObject.Find("DinklageLikenessSculpt"), 0.05f);
        }
        Geometry.SetActive(true);
    }
    private IEnumerator WaitForLoadAndSetOpacity()
    {
        // wait until glb is loaded fully
        while (Geometry.transform.childCount == 0)
        {
            yield return null;
        }
        Debug.Log("Waiting...");
        yield return new WaitForSeconds(0.05f);
        Debug.Log("Setting Opacity...");

        SetOpacity(Geometry.transform.GetChild(0).gameObject, 0.05f);
    }
    public static void SetOpacity(GameObject parent, float opacity)
    {
        foreach (Transform child in parent.transform)
        {
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (Material mat in renderer.materials)
                {
                    Color color = mat.color;
                    color.a = opacity;
                    mat.color = color;
                    mat.SetFloat("_Mode", 2);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                }
            }
        }
    }

    async Task LoadGLB(string url) // GLB is binary form of GLTF - unity recommends since embedded buffers in gltf is slow apparently. 
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.downloadHandler = new DownloadHandlerBuffer();
            var request = www.SendWebRequest();

            while (!request.isDone)
               { 
                
                await Task.Yield(); }

            if (www.result != UnityWebRequest.Result.Success)
            {
                error_header.text = "URL Error";
                error_message.text = "There was an error loading data from this address. Please check that it is reachable and try again.";
                return;
            }

            byte[] data = www.downloadHandler.data;
            Debug.Log(data.Length);
            var gltf = new GltfImport();
            //bool success = await gltf.LoadGltfBinary(data, new Uri(url));
            bool success = await gltf.LoadGltfBinary(data);
            Debug.Log(success);
            success = await gltf.InstantiateMainSceneAsync(Geometry.transform);
            if (!success)
            //Debug.LogError("Failed to instantiate scene.");
            {
                error_header.text = "Geometry Load Error";
                error_message.text = "G4VR is unable to load the geometry data from this address. Please check your experiment and try again later";
            }

            // Post-import render optimization (see ServerGET for the full rationale). Disable shadows, cache
            // each piece's original mesh for TrackAnalyser's per-piece colliders, then hand rendering to
            // InstancedGeometryDrawer, which collapses the thousands of pieces into ~a dozen instanced draw
            // calls -- Built-in RP's automatic batchers don't handle this geometry.
            TrackAnalyser.originalMeshes.Clear();
            foreach (MeshRenderer mr in Geometry.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    TrackAnalyser.originalMeshes[mr.gameObject] = mf.sharedMesh;
            }

            if (Geometry.transform.childCount > 0)
            {
                GameObject modelRoot = Geometry.transform.GetChild(0).gameObject;
                InstancedGeometryDrawer drawer = modelRoot.GetComponent<InstancedGeometryDrawer>();
                if (drawer == null)
                    drawer = modelRoot.AddComponent<InstancedGeometryDrawer>();
                drawer.Build(modelRoot);
            }


        }
    }
}

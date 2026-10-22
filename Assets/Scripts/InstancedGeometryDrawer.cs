using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Renders large, highly-repeated GLB geometry (thousands of copies of a handful of meshes) via explicit
// GPU instancing, bypassing Built-in RP's automatic batchers.
//
// Why this exists: the imported detector geometry is ~11.6k pieces built from only ~6 distinct meshes and
// ~6 materials. That is the ideal case for GPU instancing, but Built-in RP's *automatic* instancing only
// merges instances that are adjacent in the render sort order, and these pieces are spatially interleaved,
// so it fragments into ~1-instance batches (20k+ draw calls). Runtime static batching didn't collapse the
// draw calls either (and would bake all vertices into combined buffers -- bad for headset memory).
// Graphics.DrawMeshInstanced ignores sort order: it draws each (mesh, submesh, material) group in chunks of
// up to 1023, taking the idle scene to roughly (groups * ceil(instances/1023)) draw calls -- about a dozen --
// while reusing the 6 source meshes (no memory blow-up).
//
// The source GameObjects and MeshRenderers are kept -- TrackAnalyser still needs them for per-piece
// MeshColliders / click-to-inspect and for per-piece recoloring in edep mode -- but their MeshRenderers are
// disabled while instancing is active so Unity doesn't also draw them. When TrackAnalyser enters edep mode it
// recolors individual pieces on the real renderers, which instancing can't express, so it calls
// SetInstancingActive(false) to hand rendering back to the MeshRenderers for the duration of the analysis.
[DefaultExecutionOrder(1000)]
public class InstancedGeometryDrawer : MonoBehaviour
{
    // Set to the drawer that owns the currently loaded geometry so TrackAnalyser can toggle it without a
    // hard reference. Only one detector geometry is ever loaded at a time.
    public static InstancedGeometryDrawer Active;

    // Graphics.DrawMeshInstanced caps each call at 1023 instances.
    private const int MaxPerBatch = 1023;

    private class Group
    {
        public Mesh mesh;
        public int subMesh;
        public Material material;
        public readonly List<Matrix4x4> local = new List<Matrix4x4>();     // every instance, relative to root
        public readonly List<GameObject> sources = new List<GameObject>(); // parallel to `local`
        public readonly List<Matrix4x4> draw = new List<Matrix4x4>();      // the subset actually instanced
        public Matrix4x4[][] worldChunks;                                  // reused world-space buffers (no per-frame GC)
        public int[] chunkCounts;
    }

    private readonly List<Group> groups = new List<Group>();
    private readonly List<MeshRenderer> managedRenderers = new List<MeshRenderer>();
    private Transform root;
    private bool instancingActive;

    // Pieces that must render themselves rather than be instanced, because they need an individual
    // material. Edep mode puts the handful of pieces that actually received energy in here; everything
    // else stays instanced, so the detector keeps costing ~a dozen draw calls instead of ~11.6k.
    private HashSet<GameObject> excluded;

    // The world-matrix chunks only go stale when the model actually moves. Idle -- which is most of
    // the time -- this saves ~11.6k matrix multiplies every frame.
    private Matrix4x4 lastRootToWorld;
    private bool chunksValid;

    // Drawn instead of each group's own material when set. Edep mode uses one shared faint material for
    // the whole un-hit detector, which is exactly the case instancing handles best.
    private Material overrideMaterial;

    // Drawn when nothing is overriding. Set by CustomManager to a Standard-shader material.
    //
    // The imported glTF materials CANNOT be used for the instanced draw in a player build. The glTF
    // shader declares its transparency keywords as
    //     #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
    // and shader_feature variants are stripped at build time unless some Material ASSET in the build
    // already has the keyword enabled. These materials are created at runtime by glTFast and the
    // keyword is set at runtime, so Unity's build pipeline never sees it: the _ALPHABLEND_ON variant
    // -- and with it the _ALPHABLEND_ON x INSTANCING_ON combination this draw needs -- is simply
    // absent from the APK, and the geometry does not render. The editor compiles variants on demand,
    // which is why it only fails on device.
    //
    // Standard is a built-in shader listed in Always Included Shaders and ships its full variant set,
    // so a Standard-based material draws correctly instanced on the headset -- as edep mode already
    // demonstrates, since that is exactly what it overrides with.
    public Material defaultMaterial;

    // Scans every MeshRenderer under rootObject, groups instances by (mesh, submesh, material), captures their
    // transforms relative to rootObject, disables the MeshRenderers, and begins drawing via instancing.
    public void Build(GameObject rootObject)
    {
        root = rootObject.transform;
        groups.Clear();
        managedRenderers.Clear();

        Dictionary<(Mesh, int, Material), Group> byKey = new Dictionary<(Mesh, int, Material), Group>();
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;

        foreach (MeshRenderer mr in rootObject.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;

            Mesh mesh = mf.sharedMesh;
            Material[] mats = mr.sharedMaterials;
            Matrix4x4 localToRoot = worldToRoot * mr.localToWorldMatrix;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                Material mat = mats.Length == 0 ? null : mats[Mathf.Min(s, mats.Length - 1)];
                if (mat == null)
                    continue;

                mat.enableInstancing = true; // required for DrawMeshInstanced; the glTF PBR shader supports it

                var key = (mesh, s, mat);
                if (!byKey.TryGetValue(key, out Group g))
                {
                    g = new Group { mesh = mesh, subMesh = s, material = mat };
                    byKey[key] = g;
                    groups.Add(g);
                }
                g.local.Add(localToRoot);
                g.sources.Add(mr.gameObject);
            }

            managedRenderers.Add(mr);
        }

        excluded = null;
        overrideMaterial = null;
        instancingActive = true;
        chunksValid = false;

        RebuildDrawSets();
        ApplyRendererStates();

        Active = this;
        enabled = true;

        Debug.Log($"[RENDER-DIAG] InstancedGeometryDrawer built: {managedRenderers.Count} renderers -> {groups.Count} instanced groups");
    }

    // Drops the instancing bookkeeping for the current geometry. The per-group world-matrix chunks are
    // sized to the piece count (~11.6k matrices, ~750 KB) and `Active` is a static that would otherwise
    // keep a destroyed drawer -- and through it every mesh and material of the previous detector --
    // reachable for the rest of the process, defeating Resources.UnloadUnusedAssets.
    public static void ResetStaticState()
    {
        if (Active != null)
            Active.ReleaseBuffers();

        Active = null;
    }

    private void ReleaseBuffers()
    {
        foreach (Group g in groups)
        {
            g.worldChunks = null;
            g.chunkCounts = null;
            g.mesh = null;
            g.material = null;
            g.local.Clear();
            g.local.TrimExcess();
            g.sources.Clear();
            g.sources.TrimExcess();
            g.draw.Clear();
            g.draw.TrimExcess();
        }

        groups.Clear();
        managedRenderers.Clear();
        excluded = null;
        overrideMaterial = null;
        root = null;
        instancingActive = false;
    }

    // Unlike OnDisable (see the note at the bottom of this file), OnDestroy means the geometry really
    // is going away, so releasing here is safe and keeps a run/scene switch from stranding the buffers.
    private void OnDestroy()
    {
        if (Active == this)
            Active = null;

        ReleaseBuffers();
    }

    // Toggle between instanced drawing (renderers off) and the real MeshRenderers (renderers on).
    // Kept as a blunt all-or-nothing switch; edep mode should prefer SetExcluded, which keeps the
    // instancer running for everything that does not need its own material.
    public void SetInstancingActive(bool on)
    {
        instancingActive = on;
        ApplyRendererStates();
    }

    // Draw every instanced group with `material` instead of its own. Pass null to go back to the
    // model's materials.
    public void SetOverrideMaterial(Material material)
    {
        if (material != null)
            material.enableInstancing = true; // required by DrawMeshInstanced

        overrideMaterial = material;
    }

    // Hand the listed pieces back to their own MeshRenderers and drop them from the instanced batches.
    // Null or empty restores full instancing.
    public void SetExcluded(HashSet<GameObject> pieces)
    {
        excluded = (pieces != null && pieces.Count > 0) ? pieces : null;
        chunksValid = false;

        RebuildDrawSets();
        ApplyRendererStates();
    }

    // Recomputes the subset each group actually instances and resizes the reusable world-matrix chunk
    // buffers to match. Only runs on build and on an exclusion change, never per frame.
    private void RebuildDrawSets()
    {
        foreach (Group g in groups)
        {
            g.draw.Clear();

            for (int i = 0; i < g.local.Count; i++)
            {
                if (excluded != null && excluded.Contains(g.sources[i]))
                    continue;

                g.draw.Add(g.local[i]);
            }

            int chunks = Mathf.CeilToInt(g.draw.Count / (float)MaxPerBatch);
            g.worldChunks = new Matrix4x4[chunks][];
            g.chunkCounts = new int[chunks];

            for (int c = 0; c < chunks; c++)
            {
                int count = Mathf.Min(MaxPerBatch, g.draw.Count - c * MaxPerBatch);
                g.worldChunks[c] = new Matrix4x4[count];
                g.chunkCounts[c] = count;
            }
        }
    }

    // A MeshRenderer draws itself only when the instancer is not drawing its piece.
    private void ApplyRendererStates()
    {
        foreach (MeshRenderer mr in managedRenderers)
        {
            if (mr == null)
                continue;

            bool instancedHere = instancingActive && (excluded == null || !excluded.Contains(mr.gameObject));
            mr.enabled = !instancedHere;
        }
    }

    private void LateUpdate()
    {
        if (!instancingActive || groups.Count == 0 || root == null)
            return;

        // World matrices are rebuilt from the root-relative ones only when the model has actually moved
        // or been rescaled (a VR grab, the scale slider). The draw calls still have to be issued every
        // frame -- DrawMeshInstanced is immediate mode -- but the ~11.6k matrix multiplies behind them
        // do not.
        Matrix4x4 rootToWorld = root.localToWorldMatrix;
        bool rebuild = !chunksValid || rootToWorld != lastRootToWorld;

        if (rebuild)
        {
            lastRootToWorld = rootToWorld;
            chunksValid = true;
        }

        foreach (Group g in groups)
        {
            Material material = overrideMaterial != null ? overrideMaterial
                              : defaultMaterial != null ? defaultMaterial
                              : g.material;

            int idx = 0;
            for (int c = 0; c < g.worldChunks.Length; c++)
            {
                Matrix4x4[] chunk = g.worldChunks[c];
                int count = g.chunkCounts[c];

                if (rebuild)
                {
                    for (int i = 0; i < count; i++)
                        chunk[i] = rootToWorld * g.draw[idx++];
                }

                Graphics.DrawMeshInstanced(g.mesh, g.subMesh, material, chunk, count, null,
                    ShadowCastingMode.Off, false);
            }
        }
    }
    // NOTE: intentionally no OnDisable/OnEnable clearing of Active. The model root is toggled inactive
    // (e.g. hidden in the menu, shown in the analysis scene) throughout its life, and Active must remain
    // valid across those toggles so TrackAnalyser can hand rendering back to the real renderers in edep
    // mode. Only one detector geometry is loaded at a time, and Build() always reassigns Active on load.
}

using UnityEngine;

// Shows the "Note" panel when the run files forced a reduced-fidelity load.
//
// Lives on the Note Board in the analysis scene and drives a HelpMenu panel built from the shared
// Assets/UI/HelpMenu.prefab, so the notice looks exactly like the main menu's help panel. When the
// run loaded at full fidelity there is nothing to say, and the whole board switches itself off.
public class MemoryNotice : MonoBehaviour
{
    [Tooltip("The panel to drive. Found in the children if left empty.")]
    public HelpMenu menu;

    public string title = "Note";

    [TextArea(3, 10)]
    public string message = RunBudget.MeshOnlyNotice;

    private void Start()
    {
        if (menu == null)
            menu = GetComponentInChildren<HelpMenu>(true);

        if (menu == null)
        {
            Debug.LogWarning("[MEMORY-NOTICE] No HelpMenu found to display the notice on.");
            return;
        }

        if (RunBudget.Mode != RunBudget.Fidelity.MeshOnly)
        {
            // Full fidelity (or a refusal, which never reaches this scene): nothing to report.
            gameObject.SetActive(false);
            return;
        }

        menu.SetHeader(title);
        menu.SetBody(message);
        menu.Open();
    }
}

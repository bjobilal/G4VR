using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The main-menu help overlay: a round "?" button that opens a scrollable panel carrying the controls
// diagram and whatever explanatory text you want to add.
//
// The hierarchy itself is produced by Assets/Editor/HelpMenuBuilder.cs ("G4VR > Build Help Menu"),
// which clones its styling from the panels already in the scene. This component owns only the
// behaviour and the content, so the text below can be edited straight from the Inspector without
// touching any of the layout.
public class HelpMenu : MonoBehaviour
{
    [Header("Wiring (filled in by the builder)")]
    public GameObject panel;
    public Image controlsImage;
    public LayoutElement controlsImageLayout;
    public TextMeshProUGUI bodyText;
    public ScrollRect scrollRect;
    public RectTransform contentRect;

    // The panel's labels can be either TMP or legacy UI Text -- the Main Menu panel uses legacy Text
    // for its header and body, which is why `bodyText` above serialises as null there. Whichever of
    // each pair is set gets written, so both styles work and neither has to be converted.
    public Text bodyTextLegacy;
    public TextMeshProUGUI headerText;
    public Text headerTextLegacy;

    private bool labelsResolved;

    [Header("Content")]
    [Tooltip("The controls diagram pinned to the top of the panel.")]
    public Sprite controlsSprite;

    [TextArea(4, 30)]
    [Tooltip("Body text under the diagram. Add as much as you like; the panel scrolls.")]
    public string body = "Welcome to G4VR. (v2.5)";

    private void OnEnable()
    {
        Apply();
    }

    // Pushes the serialized content onto the panel. Public so the builder -- and an Inspector tweak
    // while playing -- can refresh it without reopening the menu.
    public void Apply()
    {
        if (controlsImage != null)
        {
            controlsImage.sprite = controlsSprite;
            controlsImage.preserveAspect = true;
            controlsImage.enabled = controlsSprite != null;
        }

        WriteText(bodyText, bodyTextLegacy, body);
    }

    // Sets the panel's title. Resolves the label by name if it was never wired, so a panel built from
    // the prefab works without the reference having been filled in.
    public void SetHeader(string value)
    {
        ResolveLabels();
        WriteText(headerText, headerTextLegacy, value);
    }

    // Sets the panel's body text, and keeps `body` in step so a later Apply() does not revert it.
    public void SetBody(string value)
    {
        ResolveLabels();
        body = value;
        WriteText(bodyText, bodyTextLegacy, value);
    }

    private static void WriteText(TextMeshProUGUI tmp, Text legacy, string value)
    {
        if (tmp != null)
            tmp.text = value;

        if (legacy != null)
            legacy.text = value;
    }

    // Finds the header and body labels by name when they were not wired explicitly.
    //
    // Deliberately NOT called from Apply(): a panel whose text was authored directly in the scene
    // would otherwise have it silently overwritten by the `body` field the first time it opened.
    // Only the explicit SetHeader/SetBody calls resolve.
    private void ResolveLabels()
    {
        if (labelsResolved || panel == null)
            return;

        labelsResolved = true;

        if (headerText == null && headerTextLegacy == null)
            FindLabel("Header", out headerText, out headerTextLegacy);

        if (bodyText == null && bodyTextLegacy == null)
            FindLabel("Body", out bodyText, out bodyTextLegacy);
    }

    private void FindLabel(string childName, out TextMeshProUGUI tmp, out Text legacy)
    {
        tmp = null;
        legacy = null;

        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != childName)
                continue;

            tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            legacy = t.GetComponentInChildren<Text>(true);

            if (tmp != null || legacy != null)
                return;
        }
    }

    // The VerticalLayoutGroup decides the diagram's width; its height has to follow from the sprite's
    // aspect or the image comes out squashed. An AspectRatioFitter fights the layout group here, so
    // the height is computed directly instead.
    public void ResizeControlsImage()
    {
        if (controlsImageLayout == null || controlsImage == null || controlsImage.sprite == null)
            return;

        float width = controlsImage.rectTransform.rect.width;
        if (width <= 0f && contentRect != null)
            width = contentRect.rect.width;

        if (width <= 0f)
            return;

        Rect spriteRect = controlsImage.sprite.rect;
        controlsImageLayout.preferredHeight = width * (spriteRect.height / Mathf.Max(spriteRect.width, 1f));
    }

    public void Open()
    {
        if (panel == null)
            return;

        panel.SetActive(true);
        Apply();

        // The panel was inactive a moment ago, so its rects still hold stale sizes. Flush the canvas
        // before measuring the diagram, then rebuild with the height that measurement produced.
        Canvas.ForceUpdateCanvases();
        ResizeControlsImage();

        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f; // always open at the top
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    public void Toggle()
    {
        if (panel == null)
            return;

        if (panel.activeSelf)
            Close();
        else
            Open();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (isActiveAndEnabled)
            Apply();
    }
#endif
}

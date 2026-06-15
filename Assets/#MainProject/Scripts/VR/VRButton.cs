using UnityEngine;
using UnityEngine.UI;

/// A code-built world-space button for VR menus. The laser (VRLaserPointer) raycasts physics
/// against its BoxCollider, highlights it on hover, and invokes OnClick on a trigger press.
/// Disabled ("used") options are dimmed and ignore clicks.
public class VRButton : MonoBehaviour
{
    public System.Action OnClick;
    public Image background;
    public bool interactable = true;

    public Color normalColor = new Color(0.10f, 0.12f, 0.22f, 0.96f);
    public Color hoverColor  = new Color(0.20f, 0.55f, 1.00f, 0.98f);
    public Color usedColor   = new Color(0.15f, 0.15f, 0.15f, 0.90f);

    private bool _hovered;

    public void Refresh()
    {
        if (background == null) return;
        background.color = !interactable ? usedColor : (_hovered ? hoverColor : normalColor);
    }

    public void SetHover(bool on)
    {
        _hovered = on;
        Refresh();
    }

    public void Click()
    {
        if (interactable) OnClick?.Invoke();
    }
}

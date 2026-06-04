using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the emission on a fighter's GLOVE materials for combat juice:
///   • CHARGE  — gloves glow + pulse, ramping up to the beat (and pulsing faster as it nears).
///   • STRIKE  — a bright white-hot burst when this fighter LANDS a hit.
///   • HURT    — a red burst when this fighter GETS hit.
///
/// Put this on the root player object (same GameObject as PlayerCombat) and drag the glove
/// Renderers into the list. It instances the glove materials so each fighter glows independently
/// and never edits the shared asset. Emission-only — no custom shader needed (URP Lit/Standard
/// both have _EmissionColor).
/// </summary>
public class GloveBeatGlow : MonoBehaviour
{
    [Header("Glove Renderers (drag the glove objects)")]
    public Renderer[] gloveRenderers;

    [Header("Beat Charge")]
    [ColorUsage(false, true)] public Color chargeColor = new Color(0.0f, 0.85f, 1.0f); // cyan
    public float chargeIntensity = 1.0f;
    [Tooltip("Seconds before the beat the gloves start charging.")]
    public float chargeLead = 0.6f;
    public float chargePulseMax = 12f;

    [Header("Strike / Hurt Bursts")]
    [ColorUsage(false, true)] public Color strikeFlashColor = new Color(1.0f, 0.85f, 0.5f); // warm white-hot
    [ColorUsage(false, true)] public Color hurtFlashColor   = new Color(1.0f, 0.15f, 0.10f); // red
    public float flashIntensity = 4f;
    public float flashDecay     = 6f;

    private Material[] _mats;
    private float _flashTimer;
    private Color _flashColor;
    static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        var list = new List<Material>();
        if (gloveRenderers != null)
            foreach (var r in gloveRenderers)
                if (r != null)
                {
                    r.material.EnableKeyword("_EMISSION"); // r.material auto-instances (no shared-asset edit)
                    list.Add(r.material);
                }
        _mats = list.ToArray();
    }

    public void FlashStrike() => Flash(strikeFlashColor);
    public void FlashHurt()   => Flash(hurtFlashColor);

    private void Flash(Color c)
    {
        _flashColor = c;
        _flashTimer = 1f;
    }

    void Update()
    {
        if (_mats == null || _mats.Length == 0) return;

        var mgr = RhythmRoundManager.Instance;
        bool round = mgr != null && mgr.isRoundActive;

        // Charge ramp toward the next beat.
        float charge = 0f;
        if (round)
        {
            float toBeat = mgr.GetNextBeatTime() - mgr.GetCurrentTrackTime();
            if (toBeat >= 0f && toBeat <= chargeLead && chargeLead > 0.001f)
                charge = 1f - (toBeat / chargeLead);
        }

        if (_flashTimer > 0f)
            _flashTimer = Mathf.Max(0f, _flashTimer - Time.deltaTime * flashDecay);

        Color emis;
        if (_flashTimer > 0f)
        {
            emis = _flashColor * Mathf.Lerp(chargeIntensity * 0.3f, flashIntensity, _flashTimer);
        }
        else if (charge > 0f)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * Mathf.Lerp(3f, chargePulseMax, charge) * Mathf.PI);
            emis = chargeColor * (chargeIntensity * charge * pulse);
        }
        else
        {
            emis = Color.black; // idle: gloves dark
        }

        foreach (var m in _mats)
            if (m != null) m.SetColor(ID_Emission, emis);
    }

    void OnDestroy()
    {
        if (_mats != null)
            foreach (var m in _mats)
                if (m != null) Destroy(m);
    }
}

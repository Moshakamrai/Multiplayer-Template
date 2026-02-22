using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class PlayerEnergy : NetworkBehaviour
{
    [Header("Energy Settings")]
    public float MaxEnergy = 6.0f;
    public float StartingEnergy = 2.0f;
    public float RechargeRate = 0.5f;   // Energy per second
    public float PauseDuration = 1.0f;  // Delay before recharging starts again

    [Header("UI References")]
    public Slider EnergySlider;
    public Text EnergyText;

    public float CurrentEnergy { get; private set; }
    private float _pauseTimer;

    private void Start()
    {
        // Only the local player needs to calculate and see their own energy
        if (!isLocalPlayer)
        {
            this.enabled = false;
            return;
        }
        
        CurrentEnergy = StartingEnergy;

        // Auto-find the UI if you forgot to drag it in the inspector
        if (EnergySlider == null) EnergySlider = GameObject.Find("EnergySlider")?.GetComponent<Slider>();
        if (EnergyText == null) EnergyText = GameObject.Find("EnergyText")?.GetComponent<Text>();
        
        if (EnergySlider != null) EnergySlider.maxValue = MaxEnergy;
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        // 1. Handle the Pause Timer
        if (_pauseTimer > 0)
        {
            _pauseTimer -= Time.deltaTime;
        }
        else
        {
            // 2. Recharge Energy over time
            if (CurrentEnergy < MaxEnergy)
            {
                CurrentEnergy += RechargeRate * Time.deltaTime;
                if (CurrentEnergy > MaxEnergy) CurrentEnergy = MaxEnergy;
            }
        }

        UpdateUI();
    }

    /// <summary>
    /// Checks if the player has enough energy. If they do, deducts the cost, 
    /// pauses the recharge, and returns true so the action can execute.
    /// </summary>
    public bool TryUseEnergy(float cost)
    {
        if (CurrentEnergy >= cost)
        {
            CurrentEnergy -= cost;
            PauseRecharge();
            return true;
        }
        
        return false; // Not enough energy!
    }

    /// <summary>
    /// Forcefully pauses the recharge timer (used when taking damage).
    /// </summary>
    public void PauseRecharge()
    {
        _pauseTimer = PauseDuration;
    }

    private void UpdateUI()
    {
        if (EnergySlider != null) EnergySlider.value = CurrentEnergy;
        
        // "F1" formatting turns 2.50003f into a clean "2.5" on the screen!
        if (EnergyText != null) EnergyText.text = $"{CurrentEnergy.ToString("F1")} / {MaxEnergy}";
    }
}
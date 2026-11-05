using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Simple HUD: a speed readout and a speed-lines 
/// particle effect whose density scales with horizontal speed.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("References")]
    public Rigidbody playerRb;
    public TextMeshProUGUI speedText;
    public ParticleSystem speedLinesParticle;

    [Header("Settings")]
    public float speedMultiplier = 3.6f;
    public string speedUnit = "km/h";
    public float minSpeedToShowLines = 15f; // speed required to start seeing lines
    public float maxSpeed = 30f; // speed at which lines are at maximum density
    public float maxEmissionRate = 150f; // max number of particles

    private ParticleSystem.EmissionModule emissionModule;

    private Vector3 horizontalVelocity;

    /// <summary>
    /// Unity message. Caches the particle system's emission module if a speed-lines system is assigned.
    /// </summary>
    private void Start()
    {
        if (speedLinesParticle != null)
        {
            emissionModule = speedLinesParticle.emission;
        }
    }

    /// <summary>
    /// Unity message. Computes the player's horizontal speed, scales the speed-line emission rate between
    /// <see cref="minSpeedToShowLines"/> and <see cref="maxSpeed"/>, and updates the text with the speed converted by <see cref="speedMultiplier"/>
    /// (3.6 turns m/s into km/h).
    /// </summary>
    void Update()
    {
        if (playerRb == null) return;

        horizontalVelocity = new Vector3(playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        // Calculate a percentage between min and max speed (0.0 to 1.0)
        float speedPercentage = Mathf.InverseLerp(minSpeedToShowLines, maxSpeed, currentSpeed);

        // Apply that percentage to the maximum emission rate
        emissionModule.rateOverTime = speedPercentage * maxEmissionRate;

        if (speedText != null)
        {
            float displaySpeed = currentSpeed * speedMultiplier;
            speedText.text = $"{Mathf.RoundToInt(displaySpeed)} {speedUnit}";
        }
    }
}
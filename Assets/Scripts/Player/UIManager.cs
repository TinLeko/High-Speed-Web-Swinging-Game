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
    [SerializeField] private Rigidbody playerRb;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private ParticleSystem speedLinesParticle;

    [Header("Settings")]
    [SerializeField] private float speedMultiplier = 3.6f;
    [SerializeField] private float minSpeedToShowLines = 15f; // speed required to start seeing lines
    [SerializeField] private float maxSpeed = 30f; // speed at which lines are at maximum density
    [SerializeField] private float maxEmissionRate = 150f; // max number of particles
    private ParticleSystem.EmissionModule emissionModule;

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
        if (playerRb == null) 
            return;

        Vector3 horizontalVelocity = new Vector3(playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);

        // Apply that percentage to the maximum emission rate
        emissionModule.rateOverTime = Mathf.InverseLerp(minSpeedToShowLines, maxSpeed, horizontalVelocity.magnitude) * maxEmissionRate;

        if (speedText != null)
        {
            speedText.text = $"{Mathf.RoundToInt(horizontalVelocity.magnitude * speedMultiplier)} km/h";
        }
    }
}
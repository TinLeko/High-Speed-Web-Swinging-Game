using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Medal awarded for a time trial, ordered from worst to best so the values can be compared numerically.</summary>
public enum TrialMedal { 
    None = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3 
}

/// <summary>
/// Runs the time trials. One instance lives in the scene; it finds the player and creates the UI and the navigation arrow by itself.
/// <see cref="TimeTrialStarter"/> objects in the world talk to it.
/// </summary>
/// <remarks>
/// <para>Flow: touch a starter, answer the prompt (yes/no), countdown, race (fly through the hoops in order), results (time, top speed, medal, best time).</para>
/// <para>Hoop passing is a swept plane test between physics steps, so it works at any speed (no tunnelling through thin triggers).</para>
/// <para>Needs a <see cref="PlayerSwinging"/> in the scene (or <see cref="playerRb"/> assigned). Best times and medals are stored in <see cref="PlayerPrefs"/>.
/// Inputs are the "TrialConfirm", "TrialCancel" and "TrialRespawn" actions, with keyboard fallbacks.</para>
/// </remarks>
public class TimeTrialManager : MonoBehaviour
{
    /// <summary>The single active manager, or <c>null</c> if none exists.</summary>
    public static TimeTrialManager Instance { get; private set; }

    /// <summary>
    /// Raised after the player is teleported (e.g. on respawn). Passes the player transform and the world-space displacement so cameras can follow
    /// (see <c>CameraEffects</c>).
    /// </summary>
    public static event System.Action<Transform, Vector3> PlayerTeleported;

    /// <summary>The phases of a trial.</summary>
    public enum State { Idle, Prompt, Countdown, Racing, Results }

    [Header("References (found automatically if left empty)")]
    public Rigidbody playerRb;
    public TimeTrialUI ui;

    [Header("Input")]
    public Key confirmKey = Key.E;
    public Key cancelKey = Key.Q;
    public Key respawnKey = Key.R;

    [Header("Race")]
    public float countdownSeconds = 3f;
    public float passPadding = 0.6f;

    [Header("Models")]
    public GameObject hoopPrefab;
    public float hoopPrefabRadius = 0f;
    public string customTintProperty = "";
    public GameObject arrowPrefab;
    public float arrowScale = 1.1f;
    public float arrowHeight = 2.8f;

    [Header("Respawn")]
    public float respawnCooldown = 1f;
    public float respawnForwardOffset = 2f;

    [Header("Time limit")]
    public float urgentSeconds = 10f;

    /// <summary>The current phase of the trial.</summary>
    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>Snapshot of one hoop for the pass test: position, orientation (forward is the pass direction) and effective radius.</summary>
    private struct HoopData
    {
        public Vector3 position;
        public Quaternion rotation;
        public float radius;
    }

    private readonly List<HoopData> hoops = new List<HoopData>();
    private readonly List<RaceHoopVisual> hoopVisuals = new List<RaceHoopVisual>();
    private RaceNavigationArrow arrow;
    private PlayerSwinging playerSwinging;
    private Wallrun wallRun;
    private WebZip webZip;
    private InputAction confirmAction, cancelAction, respawnAction;
    private TimeTrialStarter promptStarter, currentStarter;
    private float countdownTimer;
    private float raceStartTime;
    private int nextIndex;
    private float topSpeed;
    private Vector3 previousPosition;
    private float activeLimit;
    private float penaltyTotal;
    private int respawnCount;
    private float nextRespawnTime;
    private bool justRespawned;

    /// <summary>Race time in seconds: time since the race started plus accumulated respawn penalties.</summary>
    private float Elapsed => Time.time - raceStartTime + penaltyTotal;

    // ----------------------------------------------------------------------------- Setup

    /// <summary>
    /// Unity message. Enforces the singleton, finds the player and its <see cref="Wallrun"/> and <see cref="WebZip"/>, finds or creates the
    /// <see cref="TimeTrialUI"/>, creates the <see cref="RaceNavigationArrow"/> and looks up the input actions.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerSwinging = FindAnyObjectByType<PlayerSwinging>();

        if (playerRb == null && playerSwinging != null) 
            playerRb = playerSwinging.GetComponent<Rigidbody>();

        if (playerSwinging == null) 
            playerSwinging = playerRb.GetComponent<PlayerSwinging>();

        wallRun = playerRb.GetComponent<Wallrun>();
        webZip = playerRb.GetComponent<WebZip>();
        

        if (ui == null)
            ui = FindAnyObjectByType<TimeTrialUI>();

        if (ui == null)
            ui = new GameObject("TimeTrialUI").AddComponent<TimeTrialUI>();

        arrow = new GameObject("RaceNavigationArrow").AddComponent<RaceNavigationArrow>();
        arrow.Setup(arrowPrefab, arrowScale, arrowHeight);
        if (playerRb != null) arrow.follow = playerRb.transform;

        confirmAction = FindAction("TrialConfirm");
        cancelAction = FindAction("TrialCancel");
        respawnAction = FindAction("TrialRespawn");
    }

    /// <summary>Unity message. Clears <see cref="Instance"/> if this was the active manager.</summary>
    private void OnDestroy()
    {
        if (Instance == this) 
            Instance = null;
    }

    /// <summary>Finds an input action by name in the project-wide actions.</summary>
    /// <param name="actionName">Name of the action.</param>
    /// <returns>The action, or <c>null</c> if the actions asset or the action is missing.</returns>
    private static InputAction FindAction(string actionName)
    {
        return InputSystem.actions != null ? InputSystem.actions.FindAction(actionName) : null;
    }

    /// <summary>True on the frame the confirm input was pressed.</summary>
    private bool ConfirmPressed => Pressed(confirmAction, confirmKey);

    /// <summary>True on the frame the cancel input was pressed.</summary>
    private bool CancelPressed => Pressed(cancelAction, cancelKey);

    /// <summary>True on the frame the respawn input was pressed.</summary>
    private bool RespawnPressed => Pressed(respawnAction, respawnKey);

    /// <summary>Checks a button press using the input action if it exists, otherwise the fallback keyboard key.</summary>
    /// <param name="action">The input action, may be <c>null</c>.</param>
    /// <param name="fallback">Key used when the action is missing.</param>
    /// <returns><c>true</c> if pressed this frame.</returns>
    private static bool Pressed(InputAction action, Key fallback)
    {
        if (action != null) 
            return action.WasPressedThisFrame();

        return Keyboard.current != null && Keyboard.current[fallback].wasPressedThisFrame;
    }

    // -------------------------------------------------------------- Called by TimeTrialStarter

    /// <summary>Called by a <see cref="TimeTrialStarter"/> when the player touches it. Opens the start prompt if no trial is in progress.</summary>
    /// <param name="starter">The starter that was touched.</param>
    public void OnPlayerEnteredStarter(TimeTrialStarter starter)
    {
        if (CurrentState != State.Idle) 
            return;

        promptStarter = starter;
        CurrentState = State.Prompt;
        ui.ShowPrompt(starter.trialName.ToUpperInvariant(), BuildPromptBody(starter));
    }

    /// <summary>Called by a <see cref="TimeTrialStarter"/> when the player walks away. Closes the prompt if it belongs to that starter.</summary>
    /// <param name="starter">The starter that was left.</param>
    public void OnPlayerLeftStarter(TimeTrialStarter starter)
    {
        if (CurrentState == State.Prompt && promptStarter == starter)
            ClosePrompt();
    }

    // ----------------------------------------------------------------------- State machine

    /// <summary>
    /// Unity message. Runs the state machine: confirm or cancel at the prompt; countdown ticking; while racing, abort, respawn, time-limit check and HUD refresh;
    /// at the results, retry or close.
    /// </summary>
    private void Update()
    {
        switch (CurrentState)
        {
            case State.Prompt:
                if (ConfirmPressed) 
                    StartRace(promptStarter);
                else if (CancelPressed) 
                    ClosePrompt();
                break;

            case State.Countdown:
                if (CancelPressed) 
                { 
                    AbortRace(); 
                    break; 
                }
                countdownTimer -= Time.deltaTime;

                if (countdownTimer <= 0f) 
                    BeginRacing();
                else 
                    ui.ShowCountdown(Mathf.CeilToInt(countdownTimer).ToString());
                break;

            case State.Racing:
                if (CancelPressed) 
                { 
                    AbortRace(); 
                    break; 
                }

                if (RespawnPressed) 
                    TryRespawn();

                if (activeLimit > 0f && Elapsed >= activeLimit)
                { 
                    FailRace(); 
                    break; 
                }

                UpdateHud();
                break;

            case State.Results:
                if (ConfirmPressed) 
                    RestartRace(currentStarter);   // retry
                else if (CancelPressed) 
                    CloseResults();

                break;
        }
    }

    /// <summary>
    /// Unity message. While racing, samples the top speed and tests whether the player crossed the next hoop since the last physics step.
    /// The step right after a respawn only resets the tracked position so the teleport does not count as a crossing.
    /// </summary>
    private void FixedUpdate()
    {
        if (CurrentState != State.Racing || playerRb == null) 
            return;

        if (justRespawned)
        {
            previousPosition = playerRb.position;
            justRespawned = false;
            return;
        }

        // Top speed: full 3D speed, sampled every physics step.
        topSpeed = Mathf.Max(topSpeed, playerRb.linearVelocity.magnitude);

        Vector3 currentRbPosition = playerRb.position;
        if (CrossedHoop(previousPosition, currentRbPosition, hoops[nextIndex]))
            OnHoopPassed();
        previousPosition = currentRbPosition;
    }

    /// <summary>Dismisses the start prompt and returns to idle.</summary>
    private void ClosePrompt()
    {
        CurrentState = State.Idle;
        promptStarter = null;
        ui.HidePrompt();
    }

    /// <summary>Retries a trial: teleports the player back to the starter and starts the race again.</summary>
    /// <param name="starter">The starter to restart.</param>
    private void RestartRace(TimeTrialStarter starter)
    {
        TeleportPlayer(starter.transform.position, Vector3.zero);
        StartRace(starter);
    }

    /// <summary>
    /// Starts a trial: validates the course and hoop prefab, builds the hoops, resets race statistics, sets the time limit, enters the countdown and sets up the HUD.
    /// Does nothing if the course or prefab is missing. Skips the countdown if it is zero seconds.
    /// </summary>
    /// <param name="starter">The starter defining the course, limit and respawn rules.</param>
    private void StartRace(TimeTrialStarter starter)
    {
        if (starter == null || playerRb == null) 
            return;

        if (!starter.HasValidCourse)
        {
            return;
        }

        if (hoopPrefab == null)
        {
            return;
        }

        currentStarter = starter;
        ui.HidePrompt();
        ui.HideResults();

        BuildHoops(starter);
        nextIndex = 0;
        topSpeed = 0f;
        penaltyTotal = 0f;
        respawnCount = 0;
        nextRespawnTime = 0f;
        justRespawned = false;
        activeLimit = Mathf.Max(0f, starter.timeLimit);

        CurrentState = State.Countdown;
        countdownTimer = Mathf.Max(0f, countdownSeconds);

        string hint = "[" + cancelKey + "] Abort race";
        if (starter.allowRespawn)
        {
            hint += "     [" + respawnKey + "] Respawn at checkpoint";
            if (starter.respawnPenalty > 0f) hint += " (+" + Num(starter.respawnPenalty, "0.#") + " s)";
        }
        ui.ShowHud(true, hint);
        ui.SetHud(FormatTime(0f), "HOOP 1/" + hoops.Count);
        ui.SetLimit(activeLimit > 0f ? "TIME LEFT  " + FormatTime(activeLimit) : "", false);

        if (countdownTimer <= 0f) BeginRacing();
    }

    /// <summary>Ends the countdown: enters the racing state, starts the clock, shows "GO!" and highlights the first hoops.</summary>
    private void BeginRacing()
    {
        CurrentState = State.Racing;
        raceStartTime = Time.time;
        previousPosition = playerRb.position;
        ui.FlashMessage("GO!", 0.7f);
        RefreshHoopVisuals();
    }

    /// <summary>Marks the current hoop as collected and advances to the next one, or finishes the race after the last.</summary>
    private void OnHoopPassed()
    {
        if (nextIndex < hoopVisuals.Count && hoopVisuals[nextIndex] != null)
            hoopVisuals[nextIndex].SetState(RaceHoopVisual.VisualState.Collected);

        nextIndex++;
        if (nextIndex >= hoops.Count)
        {
            FinishRace();
            return;
        }

        RefreshHoopVisuals();
    }

    /// <summary>
    /// Completes the race. If the time limit was exceeded it fails instead. Otherwise it works out the medal, saves the best time and best medal,
    /// builds the next-goal text, shows the results, clears the hoops and arrow, and refreshes the starter's marker.
    /// </summary>
    private void FinishRace()
    {
        float time = Elapsed;
        TimeTrialStarter starter = currentStarter;

        if (activeLimit > 0f && time > activeLimit)
        {
            FailRace();
            return;
        }

        CurrentState = State.Results;
        TrialMedal medal = starter.GetMedal(time);

        // save best time and best medal
        string bestKey = BestTimeKey(starter.trialId);
        float previousBest = PlayerPrefs.GetFloat(bestKey, 0f);
        bool newRecord = previousBest <= 0f || time < previousBest;
        float best = newRecord ? time : previousBest;

        if (newRecord)
            PlayerPrefs.SetFloat(bestKey, time);

        string medalKey = BestMedalKey(starter.trialId);

        if ((int)medal > PlayerPrefs.GetInt(medalKey, 0))
            PlayerPrefs.SetInt(medalKey, (int)medal);

        PlayerPrefs.Save();

        string nextGoal;
        switch (medal)
        {
            case TrialMedal.Gold:
                nextGoal = "You earned the top medal!";
                break;
            case TrialMedal.Silver: 
                nextGoal = "Gold: under " + FormatTime(starter.goldTime);
                break;
            case TrialMedal.Bronze:
                nextGoal = "Silver: under " + FormatTime(starter.silverTime);
                break;
            default:
                nextGoal = "Bronze: under " + FormatTime(starter.bronzeTime);
                break;
        }

        string medalName = medal == TrialMedal.None ? "NO MEDAL" : medal.ToString().ToUpperInvariant() + " MEDAL";

        ui.HideCountdown();
        ui.ShowHud(false);
        ui.ShowResults(starter.trialName, FormatTime(time), FormatTime(best), newRecord, TopSpeedText(), RespawnSummary(), medalName, MedalColor(medal), medal != TrialMedal.None, nextGoal, "[" + confirmKey + "] Retry     [" + cancelKey + "] Close");

        arrow.Clear();
        ClearHoops();
        starter.RefreshMarker();
    }

    /// <summary>Ends the race as failed (time ran out) and shows the "did not finish" results with hoop progress and the best time.</summary>
    private void FailRace()
    {
        CurrentState = State.Results;
        TimeTrialStarter starter = currentStarter;

        float best = PlayerPrefs.GetFloat(BestTimeKey(starter.trialId), 0f);
        string bestText = best > 0f ? "Best  " + FormatTime(best) : "No time set yet";

        ui.HideCountdown();
        ui.ShowHud(false);
        ui.ShowFailed(starter.trialName, "Hoops  " + nextIndex + "/" + hoops.Count, bestText, TopSpeedText(), RespawnSummary(), "Time limit was " + FormatTime(activeLimit), "[" + confirmKey + "] Retry     [" + cancelKey + "] Close");

        arrow.Clear();
        ClearHoops();
    }

    /// <summary>Cancels the race without results: hides all UI and removes the hoops and arrow.</summary>
    private void AbortRace()
    {
        CurrentState = State.Idle;
        ui.HideAll();
        arrow.Clear();
        ClearHoops();
    }

    /// <summary>Closes the results panel and returns to idle.</summary>
    private void CloseResults()
    {
        CurrentState = State.Idle;
        ui.HideResults();
    }

    /// <summary>Formats the top speed in m/s and km/h for the results screen.</summary>
    /// <returns>The formatted text.</returns>
    private string TopSpeedText()
    {
        return "Top speed  " + Num(topSpeed, "0.0") + " m/s  (" + Num(topSpeed * 3.6f, "0") + " km/h)";
    }

    /// <summary>Formats the number of respawns and total penalty for the results screen.</summary>
    /// <returns>The summary, or an empty string if the player never respawned.</returns>
    private string RespawnSummary()
    {
        if (respawnCount == 0) 
            return "";

        return "Respawns  " + respawnCount + "  (+" + Num(penaltyTotal, "0.0") + " s)";
    }

    // --------------------------------------------------------------------- Checkpoint respawn

    /// <summary>
    /// Respawns the player at the last passed hoop (launched along its direction at the starter's respawn speed) or at the start if none was passed.
    /// Requires respawning to be allowed and the cooldown to have elapsed. Adds the time penalty and shows a message.
    /// </summary>
    private void TryRespawn()
    {
        TimeTrialStarter starter = currentStarter;

        if (starter == null || !starter.allowRespawn || Time.time < nextRespawnTime) 
            return;

        nextRespawnTime = Time.time + respawnCooldown;

        int checkpoint = nextIndex - 1; // last hoop you passed, -1 is none
        Vector3 position;
        Vector3 velocity;

        if (checkpoint < 0)
        {
            position = starter.StartRespawnPosition;
            velocity = Vector3.zero;
        }
        else
        {
            Vector3 forward = hoops[checkpoint].rotation * Vector3.forward;
            position = hoops[checkpoint].position + forward * respawnForwardOffset;
            velocity = forward * starter.respawnSpeed;
        }

        TeleportPlayer(position, velocity);

        penaltyTotal += Mathf.Max(0f, starter.respawnPenalty);
        respawnCount++;

        string respawnMessage;

        if (checkpoint < 0)
        {
            respawnMessage = "RESPAWNED AT START";
        }
        else
        {
            respawnMessage = "RESPAWNED AT HOOP " + (checkpoint + 1);
        }
        if (starter.respawnPenalty > 0f) respawnMessage += "   +" + Num(starter.respawnPenalty, "0.#") + " s";
        ui.ShowMessage(respawnMessage, 1.5f);
    }

    /// <summary>
    /// Moves the player instantly and drops whatever it was doing (wall run, zip, swing, dive). Resets the Rigidbody, syncs physics, flags the next
    /// physics step as a respawn and raises <see cref="PlayerTeleported"/>.
    /// </summary>
    /// <param name="position">Destination in world space.</param>
    /// <param name="velocity">Velocity to start with.</param>
    private void TeleportPlayer(Vector3 position, Vector3 velocity)
    {
        Vector3 delta = position - playerRb.position;

        // drop whate the player was doing: wall run, zip, swing, dive
        if (wallRun != null && wallRun.IsActive) wallRun.Cancel();
        if (webZip != null) webZip.CancelForRespawn();
        if (playerSwinging != null) playerSwinging.ResetForRespawn(velocity);
        else playerRb.linearVelocity = velocity;

        playerRb.position = position;
        playerRb.transform.position = position;
        playerRb.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();

        previousPosition = position;
        justRespawned = true;

        PlayerTeleported?.Invoke(playerRb.transform, delta);
    }

    // ----------------------------------------------------------------------------- Hoops

    /// <summary>Clears old hoops and creates the hoop data and visual for each course point. The radius is the starter's hoop radius times the point's scale.</summary>
    /// <param name="starter">The starter whose course to build.</param>
    private void BuildHoops(TimeTrialStarter starter)
    {
        ClearHoops();

        foreach (Transform point in starter.hoopPoints)
        {
            if (point == null)
                continue;

            float radius = starter.hoopRadius * Mathf.Max(0.01f, point.lossyScale.x);
            hoops.Add(new HoopData { position = point.position, rotation = point.rotation, radius = radius });

            var hoopObject = new GameObject("RaceHoop_" + hoops.Count);
            hoopObject.transform.position = point.position;
            var visual = hoopObject.AddComponent<RaceHoopVisual>();
            visual.Init(radius, point.rotation, hoopPrefab, hoopPrefabRadius, customTintProperty);
            hoopVisuals.Add(visual);
        }
    }

    /// <summary>Destroys the hoop visuals and clears the hoop data. Hoops already fading out are left to remove themselves.</summary>
    private void ClearHoops()
    {
        // Hoops that are already fading out remove themselves.
        foreach (RaceHoopVisual visual in hoopVisuals)
        {
            if (visual != null && visual.StateNow != RaceHoopVisual.VisualState.Collected)
                Destroy(visual.gameObject);
        }
        hoopVisuals.Clear();
        hoops.Clear();
    }

    /// <summary>Updates each hoop's state: passed hoops collected, next hoop active, the one after previewed, the rest hidden. Points the arrow at the next hoop.</summary>
    private void RefreshHoopVisuals()
    {
        for (int i=0; i < hoopVisuals.Count; i++)
        {
            RaceHoopVisual visual = hoopVisuals[i];
            if (visual == null) 
                continue;

            if (i < nextIndex)
            {
                if (visual.StateNow != RaceHoopVisual.VisualState.Collected)
                    visual.SetState(RaceHoopVisual.VisualState.Collected);
            }
            else if (i == nextIndex) 
                visual.SetState(RaceHoopVisual.VisualState.Active);
            else if (i == nextIndex + 1) 
                visual.SetState(RaceHoopVisual.VisualState.Preview);
            else 
                visual.SetState(RaceHoopVisual.VisualState.Hidden);
        }

        if (nextIndex < hoops.Count)
            arrow.SetTarget(hoops[nextIndex].position);
    }

    /// <summary>
    /// Swept test for passing through a hoop. Checks that the movement from <paramref name="from"/> to <paramref name="to"/> crosses the hoop's plane
    /// and that the crossing point lies within the hoop radius plus <see cref="passPadding"/>. Direction of travel does not matter.
    /// </summary>
    /// <param name="from">Position at the previous physics step.</param>
    /// <param name="to">Position at the current physics step.</param>
    /// <param name="hoop">The hoop to test.</param>
    /// <returns><c>true</c> if the hoop was passed.</returns>
    private bool CrossedHoop(Vector3 from, Vector3 to, HoopData hoop)
    {
        Vector3 normal = hoop.rotation * Vector3.forward;
        float dot1 = Vector3.Dot(from - hoop.position, normal);
        float dot2 = Vector3.Dot(to - hoop.position, normal);

        if (dot1 * dot2 > 0f) 
            return false;

        Vector3 crossing;

        if (Mathf.Approximately(dot1, dot2)) 
            crossing = to;
        else 
            crossing = Vector3.Lerp(from, to, dot1 / (dot1 - dot2));

        return Vector3.Distance(crossing, hoop.position) <= hoop.radius + passPadding;
    }

    // ---------------------------------------------------------------------------------- HUD

    /// <summary>Refreshes the HUD with the race time, hoop progress, distance to the next hoop and, if there is a limit, the time left (urgent when low).</summary>
    private void UpdateHud()
    {
        float elapsed = Elapsed;
        float distance = Vector3.Distance(playerRb.position, hoops[nextIndex].position);
        float speed = playerRb.linearVelocity.magnitude;

        ui.SetHud(FormatTime(elapsed), "HOOP " + (nextIndex + 1) + "/" + hoops.Count + "   |   " + Num(distance, "0") + " m");

        if (activeLimit > 0f)
        {
            float left = Mathf.Max(0f, activeLimit - elapsed);
            ui.SetLimit("TIME LEFT  " + FormatTime(left), left <= urgentSeconds);
        }
    }

    /// <summary>Builds the start prompt text: best time, medal times, rules (time limit and respawn) and the confirm/cancel keys.</summary>
    /// <param name="starter">The starter to describe.</param>
    /// <returns>The multi-line prompt text.</returns>
    private string BuildPromptBody(TimeTrialStarter starter)
    {
        float personalBest = PlayerPrefs.GetFloat(BestTimeKey(starter.trialId), 0f);

        string bestTimeLine;
        if (personalBest > 0f)
        {
            bestTimeLine = "Your best: " + FormatTime(personalBest);
        }
        else
        {
            bestTimeLine = "No time set yet";
        }

        var rulesToShow = new List<string>();
        if (starter.timeLimit > 0f)
            rulesToShow.Add("Time limit " + FormatTime(starter.timeLimit));
        if (!starter.allowRespawn)
            rulesToShow.Add("No respawns");
        else if (starter.respawnPenalty > 0f)
            rulesToShow.Add("Respawn [" + respawnKey + "]  +" + Num(starter.respawnPenalty, "0.#") + " s");
        else 
            rulesToShow.Add("Respawn [" + respawnKey + "]  free");

        return bestTimeLine + "\n" + "Gold " + FormatTime(starter.goldTime) + "     Silver " + FormatTime(starter.silverTime) + "     Bronze " + FormatTime(starter.bronzeTime) + "\n" + string.Join("     ", rulesToShow) + "\n\n" +
               "Start the race?\n" + "[" + confirmKey + "] Yes          [" + cancelKey + "] No";
    }

    // --------------------------------------------------------------------------- Helpers

    /// <summary>Gets the <see cref="PlayerPrefs"/> key that stores the best time for a trial.</summary>
    /// <param name="trialId">The unique trial id.</param>
    /// <returns>The key.</returns>
    public static string BestTimeKey(string trialId) => "TimeTrial_BestTime_" + trialId;

    /// <summary>Gets the <see cref="PlayerPrefs"/> key that stores the best medal for a trial.</summary>
    /// <param name="trialId">The unique trial id.</param>
    /// <returns>The key.</returns>
    public static string BestMedalKey(string trialId) => "TimeTrial_BestMedal_" + trialId;

    /// <summary>Gets the display colour for a medal (gold, silver, bronze, or grey for none).</summary>
    /// <param name="medal">The medal.</param>
    /// <returns>The colour.</returns>
    public static Color MedalColor(TrialMedal medal)
    {
        switch (medal)
        {
            case TrialMedal.Gold:
                return new Color(1f, 0.84f, 0f, 1f);
            case TrialMedal.Silver:
                return new Color(0.8f, 0.84f, 0.9f, 1f);
            case TrialMedal.Bronze:
                return new Color(0.82f, 0.5f, 0.2f, 1f);
            default:
                return new Color(0.7f, 0.7f, 0.7f, 1f);
        }
    }

    /// <summary>Formats seconds as <c>m:ss.mmm</c> using the invariant culture. Negative values are treated as zero.</summary>
    /// <param name="seconds">Time in seconds.</param>
    /// <returns>The formatted time.</returns>
    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a number with the invariant culture so text does not change with the system locale.</summary>
    /// <param name="value">The number.</param>
    /// <param name="format">A numeric format string.</param>
    /// <returns>The formatted number.</returns>
    private static string Num(float value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}

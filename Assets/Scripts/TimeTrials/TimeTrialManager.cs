using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TrialMedal { 
    None = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3 
}

public class TimeTrialManager : MonoBehaviour
{
    public static TimeTrialManager Instance { get; private set; }
    public static event System.Action<Transform, Vector3> PlayerTeleported;
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

    public State CurrentState { get; private set; } = State.Idle;

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

    private float Elapsed => Time.time - raceStartTime + penaltyTotal;

    // setup

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerSwinging = FindAnyObjectByType<PlayerSwinging>();
        if (playerRb == null && playerSwinging != null) playerRb = playerSwinging.GetComponent<Rigidbody>();

        if (playerSwinging == null) playerSwinging = playerRb.GetComponent<PlayerSwinging>();
        wallRun = playerRb.GetComponent<Wallrun>();
        webZip = playerRb.GetComponent<WebZip>();
        

        if (ui == null) ui = FindAnyObjectByType<TimeTrialUI>();
        if (ui == null) ui = new GameObject("TimeTrialUI").AddComponent<TimeTrialUI>();

        arrow = new GameObject("RaceNavigationArrow").AddComponent<RaceNavigationArrow>();
        arrow.Setup(arrowPrefab, arrowScale, arrowHeight);
        if (playerRb != null) arrow.follow = playerRb.transform;

        confirmAction = FindAction("TrialConfirm");
        cancelAction = FindAction("TrialCancel");
        respawnAction = FindAction("TrialRespawn");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static InputAction FindAction(string actionName)
    {
        return InputSystem.actions != null ? InputSystem.actions.FindAction(actionName) : null;
    }

    private bool ConfirmPressed => Pressed(confirmAction, confirmKey);
    private bool CancelPressed => Pressed(cancelAction, cancelKey);
    private bool RespawnPressed => Pressed(respawnAction, respawnKey);

    private static bool Pressed(InputAction action, Key fallback)
    {
        if (action != null) return action.WasPressedThisFrame();
        return Keyboard.current != null && Keyboard.current[fallback].wasPressedThisFrame;
    }

    // called by TimeTrialStarter

    public void OnPlayerEnteredStarter(TimeTrialStarter starter)
    {
        if (CurrentState != State.Idle) return;

        promptStarter = starter;
        CurrentState = State.Prompt;
        ui.ShowPrompt(starter.trialName.ToUpperInvariant(), BuildPromptBody(starter));
    }

    public void OnPlayerLeftStarter(TimeTrialStarter starter)
    {
        if (CurrentState == State.Prompt && promptStarter == starter)
            ClosePrompt();
    }

    // state machine

    private void Update()
    {
        switch (CurrentState)
        {
            case State.Prompt:
                if (ConfirmPressed) StartRace(promptStarter);
                else if (CancelPressed) ClosePrompt();
                break;

            case State.Countdown:
                if (CancelPressed) { AbortRace(); break; }
                countdownTimer -= Time.deltaTime;
                if (countdownTimer <= 0f) BeginRacing();
                else ui.ShowCountdown(Mathf.CeilToInt(countdownTimer).ToString());
                break;

            case State.Racing:
                if (CancelPressed) { AbortRace(); break; }
                if (RespawnPressed) TryRespawn();
                if (activeLimit > 0f && Elapsed >= activeLimit) { FailRace(); break; }
                UpdateHud();
                break;

            case State.Results:
                if (ConfirmPressed) RestartRace(currentStarter);   // retry
                else if (CancelPressed) CloseResults();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (CurrentState != State.Racing || playerRb == null) return;

        if (justRespawned)
        {
            previousPosition = playerRb.position;
            justRespawned = false;
            return;
        }

        topSpeed = Mathf.Max(topSpeed, playerRb.linearVelocity.magnitude);

        Vector3 current = playerRb.position;
        if (CrossedHoop(previousPosition, current, hoops[nextIndex]))
            OnHoopPassed();
        previousPosition = current;
    }

    private void ClosePrompt()
    {
        CurrentState = State.Idle;
        promptStarter = null;
        ui.HidePrompt();
    }

    private void RestartRace(TimeTrialStarter starter)
    {
        TeleportPlayer(starter.transform.position, Vector3.zero);
        StartRace(starter);
    }

    private void StartRace(TimeTrialStarter starter)
    {
        if (starter == null || playerRb == null) return;

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
        ui.SetHud(FormatTime(0f), "HOOP 1/" + hoops.Count, "0 m/s");
        ui.SetLimit(activeLimit > 0f ? "TIME LEFT  " + FormatTime(activeLimit) : "", false);

        if (countdownTimer <= 0f) BeginRacing();
    }

    private void BeginRacing()
    {
        CurrentState = State.Racing;
        raceStartTime = Time.time;
        previousPosition = playerRb.position;
        ui.FlashMessage("GO!", 0.7f);
        RefreshHoopVisuals();
    }

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
        if (newRecord) PlayerPrefs.SetFloat(bestKey, time);

        string medalKey = BestMedalKey(starter.trialId);
        if ((int)medal > PlayerPrefs.GetInt(medalKey, 0)) PlayerPrefs.SetInt(medalKey, (int)medal);
        PlayerPrefs.Save();

        string nextGoal;
        switch (medal)
        {
            case TrialMedal.Gold:   nextGoal = "You earned the top medal!"; break;
            case TrialMedal.Silver: nextGoal = "Gold: under " + FormatTime(starter.goldTime); break;
            case TrialMedal.Bronze: nextGoal = "Silver: under " + FormatTime(starter.silverTime); break;
            default:                nextGoal = "Bronze: under " + FormatTime(starter.bronzeTime); break;
        }

        string medalName = medal == TrialMedal.None ? "NO MEDAL" : medal.ToString().ToUpperInvariant() + " MEDAL";

        ui.HideCountdown();
        ui.ShowHud(false);
        ui.ShowResults(starter.trialName, FormatTime(time), FormatTime(best), newRecord, TopSpeedText(), RespawnSummary(), medalName, MedalColor(medal), medal != TrialMedal.None, nextGoal, "[" + confirmKey + "] Retry     [" + cancelKey + "] Close");

        arrow.Clear();
        ClearHoops();
        starter.RefreshMarker();
    }

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

    private void AbortRace()
    {
        CurrentState = State.Idle;
        ui.HideAll();
        arrow.Clear();
        ClearHoops();
    }

    private void CloseResults()
    {
        CurrentState = State.Idle;
        ui.HideResults();
    }

    private string TopSpeedText()
    {
        return "Top speed  " + Num(topSpeed, "0.0") + " m/s  (" + Num(topSpeed * 3.6f, "0") + " km/h)";
    }

    private string RespawnSummary()
    {
        if (respawnCount == 0) return "";
        return "Respawns  " + respawnCount + "  (+" + Num(penaltyTotal, "0.0") + " s)";
    }

    // checkpoint respawn

    private void TryRespawn()
    {
        TimeTrialStarter starter = currentStarter;
        if (starter == null || !starter.allowRespawn || Time.time < nextRespawnTime) return;
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

    // hoops

    private void BuildHoops(TimeTrialStarter starter)
    {
        ClearHoops();

        foreach (Transform point in starter.hoopPoints)
        {
            if (point == null) continue;

            float radius = starter.hoopRadius * Mathf.Max(0.01f, point.lossyScale.x);
            hoops.Add(new HoopData { position = point.position, rotation = point.rotation, radius = radius });

            var hoopObject = new GameObject("RaceHoop_" + hoops.Count);
            hoopObject.transform.position = point.position;
            var visual = hoopObject.AddComponent<RaceHoopVisual>();
            visual.Init(radius, point.rotation, hoopPrefab, hoopPrefabRadius, customTintProperty);
            hoopVisuals.Add(visual);
        }
    }

    private void ClearHoops()
    {
        foreach (RaceHoopVisual visual in hoopVisuals)
        {
            if (visual != null && visual.StateNow != RaceHoopVisual.VisualState.Collected)
                Destroy(visual.gameObject);
        }
        hoopVisuals.Clear();
        hoops.Clear();
    }

    private void RefreshHoopVisuals()
    {
        for (int i=0; i < hoopVisuals.Count; i++)
        {
            RaceHoopVisual visual = hoopVisuals[i];
            if (visual == null) continue;

            if (i < nextIndex)
            {
                if (visual.StateNow != RaceHoopVisual.VisualState.Collected)
                    visual.SetState(RaceHoopVisual.VisualState.Collected);
            }
            else if (i == nextIndex) visual.SetState(RaceHoopVisual.VisualState.Active);
            else if (i == nextIndex + 1) visual.SetState(RaceHoopVisual.VisualState.Preview);
            else visual.SetState(RaceHoopVisual.VisualState.Hidden);
        }

        if (nextIndex < hoops.Count)
            arrow.SetTarget(hoops[nextIndex].position);
    }

    private bool CrossedHoop(Vector3 from, Vector3 to, HoopData hoop)
    {
        Vector3 normal = hoop.rotation * Vector3.forward;
        float d0 = Vector3.Dot(from - hoop.position, normal);
        float d1 = Vector3.Dot(to - hoop.position, normal);

        if (d0 * d1 > 0f) return false;

        Vector3 crossing;
        if (Mathf.Approximately(d0, d1)) crossing = to;
        else crossing = Vector3.Lerp(from, to, d0 / (d0 - d1));

        return Vector3.Distance(crossing, hoop.position) <= hoop.radius + passPadding;
    }

    // HUD

    private void UpdateHud()
    {
        float elapsed = Elapsed;
        Vector3 hoopPosition = hoops[nextIndex].position;
        float distance = Vector3.Distance(playerRb.position, hoopPosition);
        float speed = playerRb.linearVelocity.magnitude;

        ui.SetHud(FormatTime(elapsed),
                  "HOOP " + (nextIndex + 1) + "/" + hoops.Count + "   |   " + Num(distance, "0") + " m",
                  Num(speed, "0") + " m/s");

        if (activeLimit > 0f)
        {
            float left = Mathf.Max(0f, activeLimit - elapsed);
            ui.SetLimit("TIME LEFT  " + FormatTime(left), left <= urgentSeconds);
        }
    }

    private string BuildPromptBody(TimeTrialStarter starter)
    {
        float best = PlayerPrefs.GetFloat(BestTimeKey(starter.trialId), 0f);
        string bestLine = best > 0f ? "Your best: " + FormatTime(best) : "No time set yet";

        var rules = new List<string>();
        if (starter.timeLimit > 0f) rules.Add("Time limit " + FormatTime(starter.timeLimit));
        if (!starter.allowRespawn) rules.Add("No respawns");
        else if (starter.respawnPenalty > 0f) rules.Add("Respawn [" + respawnKey + "]  +" + Num(starter.respawnPenalty, "0.#") + " s");
        else rules.Add("Respawn [" + respawnKey + "]  free");

        return bestLine + "\n" + "Gold " + FormatTime(starter.goldTime) + "     Silver " + FormatTime(starter.silverTime) + "     Bronze " + FormatTime(starter.bronzeTime) + "\n" + string.Join("     ", rules) + "\n\n" +
               "Start the race?\n" + "[" + confirmKey + "] Yes          [" + cancelKey + "] No";
    }

    public static string BestTimeKey(string trialId) => "TimeTrial_BestTime_" + trialId;
    public static string BestMedalKey(string trialId) => "TimeTrial_BestMedal_" + trialId;

    public static Color MedalColor(TrialMedal medal)
    {
        switch (medal)
        {
            case TrialMedal.Gold:   return new Color(1f, 0.84f, 0f, 1f);
            case TrialMedal.Silver: return new Color(0.8f, 0.84f, 0.9f, 1f);
            case TrialMedal.Bronze: return new Color(0.82f, 0.5f, 0.2f, 1f);
            default:                return new Color(0.7f, 0.7f, 0.7f, 1f);
        }
    }

    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture);
    }

    private static string Num(float value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}

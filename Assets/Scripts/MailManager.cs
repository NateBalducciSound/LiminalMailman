using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal.Internal;
using UnityEngine.UI;

public class MailManager : MonoBehaviour
{
    public static MailManager Instance;

    [Header("Difficulty")]
    // Time the player gets for each delivery
    public float baseDeliveryTime = 20f;
    // Floor for the delivery timer
    public float minDeliveryTime = 8f;
    [Tooltip("Each delivery completed reduces the timer by this much")]
    public float timeReductionPerDelivery = 1.5f;
    [Tooltip("Max random deviation (+/- seconds) applied to each delivery's timer, so timers within a set differ")]
    public float timerVariation = 2f;
    [Tooltip("Deliveries completed before objective count starts increasing")]
    public int difficultyRampStart = 3;
    [Tooltip("One extra objective added every N deliveries after ramp start")]
    public int deliveriesPerExtraObjective = 5;

    [Header("Scoring")]
    //points awarded for every successful delivery
    public int basePointsPerDelivery = 0;
    // Extra points awarded for a delivery made instantly
    public int maxSpeedBonus = 0;
    // Score the player starts with
    public int baseScore = 100;
    // Points lost whenever a delivery expires
    public int strikePenalty = 20;

    [Header("Fail")]
    //old strike system
    int maxStrikes = 3;

    [Header("Win")]
    [Tooltip("Number of delivery sets that must be finished to win the day")]
    public int dailyMax = 5;

    [Header("Interaction")]
    public float interactDistance = 3f;
    //visual model for mail
    public GameObject mailPointVis;

    [Header("UI")]
    public TextMeshProUGUI interactPrompt;
    // Container panel for the list of active deliveries
    public GameObject objectivesPanel;
    // Parent transform that objective entries get spawned under (e.g. a vertical layout group).
    public Transform objectiveListParent;
    // Prefab for one row in the objectives list. Expected to contain a TextMeshProUGUI and an Image (radial timer).
    public GameObject objectiveEntryPrefab;
    public TextMeshProUGUI scoreText;
    // Shows one "X" per strike taken, padded with spaces up to maxStrikes.
    public TextMeshProUGUI strikesText;
    // Optional. Shows progress toward the daily goal, e.g. "Sets: 2/5".
    public TextMeshProUGUI setsText;
    // 3D/UI arrow that points toward the most urgent delivery. Optional.
    public Transform arrowTransform;
    // Shown when the player runs out of score. Optional.
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;
    // Shown when the player completes dailyMax sets. Optional. Works just like the game over panel.
    public GameObject winPanel;
    public TextMeshProUGUI winScoreText;
    public Slider scoreSlider;

    // Every delivery the player is currently responsible for, each with its own timer and UI row.
    private readonly List<ActiveDelivery> activeDeliveries = new();
    // The MailPoint the player is currently looking at AND is allowed to interact with. Null if none.
    private MailPoint currentTarget;
    // Lifetime count of successful deliveries. Drives difficulty scaling.
    private int totalDeliveriesCompleted;
    // How many full sets (batches of mail) the player has finished. Compared against dailyMax to win.
    private int setsCompleted;
    private int strikes;
    private int score;
    // When true, Update stops doing anything (the run is over, by winning or losing).
    private bool gameOver;
    // Ever-increasing counter used only to label entries as "Mail #1", "Mail #2", etc.
    private int mailCounter;

    // Cached list of every Deliver-type MailPoint in the scene, built once in Start.
    private MailPoint[] allDeliverPoints;

    // Register the singleton as early as possible so other scripts can use Instance in their own Start.
    void Awake() => Instance = this;

    void Start()
    {
        // Cache delivery points once; searching the scene every time we pick targets would be wasteful.
        allDeliverPoints = FindDeliverPoints();
        //sets score to baseline
        score = baseScore;

        // Start with all the dynamic UI hidden. It gets shown when mail is received.
        interactPrompt.gameObject.SetActive(false);
        objectivesPanel.SetActive(false);
        if (arrowTransform) arrowTransform.gameObject.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (winPanel) winPanel.SetActive(false);

        // Draw the initial score, strikes and set counter.
        RefreshUI();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Freeze all gameplay logic once the game has ended.
        if (gameOver) return;

        // Order matters: the raycast decides currentTarget, which the interact step then reads.
        HandleRaycast();
        HandleInteract();
        TickDeliveries();
        HandleArrow();
        HandleNewTasks();

    }

    // ------------------------------------------------------------------
    // Interaction
    // ------------------------------------------------------------------

    /// <summary>
    /// Casts a ray from the center of the screen (the crosshair) and works out whether the player
    /// is looking at a MailPoint they can currently use. Updates currentTarget and the prompt text.
    /// </summary>
    void HandleRaycast()
    {
        // Ray through the middle of the screen, i.e. where the player is looking.
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            // Only objects with a MailPoint component are interactable.
            MailPoint point = hit.collider.GetComponent<MailPoint>();
            if (point != null)
            {
                // Mail can only be picked up when the player has nothing in progress,
                // so the player can't stack up deliveries by spamming the receive point.
                bool canReceive = point.pointType == MailPointType.Receive && activeDeliveries.Count == 0;
                // A Deliver point is only usable if it's one of the current objectives.
                bool canDeliver = point.pointType == MailPointType.Deliver && IsActiveTarget(point);

                if (canReceive || canDeliver)
                {
                    interactPrompt.text = canReceive ? "Press E to receive mail" : "Press E to deliver mail";
                    interactPrompt.gameObject.SetActive(true);
                    currentTarget = point;
                    return; // Found something valid, so skip the "nothing here" reset below.
                }
            }
        }

        // Nothing valid in range or in view: clear the target and hide the prompt.
        currentTarget = null;
        interactPrompt.gameObject.SetActive(false);
    }

    void HandleNewTasks()
    {
        if(activeDeliveries.Count == 0)
        {
            mailPointVis.SetActive(true);
        }
        
    }
    /// <summary>
    /// Reacts to the E key when the player is looking at a valid MailPoint.
    /// Uses the new Input System (Keyboard.current) rather than the legacy Input class.
    /// </summary>
    void HandleInteract()
    {
        if (currentTarget == null) return;
        // wasPressedThisFrame makes this fire once per key press, not every frame the key is held.
        if (!Keyboard.current.eKey.wasPressedThisFrame) return;

        if (currentTarget.pointType == MailPointType.Receive)
            ReceiveMail();
        else if (currentTarget.pointType == MailPointType.Deliver)
            DeliverMail(currentTarget);
    }

    // ------------------------------------------------------------------
    // Receiving and delivering
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts a new "batch" (set) of deliveries. Decides how many objectives to hand out and how much time
    /// each gets (both depend on progress), picks random destinations, and creates a UI row for each.
    /// </summary>
    void ReceiveMail()
    {
        int count = ComputeObjectiveCount();
        // Base timer for this set. Each delivery then gets its own random offset from this value.
        float baseTimer = ComputeDeliveryTime();
        List<MailPoint> targets = PickRandomDeliverPoints(count);

        foreach (MailPoint target in targets)
        {
            mailCounter++;

            // Spawn a UI row for this delivery. If the prefab or parent isn't assigned we still
            // track the delivery, just without a visible entry (null is handled everywhere else).
            var entry = objectiveEntryPrefab && objectiveListParent
                ? Instantiate(objectiveEntryPrefab, objectiveListParent)
                : null;

            // Randomize this delivery's timer within +/- timerVariation seconds of the base.
            // Clamped to at least 1 second so a large variation can never create an instantly-expiring delivery.
            float timer = Mathf.Max(1f, baseTimer + Random.Range(-timerVariation, timerVariation));

            activeDeliveries.Add(new ActiveDelivery(target, timer, mailCounter, entry));
        }

        // Show the objective list and arrow now that there's something to deliver.
        objectivesPanel.SetActive(activeDeliveries.Count > 0);
        if (arrowTransform) arrowTransform.gameObject.SetActive(activeDeliveries.Count > 0);
        mailPointVis.SetActive(false);
    }

    /// <summary>
    /// Completes the delivery for the given target: awards points, counts the delivery toward difficulty,
    /// removes it from the active list, and checks whether that finished the whole set.
    /// </summary>
    void DeliverMail(MailPoint target)
    {
        // Look up which active delivery this point belongs to. Should always exist because
        // HandleRaycast only allows interaction with active targets, but guard anyway.
        ActiveDelivery delivery = activeDeliveries.Find(d => d.Target == target);
        if (delivery == null) return;

        // Speed bonus scales linearly with the fraction of time left:
        // delivering instantly gives the full bonus, delivering at the last moment gives ~0.
        int speedBonus = Mathf.RoundToInt(maxSpeedBonus * (delivery.Timer / delivery.MaxTimer));
        score += basePointsPerDelivery + speedBonus;
        totalDeliveriesCompleted++;

        RemoveDelivery(delivery);
        RefreshUI();
        CheckSetFinished();
    }

    /// <summary>
    /// Called after any delivery is resolved (delivered or expired). If nothing is left in the active list,
    /// the set is over: bump the set counter and trigger the win if the daily goal has been reached.
    /// A set counts as finished even if some of its deliveries expired; the score penalty already punishes those.
    /// </summary>
    void CheckSetFinished()
    {
        if (activeDeliveries.Count > 0) return;

        setsCompleted++;
        RefreshUI();

        if (setsCompleted >= dailyMax)
            TriggerWin();
    }

    // ------------------------------------------------------------------
    // Timers and failure
    // ------------------------------------------------------------------

    /// <summary>
    /// Counts down every active delivery, updates its UI row, and handles expiry (score penalty / game over).
    /// Also keeps the objectives panel and arrow visibility in sync with whether anything is active.
    /// </summary>
    void TickDeliveries()
    {
        // Iterate backwards so removing an item mid-loop doesn't skip or break the iteration.
        for (int i = activeDeliveries.Count - 1; i >= 0; i--)
        {
            ActiveDelivery d = activeDeliveries[i];
            d.Timer -= Time.deltaTime;
            d.UpdateEntryUI();

            if (d.Timer <= 0f)
            {
                // Missed delivery: costs points and the delivery disappears.
                // Note that expired deliveries do NOT count toward totalDeliveriesCompleted.
                score -= strikePenalty;
                RemoveDelivery(d);
                RefreshUI();

                // Losing takes priority over finishing the set: if this expiry zeroed the score, the run is lost.
                if (score <= 0)
                {
                    TriggerGameOver();
                    return; // Stop immediately; the list was just cleared and the UI hidden.
                }

                // If that was the last delivery in the set, count the set (this may trigger the win).
                CheckSetFinished();
                if (gameOver) return; // Won the game; the list and UI have already been cleaned up.
            }
        }

        // Keep UI visibility matching reality. This also hides the panel after the last delivery
        // is completed or expires, so the player can go pick up more mail.
        bool hasActive = activeDeliveries.Count > 0;
        objectivesPanel.SetActive(hasActive);
        if (arrowTransform) arrowTransform.gameObject.SetActive(hasActive);
    }

    // ------------------------------------------------------------------
    // Navigation arrow
    // ------------------------------------------------------------------

    // Arrow points at whichever active delivery has the least time remaining
    void HandleArrow()
    {
        if (arrowTransform == null || activeDeliveries.Count == 0) return;

        // Linear scan for the delivery with the smallest remaining timer (the most urgent one).
        ActiveDelivery urgent = activeDeliveries[0];
        for (int i = 1; i < activeDeliveries.Count; i++)
            if (activeDeliveries[i].Timer < urgent.Timer) urgent = activeDeliveries[i];

        // Direction from the arrow to the target's position in world space.
        Vector3 dir = urgent.Target.transform.position - arrowTransform.position;

        // LookRotation errors on a zero vector, so skip if the arrow is exactly on the target.
        // The extra 180 degree Y rotation compensates for the arrow model's forward axis facing
        // the opposite way (i.e. the mesh points backwards by default).
        if (dir != Vector3.zero)
            arrowTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 180f, 0f);
    }


    // True if this MailPoint is the destination of any delivery the player currently holds
    bool IsActiveTarget(MailPoint point) => activeDeliveries.Exists(d => d.Target == point);


    /// How many objectives to hand out for the next batch.
    int ComputeObjectiveCount()
    {
        if (totalDeliveriesCompleted < difficultyRampStart)
            return Random.Range(1, 3);

        //gives "steps" of difficulty since the ramp began
        int extra = (totalDeliveriesCompleted - difficultyRampStart) / deliveriesPerExtraObjective;
        // Can't assign more objectives than there are destinations in scene
        return Mathf.Min(2 + extra, allDeliverPoints.Length);
    }

    /// Base time allowed per delivery in the next batch
    float ComputeDeliveryTime()
    {
        return Mathf.Max(minDeliveryTime, baseDeliveryTime - totalDeliveriesCompleted * timeReductionPerDelivery);
    }


    /// Picks up to 'count' distinct random Deliver points, excluding ones that are already active targets.
    List<MailPoint> PickRandomDeliverPoints(int count)
    {
        // Work on a copy so the cached allDeliverPoints array is never modified
        List<MailPoint> pool = new(allDeliverPoints);
        pool.RemoveAll(p => activeDeliveries.Exists(d => d.Target == p));

        // Draw without replacement: pick a random index, then remove it
        List<MailPoint> result = new();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            result.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return result;
    }

    /// Scans the scene once for every MailPoint and keeps only the Deliver ones.
    MailPoint[] FindDeliverPoints()
    {
        // SortMode. None skips sorting for speed
        MailPoint[] all = FindObjectsByType<MailPoint>(FindObjectsSortMode.None);
        List<MailPoint> deliver = new();
        foreach (MailPoint p in all)
            if (p.pointType == MailPointType.Deliver) deliver.Add(p);
        return deliver.ToArray();
    }


    /// Removes a delivery from the active list
    /// Always use this instead of removing from the list directly
    void RemoveDelivery(ActiveDelivery d)
    {
        if (d.EntryObject) Destroy(d.EntryObject);
        activeDeliveries.Remove(d);
    }


    /// Redraws the score
    void RefreshUI()
    {
        if (scoreText) scoreText.text = score.ToString();
        if (setsText) setsText.text = "Completed Delivery sets: " + setsCompleted + "/" + dailyMax;

    }


    /// gamopver :(
    void TriggerGameOver()
    {
        EndRun(gameOverPanel, gameOverScoreText);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }


    /// win

    void TriggerWin()
    {
        EndRun(winPanel, winScoreText);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }


    /// Shared end-of-run logic
    void EndRun(GameObject endPanel, TextMeshProUGUI endScoreText)
    {
        gameOver = true; //stop running game logic from the next frame

        // Destroy all leftover UI rows
        foreach (ActiveDelivery d in activeDeliveries)
            if (d.EntryObject) Destroy(d.EntryObject);
        activeDeliveries.Clear();

        // Hide all in-game HUD elements.
        objectivesPanel.SetActive(false);
        interactPrompt.gameObject.SetActive(false);
        if (arrowTransform) arrowTransform.gameObject.SetActive(false);

        if (endPanel)
        {
            endPanel.SetActive(true);
            if (endScoreText) endScoreText.text = score.ToString();
        }
    }


    public void Restart()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }


    /// Plain data holder for one in-progress delivery: where it's going, how long is left,
    /// and the UI row that displays it. Not a MonoBehaviour, so it's created with 'new'.
    class ActiveDelivery
    {
        // Destination MailPoint
        public MailPoint Target;
        // Seconds remaining
        public float Timer;
        // Starting time
        public float MaxTimer;
        //spawned UI row for this delivery
        public GameObject EntryObject;

        // The "Mail #N" label number
        private readonly int mailNumber;
        // Cached child components of the UI row so we don't look them up every frame
        private readonly TextMeshProUGUI entryText;
        private readonly Image timerCircle;

        public ActiveDelivery(MailPoint target, float timer, int number, GameObject entry)
        {
            Target = target;
            Timer = timer;
            MaxTimer = timer;
            mailNumber = number;
            EntryObject = entry;

            // Grab the text and radial image from the row
            if (entry)
            {
                entryText = entry.GetComponentInChildren<TextMeshProUGUI>();
                timerCircle = entry.GetComponentInChildren<Image>();
            }
        }


        /// Refreshes this delivery's UI row
        /// and the radial fill, which shrinks from 1 to 0 as time runs out.
        public void UpdateEntryUI()
        {
            // Clamp so we never display a negative time on the frame
            float t = Mathf.Max(Timer, 0f);
            if (entryText)
                entryText.text = "Mail #" + mailNumber + "   " + Mathf.CeilToInt(t) + "s";
            if (timerCircle)
                timerCircle.fillAmount = t / MaxTimer;
        }
    }
}
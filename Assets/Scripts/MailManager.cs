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
    // Parent transform that objective entries get spawned under
    public Transform objectiveListParent;
    // Prefab for one row in the objectives list. Expected to contain a TextMeshProUGUI and an Image (radial timer).
    public GameObject objectiveEntryPrefab;
    public TextMeshProUGUI scoreText;
    //old strikes
    public TextMeshProUGUI strikesText;
    public TextMeshProUGUI setsText;
    // 3D UI arrow
    public Transform arrowTransform;
    //gameover
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;
    // win panel
    public GameObject winPanel;
    public TextMeshProUGUI winScoreText;
    //score ui element
    public Slider scoreSlider;

    // Every delivery the player is currently responsible for
    private readonly List<ActiveDelivery> activeDeliveries = new();
    // The MailPoint the player is currently looking at AND is allowed to interact with
    private MailPoint currentTarget;
    // Lifetime count of successful deliveries
    private int totalDeliveriesCompleted;
    //completed sets
    private int setsCompleted;
    private int strikes;
    private int score;
    private bool gameOver;
    //counter used only to label entries as "Mail #1"etc.
    private int mailCounter;

    // Cached list of every Deliver-type MailPoint
    private MailPoint[] allDeliverPoints;

    // Register the singleton as early as possible
    void Awake() => Instance = this;

    void Start()
    {
        // Cache delivery points once
        allDeliverPoints = FindDeliverPoints();
        score = baseScore;

        // Start with all the dynamic UI hidden
        interactPrompt.gameObject.SetActive(false);
        objectivesPanel.SetActive(false);
        if (arrowTransform) arrowTransform.gameObject.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (winPanel) winPanel.SetActive(false);

        // Draw the initial score, strikes and set counter
        RefreshUI();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (gameOver) return;

        HandleRaycast();
        HandleInteract();
        TickDeliveries();
        HandleArrow();
        HandleNewTasks();

    }

    /// Casts a ray from the center of the screen (the crosshair) and works out whether the player
    /// is looking at a MailPoint they can currently use. Updates currentTarget and the prompt text.
    void HandleRaycast()
    {
        // Ray through the middle of the screen
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            // Only objects with a MailPoint component are interactable 
            MailPoint point = hit.collider.GetComponent<MailPoint>();
            if (point != null)
            {

                bool canReceive = point.pointType == MailPointType.Receive && activeDeliveries.Count == 0;
                // Deliver point only usable if one of current objectives
                bool canDeliver = point.pointType == MailPointType.Deliver && IsActiveTarget(point);

                if (canReceive || canDeliver)
                {
                    interactPrompt.text = canReceive ? "Press E to receive mail" : "Press E to deliver mail";
                    interactPrompt.gameObject.SetActive(true);
                    currentTarget = point;
                    return;
                }
            }
        }

        // Nothing valid in range or in view: clear the target and hide the prompt
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

    /// Reacts to the E key when the player is looking at a valid MailPoint

    void HandleInteract()
    {
        if (currentTarget == null) return;
        // wasPressedThisFrame makes this fire once per key press
        if (!Keyboard.current.eKey.wasPressedThisFrame) return;

        if (currentTarget.pointType == MailPointType.Receive)
            ReceiveMail();
        else if (currentTarget.pointType == MailPointType.Deliver)
            DeliverMail(currentTarget);
    }

    /// Starts a new "batch" (set) of deliveries. Decides how many objectives to hand out

    void ReceiveMail()
    {
        int count = ComputeObjectiveCount();
        // Base timer for this set. Each delivery then gets its own random offset from this value.
        float baseTimer = ComputeDeliveryTime();
        List<MailPoint> targets = PickRandomDeliverPoints(count);

        foreach (MailPoint target in targets)
        {
            mailCounter++;

            // Spawn a UI row for this delivery
            // track the delivery
            var entry = objectiveEntryPrefab && objectiveListParent
                ? Instantiate(objectiveEntryPrefab, objectiveListParent)
                : null;

            // Randomize this delivery's timer
            // Clamped to at least 1 second
            float timer = Mathf.Max(1f, baseTimer + Random.Range(-timerVariation, timerVariation));

            activeDeliveries.Add(new ActiveDelivery(target, timer, mailCounter, entry));
        }

        // Show the objective list and arrow now that there's something to deliver
        objectivesPanel.SetActive(activeDeliveries.Count > 0);
        if (arrowTransform) arrowTransform.gameObject.SetActive(activeDeliveries.Count > 0);
        mailPointVis.SetActive(false);
    }


    /// Completes the delivery for the given target
    /// removes it from the active list
    void DeliverMail(MailPoint target)
    {
        // Look up which active delivery this point belongs to
        // HandleRaycast only allows interaction with active targets
        ActiveDelivery delivery = activeDeliveries.Find(d => d.Target == target);
        if (delivery == null) return;

        // Speed bonus scales linearly with the fraction of time left:
        int speedBonus = Mathf.RoundToInt(maxSpeedBonus * (delivery.Timer / delivery.MaxTimer));
        score += basePointsPerDelivery + speedBonus;
        totalDeliveriesCompleted++;

        RemoveDelivery(delivery);
        RefreshUI();
        CheckSetFinished();
    }

    /// handles setscompleted counter
    void CheckSetFinished()
    {
        if (activeDeliveries.Count > 0) return;

        setsCompleted++;
        RefreshUI();

        if (setsCompleted >= dailyMax)
            TriggerWin();
    }

    /// Counts down every active delivery, updates its UI row, and handles expiry (score penalty / game over).
    /// Also keeps the objectives panel and arrow visibility in sync with whether anything is active.
    void TickDeliveries()
    {
        // Iterate backwards so removing an item mid-loop doesn't skip or break the iteration
        for (int i = activeDeliveries.Count - 1; i >= 0; i--)
        {
            ActiveDelivery d = activeDeliveries[i];
            d.Timer -= Time.deltaTime;
            d.UpdateEntryUI();

            if (d.Timer <= 0f)
            {
                // Missed delivery: costs points and the delivery disappears
                score -= strikePenalty;
                RemoveDelivery(d);
                RefreshUI();

                // Losing takes priority over finishing the set
                if (score <= 0)
                {
                    TriggerGameOver();
                    return; // Stop immediately; the list was just cleared and the UI hidden
                }

                // If that was the last delivery in the set
                CheckSetFinished();
                if (gameOver) return; // Won the game
            }
        }

        // Keep UI visibility matching reality. hides the panel after the last deliveryis completed
        bool hasActive = activeDeliveries.Count > 0;
        objectivesPanel.SetActive(hasActive);
        if (arrowTransform) arrowTransform.gameObject.SetActive(hasActive);
    }



    void HandleArrow()
    {
        if (arrowTransform == null || activeDeliveries.Count == 0) return;

        // handle "urgent"/tracking delivery
        ActiveDelivery urgent = activeDeliveries[0];
        for (int i = 1; i < activeDeliveries.Count; i++)
            if (activeDeliveries[i].Timer < urgent.Timer) urgent = activeDeliveries[i];
        /////////////////////////////////////////////////////////////////////////////////////////
        // Direction from the arrow to the target's position in world space
        Vector3 dir = urgent.Target.transform.position - arrowTransform.position;

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
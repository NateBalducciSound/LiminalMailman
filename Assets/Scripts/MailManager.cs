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
    public float baseDeliveryTime = 20f;
    public float minDeliveryTime = 8f;
    [Tooltip("Each delivery completed reduces the timer by this much")]
    public float timeReductionPerDelivery = 1.5f;
    [Tooltip("Deliveries completed before objective count starts increasing")]
    public int difficultyRampStart = 3;
    [Tooltip("One extra objective added every N deliveries after ramp start")]
    public int deliveriesPerExtraObjective = 5;

    [Header("Scoring")]
    public int basePointsPerDelivery = 100;
    public int maxSpeedBonus = 200;

    [Header("Fail")]
    public int maxStrikes = 3;

    [Header("Interaction")]
    public float interactDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI interactPrompt;
    public GameObject objectivesPanel;
    public Transform objectiveListParent;
    public GameObject objectiveEntryPrefab;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI strikesText;
    public Transform arrowTransform;
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;

    // runtime state
    private readonly List<ActiveDelivery> activeDeliveries = new();
    private MailPoint currentTarget;
    private int totalDeliveriesCompleted;
    private int strikes;
    private int score;
    private bool gameOver;
    private int mailCounter;

    private MailPoint[] allDeliverPoints;

    void Awake() => Instance = this;

    void Start()
    {
        allDeliverPoints = FindDeliverPoints();
        interactPrompt.gameObject.SetActive(false);
        objectivesPanel.SetActive(false);
        if (arrowTransform) arrowTransform.gameObject.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        RefreshUI();
    }

    void Update()
    {
        if (gameOver) return;
        HandleRaycast();
        HandleInteract();
        TickDeliveries();
        HandleArrow();
    }

    void HandleRaycast()
    {
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            MailPoint point = hit.collider.GetComponent<MailPoint>();
            if (point != null)
            {
                bool canReceive = point.pointType == MailPointType.Receive && activeDeliveries.Count == 0;
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

        currentTarget = null;
        interactPrompt.gameObject.SetActive(false);
    }

    void HandleInteract()
    {
        if (currentTarget == null) return;
        if (!Keyboard.current.eKey.wasPressedThisFrame) return;

        if (currentTarget.pointType == MailPointType.Receive)
            ReceiveMail();
        else if (currentTarget.pointType == MailPointType.Deliver)
            DeliverMail(currentTarget);
    }

    void ReceiveMail()
    {
        int count = ComputeObjectiveCount();
        float timer = ComputeDeliveryTime();
        List<MailPoint> targets = PickRandomDeliverPoints(count);

        foreach (MailPoint target in targets)
        {
            mailCounter++;
            var entry = objectiveEntryPrefab && objectiveListParent
                ? Instantiate(objectiveEntryPrefab, objectiveListParent)
                : null;
            activeDeliveries.Add(new ActiveDelivery(target, timer, mailCounter, entry));
        }

        objectivesPanel.SetActive(activeDeliveries.Count > 0);
        if (arrowTransform) arrowTransform.gameObject.SetActive(activeDeliveries.Count > 0);
    }

    void DeliverMail(MailPoint target)
    {
        ActiveDelivery delivery = activeDeliveries.Find(d => d.Target == target);
        if (delivery == null) return;

        int speedBonus = Mathf.RoundToInt(maxSpeedBonus * (delivery.Timer / delivery.MaxTimer));
        score += basePointsPerDelivery + speedBonus;
        totalDeliveriesCompleted++;

        RemoveDelivery(delivery);
        RefreshUI();
    }

    void TickDeliveries()
    {
        for (int i = activeDeliveries.Count - 1; i >= 0; i--)
        {
            ActiveDelivery d = activeDeliveries[i];
            d.Timer -= Time.deltaTime;
            d.UpdateEntryUI();

            if (d.Timer <= 0f)
            {
                strikes++;
                RemoveDelivery(d);
                RefreshUI();

                if (strikes >= maxStrikes)
                {
                    TriggerGameOver();
                    return;
                }
            }
        }

        bool hasActive = activeDeliveries.Count > 0;
        objectivesPanel.SetActive(hasActive);
        if (arrowTransform) arrowTransform.gameObject.SetActive(hasActive);
    }

    // Arrow points at whichever active delivery has the least time remaining
    void HandleArrow()
    {
        if (arrowTransform == null || activeDeliveries.Count == 0) return;

        ActiveDelivery urgent = activeDeliveries[0];
        for (int i = 1; i < activeDeliveries.Count; i++)
            if (activeDeliveries[i].Timer < urgent.Timer) urgent = activeDeliveries[i];

        Vector3 dir = urgent.Target.transform.position - arrowTransform.position;
        if (dir != Vector3.zero)
            arrowTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 180f, 0f);
    }

    bool IsActiveTarget(MailPoint point) => activeDeliveries.Exists(d => d.Target == point);

    int ComputeObjectiveCount()
    {
        if (totalDeliveriesCompleted < difficultyRampStart)
            return Random.Range(1, 3); // 1 or 2 early on

        int extra = (totalDeliveriesCompleted - difficultyRampStart) / deliveriesPerExtraObjective;
        return Mathf.Min(2 + extra, allDeliverPoints.Length);
    }

    float ComputeDeliveryTime()
    {
        return Mathf.Max(minDeliveryTime, baseDeliveryTime - totalDeliveriesCompleted * timeReductionPerDelivery);
    }

    List<MailPoint> PickRandomDeliverPoints(int count)
    {
        List<MailPoint> pool = new(allDeliverPoints);
        pool.RemoveAll(p => activeDeliveries.Exists(d => d.Target == p));

        List<MailPoint> result = new();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            result.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return result;
    }

    MailPoint[] FindDeliverPoints()
    {
        MailPoint[] all = FindObjectsByType<MailPoint>(FindObjectsSortMode.None);
        List<MailPoint> deliver = new();
        foreach (MailPoint p in all)
            if (p.pointType == MailPointType.Deliver) deliver.Add(p);
        return deliver.ToArray();
    }

    void RemoveDelivery(ActiveDelivery d)
    {
        if (d.EntryObject) Destroy(d.EntryObject);
        activeDeliveries.Remove(d);
    }

    void RefreshUI()
    {
        if (scoreText) scoreText.text = score.ToString();

        if (strikesText)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < maxStrikes; i++)
                sb.Append(i < strikes ? "X" : " ");
            strikesText.text = sb.ToString();
        }
    }

    void TriggerGameOver()
    {
        gameOver = true;
        foreach (ActiveDelivery d in activeDeliveries)
            if (d.EntryObject) Destroy(d.EntryObject);
        activeDeliveries.Clear();

        objectivesPanel.SetActive(false);
        interactPrompt.gameObject.SetActive(false);
        if (arrowTransform) arrowTransform.gameObject.SetActive(false);

        if (gameOverPanel)
        {
            gameOverPanel.SetActive(true);
            if (gameOverScoreText) gameOverScoreText.text = score.ToString();
        }
    }

    public void Restart()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    class ActiveDelivery
    {
        public MailPoint Target;
        public float Timer;
        public float MaxTimer;
        public GameObject EntryObject;

        private readonly int mailNumber;
        private readonly TextMeshProUGUI entryText;
        private readonly Image timerCircle;

        public ActiveDelivery(MailPoint target, float timer, int number, GameObject entry)
        {
            Target = target;
            Timer = timer;
            MaxTimer = timer;
            mailNumber = number;
            EntryObject = entry;
            if (entry)
            {
                entryText = entry.GetComponentInChildren<TextMeshProUGUI>();
                timerCircle = entry.GetComponentInChildren<Image>();
            }
        }

        public void UpdateEntryUI()
        {
            float t = Mathf.Max(Timer, 0f);
            if (entryText)
                entryText.text = "Mail #" + mailNumber + " - " + Mathf.CeilToInt(t) + "s";
            if (timerCircle)
                timerCircle.fillAmount = t / MaxTimer;
        }
    }
}

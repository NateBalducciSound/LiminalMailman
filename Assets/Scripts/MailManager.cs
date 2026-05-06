using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MailManager : MonoBehaviour
{
    public static MailManager Instance;

    [Header("Settings")]
    public float deliveryTime = 60f;
    public float interactDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI interactPrompt;
    public GameObject objectivesPanel;
    public TextMeshProUGUI timerText;
    public Image timerFill;
    public Transform arrowTransform;

    private bool hasMail;
    private float timer;
    private MailPoint currentTarget;
    private MailPoint activeDeliveryPoint;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        interactPrompt.gameObject.SetActive(false);
        objectivesPanel.SetActive(false);
        arrowTransform.gameObject.SetActive(false);
    }

    void Update()
    {
        HandleRaycast();
        HandleInteract();
        HandleTimer();
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
                bool canReceive = point.pointType == MailPointType.Receive && !hasMail;
                bool canDeliver = point.pointType == MailPointType.Deliver && hasMail;

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

        if (currentTarget.pointType == MailPointType.Receive && !hasMail)
            ReceiveMail();
        else if (currentTarget.pointType == MailPointType.Deliver && hasMail)
            DeliverMail();
    }

    void ReceiveMail()
    {
        hasMail = true;
        timer = deliveryTime;

        activeDeliveryPoint = FindDeliveryPoint();

        objectivesPanel.SetActive(true);
        arrowTransform.gameObject.SetActive(true);
    }

    void DeliverMail()
    {
        hasMail = false;
        activeDeliveryPoint = null;
        objectivesPanel.SetActive(false);
        arrowTransform.gameObject.SetActive(false);
        interactPrompt.gameObject.SetActive(false);
    }

    void HandleTimer()
    {
        if (!hasMail) return;

        timer -= Time.deltaTime;
        timerText.text = Mathf.CeilToInt(Mathf.Max(timer, 0f)) + "s";
        if (timerFill != null) timerFill.fillAmount = Mathf.Clamp01(timer / deliveryTime);

        if (timer <= 0f)
        {
            hasMail = false;
            activeDeliveryPoint = null;
            objectivesPanel.SetActive(false);
            arrowTransform.gameObject.SetActive(false);
        }
    }

    void HandleArrow()
    {
        if (!hasMail || activeDeliveryPoint == null) return;

        Vector3 dir = activeDeliveryPoint.transform.position - arrowTransform.position;
        arrowTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 180f, 0f);
    }

    MailPoint FindDeliveryPoint()
    {
        MailPoint[] allPoints = FindObjectsByType<MailPoint>(FindObjectsSortMode.None);
        foreach (MailPoint p in allPoints)
        {
            if (p.pointType == MailPointType.Deliver)
                return p;
        }
        return null;
    }
}

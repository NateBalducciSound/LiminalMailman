using UnityEngine;

public class floatyspinner : MonoBehaviour
{
    public float spinSpeed = 100;
    public float bobSpeed = 1;
    public float amplitude = 0.5f;
    Vector3 startPos;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startPos = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(0f, Time.deltaTime * spinSpeed, 0f);
        float offset = Mathf.Sin(Time.time * bobSpeed * 2f * Mathf.PI) * amplitude;
        transform.position = startPos + Vector3.up * offset;
    }
}

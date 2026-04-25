using System;
using NUnit.Framework;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 8f;
    public float sprintSpeed = 14f;
    public float acceleration = 20f;
    public float deceleration = 15f;

    [Header("Jump")]
    public float jumpForce = 10f;
    public float coyoteTime = 0.15f;
    public float jumpBufferTime = 0.15f;
    

    [Header("Wall")]
    public float wallJumpForce = 8f;
    public float wallRunSpeed = 10f;
    public float wallCheckDistance = 0.6f;
    public float wallRunMaxTime = 1.5f;

    [Header("Slide")]
    public float slideForce = 6f;
    public float slideColliderHeight = 0.5f;

    [Header("Ledge Launch")]
    public float ledgeLaunchForce = 6f;
    public float ledgeCheckDistance = 1.2f;
    public float ledgeMaxHeight = 0.8f;

    [Header("References")]
    public Rigidbody rb;
    public BoxCollider col;
    public TextMeshProUGUI currentState;

    // State
    public bool isWallRunning;
    public bool isWallSliding;
    public bool isCrouching;

    // Private vars
    private bool isGrounded;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float wallRunTimer;
    private Vector3 currentWallNormal;
    private Vector3 wallRunDirection;
    private float defaultColliderHeight;
    private Vector3 defaultColliderCenter;
    private float wallJumpCooldown;

    // Input
    private float inputX;
    private float inputZ;
    private bool sprintHeld;
    private bool crouchPressed;
    private bool crouchHeld;

    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference crouchAction;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<BoxCollider>();
        defaultColliderHeight = col.size.y;
        defaultColliderCenter = col.center;
        rb.freezeRotation = true;
    }

    //Enable and Disable functions for input system

    void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        sprintAction.action.Enable();
        crouchAction.action.Enable();
    }

    void OnDisable()
    {
        moveAction.action.Disable();
        jumpAction.action.Disable();
        sprintAction.action.Disable();
        crouchAction.action.Disable();
    }

    void Update()
    {
        GatherInput();
        TickTimers();
        HandleCrouch();
        getCurrentState();
    }

    void FixedUpdate()
    {
        CheckGround();
        CheckWalls();
        HandleMovement();
        HandleJump();
        HandleWallRun();
    }

    void GatherInput()
    {
        Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
        inputX = moveInput.x;
        inputZ = moveInput.y;
        sprintHeld = sprintAction.action.IsPressed();
        crouchPressed = crouchAction.action.WasPressedThisFrame();
        crouchHeld = crouchAction.action.IsPressed();

        if (jumpAction.action.WasPressedThisFrame())
        {
            jumpBufferTimer = jumpBufferTime;
        }
    }

    void TickTimers()
    {
        coyoteTimer = isGrounded ? coyoteTime : coyoteTimer - Time.deltaTime;

        if (jumpBufferTimer > 0)
            jumpBufferTimer -= Time.deltaTime;

        if (isWallRunning)
        {
            wallRunTimer -= Time.deltaTime;
            if (wallRunTimer <= 0) StopWallRun();
        }
        if (wallJumpCooldown > 0) wallJumpCooldown -= Time.deltaTime;
    }

    void CheckGround()
    {
        float halfHeight = col.size.y * 0.5f + 0.05f;
        isGrounded = Physics.Raycast(transform.position, Vector3.down, halfHeight);
    }

    void CheckWalls()
    {
        if (isGrounded) return;

        //jump cooldown for wall jump
        if(wallJumpCooldown > 0) return;

        bool hitLeft  = Physics.Raycast(transform.position, -transform.right, out RaycastHit leftHit,  wallCheckDistance);
        bool hitRight = Physics.Raycast(transform.position,  transform.right, out RaycastHit rightHit, wallCheckDistance);

        if (hitLeft || hitRight)
        {
            currentWallNormal = hitLeft ? leftHit.normal : rightHit.normal;

            //check if player is pushing towards a wall normal
            Vector3 inputDir = (transform.right * inputX + transform.forward * inputZ).normalized;
            bool pressingTowardWall = Vector3.Dot(inputDir, -currentWallNormal) > 0.3f;

            Vector3 horizontal = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            if (rb.linearVelocity.y < 0 && !isWallRunning)
            {
                if (horizontal.magnitude > 3f) StartWallRun();
                else isWallSliding = true;
            }

            if (isWallSliding)
            {
                Vector3 vel = rb.linearVelocity;
                vel.y = Mathf.Max(vel.y, -6f);
                rb.linearVelocity = vel;
            }
        }
        else
        {
            isWallSliding = false;
            if (isWallRunning) StopWallRun();
        }
    }

    void HandleMovement()
    {
        if (isWallRunning) return;
        if (wallJumpCooldown > 0) return;

        Vector3 inputDir = (transform.right * inputX + transform.forward * inputZ).normalized;
        float targetSpeed = sprintHeld ? sprintSpeed : moveSpeed;
        Vector3 targetVelocity = inputDir * targetSpeed;

        Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float accel = inputDir.magnitude > 0.1f ? acceleration : deceleration;
        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, targetVelocity, accel * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(newHorizontal.x, rb.linearVelocity.y, newHorizontal.z);
    }

    void HandleJump()
    {
        bool canJump = coyoteTimer > 0 || isWallSliding || isWallRunning;

        if (jumpBufferTimer > 0 && canJump)
        {
            jumpBufferTimer = 0;
            if (isWallSliding || isWallRunning) DoWallJump();
            else DoJump();
        }
    }

    void DoJump()
    {
        coyoteTimer = 0;

        float feetY = transform.position.y - col.size.y * 0.5f;
        bool ledgeHit = Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, ledgeCheckDistance);
        bool isLowObject = ledgeHit && hit.point.y < feetY + ledgeMaxHeight;

        Vector3 vel = rb.linearVelocity;
        vel.y = jumpForce;
        if (isLowObject) vel += transform.forward * ledgeLaunchForce;

        rb.linearVelocity = vel;
    }

    void DoWallJump()
    {
        StopWallRun();
        isWallSliding = false;
        coyoteTimer = 0;
        wallJumpCooldown = 0.4f;
        Vector3 jumpDir = (currentWallNormal + Vector3.up).normalized;
        rb.linearVelocity = jumpDir * wallJumpForce;
    }

    void StartWallRun()
    {
        isWallRunning = true;
        isWallSliding = false;
        wallRunTimer = wallRunMaxTime;
        rb.useGravity = false;

        wallRunDirection = Vector3.Cross(currentWallNormal, Vector3.up).normalized;
        if (Vector3.Dot(wallRunDirection, transform.forward) < 0)
            wallRunDirection = -wallRunDirection;
    }

    void StopWallRun()
    {
        isWallRunning = false;
        rb.useGravity = true;
    }

    void HandleWallRun()
    {
       if (!isWallRunning) return;

       //end wall run if pushing away from wall
       Vector3 inputDir = (transform.right * inputX + transform.forward * inputZ).normalized;
       if (Vector3.Dot(inputDir, currentWallNormal) > 0.3f || inputDir.magnitude < 0.1f)
        {
            StopWallRun();
            return;
        }
        float newY = Mathf.MoveTowards(rb.linearVelocity.y, -2f, 2f * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(wallRunDirection.x * wallRunSpeed, newY, wallRunDirection.z * wallRunSpeed);
    }

    void HandleCrouch()
    {
        if (crouchPressed && isGrounded && !isCrouching) StartSlide();
        if (!crouchHeld && isCrouching) StopSlide();
    }

    void StartSlide()
    {
        isCrouching = true;
        col.size = new Vector3(col.size.x, slideColliderHeight, col.size.z);
        col.center = new Vector3(col.center.x, slideColliderHeight * 0.5f, col.center.z);

        Vector3 slideDir = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).normalized;
        rb.AddForce(slideDir * slideForce, ForceMode.Impulse);
    }

    void StopSlide()
    {
        isCrouching = false;
        col.size = new Vector3(col.size.x, defaultColliderHeight, col.size.z);
        col.center = defaultColliderCenter; 
    }
// script ref so we can know which states we are in
    void getCurrentState()
    {
        if (isWallRunning)
            currentState.text = "Wall Run";
        else if (isWallSliding)
            currentState.text = "Wall Slide";
        else if (isCrouching)
            currentState.text = "Crouch";
        else if (sprintHeld && isGrounded)
            currentState.text = "Sprint";
        else if (!isGrounded)
            currentState.text = "Jump";
        else
            currentState.text = "Idle";
    }
}
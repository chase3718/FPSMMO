using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Thanks to cranky https://forum.unity.com/members/cranky.641707/ https://forum.unity.com/threads/rigidbody-fps-controller.257353/

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    public float AccelerationRate = 20f;
    public float DecelerationsRate = 20f;
    public float AerialAcceleration = 5f;
    public float JumpSpeed = 4f;
    public float nudgeExtra = 0.5f;
    public float MaxSlope = 45f;
    public float curSpeed = 5f;
    public float lookSensitivity = 3f;
    public float maxLookAngle = 85f,
        minLookAngle = -90f;
    public bool toggleSprint = false;
    public float maxInputSpeed = 8f;
    public float landingSoak = 1f;
    public float jumpCooldownTime = 0.1f;

    // States
    public float walk = 5f;
    public float sprint = 8f;
    public float crouch = 2.5f;

    private bool grounded;
    public bool isGrounded
    {
        get { return grounded; }
    }

    private bool sprinting;

    // Unity Components
    private Rigidbody rigidbody;
    private CapsuleCollider capsuleCollider;
    private Camera camera;

    // Temporary variables
    private float inputX;
    private float inputY;
    private float lookX;
    private float lookY;
    private float xRotation;
    private Vector2 movementInput;
    private Vector3 movementVector;
    private Vector2 lookInput;
    private float acceleration;
    private float jumpCooldown;

    // Falling variables
    private bool falling;
    public bool isFalling
    {
        get { return falling; }
    }
    private float fallSpeed;
    public float FallSpeed
    {
        get { return fallSpeed; }
    }

    /*
     * Jump state variables
     * 0 = hit ground since last jump, can jump if grounded = true
     * 1 = jump button pressed, try to jump during fixedUpdate
     * 2 = jump force applied, waiting to leave the ground
     * 3 = jump force applied, waiting to hit the ground
     */
    private byte jumpState;

    // Average normal of the ground the player is standing on
    private Vector3 groundNormal;
    public Vector3 GroundNormal
    {
        get { return groundNormal; }
    }

    // If touching a dynamic object, don't prevent idle sliding
    private bool touchingDynamic;

    // Grounded last frame?
    private bool groundedLastFrame;

    // Objects the player is colliding with
    private List<GameObject> collisions;

    // Collision contact points
    private Dictionary<int, ContactPoint[]> contactPoints;

    // Temporary Calculations
    private float halfHeight;
    private float nudgeCheck;
    private float bottomCapsuleSphereOrigin;
    private float capsuleRadius;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        camera = GetComponentInChildren<Camera>();

        // Set cursor to locked
        Cursor.lockState = CursorLockMode.Locked;

        movementVector = Vector3.zero;

        grounded = false;
        groundNormal = Vector3.zero;
        touchingDynamic = false;
        groundedLastFrame = false;

        collisions = new List<GameObject>();
        contactPoints = new Dictionary<int, ContactPoint[]>();

        halfHeight = capsuleCollider.height / 2f;
        nudgeCheck = halfHeight + nudgeExtra;
        bottomCapsuleSphereOrigin = halfHeight - capsuleCollider.radius;
        capsuleRadius = capsuleCollider.radius;

        PhysicMaterial controllerMat = new PhysicMaterial();
        controllerMat.bounciness = 0f;
        controllerMat.dynamicFriction = 0f;
        controllerMat.staticFriction = 0f;
        controllerMat.bounceCombine = PhysicMaterialCombine.Minimum;
        controllerMat.frictionCombine = PhysicMaterialCombine.Minimum;
        capsuleCollider.material = controllerMat;

        rigidbody.freezeRotation = true;
    }

    void FixedUpdate()
    {
        Debug.Log(new Vector2(rigidbody.velocity.x, rigidbody.velocity.z).magnitude);

        CheckGrounded();

        RaycastHit hit;

        if (sprinting)
        {
            curSpeed = sprint;
        }
        else
        {
            curSpeed = walk;
        }

        if (isGrounded)
        {
            if (jumpCooldown > 0f)
            {
                jumpCooldown -= Time.fixedDeltaTime;
            } else {
                jumpCooldown = 0f;
            }
            groundNormal.Normalize();

            if (jumpState == 3)
            {
                jumpState = 0;
            }
        }
        else if (jumpState == 2)
        {
            jumpState = 3;
        }

        if (!isGrounded) {
            jumpCooldown = jumpCooldownTime;
        }

        GetInput();

        float length = 0f;
        bool accelerating = false;

        if (isGrounded && jumpState != 3)
        {
            if (isFalling)
            {
                // Landed!
                falling = false;
                if (
                    new Vector2(rigidbody.velocity.x, rigidbody.velocity.z).magnitude <= landingSoak
                )
                {
                    rigidbody.velocity = new Vector3(0, 0, 0);
                }
                else
                {
                    rigidbody.AddForce(-transform.forward * landingSoak, ForceMode.Impulse);
                }
                // TODO: Land animation and Fall Damage
            }

            // Allign movement with ground normal
            Vector3 newForward = transform.forward;
            Vector3.OrthoNormalize(ref groundNormal, ref newForward);

            Vector3 targetSpeed =
                Vector3.Cross(groundNormal, newForward) * inputX * curSpeed
                + newForward * inputY * curSpeed;

            length = targetSpeed.magnitude;
            float difference = length - rigidbody.velocity.magnitude;

            // Avoid /0
            if (Mathf.Approximately(difference, 0f))
            {
                movementVector = Vector3.zero;
            }
            else
            {
                // Accelerate or decelerate?
                accelerating = difference > 0f;
                if (difference > 0.0f)
                {
                    acceleration = Mathf.Min(AccelerationRate * Time.deltaTime, difference);
                }
                else
                {
                    acceleration = Mathf.Max(-DecelerationsRate * Time.deltaTime, difference);
                }

                difference = 1.0f / difference;
                movementVector = (targetSpeed - rigidbody.velocity) * acceleration * difference;
            }

            if (jumpState == 1)
            {
                // Jump!
                movementVector.y = JumpSpeed - rigidbody.velocity.y;
                jumpState = 2;
            }
            else if (!touchingDynamic && Mathf.Approximately(inputX + inputY, 0f) && jumpState < 2)
            {
                // Prevent sliding by countering gravity
                movementVector.y -= Physics.gravity.y * Time.deltaTime;
            }

            if (accelerating)
            {
                float currentSpeed = new Vector2(
                    rigidbody.velocity.x,
                    rigidbody.velocity.z
                ).magnitude;

                if (currentSpeed >= maxInputSpeed)
                {
                    movementVector.x = 0;
                    movementVector.z = 0;
                }
            }

            rigidbody.AddForce(movementVector, ForceMode.VelocityChange);
            groundedLastFrame = true;
        }
        else
        {
            // Not grounded, check if need to nudge or do air accel

            // Nudge
            if (groundedLastFrame && jumpState != 3 && !isFalling)
            {
                // Check for surface beneath player within nudgeCheck distance
                if (
                    Physics.Raycast(
                        transform.position,
                        Vector3.down,
                        out hit,
                        nudgeCheck + (rigidbody.velocity.magnitude * Time.deltaTime),
                        ~0
                    )
                    && Vector3.Angle(hit.normal, Vector3.up) <= MaxSlope
                )
                {
                    groundedLastFrame = true;

                    // Catch jump attempts that would have been missed if we weren't nudging
                    if (jumpState == 1)
                    {
                        movementVector.y += JumpSpeed;
                        jumpState = 2;
                        return;
                    }

                    // we can't go straight down, so do another raycast for the exact distance towards the surface
                    // i tried doing exsec and excsc to avoid doing another raycast, but my math sucks and it failed
                    // horribly. if anyone else knows a reasonable way to implement a simple trig function to bypass
                    // this raycast, please contribute to the thread!
                    if (
                        Physics.Raycast(
                            new Vector3(
                                transform.position.x,
                                transform.position.y - bottomCapsuleSphereOrigin,
                                transform.position.z
                            ),
                            -hit.normal,
                            out hit,
                            hit.distance,
                            ~0
                        )
                    )
                    {
                        rigidbody.AddForce(hit.normal * -hit.distance, ForceMode.VelocityChange);
                        return; // skip air accel because we should be grounded
                    }
                }
            }

            // Airborne
            if (!isFalling)
            {
                falling = true;
            }

            fallSpeed = rigidbody.velocity.y;

            // Air Acceleration
            if (!Mathf.Approximately(inputX + inputY, 0.0f))
            {
                // Get direction vector
                movementVector = transform.TransformDirection(
                    new Vector3(
                        inputX * AerialAcceleration * Time.deltaTime,
                        0f,
                        inputY * AerialAcceleration * Time.deltaTime
                    )
                );

                // Do check to see if new velocity is greater than max speed
                float a = movementVector.x + rigidbody.velocity.x;
                float b = movementVector.z + rigidbody.velocity.z;

                length = Mathf.Sqrt(a * a + b * b);
                if (length > 0.0f && length > curSpeed)
                {
                    length =
                        1.0f
                        / Mathf.Sqrt(
                            movementVector.x * movementVector.x
                                + movementVector.z * movementVector.z
                        );
                    movementVector.x *= length;
                    movementVector.z *= length;

                    length =
                        1.0f
                        / Mathf.Sqrt(
                            rigidbody.velocity.x * rigidbody.velocity.x
                                + rigidbody.velocity.z * rigidbody.velocity.z
                        );
                    Vector3 rigidbodyDirection = new Vector3(
                        rigidbody.velocity.x * length,
                        0f,
                        rigidbody.velocity.z * length
                    );

                    length =
                        1.0f
                        / Mathf.Sqrt(
                            movementVector.x * movementVector.x
                                + movementVector.z * movementVector.z
                        )
                        * AerialAcceleration
                        * Time.deltaTime;
                    movementVector.x *= length;
                    movementVector.z *= length;
                }

                movementVector *= 0.5f;

                // Add the force
                rigidbody.AddForce(
                    new Vector3(movementVector.x, 0f, movementVector.z),
                    ForceMode.VelocityChange
                );
            }

            groundedLastFrame = false;
        }
    }

    void CheckGrounded()
    {
        RaycastHit hit;
        grounded = false;
        groundNormal = Vector3.zero;

        foreach (ContactPoint[] contacts in contactPoints.Values)
        {
            for (int i = 0; i < contacts.Length; i++)
            {
                if (contacts[i].point.y < transform.position.y - bottomCapsuleSphereOrigin)
                {
                    if (
                        Physics.Raycast(
                            contacts[i].point + Vector3.up,
                            Vector3.down,
                            out hit,
                            1.1f,
                            ~0
                        )
                        && Vector3.Angle(hit.normal, Vector3.up) <= MaxSlope
                    )
                    {
                        grounded = true;
                        groundNormal += hit.normal;
                    }
                }
            }
        }
    }

    void GetInput()
    {
        inputX = movementInput.x;
        inputY = movementInput.y;
    }

    // TODO: Add fall damage
    void DoFallDamage(float fallSpeed) { }

    void OnCollisionEnter(Collision collision)
    {
        // Keep track of collision objects and contact points
        collisions.Add(collision.gameObject);
        contactPoints.Add(collision.gameObject.GetInstanceID(), collision.contacts);

        // Check if we're touching a dynamic object
        if (!collision.gameObject.isStatic)
        {
            touchingDynamic = true;
        }

        // Reset jump if able
        if (jumpState == 3)
        {
            jumpState = 0;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        // Keep track of contact points
        contactPoints[collision.gameObject.GetInstanceID()] = collision.contacts;
    }

    void OnCollisionExit(Collision collision)
    {
        touchingDynamic = false;

        // remove this collision and its associated contact points from the list
        // don't break from the list once we find it because we might somehow have duplicate entries,
        // and we need to recheck groundedOnDynamic anyways

        for (int i = 0; i < collisions.Count; i++)
        {
            if (collisions[i] == collision.gameObject)
            {
                collisions.RemoveAt(i--);
            }
            else if (!collisions[i].isStatic)
            {
                touchingDynamic = true;
            }
        }

        contactPoints.Remove(collision.gameObject.GetInstanceID());
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        movementInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (groundedLastFrame && jumpCooldown <= 0f)
        {
            jumpState = 1;
        }
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookInput = context.ReadValue<Vector2>();
        lookX = lookInput.x * lookSensitivity * Time.deltaTime;
        lookY = lookInput.y * lookSensitivity * Time.deltaTime;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, minLookAngle, maxLookAngle);
        // Rotate the camera
        camera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        // Rotate the player
        transform.Rotate(Vector3.up * lookX);
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (toggleSprint)
        {
            sprinting = !sprinting;
        }
        else
        {
            sprinting = context.ReadValueAsButton();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class AdvancedCarController : MonoBehaviour
{
    private const string SteerActionName = "VehicleSteer";
    private const string ThrottleActionName = "VehicleThrottle";
    private const string BrakeActionName = "VehicleBrake";
    private const string HandbrakeActionName = "VehicleHandbrake";

    [Serializable]
    public class Axle
    {
        public string name = "Axle";
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public Transform leftVisual;
        public Transform rightVisual;
        public bool steering;
        public bool motor;
        public bool brake = true;
        public bool handbrake;
    }

    [Header("Input")]
    [SerializeField] private bool requireDriver = true;
    [SerializeField] private bool keyboardFallback = true;

    [Header("Wheel Setup")]
    [SerializeField] private bool autoAssignAxles = true;
    [SerializeField] private List<Axle> axles = new();
    [SerializeField] private float wheelVisualLerp = 1f;

    [Header("Engine / Transmission")]
    [SerializeField] private float maxForwardSpeedKmh = 170f;
    [SerializeField] private float maxReverseSpeedKmh = 35f;
    [SerializeField] private float maxMotorTorque = 1650f;
    [SerializeField] private float maxReverseTorque = 900f;
    [SerializeField] private AnimationCurve torqueBySpeed = new(
        new Keyframe(0f, 1f),
        new Keyframe(0.5f, 0.65f),
        new Keyframe(1f, 0.2f)
    );

    [Header("Steering")]
    [SerializeField] private float lowSpeedSteerAngle = 34f;
    [SerializeField] private float highSpeedSteerAngle = 10f;
    [SerializeField] private float speedForHighSteerDropKmh = 120f;
    [SerializeField] private float steerResponsiveness = 8f;

    [Header("Braking")]
    [SerializeField] private float maxBrakeTorque = 3800f;
    [SerializeField] private float maxHandbrakeTorque = 5200f;
    [SerializeField] private float parkedBrakeTorque = 9000f;
    [SerializeField] private float parkedLateralDamping = 0.15f;

    [Header("Handling Aids")]
    [SerializeField] private float antiRollForce = 6500f;
    [SerializeField] private float downforce = 70f;
    [SerializeField] private float tractionControlSlip = 0.42f;
    [SerializeField] private float tractionControlStrength = 0.55f;
    [SerializeField] private float absSlipThreshold = 0.5f;
    [SerializeField] private float absReleaseFactor = 0.45f;

    [Header("Rigidbody")]
    [SerializeField] private Vector3 centerOfMassOffset = new(0f, -0.45f, 0f);

    public float SpeedKmh => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
    public bool HasDriver => currentDriverInput != null;

    private Rigidbody rb;
    private PlayerInput currentDriverInput;
    private InputAction steerAction;
    private InputAction throttleAction;
    private InputAction brakeAction;
    private InputAction handbrakeAction;

    private float steerInput;
    private float throttleInput;
    private float brakeInput;
    private float handbrakeInput;
    private float smoothedSteer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;
        AutoAssignAxlesIfNeeded();
    }

    private void OnEnable()
    {
        EnableAction(steerAction);
        EnableAction(throttleAction);
        EnableAction(brakeAction);
        EnableAction(handbrakeAction);
    }

    private void OnDisable()
    {
        DisableAction(steerAction);
        DisableAction(throttleAction);
        DisableAction(brakeAction);
        DisableAction(handbrakeAction);
    }

    public void SetDriver(PlayerInput driverInput)
    {
        if (driverInput == null)
            return;

        UnbindDriverActions();
        currentDriverInput = driverInput;

        steerAction = currentDriverInput.actions.FindAction(SteerActionName);
        throttleAction = currentDriverInput.actions.FindAction(ThrottleActionName);
        brakeAction = currentDriverInput.actions.FindAction(BrakeActionName);
        handbrakeAction = currentDriverInput.actions.FindAction(HandbrakeActionName);

        EnableAction(steerAction);
        EnableAction(throttleAction);
        EnableAction(brakeAction);
        EnableAction(handbrakeAction);
    }

    public void ClearDriver()
    {
        UnbindDriverActions();
        currentDriverInput = null;

        steerInput = 0f;
        throttleInput = 0f;
        brakeInput = 1f;
        handbrakeInput = 1f;
    }

    private void Update()
    {
        ReadInputs();
        UpdateWheelVisuals();
    }

    private void FixedUpdate()
    {
        if (requireDriver && !HasDriver)
        {
            ApplyParkedState();
            return;
        }

        ApplyDownforce();
        ApplySteering();
        ApplyDriveAndBrakes();
        ApplyAntiRollBars();
    }

    private void ApplyParkedState()
    {
        foreach (var axle in axles)
        {
            if (axle.leftWheel == null || axle.rightWheel == null)
                continue;

            axle.leftWheel.motorTorque = 0f;
            axle.rightWheel.motorTorque = 0f;
            axle.leftWheel.steerAngle = 0f;
            axle.rightWheel.steerAngle = 0f;
            axle.leftWheel.brakeTorque = parkedBrakeTorque;
            axle.rightWheel.brakeTorque = parkedBrakeTorque;
        }

        // Remove side drift while parked to avoid launch on play due to setup asymmetry.
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        localVel.x *= (1f - Mathf.Clamp01(parkedLateralDamping));
        rb.linearVelocity = transform.TransformDirection(localVel);
    }

    private void ReadInputs()
    {
        if (requireDriver && !HasDriver)
        {
            steerInput = 0f;
            throttleInput = 0f;
            brakeInput = 1f;
            handbrakeInput = 1f;
            return;
        }

        float fallbackSteer = 0f;
        float fallbackThrottle = 0f;
        float fallbackBrake = 0f;
        float fallbackHandbrake = 0f;

        if (keyboardFallback && Keyboard.current != null)
        {
            fallbackSteer = (Keyboard.current.aKey.isPressed ? -1f : 0f) + (Keyboard.current.dKey.isPressed ? 1f : 0f);
            fallbackThrottle = Keyboard.current.wKey.isPressed ? 1f : 0f;
            fallbackBrake = Keyboard.current.sKey.isPressed ? 1f : 0f;
            fallbackHandbrake = Keyboard.current.spaceKey.isPressed ? 1f : 0f;
        }

        float steerValue = ReadSignedAxis(steerAction, 0f);
        float throttleValue = ReadPositiveAxis(throttleAction, 0f);
        float brakeValue = ReadPositiveAxis(brakeAction, 0f);
        float handbrakeValue = ReadPositiveAxis(handbrakeAction, 0f);

        // If action values are unavailable/inactive, fall back to direct keyboard input.
        if (Mathf.Abs(steerValue) < 0.01f && Mathf.Abs(fallbackSteer) > 0.01f)
            steerValue = fallbackSteer;
        if (throttleValue < 0.01f && fallbackThrottle > 0.01f)
            throttleValue = fallbackThrottle;
        if (brakeValue < 0.01f && fallbackBrake > 0.01f)
            brakeValue = fallbackBrake;
        if (handbrakeValue < 0.01f && fallbackHandbrake > 0.01f)
            handbrakeValue = fallbackHandbrake;

        steerInput = Mathf.Clamp(steerValue, -1f, 1f);
        throttleInput = Mathf.Clamp01(throttleValue);
        brakeInput = Mathf.Clamp01(brakeValue);
        handbrakeInput = Mathf.Clamp01(handbrakeValue);
    }

    private void ApplySteering()
    {
        float speed01 = Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, speedForHighSteerDropKmh));
        float maxSteerNow = Mathf.Lerp(lowSpeedSteerAngle, highSpeedSteerAngle, speed01);
        smoothedSteer = Mathf.Lerp(smoothedSteer, steerInput, steerResponsiveness * Time.fixedDeltaTime);
        float steerAngle = smoothedSteer * maxSteerNow;

        foreach (var axle in axles)
        {
            if (!axle.steering)
                continue;

            if (axle.leftWheel != null)
                axle.leftWheel.steerAngle = steerAngle;
            if (axle.rightWheel != null)
                axle.rightWheel.steerAngle = steerAngle;
        }
    }

    private void ApplyDriveAndBrakes()
    {
        float speed = SpeedKmh;
        float forwardSpeedFactor = Mathf.Clamp01(speed / Mathf.Max(1f, maxForwardSpeedKmh));
        float reverseSpeedFactor = Mathf.Clamp01(speed / Mathf.Max(1f, maxReverseSpeedKmh));

        float forwardTorque = throttleInput * maxMotorTorque * torqueBySpeed.Evaluate(forwardSpeedFactor);
        float reverseTorque = -brakeInput * maxReverseTorque * torqueBySpeed.Evaluate(reverseSpeedFactor);

        bool shouldDriveForward = throttleInput > 0.01f && speed < maxForwardSpeedKmh;
        bool shouldDriveReverse = brakeInput > 0.01f && speed < 2.5f;

        float motorTorque = 0f;
        if (shouldDriveForward)
            motorTorque = forwardTorque;
        else if (shouldDriveReverse)
            motorTorque = reverseTorque;

        motorTorque = ApplyTractionControl(motorTorque);

        float serviceBrake = brakeInput * maxBrakeTorque;
        float handBrakeTorque = handbrakeInput * maxHandbrakeTorque;

        foreach (var axle in axles)
        {
            if (axle.leftWheel == null || axle.rightWheel == null)
                continue;

            if (axle.motor)
            {
                axle.leftWheel.motorTorque = motorTorque;
                axle.rightWheel.motorTorque = motorTorque;
            }
            else
            {
                axle.leftWheel.motorTorque = 0f;
                axle.rightWheel.motorTorque = 0f;
            }

            float axleBrakeTorque = 0f;
            if (axle.brake)
                axleBrakeTorque += serviceBrake;
            if (axle.handbrake)
                axleBrakeTorque += handBrakeTorque;

            axleBrakeTorque = ApplyABS(axle.leftWheel, axle.rightWheel, axleBrakeTorque);

            axle.leftWheel.brakeTorque = axleBrakeTorque;
            axle.rightWheel.brakeTorque = axleBrakeTorque;
        }
    }

    private float ApplyTractionControl(float requestedTorque)
    {
        if (Mathf.Approximately(requestedTorque, 0f))
            return 0f;

        float worstSlip = 0f;
        foreach (var axle in axles)
        {
            if (!axle.motor)
                continue;

            worstSlip = Mathf.Max(worstSlip, GetAbsoluteForwardSlip(axle.leftWheel));
            worstSlip = Mathf.Max(worstSlip, GetAbsoluteForwardSlip(axle.rightWheel));
        }

        if (worstSlip <= tractionControlSlip)
            return requestedTorque;

        float excess = worstSlip - tractionControlSlip;
        float reduction = Mathf.Clamp01(excess * tractionControlStrength);
        return requestedTorque * (1f - reduction);
    }

    private float ApplyABS(WheelCollider leftWheel, WheelCollider rightWheel, float requestedBrakeTorque)
    {
        if (requestedBrakeTorque <= 0f)
            return 0f;

        float leftSlip = GetAbsoluteForwardSlip(leftWheel);
        float rightSlip = GetAbsoluteForwardSlip(rightWheel);
        float worstSlip = Mathf.Max(leftSlip, rightSlip);

        if (worstSlip <= absSlipThreshold)
            return requestedBrakeTorque;

        return requestedBrakeTorque * absReleaseFactor;
    }

    private void ApplyAntiRollBars()
    {
        foreach (var axle in axles)
        {
            if (axle.leftWheel == null || axle.rightWheel == null)
                continue;

            float leftTravel = 1f;
            float rightTravel = 1f;

            bool leftGrounded = axle.leftWheel.GetGroundHit(out WheelHit leftHit);
            if (leftGrounded)
            {
                leftTravel = (-axle.leftWheel.transform.InverseTransformPoint(leftHit.point).y - axle.leftWheel.radius) / axle.leftWheel.suspensionDistance;
            }

            bool rightGrounded = axle.rightWheel.GetGroundHit(out WheelHit rightHit);
            if (rightGrounded)
            {
                rightTravel = (-axle.rightWheel.transform.InverseTransformPoint(rightHit.point).y - axle.rightWheel.radius) / axle.rightWheel.suspensionDistance;
            }

            float antiRoll = (leftTravel - rightTravel) * antiRollForce;

            if (leftGrounded)
                rb.AddForceAtPosition(axle.leftWheel.transform.up * -antiRoll, axle.leftWheel.transform.position);
            if (rightGrounded)
                rb.AddForceAtPosition(axle.rightWheel.transform.up * antiRoll, axle.rightWheel.transform.position);
        }
    }

    private void ApplyDownforce()
    {
        rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude, ForceMode.Force);
    }

    private void UpdateWheelVisuals()
    {
        foreach (var axle in axles)
        {
            UpdateWheelVisual(axle.leftWheel, axle.leftVisual);
            UpdateWheelVisual(axle.rightWheel, axle.rightVisual);
        }
    }

    private void UpdateWheelVisual(WheelCollider wheel, Transform visual)
    {
        if (wheel == null || visual == null)
            return;

        wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
        if (wheelVisualLerp >= 1f)
        {
            visual.SetPositionAndRotation(pos, rot);
            return;
        }

        visual.position = Vector3.Lerp(visual.position, pos, wheelVisualLerp);
        visual.rotation = Quaternion.Slerp(visual.rotation, rot, wheelVisualLerp);
    }

    private float GetAbsoluteForwardSlip(WheelCollider wheel)
    {
        if (wheel == null)
            return 0f;

        if (!wheel.GetGroundHit(out WheelHit hit))
            return 0f;

        return Mathf.Abs(hit.forwardSlip);
    }

    private void AutoAssignAxlesIfNeeded()
    {
        if (!autoAssignAxles)
            return;

        bool hasCompleteManualSetup = axles != null && axles.Count >= 2;
        if (hasCompleteManualSetup)
        {
            bool missingWheel = false;
            for (int i = 0; i < axles.Count; i++)
            {
                if (axles[i].leftWheel == null || axles[i].rightWheel == null)
                {
                    missingWheel = true;
                    break;
                }
            }

            if (!missingWheel)
                return;
        }

        WheelCollider[] foundWheels = GetComponentsInChildren<WheelCollider>(true);
        if (foundWheels == null || foundWheels.Length < 4)
            return;

        WheelCollider frontLeft = null;
        WheelCollider frontRight = null;
        WheelCollider rearLeft = null;
        WheelCollider rearRight = null;
        float maxFrontLeftZ = float.MinValue;
        float maxFrontRightZ = float.MinValue;
        float minRearLeftZ = float.MaxValue;
        float minRearRightZ = float.MaxValue;

        for (int i = 0; i < foundWheels.Length; i++)
        {
            WheelCollider wheel = foundWheels[i];
            Vector3 local = transform.InverseTransformPoint(wheel.transform.position);
            bool isLeft = local.x <= 0f;

            if (isLeft)
            {
                if (local.z > maxFrontLeftZ)
                {
                    maxFrontLeftZ = local.z;
                    frontLeft = wheel;
                }

                if (local.z < minRearLeftZ)
                {
                    minRearLeftZ = local.z;
                    rearLeft = wheel;
                }
            }
            else
            {
                if (local.z > maxFrontRightZ)
                {
                    maxFrontRightZ = local.z;
                    frontRight = wheel;
                }

                if (local.z < minRearRightZ)
                {
                    minRearRightZ = local.z;
                    rearRight = wheel;
                }
            }
        }

        if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            return;

        axles = new List<Axle>
        {
            new Axle
            {
                name = "Front Axle",
                leftWheel = frontLeft,
                rightWheel = frontRight,
                leftVisual = FindClosestWheelVisual(frontLeft),
                rightVisual = FindClosestWheelVisual(frontRight),
                steering = true,
                motor = false,
                brake = true,
                handbrake = false
            },
            new Axle
            {
                name = "Rear Axle",
                leftWheel = rearLeft,
                rightWheel = rearRight,
                leftVisual = FindClosestWheelVisual(rearLeft),
                rightVisual = FindClosestWheelVisual(rearRight),
                steering = false,
                motor = true,
                brake = true,
                handbrake = true
            }
        };
    }

    private Transform FindClosestWheelVisual(WheelCollider wheel)
    {
        if (wheel == null)
            return null;

        Transform best = null;
        float bestDistance = float.MaxValue;
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform t = allChildren[i];
            if (t == null || t == wheel.transform)
                continue;

            if (t.GetComponent<WheelCollider>() != null)
                continue;

            float dist = Vector3.Distance(t.position, wheel.transform.position);
            if (dist > 1.75f)
                continue;

            string lowerName = t.name.ToLowerInvariant();
            if (!lowerName.Contains("wheel") && !lowerName.Contains("tyre") && !lowerName.Contains("tire"))
                continue;

            if (dist < bestDistance)
            {
                bestDistance = dist;
                best = t;
            }
        }

        return best;
    }

    private void UnbindDriverActions()
    {
        DisableAction(steerAction);
        DisableAction(throttleAction);
        DisableAction(brakeAction);
        DisableAction(handbrakeAction);

        steerAction = null;
        throttleAction = null;
        brakeAction = null;
        handbrakeAction = null;
    }

    private static void EnableAction(InputAction action)
    {
        if (action != null)
            action.Enable();
    }

    private static void DisableAction(InputAction action)
    {
        if (action != null)
            action.Disable();
    }

    private static float ReadSignedAxis(InputAction action, float fallback)
    {
        if (action == null || !action.enabled || action.controls.Count == 0)
            return fallback;

        try
        {
            return action.ReadValue<float>();
        }
        catch
        {
            try
            {
                return action.ReadValue<Vector2>().x;
            }
            catch
            {
                return fallback;
            }
        }
    }

    private static float ReadPositiveAxis(InputAction action, float fallback)
    {
        if (action == null || !action.enabled || action.controls.Count == 0)
            return fallback;

        try
        {
            float value = action.ReadValue<float>();
            return Mathf.Max(0f, value);
        }
        catch
        {
            try
            {
                Vector2 v = action.ReadValue<Vector2>();
                return Mathf.Max(0f, v.y);
            }
            catch
            {
                return fallback;
            }
        }
    }
}

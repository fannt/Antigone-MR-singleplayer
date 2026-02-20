using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class HandMenuXRButtonActivator : MonoBehaviour
{
    private enum ControllerSide
    {
        Left,
        Right,
        Either
    }

    private enum ActivationButton
    {
        GripButton,
        Primary2DAxisClick,
        TriggerButton,
        PrimaryButton,
        SecondaryButton
    }

    private enum ActivationMode
    {
        EnableOnce,
        ToggleOnPress,
        ActiveWhileHeld
    }

    [Header("Target")]
    [SerializeField] private GameObject targetObject;
    [SerializeField] private bool forceDisabledOnStart = true;

    [Header("Controller Input")]
    [SerializeField] private ControllerSide controllerSide = ControllerSide.Right;
    [SerializeField] private ActivationButton activationButton = ActivationButton.GripButton;

    [Header("Behavior")]
    [SerializeField] private ActivationMode activationMode = ActivationMode.EnableOnce;
    [SerializeField] private bool disableSelfAfterEnable = true;

    private bool pressedLastFrame;

    private void Start()
    {
        if (targetObject == null)
        {
            Debug.LogWarning($"{nameof(HandMenuXRButtonActivator)} on {name} has no target object assigned.");
            enabled = false;
            return;
        }

        if (forceDisabledOnStart && targetObject.activeSelf)
            targetObject.SetActive(false);
    }

    private void Update()
    {
        bool pressed = IsSelectedButtonPressed();
        bool pressedThisFrame = pressed && !pressedLastFrame;

        switch (activationMode)
        {
            case ActivationMode.ActiveWhileHeld:
                if (targetObject.activeSelf != pressed)
                    targetObject.SetActive(pressed);
                break;

            case ActivationMode.ToggleOnPress:
                if (pressedThisFrame)
                    targetObject.SetActive(!targetObject.activeSelf);
                break;

            case ActivationMode.EnableOnce:
                if (pressedThisFrame && !targetObject.activeSelf)
                {
                    targetObject.SetActive(true);
                    if (disableSelfAfterEnable)
                        enabled = false;
                }
                break;
        }

        pressedLastFrame = pressed;
    }

    private bool IsSelectedButtonPressed()
    {
        switch (controllerSide)
        {
            case ControllerSide.Left:
                return IsSelectedButtonPressed(XRNode.LeftHand);
            case ControllerSide.Right:
                return IsSelectedButtonPressed(XRNode.RightHand);
            default:
                return IsSelectedButtonPressed(XRNode.LeftHand) || IsSelectedButtonPressed(XRNode.RightHand);
        }
    }

    private bool IsSelectedButtonPressed(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        switch (activationButton)
        {
            case ActivationButton.GripButton:
                return TryGetButtonState(device, CommonUsages.gripButton);
            case ActivationButton.Primary2DAxisClick:
                return TryGetButtonState(device, CommonUsages.primary2DAxisClick);
            case ActivationButton.TriggerButton:
                return TryGetButtonState(device, CommonUsages.triggerButton);
            case ActivationButton.PrimaryButton:
                return TryGetButtonState(device, CommonUsages.primaryButton);
            case ActivationButton.SecondaryButton:
                return TryGetButtonState(device, CommonUsages.secondaryButton);
            default:
                return false;
        }
    }

    private static bool TryGetButtonState(InputDevice device, InputFeatureUsage<bool> usage)
    {
        return device.TryGetFeatureValue(usage, out bool isPressed) && isPressed;
    }
}

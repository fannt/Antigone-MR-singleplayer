using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI.BodyUI;

[DisallowMultipleComponent]
public class HandMenuButtonGate : MonoBehaviour
{
    private enum ControllerSide
    {
        Left,
        Right,
        Either
    }

    private enum RequiredButton
    {
        JoystickClick,
        PrimaryButton,
        SecondaryButton,
        PrimaryOrSecondaryButton,
        JoystickOrPrimaryOrSecondary
    }

    [Header("Hand Menu")]
    [SerializeField] private HandMenu handMenu;
    [SerializeField] private bool useHandMenuDefaultHandedness = true;
    [SerializeField] private HandMenu.MenuHandedness allowedHandedness = HandMenu.MenuHandedness.Either;

    [Header("Input")]
    [SerializeField] private ControllerSide controllerSide = ControllerSide.Right;
    [SerializeField] private RequiredButton requiredButton = RequiredButton.JoystickOrPrimaryOrSecondary;

    private HandMenu.MenuHandedness initialHandedness = HandMenu.MenuHandedness.Either;

    private void Awake()
    {
        if (handMenu == null)
            handMenu = GetComponent<HandMenu>();

        if (handMenu == null)
        {
            Debug.LogWarning($"{nameof(HandMenuButtonGate)} on {name} requires a {nameof(HandMenu)} reference.");
            enabled = false;
            return;
        }

        initialHandedness = handMenu.menuHandedness;
        if (initialHandedness == HandMenu.MenuHandedness.None)
            initialHandedness = HandMenu.MenuHandedness.Either;
    }

    private void OnEnable()
    {
        ApplyGate(IsRequiredButtonPressed());
    }

    private void Update()
    {
        ApplyGate(IsRequiredButtonPressed());
    }

    private void OnDisable()
    {
        if (handMenu == null)
            return;

        HandMenu.MenuHandedness restoredHandedness = useHandMenuDefaultHandedness ? initialHandedness : allowedHandedness;
        if (restoredHandedness == HandMenu.MenuHandedness.None)
            restoredHandedness = HandMenu.MenuHandedness.Either;

        handMenu.menuHandedness = restoredHandedness;
    }

    private void ApplyGate(bool allowMenu)
    {
        HandMenu.MenuHandedness desiredHandedness = HandMenu.MenuHandedness.None;

        if (allowMenu)
        {
            desiredHandedness = useHandMenuDefaultHandedness ? initialHandedness : allowedHandedness;
            if (desiredHandedness == HandMenu.MenuHandedness.None)
                desiredHandedness = HandMenu.MenuHandedness.Either;
        }

        if (handMenu.menuHandedness != desiredHandedness)
            handMenu.menuHandedness = desiredHandedness;
    }

    private bool IsRequiredButtonPressed()
    {
        switch (controllerSide)
        {
            case ControllerSide.Left:
                return IsRequiredButtonPressedOnNode(XRNode.LeftHand);
            case ControllerSide.Right:
                return IsRequiredButtonPressedOnNode(XRNode.RightHand);
            default:
                return IsRequiredButtonPressedOnNode(XRNode.LeftHand) || IsRequiredButtonPressedOnNode(XRNode.RightHand);
        }
    }

    private bool IsRequiredButtonPressedOnNode(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        bool joystickPressed = TryGetButton(device, CommonUsages.primary2DAxisClick);
        bool primaryPressed = TryGetButton(device, CommonUsages.primaryButton);
        bool secondaryPressed = TryGetButton(device, CommonUsages.secondaryButton);

        switch (requiredButton)
        {
            case RequiredButton.JoystickClick:
                return joystickPressed;
            case RequiredButton.PrimaryButton:
                return primaryPressed;
            case RequiredButton.SecondaryButton:
                return secondaryPressed;
            case RequiredButton.PrimaryOrSecondaryButton:
                return primaryPressed || secondaryPressed;
            default:
                return joystickPressed || primaryPressed || secondaryPressed;
        }
    }

    private static bool TryGetButton(InputDevice device, InputFeatureUsage<bool> usage)
    {
        return device.TryGetFeatureValue(usage, out bool isPressed) && isPressed;
    }
}

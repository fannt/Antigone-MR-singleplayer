using UnityEngine;

[DisallowMultipleComponent]
public class HandMenuKeyComboActivator : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private GameObject targetObject;
    [SerializeField] private bool forceDisabledOnStart = true;

    [Header("Shortcut")]
    [SerializeField] private bool requireCtrl = true;
    [SerializeField] private bool requireShift = true;
    [SerializeField] private bool requireAlt = false;
    [SerializeField] private bool requireCommand = false;
    [SerializeField] private KeyCode activationKey = KeyCode.M;

    [Header("Behavior")]
    [SerializeField] private bool toggleOnPress = false;
    [SerializeField] private bool disableSelfAfterEnable = true;

    private void Start()
    {
        if (targetObject == null)
        {
            Debug.LogWarning($"{nameof(HandMenuKeyComboActivator)} on {name} has no target object assigned.");
            enabled = false;
            return;
        }

        if (forceDisabledOnStart && targetObject.activeSelf)
            targetObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsActivationComboPressed())
            return;

        if (toggleOnPress)
        {
            targetObject.SetActive(!targetObject.activeSelf);
            return;
        }

        if (!targetObject.activeSelf)
            targetObject.SetActive(true);

        if (disableSelfAfterEnable)
            enabled = false;
    }

    private bool IsActivationComboPressed()
    {
        if (!Input.GetKeyDown(activationKey))
            return false;

        return IsModifierMatch(requireCtrl, KeyCode.LeftControl, KeyCode.RightControl)
               && IsModifierMatch(requireShift, KeyCode.LeftShift, KeyCode.RightShift)
               && IsModifierMatch(requireAlt, KeyCode.LeftAlt, KeyCode.RightAlt)
               && IsModifierMatch(requireCommand, KeyCode.LeftCommand, KeyCode.RightCommand);
    }

    private static bool IsModifierMatch(bool required, KeyCode leftKey, KeyCode rightKey)
    {
        if (!required)
            return true;

        return Input.GetKey(leftKey) || Input.GetKey(rightKey);
    }
}

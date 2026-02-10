using UnityEngine;

public class CueTrigger : MonoBehaviour

{
    public CueController controller;
    public int cueNumber;
    public bool shouldHideTriggerObject;

    private void OnTriggerEnter(Collider other)
    {
        controller.TriggerCue(cueNumber);
        if (shouldHideTriggerObject) {
            gameObject.SetActive(false);
        }
    }
}
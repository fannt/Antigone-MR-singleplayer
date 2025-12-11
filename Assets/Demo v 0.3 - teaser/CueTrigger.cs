using UnityEngine;

public class CueTrigger : MonoBehaviour

{
    public CueController controller;
    public int cueNumber;

    private void OnTriggerEnter(Collider other)
    {
        controller.TriggerCue(cueNumber);
    }
}
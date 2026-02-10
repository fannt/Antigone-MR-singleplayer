using UnityEngine;

public class GazeTrigger : MonoBehaviour
{
    public CueController controller;
    public int cueIndexToTrigger;
    public float gazeTime = 1f;

    private float timer = 0f;
    private bool gazing = false;

    public void OnGazeEnter()
    {
        gazing = true;
        timer = 0f;
    }

    public void OnGazeExit()
    {
        gazing = false;
        timer = 0f;
    }

    private void Update()
    {
        if (!gazing) return;

        timer += Time.deltaTime;
        if (timer >= gazeTime)
        {
            controller.TriggerCue(cueIndexToTrigger);
            gazing = false;
        }
    }
}
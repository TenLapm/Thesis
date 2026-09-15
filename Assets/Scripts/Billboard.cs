using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    // We use LateUpdate so it rotates AFTER the agent has finished moving for the frame
    void LateUpdate()
    {
        if (mainCam != null)
        {
            // Forces the UI to perfectly face the camera at all times
            transform.LookAt(transform.position + mainCam.transform.forward);
        }
    }
}